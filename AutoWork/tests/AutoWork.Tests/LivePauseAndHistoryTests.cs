using AutoWork.Agents;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Logging;
using AutoWork.Core.Runs;
using AutoWork.Core.Security;

namespace AutoWork.Tests;

/// <summary>
/// Pause, steer and run history against a real model.
///
/// The scripted-transport tests prove the correction reaches the conversation. What they cannot
/// prove is that a real model, mid-run, actually changes what it does because of it — which is
/// the entire point of the feature. So this run is set up so that obeying and ignoring the
/// correction leave different files on disk, and the assertion is on the files.
/// </summary>
public sealed class LivePauseAndHistoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "autowork-live-pause", Guid.NewGuid().ToString("n")[..8]);

    private readonly string _workspace;

    public LivePauseAndHistoryTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("AUTOWORK_LIVE_KEEP_OUTPUT") == "1") return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// Reports on the calling thread, so the pause is in place before the orchestrator reaches
    /// its next step boundary. <see cref="Progress{T}"/> defers the callback and loses that race.
    /// </summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public async Task A_correction_added_mid_run_changes_what_the_model_does_next()
    {
        var (factory, profile) = LiveModel.Require();

        var config = new ConfigStore(Path.Combine(_root, "config.json"));
        config.Save(new AutoWorkConfig
        {
            KeepRunHistory = true,
            RunHistoryRetentionDays = 30,
            Models = [profile],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowNetwork = false,
            },
            Agent = new AgentOptions
            {
                PlannerModelId = profile.Id,
                ExecutorModelId = profile.Id,
                MaxSteps = 8,
                EnableSubAgents = false,
                EnableSelfVerification = false,
                EnableAutoCompact = false,
            },
        });

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));
        var history = new FileRunHistoryStore(Path.Combine(_root, "runs"));

        var orchestrator = new AgentOrchestrator(
            config, factory, new ToolRegistry(factory, knowledge),
            NullActionLog.Instance, new AutoApproveBroker(), knowledge,
            skills: null, secrets: null, history: history);

        var controller = new RunController();
        var events = new List<RunEvent>();

        // Paused after the first step, then corrected: the remaining files are asked for under
        // different names than the goal specified.
        var progress = new InlineProgress<RunEvent>(evt =>
        {
            lock (events) events.Add(evt);

            switch (evt)
            {
                case StepFinishedEvent { Index: 1 }:
                    controller.Pause();
                    break;

                case RunPausedEvent:
                    controller.Resume(
                        "Change of plan: for every file you have not written yet, use the prefix " +
                        "'final-' instead of 'draft-'. Keep everything else the same.");
                    break;
            }
        });

        var result = await orchestrator.RunAsync(
            $"""
             Work in {_workspace}. Do this as three separate steps, one file per step, in order:
             step 1, create draft-one.txt containing the word ONE;
             step 2, create draft-two.txt containing the word TWO;
             step 3, create draft-three.txt containing the word THREE.
             Do not combine the steps and do not create any other files.
             """,
            progress, TestContext.Current.CancellationToken, controller);

        RunEvent[] seen;
        lock (events) seen = [.. events];

        Assert.NotEmpty(seen.OfType<RunPausedEvent>());

        var resumed = Assert.Single(seen.OfType<RunResumedEvent>());
        Assert.Contains("final-", resumed.Correction);

        var written = Directory.GetFiles(_workspace).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        var present = string.Join(", ", written);

        // Step 1 ran before the correction, so its file keeps the original name.
        Assert.Contains("draft-one.txt", written);

        // The claim: the model changed course. At least one file after the pause carries the new
        // prefix, and nothing after step 1 kept the old one.
        Assert.True(written.Any(n => n!.StartsWith("final-", StringComparison.OrdinalIgnoreCase)),
            $"the correction was delivered but nothing changed. Files: {present}. {result.Summary}");

        Assert.DoesNotContain("draft-two.txt", written);
        Assert.DoesNotContain("draft-three.txt", written);
    }

    /// <summary>
    /// Streaming against a real provider, with tools in play.
    ///
    /// The part no fake can settle: whether a provider that is streaming <em>and</em> being asked
    /// to call functions still produces a usable reply. Fragments arriving is only half of it —
    /// the tool call has to work too, which is why this run writes a file.
    /// </summary>
    [Fact]
    public async Task A_streamed_step_still_calls_tools_and_finishes()
    {
        var (factory, profile) = LiveModel.Require();

        var config = new ConfigStore(Path.Combine(_root, "config.json"));
        config.Save(new AutoWorkConfig
        {
            KeepRunHistory = false,
            Models = [profile],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowNetwork = false,
            },
            Agent = new AgentOptions
            {
                PlannerModelId = profile.Id,
                ExecutorModelId = profile.Id,
                MaxSteps = 4,
                EnableSubAgents = false,
                EnableSelfVerification = false,
                EnableAutoCompact = false,
                EnableStreaming = true,
            },
        });

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));

        var orchestrator = new AgentOrchestrator(
            config, factory, new ToolRegistry(factory, knowledge),
            NullActionLog.Instance, new AutoApproveBroker(), knowledge);

        var events = new List<RunEvent>();

        var result = await orchestrator.RunAsync(
            $"Write three sentences about tidy filing into filing.txt in {_workspace}, then read the file back.",
            new InlineProgress<RunEvent>(e => { lock (events) events.Add(e); }),
            TestContext.Current.CancellationToken);

        RunEvent[] seen;
        lock (events) seen = [.. events];

        var deltas = seen.OfType<AssistantDeltaEvent>().ToArray();

        Assert.True(deltas.Length > 1,
            $"the reply did not arrive in fragments ({deltas.Length}), so nothing streamed. {result.Summary}");

        // Running totals only ever grow, which is what lets the UI assign rather than append.
        // Per step, not per run: each step is its own reply and its own row on the tape, which
        // is exactly why the event carries the step index.
        foreach (var step in deltas.GroupBy(d => d.StepIndex))
        {
            var inStep = step.ToArray();

            for (var i = 1; i < inStep.Length; i++)
            {
                Assert.StartsWith(inStep[i - 1].Text, inStep[i].Text, StringComparison.Ordinal);
            }
        }

        // And the tools still ran underneath the stream.
        Assert.Contains(seen.OfType<ToolCallEvent>(), e => e.Tool.StartsWith("files_", StringComparison.Ordinal));

        var written = Path.Combine(_workspace, "filing.txt");
        Assert.True(File.Exists(written), $"nothing was written. {result.Summary}");
        Assert.NotEqual(RunStatus.Failed, result.Status);
    }

    /// <summary>
    /// The meter against a real provider.
    ///
    /// Everything else about it is unit-tested, but the one thing no unit test can establish is
    /// whether a real provider actually reports usage at all — and a meter that silently reports
    /// zero is worse than no meter.
    /// </summary>
    [Fact]
    public async Task A_real_run_reports_the_tokens_the_provider_says_it_used()
    {
        var (factory, profile) = LiveModel.Require();

        // A price the test owns, so the arithmetic can be checked without depending on what any
        // vendor charges today.
        profile.InputPricePerMillion = 1m;
        profile.OutputPricePerMillion = 2m;
        profile.Currency = "USD";

        var config = new ConfigStore(Path.Combine(_root, "config.json"));
        config.Save(new AutoWorkConfig
        {
            KeepRunHistory = true,
            Models = [profile],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowNetwork = false,
            },
            Agent = new AgentOptions
            {
                PlannerModelId = profile.Id,
                ExecutorModelId = profile.Id,
                MaxSteps = 4,
                EnableSubAgents = false,
                EnableSelfVerification = false,
                EnableAutoCompact = false,
            },
        });

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));
        var history = new FileRunHistoryStore(Path.Combine(_root, "runs"));

        var orchestrator = new AgentOrchestrator(
            config, factory, new ToolRegistry(factory, knowledge),
            NullActionLog.Instance, new AutoApproveBroker(), knowledge,
            skills: null, secrets: null, history: history);

        var events = new List<RunEvent>();

        var result = await orchestrator.RunAsync(
            $"Write a two-line poem about tidy folders into poem.txt in {_workspace}.",
            new InlineProgress<RunEvent>(e => { lock (events) events.Add(e); }),
            TestContext.Current.CancellationToken);

        RunEvent[] seen;
        lock (events) seen = [.. events];

        var usage = seen.OfType<UsageEvent>().LastOrDefault();

        Assert.True(usage is not null,
            $"the run reported no usage at all. Status: {result.Status}. {result.Summary}");

        Assert.True(usage!.InputTokens > 0, "the provider reported no input tokens");
        Assert.True(usage.OutputTokens > 0, "the provider reported no output tokens");

        // Several provider round trips happen inside one step once tools are called; a meter
        // that counted only the outermost call would report far fewer.
        Assert.True(usage.Calls >= 2, $"only {usage.Calls} calls were counted for a whole run");

        Assert.Equal(0, usage.CallsWithoutUsage);

        var expected = usage.InputTokens / 1_000_000m * 1m + usage.OutputTokens / 1_000_000m * 2m;
        Assert.Equal(expected, usage.Cost);

        // And it survives into the run's saved record, which is where a user goes to look later.
        var entry = Assert.Single(history.List());

        Assert.Equal(usage.InputTokens, entry.InputTokens);
        Assert.Equal(usage.OutputTokens, entry.OutputTokens);
        Assert.Equal(usage.Cost, entry.Cost);
        Assert.True(entry.UsageComplete);
    }

    /// <summary>
    /// A real run's transcript, reloaded. The unit tests use hand-built events; this one uses
    /// whatever a live model actually produced, which is where an unregistered event type or an
    /// oversized tool result would show up.
    /// </summary>
    [Fact]
    public async Task A_real_run_is_written_to_history_and_reads_back_intact()
    {
        var (factory, profile) = LiveModel.Require();

        var config = new ConfigStore(Path.Combine(_root, "config.json"));
        config.Save(new AutoWorkConfig
        {
            KeepRunHistory = true,
            RunHistoryRetentionDays = 30,
            Models = [profile],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowNetwork = false,
            },
            Agent = new AgentOptions
            {
                PlannerModelId = profile.Id,
                ExecutorModelId = profile.Id,
                MaxSteps = 6,
                EnableSubAgents = false,
                EnableSelfVerification = true,
                EnableAutoCompact = false,
            },
        });

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));
        var history = new FileRunHistoryStore(Path.Combine(_root, "runs"));

        var orchestrator = new AgentOrchestrator(
            config, factory, new ToolRegistry(factory, knowledge),
            NullActionLog.Instance, new AutoApproveBroker(), knowledge,
            skills: null, secrets: null, history: history);

        var live = new List<RunEvent>();

        var result = await orchestrator.RunAsync(
            $"Create a file called notes.txt in {_workspace} containing three short lines about tidy filing, then read it back.",
            new InlineProgress<RunEvent>(e => { lock (live) live.Add(e); }),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(history.List());
        Assert.Equal(result.RunId, entry.Id);
        Assert.Equal(result.Status, entry.Status);
        Assert.True(entry.ToolCalls > 0, "the run made no tool calls, so this proves nothing about the transcript");

        var transcript = history.Load(result.RunId);
        Assert.NotNull(transcript);

        RunEvent[] asItHappened;
        lock (live) asItHappened = [.. live];

        // Every event the run emitted survived the round trip, in order and as the same type.
        Assert.Equal(
            asItHappened.Select(e => e.GetType()).ToArray(),
            transcript.Events.Select(e => e.GetType()).ToArray());

        var plan = Assert.Single(transcript.Events.OfType<PlanReadyEvent>());
        Assert.NotEmpty(plan.Plan.Steps);

        var calls = transcript.Events.OfType<ToolCallEvent>().Where(e => e.Result is not null).ToArray();
        Assert.NotEmpty(calls);
        Assert.All(calls, c => Assert.False(string.IsNullOrWhiteSpace(c.Tool)));
    }
}
