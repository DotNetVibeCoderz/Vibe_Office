using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using Microsoft.Extensions.AI;

namespace AutoWork.Agents;

/// <summary>
/// Rough token accounting. Exact counts need the provider's own tokenizer, and AutoWork talks
/// to a dozen providers, so this deliberately over-estimates: compacting slightly early costs
/// one summarisation call, while compacting late costs the whole run.
/// </summary>
public static class TokenEstimator
{
    /// <summary>Bytes per token is ~4 for English and Indonesian alike; 3.5 builds in headroom.</summary>
    private const double CharsPerToken = 3.5;

    /// <summary>Role markers, delimiters and other per-message protocol overhead.</summary>
    private const int PerMessageOverhead = 8;

    public static int Estimate(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / CharsPerToken);

    public static int Estimate(ChatMessage message)
    {
        var total = PerMessageOverhead;

        foreach (var content in message.Contents)
        {
            total += content switch
            {
                TextContent text => Estimate(text.Text),
                FunctionCallContent call => Estimate(call.Name) + Estimate(FormatArguments(call)) + 8,
                FunctionResultContent result => Estimate(result.Result?.ToString()) + 8,

                // A vision tile is roughly this many tokens across the major providers; the
                // exact figure varies but the order of magnitude is what matters here.
                DataContent data when data.HasTopLevelMediaType("image") => 1_200,

                _ => 16,
            };
        }

        return total;
    }

    public static int Estimate(IEnumerable<ChatMessage> messages) =>
        messages.Sum(Estimate);

    /// <summary>Tool schemas ride along on every single request, so they count.</summary>
    public static int EstimateTools(IEnumerable<AITool>? tools)
    {
        if (tools is null) return 0;

        var total = 0;
        foreach (var tool in tools.OfType<AIFunction>())
            total += Estimate(tool.Name) + Estimate(tool.Description) + Estimate(tool.JsonSchema.GetRawText());

        return total;
    }

    private static string FormatArguments(FunctionCallContent call) =>
        call.Arguments is null ? "" : string.Join(',', call.Arguments.Select(a => $"{a.Key}={a.Value}"));
}

public sealed record CompactionOutcome(bool Compacted, int TokensBefore, int TokensAfter, int TurnsSummarised);

/// <summary>
/// Auto-compaction: when the conversation approaches the model's context window, the middle of
/// it is replaced by a summary so a long run can keep going instead of hitting a wall.
///
/// The part that needs care is the cut point. Tool calls and their results are paired, and
/// providers reject a conversation where a tool result has no matching call (or vice versa).
/// So the boundary is always nudged to a clean turn edge before anything is dropped.
/// </summary>
public sealed class ContextCompactor
{
    private readonly IChatClient _client;
    private readonly AgentOptions _options;

    public ContextCompactor(IChatClient client, AgentOptions options)
    {
        _client = client;
        _options = options;
    }

    /// <summary>True when the conversation is close enough to the limit to act.</summary>
    public bool ShouldCompact(IReadOnlyList<ChatMessage> messages, int contextWindow, IEnumerable<AITool>? tools = null)
    {
        if (!_options.EnableAutoCompact || contextWindow <= 0) return false;

        var used = TokenEstimator.Estimate(messages) + TokenEstimator.EstimateTools(tools);
        return used >= contextWindow * _options.AutoCompactThreshold;
    }

    /// <summary>
    /// Rewrites <paramref name="messages"/> in place: leading system messages and the original
    /// goal stay, the middle becomes one summary message, and the most recent turns are kept
    /// verbatim so the model does not lose its immediate footing.
    /// </summary>
    public async Task<CompactionOutcome> CompactAsync(
        List<ChatMessage> messages,
        int contextWindow,
        IEnumerable<AITool>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var before = TokenEstimator.Estimate(messages);

        if (!ShouldCompact(messages, contextWindow, tools))
            return new CompactionOutcome(false, before, before, 0);

        // Everything before this index is a candidate for summarising.
        var preserveFrom = FindPreserveBoundary(messages, _options.CompactKeepRecentTurns);

        // Leading system messages and the first user turn are the run's charter — never drop them.
        var headCount = messages.TakeWhile(m => m.Role == ChatRole.System).Count();
        if (headCount < messages.Count && messages[headCount].Role == ChatRole.User) headCount++;

        if (preserveFrom <= headCount + 1)
            return new CompactionOutcome(false, before, before, 0);

        var middle = messages.Skip(headCount).Take(preserveFrom - headCount).ToList();
        if (middle.Count == 0) return new CompactionOutcome(false, before, before, 0);

        var summary = await SummariseAsync(middle, cancellationToken).ConfigureAwait(false);

        var rebuilt = new List<ChatMessage>(messages.Count - middle.Count + 1);
        rebuilt.AddRange(messages.Take(headCount));
        rebuilt.Add(new ChatMessage(ChatRole.User,
            $"""
             [Earlier work in this run has been summarised to free up context. This is the record of it.]

             {summary}

             [End of summary. Continue from here — do not repeat work already recorded above.]
             """));
        rebuilt.AddRange(messages.Skip(preserveFrom));

        var after = TokenEstimator.Estimate(rebuilt);

        // A summary can come out longer than the turns it replaces — a handful of short tool
        // calls summarised into careful prose, plus this wrapper, and the context has grown.
        // Keeping that would be the worst of both worlds: a model call paid for, more tokens than
        // before, and the threshold still crossed, so the next step compacts again and again.
        //
        // Observed live: 5,102 tokens in, 5,184 out, four times in one run.
        if (after >= before) return new CompactionOutcome(false, before, before, 0);

        messages.Clear();
        messages.AddRange(rebuilt);

        return new CompactionOutcome(true, before, after, middle.Count);
    }

