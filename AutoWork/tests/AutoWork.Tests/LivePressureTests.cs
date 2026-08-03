using AutoWork.Agents;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Providers;

namespace AutoWork.Tests;

/// <summary>
/// The two parts of the agent loop that unit tests can only approximate: compaction actually
/// firing mid-run, and sub-agents actually running in parallel against a real provider.
///
/// `ContextCompactionTests` proves the cut point is never illegal. What it cannot prove is that a
/// provider accepts the conversation afterwards — and a rejected conversation is exactly how this
/// bug class shows up, at the worst moment, on the longest run.
/// </summary>
public sealed class LiveCompactionTests : IDisposable
{
    /// <summary>
    /// A stable copy of the events seen so far. `Progress&lt;T&gt;` delivers on another thread, so a
    /// callback can still arrive while the assertions enumerate — which is exactly how one live
    /// run failed with "Collection was modified".
    /// </summary>
    private static RunEvent[] Snapshot(List<RunEvent> events)
    {
        lock (events) return [.. events];
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "autowork-live-compaction", Guid.NewGuid().ToString("n")[..8]);
    private readonly string _workspace;

    public LiveCompactionTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Compaction_fires_mid_run_and_the_provider_still_accepts_the_conversation()
    {
        var (factory, profile) = LiveModel.Require();

        // Bulk for the run to read, so the conversation grows from real tool output rather than
        // from a contrived prompt.
        for (var i = 1; i <= 4; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_workspace, $"note{i}.txt"),
                string.Join('\n', Enumerable.Range(1, 120).Select(n => $"Note {i} line {n}: the quarterly figure for item {n} was {n * i * 37}.")),
                TestContext.Current.CancellationToken);
        }

        // Compaction is checked *between steps*, never inside one. That is the whole reason two
        // earlier attempts at this test failed to trigger it: the model happily read all four
        // files inside a single step, so nothing ever accumulated across a boundary. The goal
        // below therefore spells out one file per step, and the window is squeezed hard enough
        // that a couple of steps' worth of file content crosses the threshold.
        var squeezed = new ModelProfile
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            Preset = profile.Preset,
            Kind = profile.Kind,
            Endpoint = profile.Endpoint,
            ModelId = profile.ModelId,
            ApiKeyRef = profile.ApiKeyRef,
            ContextWindow = 5_000,
            MaxOutputTokens = profile.MaxOutputTokens,
            Capabilities = profile.Capabilities,
        };

        var config = new ConfigStore(Path.Combine(_root, "config.json"));
        config.Save(new AutoWorkConfig
        {
            Models = [squeezed],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowNetwork = false,
            },
            Agent = new AgentOptions
            {
                PlannerModelId = squeezed.Id,
                ExecutorModelId = squeezed.Id,
                MaxSteps = 8,
                EnableSubAgents = false,
                EnableSelfVerification = false,
                EnableAutoCompact = true,
                AutoCompactThreshold = 0.4,
                CompactKeepRecentTurns = 2,
            },
        });

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));

        var orchestrator = new AgentOrchestrator(
            config, factory, new ToolRegistry(factory, knowledge),
            NullActionLog.Instance, new AutoApproveBroker(), knowledge);

        var events = new List<RunEvent>();

        var result = await orchestrator.RunAsync(
            $"""
             Work through this as five separate steps, one per step, in this order:
             step 1, read {Path.Combine(_workspace, "note1.txt")} in full;
             step 2, read note2.txt in full;
             step 3, read note3.txt in full;
             step 4, read note4.txt in full;
             step 5, write a file called total.txt containing only the number of files you read.
             Do not combine the steps.
             """,
            new Progress<RunEvent>(e => { lock (events) events.Add(e); }), TestContext.Current.CancellationToken);

        var compactions = Snapshot(events).OfType<CompactionEvent>().ToArray();

        var steps = Snapshot(events).OfType<StepStartedEvent>().Count();

        Assert.True(compactions.Length > 0,
            $"No compaction happened, so this run did not test it. Steps run: {steps}. " +
            $"Status: {result.Status}. {result.Summary}");

        // Compaction has to make room, not merely run.
        Assert.All(compactions, c => Assert.True(c.TokensAfter < c.TokensBefore,
            $"compaction did not shrink the context: {c.TokensBefore} → {c.TokensAfter}"));

        // The real claim: the provider kept accepting the conversation afterwards. A split
        // tool-call pair would have ended the run with an error instead.
        Assert.NotEqual(RunStatus.Failed, result.Status);

        var total = Path.Combine(_workspace, "total.txt");
        Assert.True(File.Exists(total),
            $"the run did not finish its work after compacting. {result.Summary}");
    }
}

