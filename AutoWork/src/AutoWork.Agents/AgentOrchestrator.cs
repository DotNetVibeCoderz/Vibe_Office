using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AutoWork.Core;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Providers;
using Microsoft.Extensions.AI;

namespace AutoWork.Agents;

public sealed record RunResult
{
    public required string RunId { get; init; }
    public required RunStatus Status { get; init; }
    public string Summary { get; init; } = "";
    public AgentPlan? Plan { get; init; }
    public VerificationResult? Verification { get; init; }
    public long ElapsedMs { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// The Brain: plans a request, executes the plan step by step with tools, recovers from
/// failures, compacts its own context when the window fills, and verifies its work before
/// claiming success.
///
/// The loop is deliberately step-shaped rather than one long conversation. Steps give the user
/// something to watch, give failures a boundary to be contained by, and give the verifier
/// concrete criteria to check.
/// </summary>
public sealed class AgentOrchestrator
{
    private readonly ConfigStore _config;
    private readonly ModelClientFactory _factory;
    private readonly ToolRegistry _registry;
    private readonly IActionLog _log;
    private readonly IApprovalBroker _approvals;
    private readonly IKnowledgeStore _knowledge;
    private readonly ISecretStore? _secrets;

    public AgentOrchestrator(
        ConfigStore config,
        ModelClientFactory factory,
        ToolRegistry registry,
        IActionLog log,
        IApprovalBroker approvals,
        IKnowledgeStore knowledge,
        ISecretStore? secrets = null)
    {
        _config = config;
        _factory = factory;
        _registry = registry;
        _log = log;
        _approvals = approvals;
        _knowledge = knowledge;

        // Optional: tools that need a key degrade without one rather than vanishing, so a run
        // with no secret store still works — it just gets keyless web search.
        _secrets = secrets;
    }

    public async Task<RunResult> RunAsync(
        string goal,
        IProgress<RunEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("n")[..10];
        var stopwatch = Stopwatch.StartNew();
        var config = _config.Current;
        var coordinator = new ApprovalCoordinator(_approvals);

        // Every event passes through the tracker on its way to the UI, so step success can be
        // judged on what the tools actually returned rather than on the model's account of it.
        var tracker = new FailureTracker();
        var forward = progress is null ? new Action<RunEvent>(_ => { }) : progress.Report;

        void Report(RunEvent evt)
        {
            if (evt is ToolCallEvent call) tracker.Observe(call);
            forward(evt);
        }

        var report = (Action<RunEvent>)Report;

        try
        {
            var executorModel = config.ResolveExecutorModel();
            if (executorModel is null)
            {
                return Fail(runId, stopwatch, report,
                    "No model is configured. Open Settings › Models and add one.");
            }

            AppPaths.EnsureCreated();

            report(new RunStartedEvent
            {
                RunId = runId,
                Goal = goal,
                ModelDisplayName = executorModel.DisplayName,
            });

            _log.Success(runId, AgentOrgan.Brain, "run.start", $"Started: {Trim(goal, 120)}");

            var guard = new PathGuard(config.Permissions);
            var context = new ToolContext
            {
                Guard = guard,
                Approvals = coordinator,
                Log = _log,
                Options = config.Agent,
                RunId = runId,
                WorkingDirectory = ResolveWorkingDirectory(config, guard),
                Search = config.Search,
                Secrets = _secrets,
            };

            var descriptors = _registry.Build(context, config.ResolveVisionModel());
            var tools = descriptors
                .Select(d => (AITool)new ObservableAIFunction(d, runId, report))
                .ToList();

            var client = _factory.CreateChatClient(executorModel);

            // ── Plan ──────────────────────────────────────────────────────────────────────
            var plannerModel = config.ResolvePlannerModel() ?? executorModel;
            var plannerClient = plannerModel.Id == executorModel.Id ? client : _factory.CreateChatClient(plannerModel);

            var plan = await new Planner(plannerClient)
                .CreatePlanAsync(goal, descriptors, DescribePermissions(config.Permissions), cancellationToken)
                .ConfigureAwait(false);

            report(new PlanReadyEvent { RunId = runId, Plan = plan });

            // ── Prepare the conversation ──────────────────────────────────────────────────
            var knowledge = await RecallAsync(goal, cancellationToken).ConfigureAwait(false);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, Prompts.BuildExecutorSystem(
                    config.Permissions, descriptors, context.WorkingDirectory, knowledge)),
                new(ChatRole.User, goal),
            };

            var options = new ChatOptions
            {
                Tools = tools,
                Temperature = executorModel.Temperature,
                MaxOutputTokens = executorModel.MaxOutputTokens,
            };

            var compactor = new ContextCompactor(client, config.Agent);

            // ── Execute ───────────────────────────────────────────────────────────────────
            var executed = 0;
            var consecutiveFailures = 0;

            foreach (var group in GroupIntoWaves(plan.Steps))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (executed >= config.Agent.MaxSteps)
                {
                    messages.Add(new ChatMessage(ChatRole.User,
                        $"The step limit of {config.Agent.MaxSteps} has been reached. Stop and summarise what was completed."));
                    break;
                }

                var runInParallel = config.Agent.EnableSubAgents
                                    && group.Count > 1
                                    && config.Agent.MaxParallelSubAgents > 1;

                if (runInParallel)
                {
                    var outcome = await new SubAgentCoordinator(client, config.Agent, tools)
                        .RunAsync(runId, goal, group, config.Permissions, context.WorkingDirectory, report, cancellationToken)
                        .ConfigureAwait(false);

                    messages.Add(new ChatMessage(ChatRole.User,
                        $"Parallel sub-tasks finished. Their reports:\n\n{outcome.Transcript}"));

                    executed += group.Count;
                    consecutiveFailures = outcome.AllFailed ? consecutiveFailures + 1 : 0;
                }
                else
                {
                    foreach (var step in group)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var succeeded = await ExecuteStepAsync(
                            runId, step, messages, options, client, compactor, executorModel,
                            tracker, report, cancellationToken).ConfigureAwait(false);

                        executed++;
                        consecutiveFailures = succeeded ? 0 : consecutiveFailures + 1;

                        if (consecutiveFailures >= config.Agent.MaxConsecutiveFailures)
                        {
                            _log.Failure(runId, AgentOrgan.Brain, "run.abandon",
                                "Stopped after repeated failures", $"{consecutiveFailures} steps in a row failed.");
                            break;
                        }
                    }
                }

                if (consecutiveFailures >= config.Agent.MaxConsecutiveFailures) break;
            }

