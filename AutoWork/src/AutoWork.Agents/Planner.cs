using System.Text.Json;
using System.Text.Json.Serialization;
using AutoWork.Core.Agents;
using Microsoft.Extensions.AI;

namespace AutoWork.Agents;

/// <summary>
/// Turns a request into an ordered plan with checkable success criteria.
///
/// Planning is separated from doing for two reasons: the user gets to see what is about to
/// happen before it happens, and the verification pass at the end has something concrete to
/// check against rather than re-deriving intent from a transcript.
/// </summary>
public sealed class Planner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
    };

    private readonly IChatClient _client;

    public Planner(IChatClient client) => _client = client;

    public async Task<AgentPlan> CreatePlanAsync(
        string goal,
        IReadOnlyList<ToolDescriptor> tools,
        string permissionSummary,
        CancellationToken cancellationToken = default)
    {
        var capabilities = string.Join('\n', tools
            .GroupBy(t => t.Category)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"  {g.Key}: {string.Join(", ", g.Select(t => t.Name))}"));

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, Prompts.PlannerSystem),
            new(ChatRole.User,
                $"""
                 Request: {goal}

                 Capabilities available to the agent:
                 {capabilities}

                 {permissionSummary}
                 """),
        };

        try
        {
            var response = await _client.GetResponseAsync(messages,
                new ChatOptions { Temperature = 0.1f, MaxOutputTokens = 2000 },
                cancellationToken).ConfigureAwait(false);

            var parsed = Parse(goal, response.Text);
            if (parsed is not null) return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Planning is an optimisation, not a gate. If it fails, run the goal directly
            // rather than refusing to start.
        }

        return AgentPlan.SingleStep(goal);
    }

    private sealed class PlanPayload
    {
        public string? Notes { get; set; }
        public List<string>? SuccessCriteria { get; set; }
        public List<StepPayload>? Steps { get; set; }
    }

    private sealed class StepPayload
    {
        public int Index { get; set; }
        public string Title { get; set; } = "";
        public string Intent { get; set; } = "";
        public string Organ { get; set; } = "Hands";
        public List<int>? DependsOn { get; set; }
        public List<string>? SuccessCriteria { get; set; }
    }

    internal static AgentPlan? Parse(string goal, string? raw)
    {
        var json = ExtractJson(raw);
        if (json is null) return null;

        PlanPayload? payload;
        try { payload = JsonSerializer.Deserialize<PlanPayload>(json, Json); }
        catch (JsonException) { return null; }

        if (payload?.Steps is not { Count: > 0 }) return null;

        var steps = payload.Steps
            .Where(s => !string.IsNullOrWhiteSpace(s.Title))
            .Select((s, position) => new PlanStep
            {
                Index = s.Index > 0 ? s.Index : position + 1,
                Title = s.Title.Trim(),
                Intent = string.IsNullOrWhiteSpace(s.Intent) ? s.Title.Trim() : s.Intent.Trim(),
                Organ = ParseOrgan(s.Organ),
                DependsOn = s.DependsOn?.Where(d => d > 0).Distinct().ToArray() ?? [],
                SuccessCriteria = s.SuccessCriteria?.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray() ?? [],
            })
            .OrderBy(s => s.Index)
            .ToList();

        if (steps.Count == 0) return null;

        return new AgentPlan
        {
            Goal = goal,
            Steps = steps,
            SuccessCriteria = payload.SuccessCriteria?.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray() ?? [],
            Notes = string.IsNullOrWhiteSpace(payload.Notes) ? null : payload.Notes.Trim(),
        };
    }

    private static AgentOrgan ParseOrgan(string? organ) =>
        Enum.TryParse<AgentOrgan>(organ, ignoreCase: true, out var parsed) ? parsed : AgentOrgan.Hands;

    /// <summary>
    /// Models wrap JSON in code fences or add a sentence before it no matter how firmly the
    /// prompt says otherwise, so pull out the outermost object rather than trusting the shape.
    /// </summary>
    internal static string? ExtractJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim();

        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = text.IndexOf('\n', fence);
            var end = text.IndexOf("```", fence + 3, StringComparison.Ordinal);
            if (start > 0 && end > start) text = text[(start + 1)..end].Trim();
        }

        var open = text.IndexOf('{');
        var close = text.LastIndexOf('}');

        return open >= 0 && close > open ? text[open..(close + 1)] : null;
    }
}