/// <summary>
/// Sub-agents against a real provider. The wave scheduler is unit-tested, but nothing had ever
/// confirmed that several agents talking to a live model concurrently produce usable reports.
/// </summary>
public sealed class LiveSubAgentTests : IDisposable
{
    /// <summary>
    /// A stable copy of the events seen so far. `Progress&lt;T&gt;` delivers on another thread, so a
    /// callback can still arrive while the assertions enumerate — which is exactly how one live
    /// run failed with "Collection was modified".
    /// </summary>
    private static RunEvent[] Snapshot(List<RunEvent> events)
    {
        lock (events) return [.. events];
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "autowork-live-subagents", Guid.NewGuid().ToString("n")[..8]);
    private readonly string _workspace;

    public LiveSubAgentTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// The coordinator is driven directly rather than through the planner. Whether a planner
    /// chooses to mark steps independent is its own question; what was unproven is whether the
    /// parallel machinery works at all against a live model, and a hand-built wave answers that
    /// without depending on the planner's mood.
    /// </summary>
    [Fact]
    public async Task Independent_steps_run_concurrently_and_each_reports_back()
    {
        var (factory, profile) = LiveModel.Require();

        var policy = new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            AllowNetwork = false,
        };

        var context = new ToolContext
        {
            Guard = new PathGuard(policy),
            Approvals = new AutoApproveBroker(),
            Log = NullActionLog.Instance,
            Options = new AgentOptions { ToolTimeoutSeconds = 120 },
            RunId = "subagents",
            WorkingDirectory = _workspace,
        };

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));

        var events = new List<RunEvent>();

        // Wrapped the way the orchestrator wraps them, so this also shows that a sub-agent's tool
        // calls are attributed to the parent run rather than disappearing into the sub-agent.
        var tools = new ToolRegistry(factory, knowledge)
            .Build(context, visionModel: null)
            .Select(d => (Microsoft.Extensions.AI.AITool)new ObservableAIFunction(d, "subagents", e =>
            {
                lock (events) events.Add(e);
            }))
            .ToList();

        PlanStep[] wave =
        [
            new() { Index = 1, Title = "Write alpha", Intent = $"Create alpha.txt in {_workspace} containing exactly the word ALPHA.", DependsOn = [0] },
            new() { Index = 2, Title = "Write beta", Intent = $"Create beta.txt in {_workspace} containing exactly the word BETA.", DependsOn = [0] },
            new() { Index = 3, Title = "Write gamma", Intent = $"Create gamma.txt in {_workspace} containing exactly the word GAMMA.", DependsOn = [0] },
        ];

        var outcome = await new SubAgentCoordinator(
                factory.CreateChatClient(profile),
                new AgentOptions { MaxParallelSubAgents = 3, EnableSubAgents = true },
                tools)
            .RunAsync("subagents", "Create three files", wave, policy, _workspace,
                e => { lock (events) events.Add(e); }, TestContext.Current.CancellationToken);

        Assert.False(outcome.AllFailed, $"every sub-agent failed. Transcript:\n{outcome.Transcript}");

        // Each sub-agent had its own job; all three artefacts have to exist.
        foreach (var (name, content) in new[] { ("alpha.txt", "ALPHA"), ("beta.txt", "BETA"), ("gamma.txt", "GAMMA") })
        {
            var path = Path.Combine(_workspace, name);

            Assert.True(File.Exists(path),
                $"{name} was not created. Files present: " +
                $"{string.Join(", ", Directory.GetFiles(_workspace).Select(Path.GetFileName))}\n{outcome.Transcript}");

            Assert.Contains(content, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken),
                StringComparison.OrdinalIgnoreCase);
        }

        // And their work showed up as tool calls attributed to the run, not silently.
        Assert.Contains(Snapshot(events).OfType<ToolCallEvent>(), e => e.Tool.StartsWith("files_", StringComparison.Ordinal));
    }
}