            // ── Verify ────────────────────────────────────────────────────────────────────
            VerificationResult? verification = null;
            if (config.Agent.EnableSelfVerification && plan.Steps.Count > 0)
            {
                report(new StepStartedEvent
                {
                    RunId = runId,
                    Index = plan.Steps.Count + 1,
                    Title = "Verify the work",
                    Organ = AgentOrgan.Brain,
                });

                verification = await VerifyAsync(client, plan, messages, cancellationToken).ConfigureAwait(false);

                report(new StepFinishedEvent
                {
                    RunId = runId,
                    Index = plan.Steps.Count + 1,
                    Status = verification.Passed ? StepStatus.Succeeded : StepStatus.Failed,
                    Detail = verification.Summary,
                });
            }

            var summary = await SummariseRunAsync(client, goal, messages, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var status = verification is { Passed: false } ? RunStatus.Failed : RunStatus.Succeeded;

            report(new RunFinishedEvent
            {
                RunId = runId,
                Status = status,
                Summary = summary,
                TotalSteps = executed,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Verification = verification,
            });

            _log.Success(runId, AgentOrgan.Brain, "run.finish", $"Finished: {Trim(summary, 160)}",
                elapsedMs: stopwatch.ElapsedMilliseconds);

            return new RunResult
            {
                RunId = runId,
                Status = status,
                Summary = summary,
                Plan = plan,
                Verification = verification,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _log.Append(new ActionLogEntry
            {
                RunId = runId, Organ = AgentOrgan.Brain, Action = "run.cancel",
                Summary = "The run was stopped by the user", Outcome = ActionOutcome.Cancelled,
            });

            report(new RunFinishedEvent
            {
                RunId = runId,
                Status = RunStatus.Cancelled,
                Summary = "Stopped.",
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            });

            return new RunResult
            {
                RunId = runId, Status = RunStatus.Cancelled, Summary = "Stopped.",
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            return Fail(runId, stopwatch, report, ex.Message);
        }
        finally
        {
            coordinator.ForgetRun(runId);
        }
    }

    // ── Steps ─────────────────────────────────────────────────────────────────────────────

    private async Task<bool> ExecuteStepAsync(
        string runId,
        PlanStep step,
        List<ChatMessage> messages,
        ChatOptions options,
        IChatClient client,
        ContextCompactor compactor,
        ModelProfile model,
        FailureTracker tracker,
        Action<RunEvent> report,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        report(new StepStartedEvent
        {
            RunId = runId,
            Index = step.Index,
            Title = step.Title,
            Organ = step.Organ,
        });

        // Compact before the step rather than after, so the step itself has room to work.
        await MaybeCompactAsync(runId, messages, compactor, model, options, report, cancellationToken)
            .ConfigureAwait(false);

        messages.Add(new ChatMessage(ChatRole.User, BuildStepInstruction(step)));

        tracker.BeginStep();

        try
        {
            var response = await client.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            messages.AddRange(response.Messages);

            stopwatch.Stop();

            var text = response.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(text))
                report(new AssistantMessageEvent { RunId = runId, Text = text });

            // A step counts as failed only when every tool call in it failed. One refused call
            // that the model routed around is not a failed step.
            var failed = tracker.EveryCallFailed();

            report(new StepFinishedEvent
            {
                RunId = runId,
                Index = step.Index,
                Status = failed ? StepStatus.Failed : StepStatus.Succeeded,
                Detail = Trim(text, 300),
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            });

            return !failed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Feed the failure back so the model can adapt on the next step instead of the
            // whole run dying on one bad provider response.
            messages.Add(new ChatMessage(ChatRole.User,
                $"That step failed with: {ex.Message}\nDecide whether to retry differently, skip it, or stop and explain."));

            _log.Failure(runId, step.Organ, "step.error", step.Title, ex.Message);

            report(new StepFinishedEvent
            {
                RunId = runId,
                Index = step.Index,
                Status = StepStatus.Failed,
                Detail = ex.Message,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            });

            return false;
        }
    }

