using System.Text;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Agents;

public sealed record SubAgentOutcome(string Transcript, bool AllFailed, int Succeeded, int Failed);

/// <summary>
/// Runs independent plan steps concurrently, each in its own conversation.
///
/// The win is latency, not cleverness: three folders to reorganise is three round-trip-bound
/// jobs that have no reason to queue. The constraint is that sub-agents must not overlap —
/// two agents renaming inside the same folder will interleave and corrupt each other's work —
/// which is why only steps the planner explicitly marked independent are dispatched this way.
///
/// Each sub-agent gets a fresh context holding just its own task, so their token use does not
/// compound, and the parent sees only their final reports.
/// </summary>
public sealed class SubAgentCoordinator
{
    private readonly IChatClient _client;
    private readonly AgentOptions _options;
    private readonly IList<AITool> _tools;

    public SubAgentCoordinator(IChatClient client, AgentOptions options, IList<AITool> tools)
    {
        _client = client;
        _options = options;
        _tools = tools;
    }

    public async Task<SubAgentOutcome> RunAsync(
        string runId,
        string parentGoal,
        IReadOnlyList<PlanStep> steps,
        PermissionPolicy policy,
        string workingDirectory,
        Action<RunEvent> report,
        CancellationToken cancellationToken = default)
    {
        using var throttle = new SemaphoreSlim(Math.Max(1, _options.MaxParallelSubAgents));

        var system = Prompts.BuildSubAgentSystem(parentGoal, policy, workingDirectory);

        var tasks = steps.Select(step => RunOneAsync(
            runId, step, system, throttle, report, cancellationToken)).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var builder = new StringBuilder();
        foreach (var (step, report_, succeeded) in results)
        {
            builder.AppendLine($"── Step {step.Index}: {step.Title} — {(succeeded ? "done" : "failed")}");
            builder.AppendLine(report_);
            builder.AppendLine();
        }

        var succeededCount = results.Count(r => r.Succeeded);

        return new SubAgentOutcome(
            builder.ToString().TrimEnd(),
            AllFailed: succeededCount == 0,
            Succeeded: succeededCount,
            Failed: results.Length - succeededCount);
    }

    private async Task<(PlanStep Step, string Report, bool Succeeded)> RunOneAsync(
        string runId,
        PlanStep step,
        string system,
        SemaphoreSlim throttle,
        Action<RunEvent> report,
        CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);

        var subAgentId = $"{runId}-s{step.Index}";

        try
        {
            report(new SubAgentEvent
            {
                RunId = runId,
                SubAgentId = subAgentId,
                Task = step.Title,
                Status = StepStatus.Running,
            });

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, system),
                new(ChatRole.User, BuildTaskPrompt(step)),
            };

            var response = await _client.GetResponseAsync(messages, new ChatOptions
            {
                Tools = _tools,
                Temperature = 0.2f,
            }, cancellationToken).ConfigureAwait(false);

            var text = response.Text?.Trim() ?? "";
            var succeeded = !string.IsNullOrWhiteSpace(text);

            report(new SubAgentEvent
            {
                RunId = runId,
                SubAgentId = subAgentId,
                Task = step.Title,
                Status = succeeded ? StepStatus.Succeeded : StepStatus.Failed,
                Result = text,
            });

            return (step, succeeded ? text : "The sub-agent returned nothing.", succeeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            report(new SubAgentEvent
            {
                RunId = runId,
                SubAgentId = subAgentId,
                Task = step.Title,
                Status = StepStatus.Failed,
                Result = ex.Message,
            });

            // A failed sub-agent is reported back to the parent, which decides what to do —
            // one branch failing should not take down the others.
            return (step, $"Failed: {ex.Message}", false);
        }
        finally
        {
            throttle.Release();
        }
    }

    private static string BuildTaskPrompt(PlanStep step)
    {
        var builder = new StringBuilder($"Your task: {step.Title}\n\n{step.Intent}");

        if (step.SuccessCriteria.Count > 0)
        {
            builder.AppendLine().AppendLine().AppendLine("You are done when:");
            foreach (var criterion in step.SuccessCriteria)
                builder.AppendLine($"  - {criterion}");
        }

        builder.AppendLine().AppendLine("Finish with a short factual report of what you did.");
        return builder.ToString();
    }
}
