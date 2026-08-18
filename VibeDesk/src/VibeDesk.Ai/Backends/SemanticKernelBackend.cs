using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using VibeDesk.Application.Assistant;

namespace VibeDesk.Ai.Backends;

/// <summary>
/// The OpenAI, Google and Ollama path. The connector runs the tool loop itself, so this class only
/// has to shape the execution settings and relay chunks.
/// </summary>
public sealed class SemanticKernelBackend : IChatBackend
{
    public async IAsyncEnumerable<ClippyChunk> StreamAsync(
        Kernel kernel,
        ChatHistory history,
        ChatRunSettings settings,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        // Base PromptExecutionSettings rather than a connector-specific subclass: each connector
        // deserialises ExtensionData into its own settings type, so one shape works for all three and
        // the project never has to reference an alpha connector's option classes.
        var execution = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = settings.Temperature,
                ["max_tokens"] = settings.MaxTokens,
                // Ollama spells the output cap differently; sending both is harmless because each
                // connector ignores keys it does not know.
                ["num_predict"] = settings.MaxTokens,
            },
        };

        // Tool calls are invoked inside the connector, so a filter is the only place they surface.
        var recorder = new ToolCallRecorder();
        kernel.FunctionInvocationFilters.Add(recorder);

        try
        {
            IAsyncEnumerator<StreamingChatMessageContent>? enumerator = null;
            string? openFailure = null;

            try
            {
                enumerator = chat
                    .GetStreamingChatMessageContentsAsync(history, execution, kernel, ct)
                    .GetAsyncEnumerator(ct);
            }
            catch (Exception ex)
            {
                openFailure = Describe(ex);
            }

            if (enumerator is null)
            {
                yield return new ClippyChunk(null, null, true, openFailure);
                yield break;
            }

            await using (enumerator.ConfigureAwait(false))
            {
                while (true)
                {
                    bool moved;
                    string? error = null;

                    try
                    {
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        yield break;
                    }
                    catch (Exception ex)
                    {
                        moved = false;
                        error = Describe(ex);
                    }

                    if (error is not null)
                    {
                        yield return new ClippyChunk(null, null, true, error);
                        yield break;
                    }

                    if (!moved) break;

                    // Drain any tool calls the filter saw since the last chunk so the badge appears
                    // while the tool is running, not after the whole reply lands.
                    while (recorder.TryDequeue(out var call))
                    {
                        yield return new ClippyChunk(null, call, false, null);
                    }

                    var text = enumerator.Current.Content;
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return new ClippyChunk(text, null, false, null);
                    }
                }
            }

            while (recorder.TryDequeue(out var trailing))
            {
                yield return new ClippyChunk(null, trailing, false, null);
            }
        }
        finally
        {
            kernel.FunctionInvocationFilters.Remove(recorder);
        }

        yield return new ClippyChunk(null, null, true, null);
    }

    /// <summary>
    /// Turns a provider failure into something the user can act on. Connection refusals matter most:
    /// Ollama is listed as available on the strength of a configured endpoint alone, so "connection
    /// refused" is the expected first experience for anyone who has not started it.
    /// </summary>
    private static string Describe(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is System.Net.Sockets.SocketException or HttpRequestException)
            {
                return $"Tidak bisa terhubung ke penyedia AI ({current.Message}). " +
                       "Untuk Ollama, pastikan servisnya berjalan di endpoint yang dikonfigurasi; " +
                       "untuk penyedia cloud, periksa API key dan koneksi internet.";
            }
        }

        return ex is HttpOperationException http
            ? $"{http.Message} ({(int?)http.StatusCode})"
            : ex.Message;
    }

    /// <summary>
    /// Captures every kernel function the connector invokes. A queue rather than a list because the
    /// caller drains it between chunks to keep the UI live.
    /// </summary>
    private sealed class ToolCallRecorder : IFunctionInvocationFilter
    {
        private readonly Queue<ToolCallDto> _calls = new();

        public bool TryDequeue(out ToolCallDto call)
        {
            lock (_calls)
            {
                if (_calls.Count == 0)
                {
                    call = default!;
                    return false;
                }

                call = _calls.Dequeue();
                return true;
            }
        }

        public async Task OnFunctionInvocationAsync(
            FunctionInvocationContext context,
            Func<FunctionInvocationContext, Task> next)
        {
            var name = context.Function.Name;
            var summary = ToolSummary.Describe(name, context.Arguments);

            try
            {
                await next(context).ConfigureAwait(false);
                Record(new ToolCallDto(name, summary, true));
            }
            catch (Exception ex)
            {
                Record(new ToolCallDto(name, $"{summary} — {ex.Message}", false));
                throw;
            }
        }

        private void Record(ToolCallDto call)
        {
            lock (_calls) _calls.Enqueue(call);
        }
    }
}

/// <summary>Turns a function name plus arguments into the one line the badge shows.</summary>
internal static class ToolSummary
{
    public static string? Describe(string function, KernelArguments arguments)
    {
        // The first argument is almost always the interesting one (the query, the URL, the id).
        foreach (var (_, value) in arguments)
        {
            var text = value?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Length > 120 ? text[..120] + "…" : text;
            }
        }

        return null;
    }
}