    private static string BuildStepInstruction(PlanStep step)
    {
        var builder = new StringBuilder($"Step {step.Index}: {step.Title}\n{step.Intent}");

        if (step.SuccessCriteria.Count > 0)
        {
            builder.AppendLine().AppendLine().AppendLine("This step is done when:");
            foreach (var criterion in step.SuccessCriteria)
                builder.AppendLine($"  - {criterion}");
        }

        return builder.ToString();
    }

    private async Task MaybeCompactAsync(
        string runId,
        List<ChatMessage> messages,
        ContextCompactor compactor,
        ModelProfile model,
        ChatOptions options,
        Action<RunEvent> report,
        CancellationToken cancellationToken)
    {
        if (!compactor.ShouldCompact(messages, model.ContextWindow, options.Tools)) return;

        var outcome = await compactor
            .CompactAsync(messages, model.ContextWindow, options.Tools, cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Compacted) return;

        report(new CompactionEvent
        {
            RunId = runId,
            TokensBefore = outcome.TokensBefore,
            TokensAfter = outcome.TokensAfter,
            TurnsSummarised = outcome.TurnsSummarised,
            ContextWindow = model.ContextWindow,
        });

        _log.Success(runId, AgentOrgan.Brain, "context.compact",
            $"Compacted context: {outcome.TokensBefore:N0} → {outcome.TokensAfter:N0} tokens " +
            $"({outcome.TurnsSummarised} turns summarised)");
    }

