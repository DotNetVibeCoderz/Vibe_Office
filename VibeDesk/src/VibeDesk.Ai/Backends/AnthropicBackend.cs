using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using VibeDesk.Application.Assistant;
using SkImageContent = Microsoft.SemanticKernel.ImageContent;
using SkTextContent = Microsoft.SemanticKernel.TextContent;

namespace VibeDesk.Ai.Backends;

/// <summary>
/// Claude support, written against the official Anthropic SDK because Semantic Kernel ships no
/// Anthropic connector. It drives the tool loop itself, invoking the very same
/// <see cref="KernelFunction"/>s the other providers see.
/// </summary>
/// <remarks>
/// The loop is deliberately non-streaming. Reconstructing a tool call from a token stream means
/// accumulating partial JSON across <c>input_json_delta</c> events, and a half-parsed tool call is a
/// silently wrong action rather than a visibly failed one. The panel still renders progressively:
/// the finished reply is handed back in small slices.
/// </remarks>
public sealed class AnthropicBackend(string apiKey) : IChatBackend
{
    private readonly AnthropicClient _client = new() { ApiKey = apiKey };

    public async IAsyncEnumerable<ClippyChunk> StreamAsync(
        Kernel kernel,
        ChatHistory history,
        ChatRunSettings settings,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var system = BuildSystem(history);
        var messages = BuildMessages(history);
        var tools = BuildTools(kernel);

        for (var iteration = 0; iteration <= settings.MaxToolIterations; iteration++)
        {
            Message? response = null;
            string? failure = null;

            try
            {
                response = await _client.Messages.Create(
                    BuildRequest(system, messages, tools, settings, forceAnswer: iteration == settings.MaxToolIterations));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                failure = string.Empty;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            if (failure is not null)
            {
                if (failure.Length > 0) yield return new ClippyChunk(null, null, true, failure);
                yield break;
            }

            // A safety classifier can decline with HTTP 200 and an empty content list, so the refusal
            // has to be checked before anything reads Content.
            if (string.Equals(response.StopReason?.ToString(), "refusal", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ClippyChunk(
                    null, null, true,
                    "Claude menolak permintaan ini karena kebijakan keamanannya. Coba ubah pertanyaannya, atau pilih provider lain.");
                yield break;
            }

            var assistantBlocks = new List<ContentBlockParam>();
            var toolResults = new List<ContentBlockParam>();
            var text = new System.Text.StringBuilder();
            var pendingCalls = new List<ToolCallDto>();

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? textBlock) && textBlock is not null)
                {
                    text.Append(textBlock.Text);
                    assistantBlocks.Add(new TextBlockParam { Text = textBlock.Text });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse) && toolUse is not null)
                {
                    assistantBlocks.Add(new ToolUseBlockParam
                    {
                        ID = toolUse.ID,
                        Name = toolUse.Name,
                        Input = toolUse.Input,
                    });

                    var (result, call) = await InvokeAsync(kernel, toolUse, settings, ct).ConfigureAwait(false);
                    pendingCalls.Add(call);

                    toolResults.Add(new ToolResultBlockParam
                    {
                        ToolUseID = toolUse.ID,
                        Content = result,
                        IsError = !call.Succeeded,
                    });
                }
            }

            foreach (var call in pendingCalls)
            {
                yield return new ClippyChunk(null, call, false, null);
            }

            if (toolResults.Count == 0)
            {
                foreach (var slice in Slice(text.ToString()))
                {
                    yield return new ClippyChunk(slice, null, false, null);
                }

                yield return new ClippyChunk(null, null, true, null);
                yield break;
            }

