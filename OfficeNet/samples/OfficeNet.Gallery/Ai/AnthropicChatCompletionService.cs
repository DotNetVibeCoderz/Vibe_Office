// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace OfficeNet.Gallery.Ai;

/// <summary>
/// Claude, as a Semantic Kernel chat service.
/// </summary>
/// <remarks>
/// <para>
/// Semantic Kernel ships connectors for OpenAI and Google but not for Anthropic, so this speaks the
/// Messages API directly. Three differences from the OpenAI shape are worth knowing, because each
/// one silently produces a wrong request rather than an error:
/// </para>
/// <list type="bullet">
/// <item>The system prompt is a **top-level field**, not a message with role "system". Sending it as
/// a message is rejected.</item>
/// <item><c>max_tokens</c> is **required**, not optional.</item>
/// <item>Authentication uses <c>x-api-key</c> plus an <c>anthropic-version</c> header, not a bearer
/// token.</item>
/// </list>
/// <para>
/// Tool calling is deliberately not implemented here. The gallery's kernel functions run against
/// the OpenAI-shaped providers; wiring Anthropic's tool-use blocks through SK's function-calling
/// pipeline is a larger job than a sample warrants, and pretending otherwise would mean a chat that
/// quietly ignores the tools it advertises.
/// </para>
/// </remarks>
internal sealed class AnthropicChatCompletionService : IChatCompletionService
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string Version = "2023-06-01";
    private const int DefaultMaxTokens = 4096;

    private readonly HttpClient _http;
    private readonly string _model;

    public AnthropicChatCompletionService(string model, string apiKey, HttpClient? http = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _model = model;
        _http = http ?? new HttpClient();

        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Remove("anthropic-version");
        _http.DefaultRequestHeaders.Add("anthropic-version", Version);

        Attributes = new Dictionary<string, object?>
        {
            [AIServiceExtensions.ModelIdKey] = model,
        };
    }

    public IReadOnlyDictionary<string, object?> Attributes { get; }

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);

        var request = BuildRequest(chatHistory, stream: false);

        using var response = await _http.PostAsJsonAsync(Endpoint, request, cancellationToken);
        await ThrowIfFailed(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var text = ReadText(payload);

        return [new ChatMessageContent(AuthorRole.Assistant, text, _model)];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);

        var request = BuildRequest(chatHistory, stream: true);

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(request),
        };

        using var response = await _http.SendAsync(
            message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        await ThrowIfFailed(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // ReadLineAsync returning null is the end of the stream. Testing EndOfStream instead would
        // block the thread on a synchronous read inside an async method (CA2024).
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            // Server-sent events: only "data:" lines carry payload, and the rest are event names
            // and blank separators.
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line["data:".Length..].Trim();

            if (json.Length == 0)
            {
                continue;
            }

            var chunk = TryParse(json);

            // Only content_block_delta carries text; message_start, ping and the stop events do
            // not, and treating them as text emits stray empty chunks.
            if (chunk?["type"]?.GetValue<string>() != "content_block_delta")
            {
                continue;
            }

            if (chunk["delta"]?["text"]?.GetValue<string>() is { Length: > 0 } text)
            {
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, text, _model);
            }
        }
    }

    private AnthropicRequest BuildRequest(ChatHistory history, bool stream)
    {
        // The system prompt is a top-level field on this API, so it is lifted out of the history
        // rather than sent as a message.
        var system = string.Join("\n\n", history
            .Where(m => m.Role == AuthorRole.System)
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c)));

        var messages = history
            .Where(m => m.Role == AuthorRole.User || m.Role == AuthorRole.Assistant)
            .Select(m => new AnthropicMessage(
                m.Role == AuthorRole.User ? "user" : "assistant",
                m.Content ?? string.Empty))
            .ToList();

        return new AnthropicRequest(
            _model,
            DefaultMaxTokens,
            messages,
            system.Length == 0 ? null : system,
            stream);
    }

    private static async Task ThrowIfFailed(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(token);

        // The API's own error message is far more useful than the status code — "credit balance is
        // too low", "model not found" — so it is surfaced rather than swallowed.
        var detail = TryParse(body)?["error"]?["message"]?.GetValue<string>() ?? body;

        throw new HttpRequestException(
            $"Anthropic returned {(int)response.StatusCode}: {detail}");
    }

    private static string ReadText(JsonNode? payload)
    {
        if (payload?["content"] is not JsonArray blocks)
        {
            return string.Empty;
        }

        return string.Concat(blocks
            .Where(b => b?["type"]?.GetValue<string>() == "text")
            .Select(b => b?["text"]?.GetValue<string>() ?? string.Empty));
    }

    private static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages,
        [property: JsonPropertyName("system"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? System,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
