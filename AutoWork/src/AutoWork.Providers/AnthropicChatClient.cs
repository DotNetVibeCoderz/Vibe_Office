using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace AutoWork.Providers;

/// <summary>
/// Native Anthropic Messages API client exposed as an <see cref="IChatClient"/>.
///
/// Anthropic does publish an OpenAI-compatible shim, but it is explicitly a migration aid and
/// trails the native API. Since the Brain is the part of AutoWork that most needs full tool-use
/// and vision fidelity, this talks to /v1/messages directly.
/// </summary>
public sealed class AnthropicChatClient : IChatClient
{
    private const string AnthropicVersion = "2023-06-01";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _defaultMaxTokens;
    private readonly bool _ownsHttpClient;

    public AnthropicChatClient(
        string apiKey,
        string model,
        string endpoint = "https://api.anthropic.com/v1",
        int defaultMaxTokens = 8192,
        IDictionary<string, string>? extraHeaders = null,
        HttpClient? httpClient = null)
    {
        _model = model;
        _defaultMaxTokens = defaultMaxTokens;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();

        _http.BaseAddress ??= new Uri(endpoint.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", apiKey);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
                _http.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        }

        Metadata = new ChatClientMetadata("anthropic", _http.BaseAddress, model);
    }

    public ChatClientMetadata Metadata { get; }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? Metadata
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildRequest(messages, options);

        using var response = await _http.PostAsJsonAsync("messages", payload, Json, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Anthropic returned {(int)response.StatusCode} {response.ReasonPhrase}: {Trim(body)}",
                null, response.StatusCode);
        }

        var node = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Anthropic returned an empty response body.");

        return Translate(node);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // AutoWork's agent loop consumes complete turns (it needs the full tool-call list before
        // it can act), so a single terminal update is the honest shape here rather than a fake
        // token-by-token drip.
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        foreach (var update in response.ToChatResponseUpdates())
            yield return update;
    }

    // ── Request ───────────────────────────────────────────────────────────────────────────

    private JsonObject BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var request = new JsonObject
        {
            ["model"] = options?.ModelId ?? _model,
            ["max_tokens"] = options?.MaxOutputTokens ?? _defaultMaxTokens,
        };

        if (options?.Temperature is { } temperature) request["temperature"] = temperature;
        if (options?.TopP is { } topP) request["top_p"] = topP;

        // Anthropic carries the system prompt outside the message list.
        var systemParts = new List<string>();
        var conversation = new JsonArray();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                var text = message.Text;
                if (!string.IsNullOrWhiteSpace(text)) systemParts.Add(text);
                continue;
            }

            var content = BuildContent(message);
            if (content.Count == 0) continue;

            // Tool results are sent back as a user turn in the Messages API.
            var role = message.Role == ChatRole.Assistant ? "assistant" : "user";
            conversation.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }

        if (systemParts.Count > 0) request["system"] = string.Join("\n\n", systemParts);
        request["messages"] = conversation;

        if (options?.Tools is { Count: > 0 } tools)
        {
            var toolArray = new JsonArray();
            foreach (var tool in tools.OfType<AIFunction>())
            {
                toolArray.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = JsonNode.Parse(tool.JsonSchema.GetRawText()),
                });
            }

            if (toolArray.Count > 0) request["tools"] = toolArray;
        }

        return request;
    }

    private static JsonArray BuildContent(ChatMessage message)
    {
        var blocks = new JsonArray();

        foreach (var item in message.Contents)
        {
            switch (item)
            {
                case TextContent { Text.Length: > 0 } text:
                    blocks.Add(new JsonObject { ["type"] = "text", ["text"] = text.Text });
                    break;

                case DataContent data when data.HasTopLevelMediaType("image"):
                    blocks.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = data.MediaType,
                            ["data"] = Convert.ToBase64String(data.Data.Span),
                        },
                    });
                    break;

                case FunctionCallContent call:
                    blocks.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = call.CallId,
                        ["name"] = call.Name,
                        ["input"] = call.Arguments is null
                            ? new JsonObject()
                            : JsonSerializer.SerializeToNode(call.Arguments, Json),
                    });
                    break;

                case FunctionResultContent result:
                    blocks.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = result.CallId,
                        ["content"] = result.Result?.ToString() ?? "",
                        ["is_error"] = result.Exception is not null,
                    });
                    break;
            }
        }

        return blocks;
    }

    // ── Response ──────────────────────────────────────────────────────────────────────────

    private ChatResponse Translate(JsonNode node)
    {
        var contents = new List<AIContent>();

        if (node["content"] is JsonArray blocks)
        {
            foreach (var block in blocks)
            {
                var type = block?["type"]?.GetValue<string>();

                if (type == "text")
                {
                    var text = block!["text"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(text)) contents.Add(new TextContent(text));
                }
                else if (type == "tool_use")
                {
                    var callId = block!["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("n");
                    var name = block["name"]?.GetValue<string>() ?? "";

                    var arguments = block["input"] is JsonObject input
                        ? input.ToDictionary(p => p.Key, p => (object?)p.Value?.DeepClone())
                        : new Dictionary<string, object?>();

                    contents.Add(new FunctionCallContent(callId, name, arguments));
                }
            }
        }

        var message = new ChatMessage(ChatRole.Assistant, contents);

        var response = new ChatResponse(message)
        {
            ResponseId = node["id"]?.GetValue<string>(),
            ModelId = node["model"]?.GetValue<string>(),
            FinishReason = MapFinishReason(node["stop_reason"]?.GetValue<string>()),
        };

        if (node["usage"] is { } usage)
        {
            response.Usage = new UsageDetails
            {
                InputTokenCount = usage["input_tokens"]?.GetValue<int>(),
                OutputTokenCount = usage["output_tokens"]?.GetValue<int>(),
            };
            response.Usage.TotalTokenCount =
                (response.Usage.InputTokenCount ?? 0) + (response.Usage.OutputTokenCount ?? 0);
        }

        return response;
    }

    private static ChatFinishReason? MapFinishReason(string? reason) => reason switch
    {
        "end_turn" or "stop_sequence" => ChatFinishReason.Stop,
        "max_tokens" => ChatFinishReason.Length,
        "tool_use" => ChatFinishReason.ToolCalls,
        "refusal" => ChatFinishReason.ContentFilter,
        _ => null,
    };

    private static string Trim(string body) => body.Length <= 600 ? body : body[..600] + "…";

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