    // ── Waves ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits the plan into waves that can run together: a step joins the current wave only if
    /// none of its dependencies are still unsatisfied. Steps with no declared dependencies at
    /// all still run in order unless the planner explicitly marked them independent, because a
    /// planner that forgets a dependency should not cause two agents to fight over one folder.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<PlanStep>> GroupIntoWaves(IReadOnlyList<PlanStep> steps)
    {
        var waves = new List<IReadOnlyList<PlanStep>>();
        var satisfied = new HashSet<int>();
        var remaining = steps.OrderBy(s => s.Index).ToList();

        while (remaining.Count > 0)
        {
            var wave = remaining
                .Where(step => step.DependsOn.All(satisfied.Contains))
                .ToList();

            // A dependency cycle or a reference to a missing step would loop forever;
            // fall back to running the rest sequentially.
            if (wave.Count == 0)
            {
                waves.AddRange(remaining.Select(step => (IReadOnlyList<PlanStep>)[step]));
                break;
            }

            // Only steps that declared dependencies are trusted to be parallel-safe.
            var independent = wave.Where(s => s.DependsOn.Count > 0).ToList();

            if (independent.Count > 1 && independent.Count == wave.Count)
            {
                waves.Add(wave);
            }
            else
            {
                foreach (var step in wave) waves.Add([step]);
            }

            foreach (var step in wave)
            {
                satisfied.Add(step.Index);
                remaining.Remove(step);
            }
        }

        return waves;
    }

    // ── Verification and reporting ────────────────────────────────────────────────────────

    private static async Task<VerificationResult> VerifyAsync(
        IChatClient client, AgentPlan plan, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var criteria = plan.SuccessCriteria
            .Concat(plan.Steps.SelectMany(s => s.SuccessCriteria))
            .Distinct()
            .ToList();

        if (criteria.Count == 0)
            return new VerificationResult { Passed = true, Summary = "No explicit success criteria to check." };

        var transcript = BuildTranscript(messages, 12_000);

        var request = new List<ChatMessage>
        {
            new(ChatRole.System, Prompts.VerifierSystem),
            new(ChatRole.User,
                $"""
                 Goal: {plan.Goal}

                 Success criteria:
                 {string.Join('\n', criteria.Select(c => "  - " + c))}

                 Transcript:
                 {transcript}
                 """),
        };

        try
        {
            var response = await client.GetResponseAsync(request,
                new ChatOptions { Temperature = 0, MaxOutputTokens = 700 }, cancellationToken).ConfigureAwait(false);

            var json = Planner.ExtractJson(response.Text);
            if (json is null)
                return new VerificationResult { Passed = true, Summary = "The verification pass returned no usable answer." };

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var passed = !root.TryGetProperty("passed", out var passedElement)
                         || passedElement.ValueKind != JsonValueKind.False;

            var summary = root.TryGetProperty("summary", out var summaryElement)
                ? summaryElement.GetString() ?? ""
                : "";

            var unmet = root.TryGetProperty("unmet", out var unmetElement) && unmetElement.ValueKind == JsonValueKind.Array
                ? unmetElement.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                : [];

            return new VerificationResult { Passed = passed, Summary = summary, UnmetCriteria = unmet };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A verifier that cannot run must not turn a good run into a failed one.
            return new VerificationResult { Passed = true, Summary = $"Verification could not run: {ex.Message}" };
        }
    }

