using AutoWork.Agents;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;

namespace AutoWork.Tests;

/// <summary>
/// What a real planner actually emits.
///
/// `WaveSchedulingTests` proves the scheduler groups correctly given a plan. What was never
/// measured is whether a live planner produces the `dependsOn` that makes grouping possible —
/// and if it never does, the whole parallel path is dead code however well it is tested.
/// </summary>
public sealed class LivePlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "autowork-live-planner", Guid.NewGuid().ToString("n")[..8]);

    public LivePlannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// A goal whose middle is obviously three independent pieces of work over one shared input.
    /// If a planner ever declares dependencies, it is here.
    /// </summary>
    [Fact]
    public async Task A_live_planner_declares_dependencies_between_steps()
    {
        var (factory, profile) = LiveModel.Require();

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));

        var context = new ToolContext
        {
            Guard = new PathGuard(new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _root, Access = FolderAccess.ReadWrite }],
            }),
            Approvals = new AutoApproveBroker(),
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "planner",
            WorkingDirectory = _root,
        };

        var descriptors = new ToolRegistry(factory, knowledge).Build(context, visionModel: null);

        var plan = await new Planner(factory.CreateChatClient(profile)).CreatePlanAsync(
            """
            First read sales.csv. Then, working from it and independently of one another, produce
            three separate outputs: a summary.md, a chart-data.csv, and a totals.txt. Finally,
            write an index.md linking all three.
            """,
            descriptors,
            "Permissions: read/write in the working folder.",
            TestContext.Current.CancellationToken);

        Assert.True(plan.Steps.Count >= 3, $"the planner produced {plan.Steps.Count} step(s).");

        var withDependencies = plan.Steps.Count(s => s.DependsOn.Count > 0);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{plan.Steps.Count} steps, {withDependencies} declaring dependencies:\n" +
            string.Join('\n', plan.Steps.Select(s => $"  {s.Index}. {s.Title}  dependsOn=[{string.Join(",", s.DependsOn)}]")));

        Assert.True(withDependencies > 0,
            "the planner declared no dependencies at all, so the parallel path can never engage.");

        // Asserted only when the plan's own shape calls for it: two or more steps waiting on the
        // same thing. Demanding a wide wave unconditionally would be asserting on the model's
        // choice of plan rather than on the scheduler's handling of it.
        var waves = AgentOrchestrator.GroupIntoWaves(plan.Steps);
        var widest = waves.Count == 0 ? 0 : waves.Max(w => w.Count);

        var sharedWait = plan.Steps
            .Where(s => s.DependsOn.Count > 0)
            .GroupBy(s => string.Join(",", s.DependsOn.Order()))
            .Any(g => g.Count() > 1);

        if (sharedWait)
        {
            Assert.True(widest > 1,
                $"steps share a dependency but the scheduler still produced waves of {widest}.");
        }
    }

    /// <summary>
    /// And whether those dependencies actually produce a wave. The scheduler is deliberately
    /// conservative — only steps that *all* declared dependencies may share a wave — so a plan
    /// that mixes declared and undeclared steps still runs one at a time. Worth knowing which
    /// way real plans fall, hence the reported shape rather than a bare pass.
    /// </summary>
    [Fact]
    public async Task Whether_a_real_plan_ever_yields_a_parallel_wave_is_reported_either_way()
    {
        var (factory, profile) = LiveModel.Require();

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));

        var context = new ToolContext
        {
            Guard = new PathGuard(new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _root, Access = FolderAccess.ReadWrite }],
            }),
            Approvals = new AutoApproveBroker(),
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "planner",
            WorkingDirectory = _root,
        };

        var descriptors = new ToolRegistry(factory, knowledge).Build(context, visionModel: null);

        var plan = await new Planner(factory.CreateChatClient(profile)).CreatePlanAsync(
            """
            Read notes.txt once. Then create three files that do not depend on each other in any
            way: alpha.txt, beta.txt and gamma.txt. Each is written from notes.txt alone.
            """,
            descriptors,
            "Permissions: read/write in the working folder.",
            TestContext.Current.CancellationToken);

        var waves = AgentOrchestrator.GroupIntoWaves(plan.Steps);
        var widest = waves.Count == 0 ? 0 : waves.Max(w => w.Count);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{plan.Steps.Count} steps → {waves.Count} wave(s), widest {widest}:\n" +
            string.Join('\n', plan.Steps.Select(s => $"  {s.Index}. {s.Title}  dependsOn=[{string.Join(",", s.DependsOn)}]")));

        // Deliberately not asserting that a wave was wide. Whether a planner marks work parallel
        // is its judgement, and a suite that demands it would be measuring the model's mood.
        // What must hold is that the scheduler produced a usable, complete schedule either way.
        Assert.Equal(plan.Steps.Count, waves.Sum(w => w.Count));
        Assert.All(waves, wave => Assert.NotEmpty(wave));
    }
}