    /// <summary>
    /// Walks back <paramref name="keepRecentTurns"/> user/assistant turns, then moves the
    /// boundary earlier until it sits at a clean edge — never between an assistant message
    /// that calls a tool and the message carrying that tool's result.
    /// </summary>
    internal static int FindPreserveBoundary(IReadOnlyList<ChatMessage> messages, int keepRecentTurns)
    {
        var index = messages.Count;
        var turns = 0;

        while (index > 0 && turns < keepRecentTurns)
        {
            index--;
            if (messages[index].Role == ChatRole.User || messages[index].Role == ChatRole.Assistant)
                turns++;
        }

        return AdjustToCleanBoundary(messages, index);
    }

    /// <summary>
    /// A kept segment must not open with orphaned tool results, and must not begin midway
    /// through an assistant turn whose calls were answered earlier.
    /// </summary>
    internal static int AdjustToCleanBoundary(IReadOnlyList<ChatMessage> messages, int index)
    {
        while (index > 0 && index < messages.Count && StartsMidToolExchange(messages[index]))
            index--;

        return Math.Max(0, index);
    }

    private static bool StartsMidToolExchange(ChatMessage message)
    {
        // Tool results whose originating call would be dropped.
        if (message.Contents.Any(c => c is FunctionResultContent)) return true;

        // Tool-role messages are always the second half of a pair.
        return message.Role == ChatRole.Tool;
    }

    private async Task<string> SummariseAsync(IReadOnlyList<ChatMessage> middle, CancellationToken cancellationToken)
    {
        var transcript = string.Join("\n\n", middle.Select(Render));

        var request = new List<ChatMessage>
        {
            new(ChatRole.System,
                """
                You compress an AI agent's working transcript so the run can continue with less context.
                Keep, in this order and under 400 words total:
                  - Facts discovered, with exact file paths, names, numbers and IDs. These are the most
                    important thing; never paraphrase a path or a figure.
                  - Actions already completed, so they are not repeated.
                  - Anything that failed, and why.
                  - What is still outstanding.
                Drop pleasantries, reasoning narration and duplicated tool output.
                Write plain prose and short lists. Do not add anything that is not in the transcript.
                """),
            new(ChatRole.User, transcript),
        };

        try
        {
            var response = await _client.GetResponseAsync(request,
                new ChatOptions { Temperature = 0, MaxOutputTokens = 900 },
                cancellationToken).ConfigureAwait(false);

            var text = response.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fall through to the mechanical summary below.
        }

        // If the model cannot summarise, a truncated transcript still beats losing the run.
        return BuildFallbackSummary(middle);
    }

    private static string BuildFallbackSummary(IReadOnlyList<ChatMessage> middle)
    {
        var toolCalls = middle
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(c => c.Name)
            .GroupBy(name => name)
            .Select(g => $"{g.Key} ×{g.Count()}")
            .ToList();

        var lastText = middle.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Text))?.Text ?? "";

        return $"""
                (Automatic summary — the summarisation model was unavailable.)
                Tools used so far: {(toolCalls.Count == 0 ? "none" : string.Join(", ", toolCalls))}.
                Most recent note: {Truncate(lastText, 800)}
                """;
    }

    private static string Render(ChatMessage message)
    {
        var parts = new List<string>();

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } text:
                    parts.Add(text.Text);
                    break;
                case FunctionCallContent call:
                    parts.Add($"[called {call.Name}]");
                    break;
                case FunctionResultContent result:
                    parts.Add($"[result] {Truncate(result.Result?.ToString() ?? "", 700)}");
                    break;
            }
        }

        return $"{message.Role.Value}: {string.Join('\n', parts)}";
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
