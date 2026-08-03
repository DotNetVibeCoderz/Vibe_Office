using AutoWork.Core.Agents;
using AutoWork.Core.Runs;
using AutoWork.Core.Security;

namespace AutoWork.Tests;

/// <summary>
/// Run history has one job: what you see when you reopen a run must be what happened.
///
/// The part that can quietly break is the polymorphic write — a run event class renamed or a
/// discriminator missed means the transcript still saves, still loads, and simply comes back
/// short. So the round trip is asserted event by event, and separately every event type is
/// checked to have a discriminator at all.
/// </summary>
public sealed class RunHistoryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "autowork-history", Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private FileRunHistoryStore Store() => new(_directory);

    private static RunEvent[] SampleTranscript(string runId) =>
    [
        new RunStartedEvent { RunId = runId, Goal = "Tidy the downloads folder", ModelDisplayName = "gpt-5-mini" },
        new PlanReadyEvent
        {
            RunId = runId,
            Plan = new AgentPlan
            {
                Goal = "Tidy the downloads folder",
                Notes = "Straightforward.",
                SuccessCriteria = ["Nothing loose at the top level"],
                Steps =
                [
                    new PlanStep { Index = 1, Title = "List it", Intent = "See what is there", Organ = AgentOrgan.Hands, SuccessCriteria = ["A file list"] },
                    new PlanStep { Index = 2, Title = "Sort it", Intent = "Move by type", Organ = AgentOrgan.Hands, DependsOn = [1] },
                ],
            },
        },
        new StepStartedEvent { RunId = runId, Index = 1, Title = "List it", Organ = AgentOrgan.Hands },
        new ToolCallEvent { RunId = runId, Tool = "files_list", Organ = AgentOrgan.Hands, Arguments = "{\"path\":\"~/Downloads\"}", Result = "12 files", ElapsedMs = 40 },
        new StepFinishedEvent { RunId = runId, Index = 1, Status = StepStatus.Succeeded, Detail = "Found 12", ElapsedMs = 900 },
        new ApprovalRequestedEvent
        {
            RunId = runId,
            Request = new ApprovalRequest { RunId = runId, Kind = ApprovalKind.WriteFiles, Title = "Move 12 files", Detail = "into ~/Downloads/Documents", AffectedPaths = ["a.pdf"] },
            Decision = ApprovalDecision.ApprovedForRun,
        },
        new CompactionEvent { RunId = runId, TokensBefore = 5000, TokensAfter = 2000, TurnsSummarised = 6, ContextWindow = 8000 },
        new SubAgentEvent { RunId = runId, SubAgentId = "a1", Task = "Move the images", Status = StepStatus.Succeeded, Result = "4 moved" },
        new RunPausedEvent { RunId = runId, BeforeStep = 2 },
        new RunResumedEvent { RunId = runId, Correction = "Leave the installers alone" },
        new AssistantMessageEvent { RunId = runId, Text = "Sorted." },
        new RunFinishedEvent
        {
            RunId = runId,
            Status = RunStatus.Succeeded,
            Summary = "Moved 12 files into four folders.",
            TotalSteps = 2,
            ElapsedMs = 4200,
            Verification = new VerificationResult { Passed = true, Summary = "Checked." },
        },
    ];

    [Fact]
    public async Task A_saved_run_comes_back_as_the_same_events()
    {
        var store = Store();
        var events = SampleTranscript("run-a");

        await store.SaveAsync("run-a", "Tidy the downloads folder", events, TestContext.Current.CancellationToken);

        var loaded = store.Load("run-a");
        Assert.NotNull(loaded);

        // Same count, same order, same types — a missing discriminator shows up right here.
        Assert.Equal(events.Length, loaded.Events.Count);
        Assert.Equal(
            events.Select(e => e.GetType()).ToArray(),
            loaded.Events.Select(e => e.GetType()).ToArray());

        var plan = Assert.IsType<PlanReadyEvent>(loaded.Events[1]);
        Assert.Equal(2, plan.Plan.Steps.Count);
        Assert.Equal([1], plan.Plan.Steps[1].DependsOn);
        Assert.Equal("Straightforward.", plan.Plan.Notes);

        var call = Assert.IsType<ToolCallEvent>(loaded.Events[3]);
        Assert.Equal("files_list", call.Tool);
        Assert.Equal(AgentOrgan.Hands, call.Organ);
        Assert.Equal("12 files", call.Result);

        var approval = Assert.IsType<ApprovalRequestedEvent>(loaded.Events[5]);
        Assert.Equal(ApprovalKind.WriteFiles, approval.Request.Kind);
        Assert.Equal(ApprovalDecision.ApprovedForRun, approval.Decision);

        var resumed = Assert.IsType<RunResumedEvent>(loaded.Events[9]);
        Assert.Equal("Leave the installers alone", resumed.Correction);

        var finished = Assert.IsType<RunFinishedEvent>(loaded.Events[^1]);
        Assert.True(finished.Verification?.Passed);
    }

    /// <summary>
    /// Guards the class of bug the round-trip test can only catch for the events it happens to
    /// use: a new event type added without a discriminator serialises fine until the day a run
    /// emits one, and then the whole transcript fails to load.
    /// </summary>
    [Fact]
    public void Every_run_event_type_is_declared_for_serialisation()
    {
        var declared = typeof(RunEvent)
            .GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonDerivedTypeAttribute), inherit: false)
            .Cast<System.Text.Json.Serialization.JsonDerivedTypeAttribute>()
            .Select(a => a.DerivedType)
            .ToHashSet();

        var actual = typeof(RunEvent).Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(RunEvent)) && !t.IsAbstract)
            .ToArray();

        var missing = actual.Where(t => !declared.Contains(t)).Select(t => t.Name).ToArray();

        Assert.True(missing.Length == 0,
            $"These run events have no [JsonDerivedType], so a saved run containing one cannot be " +
            $"read back: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task The_headline_summarises_the_run_without_reading_the_transcript()
    {
        var store = Store();
        await store.SaveAsync("run-b", "goal", SampleTranscript("run-b"), TestContext.Current.CancellationToken);

        var entry = Assert.Single(store.List());

        Assert.Equal("Tidy the downloads folder", entry.Goal);
        Assert.Equal("gpt-5-mini", entry.ModelDisplayName);
        Assert.Equal(RunStatus.Succeeded, entry.Status);
        Assert.Equal(2, entry.TotalSteps);
        Assert.Equal(1, entry.ToolCalls);
        Assert.Equal(4200, entry.ElapsedMs);
        Assert.True(entry.VerificationPassed);

        // Headline and transcript are separate files, which is what lets the list stay cheap.
        Assert.True(File.Exists(Path.Combine(_directory, "run-b.json")));
        Assert.True(new FileInfo(Path.Combine(_directory, "run-b.events.json")).Length > 0);
    }

    [Fact]
    public async Task A_run_that_never_finished_is_recorded_as_stopped_rather_than_still_running()
    {
        var store = Store();

        await store.SaveAsync("run-c", "half a job",
            [new RunStartedEvent { RunId = "run-c", Goal = "half a job" }, new StepStartedEvent { RunId = "run-c", Index = 1, Title = "Go" }],
            TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Cancelled, Assert.Single(store.List()).Status);
    }

    [Fact]
    public async Task Newest_runs_are_listed_first()
    {
        var store = Store();

        foreach (var (id, days) in new[] { ("old", 5), ("newest", 0), ("middle", 2) })
        {
            await store.SaveAsync(id, id,
                [new RunStartedEvent { RunId = id, Goal = id, At = DateTimeOffset.Now.AddDays(-days) }],
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(["newest", "middle", "old"], store.List().Select(e => e.Id).ToArray());
    }

    [Fact]
    public async Task Retention_removes_old_runs_and_keeps_recent_ones()
    {
        var store = Store();

        await store.SaveAsync("ancient", "x",
            [new RunStartedEvent { RunId = "ancient", Goal = "x", At = DateTimeOffset.Now.AddDays(-40) }],
            TestContext.Current.CancellationToken);

        await store.SaveAsync("recent", "y",
            [new RunStartedEvent { RunId = "recent", Goal = "y", At = DateTimeOffset.Now.AddDays(-3) }],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, store.Prune(retentionDays: 30));

        Assert.Equal("recent", Assert.Single(store.List()).Id);
        Assert.Null(store.Load("ancient"));

        // Both files go, not only the headline.
        Assert.Empty(Directory.GetFiles(_directory, "ancient*"));
    }

    [Fact]
    public async Task A_retention_of_zero_days_keeps_everything_rather_than_deleting_everything()
    {
        var store = Store();

        await store.SaveAsync("keep", "x",
            [new RunStartedEvent { RunId = "keep", Goal = "x", At = DateTimeOffset.Now.AddDays(-400) }],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, store.Prune(retentionDays: 0));
        Assert.Single(store.List());
    }

    [Fact]
    public async Task A_huge_tool_result_is_cut_down_before_it_is_stored()
    {
        var store = Store();
        var enormous = new string('x', 250_000);

        await store.SaveAsync("run-d", "x",
            [new ToolCallEvent { RunId = "run-d", Tool = "files_read", Organ = AgentOrgan.Hands, Result = enormous }],
            TestContext.Current.CancellationToken);

        var call = Assert.IsType<ToolCallEvent>(Assert.Single(store.Load("run-d")!.Events));

        Assert.True(call.Result!.Length < 6_000, $"the result was stored at {call.Result.Length:N0} characters");
        Assert.Contains("not kept", call.Result, StringComparison.Ordinal);

        // And the file on disk is small, which is the actual point.
        Assert.True(new FileInfo(Path.Combine(_directory, "run-d.events.json")).Length < 20_000);
    }

    /// <summary>
    /// Run ids become file names. They are generated internally today, but a store that writes a
    /// path from a caller-supplied string is one refactor away from being handed a bad one.
    /// </summary>
    [Theory]
    [InlineData("../../escape")]
    [InlineData("..\\..\\escape")]
    [InlineData("nested/run")]
    public async Task A_run_id_cannot_write_outside_the_history_folder(string runId)
    {
        var store = Store();

        await store.SaveAsync(runId, "x",
            [new RunStartedEvent { RunId = runId, Goal = "x" }], TestContext.Current.CancellationToken);

        foreach (var file in Directory.GetFiles(_directory, "*", SearchOption.AllDirectories))
            Assert.Equal(_directory, Path.GetDirectoryName(Path.GetFullPath(file)));
    }

    [Fact]
    public async Task A_transcript_damaged_on_disk_still_shows_its_headline()
    {
        var store = Store();
        await store.SaveAsync("run-e", "x", SampleTranscript("run-e"), TestContext.Current.CancellationToken);

        // The app killed part-way through writing the transcript.
        await File.WriteAllTextAsync(Path.Combine(_directory, "run-e.events.json"), "[{\"kind\":\"run.st",
            TestContext.Current.CancellationToken);

        var loaded = store.Load("run-e");

        Assert.NotNull(loaded);
        Assert.Equal("Tidy the downloads folder", loaded.Entry.Goal);
        Assert.Empty(loaded.Events);
    }

    [Fact]
    public void Loading_a_run_that_was_never_saved_returns_nothing_rather_than_throwing()
    {
        Assert.Null(Store().Load("nope"));
        Assert.Null(Store().Load(""));
    }
}