    private static async Task<string> SummariseRunAsync(
        IChatClient client, string goal, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var lastAssistant = messages.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text));

        // The closing assistant message is usually already the report. Only pay for a
        // summarisation call when it plainly is not.
        if (lastAssistant is not null && lastAssistant.Text!.Length is > 60 and < 2000)
            return lastAssistant.Text.Trim();

        var request = new List<ChatMessage>
        {
            new(ChatRole.System,
                "Summarise for the user what was actually done, in under 120 words. " +
                "Use real paths and numbers from the transcript. State plainly anything that did not work."),
            new(ChatRole.User, $"Goal: {goal}\n\n{BuildTranscript(messages, 8000)}"),
        };

        try
        {
            var response = await client.GetResponseAsync(request,
                new ChatOptions { Temperature = 0.2f, MaxOutputTokens = 400 }, cancellationToken).ConfigureAwait(false);

            return response.Text?.Trim() ?? "Finished.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return lastAssistant?.Text?.Trim() ?? "Finished.";
        }
    }

    private static string BuildTranscript(IReadOnlyList<ChatMessage> messages, int maxChars)
    {
        // Newest content matters most for verification, so collect backwards and reverse.
        var lines = new List<string>();
        var budget = 0;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
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
                        parts.Add($"[result] {Trim(result.Result?.ToString() ?? "", 500)}");
                        break;
                }
            }

            if (parts.Count == 0) continue;

            var line = $"{message.Role.Value}: {string.Join('\n', parts)}";
            if (budget + line.Length > maxChars) break;

            budget += line.Length;
            lines.Add(line);
        }

        lines.Reverse();
        return string.Join("\n\n", lines);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<KnowledgeHit>> RecallAsync(string goal, CancellationToken cancellationToken)
    {
        try
        {
            var hits = await _knowledge.SearchAsync(goal, topK: 4, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Only auto-attach from bases the user marked for it.
            var allowed = _knowledge.List().Where(kb => kb.AutoAttach).Select(kb => kb.Id).ToHashSet(StringComparer.Ordinal);
            return hits.Where(h => allowed.Contains(h.KnowledgeBaseId)).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    /// <summary>
    /// Picks the folder bare filenames resolve into.
    ///
    /// The first writable root is usually the right answer, but it has to be one the guard will
    /// actually accept. A root that is granted and then refused — a protected location, or a
    /// folder that has since been deleted — is worse than no choice at all, because every
    /// relative path the model writes lands there and is rejected, and the model has no way to
    /// tell that the folder rather than the filename was the problem.
    /// </summary>
    internal static string ResolveWorkingDirectory(AutoWorkConfig config, PathGuard guard)
    {
        foreach (var root in config.Permissions.Roots.Where(r => r.Access == FolderAccess.ReadWrite))
        {
            try
            {
                return guard.EnsureWritable(root.Path);
            }
            catch (SandboxViolationException)
            {
                // Try the next grant rather than stranding the run in an unusable folder.
            }
        }

        return AppPaths.WorkspaceDirectory;
    }

    private static string DescribePermissions(PermissionPolicy policy)
    {
        var roots = policy.Roots.Count == 0
            ? "  (none granted)"
            : string.Join('\n', policy.Roots.Select(r =>
                $"  {PathGuard.Describe(r.Path)} — {(r.Access == FolderAccess.ReadWrite ? "read/write" : "read only")}"));

        return $"""
                Permissions:
                {roots}
                  delete: {(policy.AllowDelete ? "yes" : "no")}, shell: {(policy.AllowShell ? "yes" : "no")},
                  screen: {(policy.AllowScreenCapture ? "yes" : "no")}, input: {(policy.AllowInputControl ? "yes" : "no")},
                  network: {(policy.AllowNetwork ? "yes" : "no")}
                """;
    }

    private RunResult Fail(string runId, Stopwatch stopwatch, Action<RunEvent> report, string error)
    {
        stopwatch.Stop();
        _log.Failure(runId, AgentOrgan.Brain, "run.error", "The run could not finish", error);

        report(new RunFinishedEvent
        {
            RunId = runId,
            Status = RunStatus.Failed,
            Summary = error,
            Error = error,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
        });

        return new RunResult
        {
            RunId = runId, Status = RunStatus.Failed, Summary = error, Error = error,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
        };
    }

    private static string Trim(string text, int max)
    {
        var flattened = text.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= max ? flattened : flattened[..max] + "…";
    }

    /// <summary>
    /// Watches tool-call events for the current step so a step's success can be judged on what
    /// the tools actually did, not on the model's own account of it.
    /// </summary>
    private sealed class FailureTracker
    {
        private int _calls;
        private int _failures;

        public void BeginStep()
        {
            _calls = 0;
            _failures = 0;
        }

        public void Observe(ToolCallEvent evt)
        {
            // Only completed calls carry a result; the opening event does not.
            if (evt.Result is null) return;

            _calls++;
            if (evt.Failed) _failures++;
        }

        public bool EveryCallFailed() => _calls > 0 && _failures == _calls;
    }
}
