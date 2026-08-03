using System.Runtime.CompilerServices;
using AutoWork.Agents;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Logging;
using AutoWork.Core.Runs;
using AutoWork.Core.Security;
using AutoWork.Providers;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// The controller on its own: it is a small piece of concurrency, and the failure mode — a run
/// that pauses and then never wakes up — is the kind that only shows up in front of a user.
/// </summary>
public sealed class RunControllerTests
{
    [Fact]
    public async Task Waiting_while_not_paused_returns_at_once()
    {
        var controller = new RunController();

        await controller.WaitIfPausedAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task A_paused_run_waits_until_it_is_resumed()
    {
        var controller = new RunController();
        controller.Pause();

        var wait = controller.WaitIfPausedAsync(TestContext.Current.CancellationToken);

        Assert.False(wait.IsCompleted);

        controller.Resume();

        await wait.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public void A_correction_is_handed_over_once_and_then_forgotten()
    {
        var controller = new RunController();
        controller.Pause();
        controller.Resume("use the other folder");

        Assert.Equal("use the other folder", controller.TakeCorrection());
        Assert.Null(controller.TakeCorrection());
    }

    [Fact]
    public void Steering_without_pausing_still_queues_the_correction()
    {
        var controller = new RunController();
        controller.Steer("  skip the PDFs  ");

        Assert.False(controller.IsPaused);
        Assert.Equal("skip the PDFs", controller.TakeCorrection());
    }

    [Fact]
    public void Corrections_typed_more_than_once_are_all_carried_over()
    {
        var controller = new RunController();

        controller.Steer("skip the PDFs");
        controller.Pause();
        controller.Resume("and use British spelling");

        Assert.Equal("skip the PDFs\nand use British spelling", controller.TakeCorrection());
    }

    [Fact]
    public void An_empty_correction_is_not_treated_as_one()
    {
        var controller = new RunController();
        controller.Steer("   ");
        controller.Pause();
        controller.Resume("  ");

        Assert.Null(controller.TakeCorrection());
    }

    /// <summary>Stopping a paused run must release it, or the agent thread never notices.</summary>
    [Fact]
    public async Task Abandoning_releases_a_run_that_is_parked()
    {
        var controller = new RunController();
        controller.Pause();

        var wait = controller.WaitIfPausedAsync(TestContext.Current.CancellationToken);
        controller.Abandon();

        await wait.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task Cancelling_releases_a_run_that_is_parked()
    {
        var controller = new RunController();
        controller.Pause();

        using var cancellation = new CancellationTokenSource();
        var wait = controller.WaitIfPausedAsync(cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}

/// <summary>
/// Pause, steer and run history through the real orchestrator, with a scripted transport in
/// place of the provider socket. Everything above the socket — parameter repair, the function
/// invocation loop, the step loop — runs for real; only the network is replaced.
/// </summary>
public sealed class OrchestratorPauseAndHistoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "autowork-pause", Guid.NewGuid().ToString("n")[..8]);

    private readonly string _workspace;

    public OrchestratorPauseAndHistoryTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private const string TwoStepPlan =
        """
        {"notes":"Two steps.","successCriteria":[],
         "steps":[{"index":1,"title":"First","intent":"Do the first thing","organ":"Hands","dependsOn":[],"successCriteria":[]},
                  {"index":2,"title":"Second","intent":"Do the second thing","organ":"Hands","dependsOn":[],"successCriteria":[]}]}
        """;

    private (AgentOrchestrator Orchestrator, ScriptedChatClient Client, FileRunHistoryStore History) Build(
        bool keepHistory = true)
    {
        var client = new ScriptedChatClient(TwoStepPlan);

        var secrets = new FileSecretStore(Path.Combine(_root, "secrets.json"));

        // The transport stands in for the socket, so no credential is ever reached for.
        var factory = new ModelClientFactory(secrets) { Transport = _ => client };

        var config = new ConfigStore(Path.Combine(_root, "config.json"));
        config.Save(new AutoWorkConfig
        {
            KeepRunHistory = keepHistory,
            RunHistoryRetentionDays = 30,
            Models =
            [
                new ModelProfile
                {
                    Id = "test", DisplayName = "Scripted", Kind = ProviderKind.OpenAICompatible,
                    Endpoint = "https://example.invalid/v1", ModelId = "scripted", ContextWindow = 100_000,
                },
            ],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowNetwork = false,
            },
            Agent = new AgentOptions
            {
                PlannerModelId = "test",
                ExecutorModelId = "test",
                MaxSteps = 6,
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
            secrets, skills: null, history: history);

        return (orchestrator, client, history);
    }

    [Fact]
    public async Task A_correction_added_mid_run_reaches_the_model_before_the_next_step()
    {
        var (orchestrator, client, _) = Build();
        var controller = new RunController();
        var events = new List<RunEvent>();

        // Pause the moment the first step finishes, then immediately resume with a correction —
        // the same sequence a user produces, minus the thinking time.
        var progress = new InlineProgress<RunEvent>(evt =>
        {
            lock (events) events.Add(evt);

            switch (evt)
            {
                case StepFinishedEvent { Index: 1 }:
                    controller.Pause();
                    break;
                case RunPausedEvent:
                    controller.Resume("Actually, put everything in the archive folder instead.");
                    break;
            }
        });

        var result = await orchestrator.RunAsync("do two things", progress,
            TestContext.Current.CancellationToken, controller);

        Assert.NotEqual(RunStatus.Failed, result.Status);

        RunEvent[] seen;
        lock (events) seen = [.. events];

        var paused = Assert.Single(seen.OfType<RunPausedEvent>());
        Assert.Equal(2, paused.BeforeStep);

        var resumed = Assert.Single(seen.OfType<RunResumedEvent>());
        Assert.Equal("Actually, put everything in the archive folder instead.", resumed.Correction);

        // The claim that matters: the model was actually told, before it was asked for step 2.
        var stepTwo = client.Requests.First(r => r.Any(m => m.Text?.Contains("Step 2", StringComparison.Ordinal) == true));

        Assert.Contains(stepTwo, m => m.Text?.Contains("archive folder", StringComparison.Ordinal) == true);

        // And it arrived before the step instruction rather than trailing it.
        var correctionAt = stepTwo.FindIndex(m => m.Text?.Contains("archive folder", StringComparison.Ordinal) == true);
        var instructionAt = stepTwo.FindIndex(m => m.Text?.Contains("Step 2", StringComparison.Ordinal) == true);

        Assert.True(correctionAt < instructionAt,
            "the correction has to be in the conversation before the step it is meant to change");
    }

    /// <summary>
    /// The pause has to actually hold the run, not merely report that it did. Without the wait,
    /// every other assertion here would still pass while the agent carried straight on.
    /// </summary>
    [Fact]
    public async Task A_paused_run_does_not_start_the_next_step_until_it_is_resumed()
    {
        var (orchestrator, client, _) = Build();
        var controller = new RunController();

        var pausedAtBoundary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var progress = new InlineProgress<RunEvent>(evt =>
        {
            if (evt is StepFinishedEvent { Index: 1 }) controller.Pause();
            if (evt is RunPausedEvent) pausedAtBoundary.TrySetResult();
        });

        var run = orchestrator.RunAsync("do two things", progress,
            TestContext.Current.CancellationToken, controller);

        await pausedAtBoundary.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        // Long enough that a run which ignored the pause would have finished: every reply here is
        // returned synchronously with no network in the way.
        await Task.Delay(400, TestContext.Current.CancellationToken);

        Assert.False(run.IsCompleted, "the run carried on past a pause");
        Assert.DoesNotContain(client.Requests, r => r.Any(m => m.Text?.Contains("Step 2", StringComparison.Ordinal) == true));

        controller.Resume();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.NotEqual(RunStatus.Failed, result.Status);
        Assert.Contains(client.Requests, r => r.Any(m => m.Text?.Contains("Step 2", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task A_finished_run_can_be_reopened_from_history()
    {
        var (orchestrator, _, history) = Build();

        var result = await orchestrator.RunAsync("tidy the workspace", progress: null,
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(history.List());

        Assert.Equal(result.RunId, entry.Id);
        Assert.Equal("tidy the workspace", entry.Goal);
        Assert.Equal("Scripted", entry.ModelDisplayName);

        var transcript = history.Load(result.RunId);
        Assert.NotNull(transcript);

        // The tape can be rebuilt: the plan and both steps are there, in order.
        Assert.Single(transcript.Events.OfType<RunStartedEvent>());
        Assert.Single(transcript.Events.OfType<PlanReadyEvent>());
        Assert.Equal([1, 2], transcript.Events.OfType<StepStartedEvent>().Select(e => e.Index).ToArray());
        Assert.Single(transcript.Events.OfType<RunFinishedEvent>());
    }

    [Fact]
    public async Task Turning_run_history_off_records_nothing()
    {
        var (orchestrator, _, history) = Build(keepHistory: false);

        await orchestrator.RunAsync("tidy the workspace", progress: null, TestContext.Current.CancellationToken);

        Assert.Empty(history.List());
    }

    /// <summary>
    /// A stopped run is exactly the one a user wants to look back at, so the transcript has to
    /// survive the cancellation that ended it.
    /// </summary>
    [Fact]
    public async Task A_cancelled_run_is_still_written_to_history()
    {
        var (orchestrator, _, history) = Build();

        using var cancellation = new CancellationTokenSource();

        var progress = new InlineProgress<RunEvent>(evt =>
        {
            if (evt is StepFinishedEvent { Index: 1 }) cancellation.Cancel();
        });

        var result = await orchestrator.RunAsync("do two things", progress, cancellation.Token);

        Assert.Equal(RunStatus.Cancelled, result.Status);

        var entry = Assert.Single(history.List());
        Assert.Equal(RunStatus.Cancelled, entry.Status);
        Assert.Contains(history.Load(entry.Id)!.Events, e => e is StepStartedEvent { Index: 1 });
    }

    /// <summary>
    /// Reports on the calling thread.
    ///
    /// <see cref="Progress{T}"/> hands the callback off to be run later, which is right for the
    /// UI and useless here: a test that pauses the run in reaction to an event needs the pause to
    /// be in place before the orchestrator reaches its next boundary, and a deferred callback
    /// loses that race about half the time.
    /// </summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }

    /// <summary>
    /// Replies to order, and keeps every request it was given so a test can ask what the model
    /// was actually told.
    /// </summary>
    private sealed class ScriptedChatClient : IChatClient
    {
        private readonly string _plan;
        private readonly Lock _gate = new();
        private int _calls;

        public ScriptedChatClient(string plan) => _plan = plan;

        public List<List<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int call;
            lock (_gate)
            {
                Requests.Add([.. messages]);
                call = ++_calls;
            }

            // The planner always goes first, and its answer has to be a plan or there is no run.
            var reply = call == 1 ? _plan : "Done. Nothing further needed for that step.";

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