            // Every tool_use must be answered in a single following user turn, or the API rejects it.
            messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantBlocks });
            messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
        }

        yield return new ClippyChunk(
            null, null, true,
            "Batas pemakaian tool tercapai sebelum jawaban selesai. Coba persempit pertanyaannya.");
    }

    private MessageCreateParams BuildRequest(
        List<TextBlockParam> system,
        List<MessageParam> messages,
        List<ToolUnion> tools,
        ChatRunSettings settings,
        bool forceAnswer)
    {
        // Note the absence of Temperature: Anthropic removed sampling parameters from every model
        // after Claude Opus 4.6, and sending one now returns a 400. The configured temperature is
        // still honoured by the OpenAI, Google and Ollama backends.
        return new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = settings.MaxTokens,
            System = system,
            Messages = messages,
            // On the last iteration the tools are withheld so the model has to produce prose.
            Tools = forceAnswer || tools.Count == 0 ? null : tools,
        };
    }

    private static List<TextBlockParam> BuildSystem(ChatHistory history)
    {
        var parts = history
            .Where(m => m.Role == AuthorRole.System)
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => new TextBlockParam { Text = c! })
            .ToList();

        return parts.Count > 0
            ? parts
            : [new TextBlockParam { Text = "You are a helpful assistant." }];
    }

    private static List<MessageParam> BuildMessages(ChatHistory history)
    {
        var messages = new List<MessageParam>();

        foreach (var message in history)
        {
            if (message.Role == AuthorRole.System) continue;

            var blocks = new List<ContentBlockParam>();

            foreach (var item in message.Items)
            {
                switch (item)
                {
                    case SkTextContent { Text: { Length: > 0 } t }:
                        blocks.Add(new TextBlockParam { Text = t });
                        break;

                    case SkImageContent image when TryImageBlock(image, out var imageBlock):
                        blocks.Add(imageBlock!);
                        break;
                }
            }

            if (blocks.Count == 0 && !string.IsNullOrWhiteSpace(message.Content))
            {
                blocks.Add(new TextBlockParam { Text = message.Content! });
            }

            if (blocks.Count == 0) continue;

            messages.Add(new MessageParam
            {
                Role = message.Role == AuthorRole.Assistant ? Role.Assistant : Role.User,
                Content = blocks,
            });
        }

        return messages;
    }

    /// <summary>
    /// Inline images only. A URL-only attachment is skipped rather than forwarded, because the URL
    /// points at this app and Anthropic cannot reach a private deployment to fetch it.
    /// </summary>
    private static bool TryImageBlock(SkImageContent image, out ContentBlockParam? block)
    {
        block = null;

        if (image.Data is not { Length: > 0 } data) return false;

        var mediaType = (image.MimeType ?? "image/png").ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => MediaType.ImageJpeg,
            "image/gif" => MediaType.ImageGif,
            "image/webp" => MediaType.ImageWebP,
            _ => MediaType.ImagePng,
        };

        block = new ImageBlockParam
        {
            Source = new Base64ImageSource
            {
                Data = Convert.ToBase64String(data.Span),
                MediaType = mediaType,
            },
        };

        return true;
    }

    private static List<ToolUnion> BuildTools(Kernel kernel)
    {
        var tools = new List<ToolUnion>();

        foreach (var function in kernel.Plugins.GetFunctionsMetadata())
        {
            var properties = new Dictionary<string, JsonElement>();
            var required = new List<string>();

            foreach (var parameter in function.Parameters)
            {
                properties[parameter.Name] = parameter.Schema?.RootElement
                    ?? JsonSerializer.SerializeToElement(new
                    {
                        type = "string",
                        description = parameter.Description ?? string.Empty,
                    });

                if (parameter.IsRequired) required.Add(parameter.Name);
            }

            tools.Add(new Tool
            {
                Name = function.Name,
                Description = function.Description,
                InputSchema = new InputSchema
                {
                    Properties = properties,
                    Required = required,
                },
            });
        }

        return tools;
    }

    private static async Task<(string Result, ToolCallDto Call)> InvokeAsync(
        Kernel kernel,
        ToolUseBlock toolUse,
        ChatRunSettings settings,
        CancellationToken ct)
    {
        var arguments = new KernelArguments();

        foreach (var (name, value) in toolUse.Input)
        {
            arguments[name] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.TryGetInt64(out var i) ? i : value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.GetRawText(),
            };
        }

        var summary = ToolSummary.Describe(toolUse.Name, arguments);

        if (!kernel.Plugins.TryGetFunction(pluginName: null, toolUse.Name, out var function))
        {
            return ($"Unknown tool '{toolUse.Name}'.", new ToolCallDto(toolUse.Name, summary, false));
        }

        try
        {
            var result = await function.InvokeAsync(kernel, arguments, ct).ConfigureAwait(false);
            var text = result.GetValue<string>() ?? result.ToString() ?? string.Empty;

            if (text.Length > settings.MaxToolResultChars)
            {
                text = text[..settings.MaxToolResultChars] + "\n…(dipotong)";
            }

            return (text, new ToolCallDto(toolUse.Name, summary, true));
        }
        catch (Exception ex)
        {
            return ($"Tool failed: {ex.Message}", new ToolCallDto(toolUse.Name, $"{summary} — {ex.Message}", false));
        }
    }

    /// <summary>Hands the finished reply back in small pieces so the panel renders progressively.</summary>
    private static IEnumerable<string> Slice(string text)
    {
        const int size = 24;

        for (var i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }
}
