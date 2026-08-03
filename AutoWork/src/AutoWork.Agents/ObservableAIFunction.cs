using System.Diagnostics;
using System.Text.Json;
using AutoWork.Core.Agents;
using Microsoft.Extensions.AI;

namespace AutoWork.Agents;

/// <summary>
/// Wraps a tool so the UI can watch it.
///
/// Microsoft.Extensions.AI's function-invocation middleware calls tools for us, which is what
/// makes the agent loop short — but it also means the calls happen out of sight. Wrapping each
/// function is how the Work Tape gets to show "listing ~/Downloads… 47 files" as it happens
/// rather than only after the turn completes.
/// </summary>
internal sealed class ObservableAIFunction : AIFunction
{
    private readonly AIFunction _inner;
    private readonly ToolDescriptor _descriptor;
    private readonly string _runId;
    private readonly Action<RunEvent> _report;

    public ObservableAIFunction(ToolDescriptor descriptor, string runId, Action<RunEvent> report)
    {
        _descriptor = descriptor;
        _inner = descriptor.Function;
        _runId = runId;
        _report = report;
    }

    public override string Name => _inner.Name;
    public override string Description => _inner.Description;
    public override JsonElement JsonSchema => _inner.JsonSchema;
    public override JsonSerializerOptions JsonSerializerOptions => _inner.JsonSerializerOptions;
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => _inner.AdditionalProperties;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var rendered = Render(arguments);

        _report(new ToolCallEvent
        {
            RunId = _runId,
            Tool = Name,
            Organ = _descriptor.Organ,
            Arguments = rendered,
        });

        try
        {
            var result = await _inner.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var text = result?.ToString() ?? "";
            var failed = text.StartsWith("ERROR:", StringComparison.Ordinal)
                      || text.StartsWith("REFUSED:", StringComparison.Ordinal);

            _report(new ToolCallEvent
            {
                RunId = _runId,
                Tool = Name,
                Organ = _descriptor.Organ,
                Arguments = rendered,
                Result = Trim(text, 400),
                Failed = failed,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            });

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            _report(new ToolCallEvent
            {
                RunId = _runId,
                Tool = Name,
                Organ = _descriptor.Organ,
                Arguments = rendered,
                Result = ex.Message,
                Failed = true,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            });

            // Hand the model a readable failure instead of ending its turn.
            return $"ERROR: {ex.Message}";
        }
    }

    private static string Render(AIFunctionArguments arguments)
    {
        if (arguments.Count == 0) return "";

        var parts = arguments.Select(pair => $"{pair.Key}: {Trim(pair.Value?.ToString() ?? "null", 120)}");
        return string.Join(", ", parts);
    }

    private static string Trim(string text, int max)
    {
        var flattened = text.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= max ? flattened : flattened[..max] + "…";
    }
}
