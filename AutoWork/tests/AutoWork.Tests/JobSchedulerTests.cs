using AutoWork.Core.Automation;
using AutoWork.Core.Security;

namespace AutoWork.Tests;

/// <summary>
/// Due times, worked out from a clock the test controls. The thing that matters is that a
/// schedule means what a person reading it would think it means.
/// </summary>
public sealed class ScheduleTests
{
    private static SavedJob Job(SchedulePeriod period, TimeOnly at, DayOfWeek day = DayOfWeek.Monday) => new()
    {
        Goal = "do the thing",
        Trigger = TriggerKind.Schedule,
        Enabled = true,
        Period = period,
        TimeOfDay = at,
        DayOfWeek = day,
    };

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute) =>
        new(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local));

    [Fact]
    public void A_daily_job_runs_later_today_when_its_time_is_still_ahead()
    {
        var next = Job(SchedulePeriod.Daily, new TimeOnly(17, 0)).NextDueAfter(Local(2026, 8, 4, 9, 0));

        Assert.Equal(Local(2026, 8, 4, 17, 0), next);
    }

    [Fact]
    public void A_daily_job_whose_time_has_passed_runs_tomorrow()
    {
        var next = Job(SchedulePeriod.Daily, new TimeOnly(9, 0)).NextDueAfter(Local(2026, 8, 4, 17, 0));

        Assert.Equal(Local(2026, 8, 5, 9, 0), next);
    }

    [Fact]
    public void A_weekly_job_lands_on_the_day_it_names()
    {
        // Tuesday 4 August 2026 → the next Monday is the 10th.
        var next = Job(SchedulePeriod.Weekly, new TimeOnly(8, 30), DayOfWeek.Monday)
            .NextDueAfter(Local(2026, 8, 4, 12, 0));

        Assert.Equal(Local(2026, 8, 10, 8, 30), next);
        Assert.Equal(DayOfWeek.Monday, next!.Value.DayOfWeek);
    }

    /// <summary>Same day, time not yet reached: today, not a week away.</summary>
    [Fact]
    public void A_weekly_job_on_todays_day_still_runs_today_if_the_time_is_ahead()
    {
        var next = Job(SchedulePeriod.Weekly, new TimeOnly(18, 0), DayOfWeek.Tuesday)
            .NextDueAfter(Local(2026, 8, 4, 9, 0));

        Assert.Equal(Local(2026, 8, 4, 18, 0), next);
    }

    [Fact]
    public void A_weekly_job_on_todays_day_whose_time_has_passed_waits_a_week()
    {
        var next = Job(SchedulePeriod.Weekly, new TimeOnly(8, 0), DayOfWeek.Tuesday)
            .NextDueAfter(Local(2026, 8, 4, 9, 0));

        Assert.Equal(Local(2026, 8, 11, 8, 0), next);
    }

    [Fact]
    public void An_hourly_job_runs_at_the_top_of_the_next_hour()
    {
        var next = Job(SchedulePeriod.Hourly, new TimeOnly(0, 0)).NextDueAfter(Local(2026, 8, 4, 9, 42));

        Assert.Equal(Local(2026, 8, 4, 10, 0), next);
    }

    [Fact]
    public void A_disabled_or_manual_job_is_never_due()
    {
        var disabled = Job(SchedulePeriod.Daily, new TimeOnly(9, 0));
        disabled.Enabled = false;

        var manual = Job(SchedulePeriod.Daily, new TimeOnly(9, 0));
        manual.Trigger = TriggerKind.Manual;

        Assert.Null(disabled.NextDueAfter(Local(2026, 8, 4, 0, 0)));
        Assert.Null(manual.NextDueAfter(Local(2026, 8, 4, 0, 0)));
    }

    [Fact]
    public void A_job_with_nothing_to_do_or_no_folder_to_watch_is_rejected()
    {
        Assert.NotNull(new SavedJob { Goal = "" }.Validate());
        Assert.NotNull(new SavedJob { Goal = "x", Trigger = TriggerKind.FolderChange, WatchFolder = "" }.Validate());
        Assert.Null(new SavedJob { Goal = "x" }.Validate());
    }
}

/// <summary>
/// The scheduler itself, against a clock the test moves and a folder it really writes to.
/// </summary>
public sealed class JobSchedulerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "autowork-jobs", Guid.NewGuid().ToString("n")[..8]);

    private readonly string _watched;
    private readonly string _ungranted;
    private readonly FileJobStore _store;

    private DateTimeOffset _now = new(new DateTime(2026, 8, 4, 9, 0, 0, DateTimeKind.Local));

    public JobSchedulerTests()
    {
        _watched = Path.Combine(_root, "watched");
        _ungranted = Path.Combine(_root, "ungranted");
        Directory.CreateDirectory(_watched);
        Directory.CreateDirectory(_ungranted);

        _store = new FileJobStore(Path.Combine(_root, "jobs.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PermissionPolicy Policy() => new()
    {
        Roots = [new PermissionRoot { Path = _watched, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
    };

    private JobScheduler Build(List<(SavedJob Job, string Reason)> ran, List<string>? skipped = null)
    {
        var scheduler = new JobScheduler(
            _store,
            Policy,
            (job, reason, _) =>
            {
                lock (ran) ran.Add((job, reason));
                return Task.CompletedTask;
            },
            () => _now);

        if (skipped is not null)
            scheduler.Skipped += (_, why) => { lock (skipped) skipped.Add(why); };

        return scheduler;
    }

    [Fact]
    public async Task A_scheduled_job_runs_when_its_time_arrives_and_not_before()
    {
        _store.Save(new SavedJob
        {
            Goal = "summarise invoices",
            Trigger = TriggerKind.Schedule,
            Enabled = true,
            Period = SchedulePeriod.Daily,
            TimeOfDay = new TimeOnly(17, 0),
        });

        var ran = new List<(SavedJob, string)>();
        using var scheduler = Build(ran);

        scheduler.Reload();

        scheduler.Poll();
        await SettleAsync();
        Assert.Empty(ran);

        _now = new DateTimeOffset(new DateTime(2026, 8, 4, 17, 0, 30, DateTimeKind.Local));
        scheduler.Poll();

        await WaitUntilAsync(() => Count(ran) == 1, "the job to run");

        var fired = Snapshot(ran)[0];
        Assert.Equal("summarise invoices", fired.Item1.Goal);
        Assert.Contains("scheduled", fired.Item2);
    }

    /// <summary>
    /// The reason due times come from the clock rather than a countdown: an app closed over a
    /// weekend must not wake up owing three runs of the same job.
    /// </summary>
    [Fact]
    public async Task A_job_missed_while_the_app_was_closed_runs_once_not_once_per_missed_day()
    {
        _store.Save(new SavedJob
        {
            Goal = "weekly report",
            Trigger = TriggerKind.Schedule,
            Enabled = true,
            Period = SchedulePeriod.Daily,
            TimeOfDay = new TimeOnly(9, 0),
        });

        var ran = new List<(SavedJob, string)>();
        using var scheduler = Build(ran);

        scheduler.Reload();

        // Three days pass with the app shut.
        _now = _now.AddDays(3);
        scheduler.Poll();
        await WaitUntilAsync(() => Count(ran) == 1, "the job to run once");

        scheduler.Poll();
        await SettleAsync();

        Assert.Equal(1, Count(ran));
    }

    [Fact]
    public async Task A_disabled_job_never_fires()
    {
        _store.Save(new SavedJob
        {
            Goal = "nope",
            Trigger = TriggerKind.Schedule,
            Enabled = false,
            Period = SchedulePeriod.Hourly,
        });

        var ran = new List<(SavedJob, string)>();
        using var scheduler = Build(ran);

        scheduler.Reload();
        _now = _now.AddDays(2);
        scheduler.Poll();
        await SettleAsync();

        Assert.Empty(ran);
    }

    [Fact]
    public async Task A_change_in_a_watched_folder_fires_the_job_once_the_folder_goes_quiet()
    {
        _store.Save(new SavedJob
        {
            Goal = "file the new invoices",
            Trigger = TriggerKind.FolderChange,
            Enabled = true,
            WatchFolder = _watched,
            QuietSeconds = 5,
        });

        var ran = new List<(SavedJob, string)>();
        using var scheduler = Build(ran);

        scheduler.Reload();

        await File.WriteAllTextAsync(Path.Combine(_watched, "invoice.txt"), "x", TestContext.Current.CancellationToken);

        // FileSystemWatcher delivers on its own thread; give it a moment to arrive.
        await WaitForWatcherAsync();

        // Still inside the quiet period.
        scheduler.Poll();
        await SettleAsync();
        Assert.Empty(ran);

        _now = _now.AddSeconds(10);
        scheduler.Poll();

        await WaitUntilAsync(() => Count(ran) == 1, "the job to run once the folder went quiet");
        Assert.Contains("changed", Snapshot(ran)[0].Item2);
    }

    /// <summary>
    /// Copying fifty files raises fifty events. The job should run once, after the copying stops
    /// — not once per file, and not on the first one while the folder is still half-written.
    /// </summary>
    [Fact]
    public async Task A_burst_of_changes_produces_one_run_not_one_per_file()
    {
        _store.Save(new SavedJob
        {
            Goal = "process the batch",
            Trigger = TriggerKind.FolderChange,
            Enabled = true,
            WatchFolder = _watched,
            QuietSeconds = 5,
        });

        var ran = new List<(SavedJob, string)>();
        using var scheduler = Build(ran);

        scheduler.Reload();

        for (var i = 0; i < 20; i++)
            await File.WriteAllTextAsync(Path.Combine(_watched, $"file{i}.txt"), "x", TestContext.Current.CancellationToken);

        await WaitForWatcherAsync();

        _now = _now.AddSeconds(10);
        scheduler.Poll();
        await WaitUntilAsync(() => Count(ran) == 1, "the batch to be processed once");

        scheduler.Poll();
        await SettleAsync();

        Assert.Equal(1, Count(ran));
    }

    /// <summary>
    /// The security property: saving a job must not become a way to make AutoWork watch — and
    /// then act on — a folder the sandbox was never given.
    /// </summary>
    [Fact]
    public async Task A_folder_the_sandbox_does_not_grant_is_not_watched()
    {
        _store.Save(new SavedJob
        {
            Goal = "read the private folder",
            Trigger = TriggerKind.FolderChange,
            Enabled = true,
            WatchFolder = _ungranted,
            QuietSeconds = 1,
        });

        var ran = new List<(SavedJob, string)>();
        var skipped = new List<string>();
        using var scheduler = Build(ran, skipped);

        scheduler.Reload();

        Assert.Contains(skipped, s => s.Contains("not a folder you have granted"));

        await File.WriteAllTextAsync(Path.Combine(_ungranted, "secret.txt"), "x", TestContext.Current.CancellationToken);
        await WaitForWatcherAsync();

        _now = _now.AddSeconds(30);
        scheduler.Poll();
        await SettleAsync();

        Assert.Empty(ran);
    }

    /// <summary>
    /// A run holds the Work view and the consent prompts. A second starting underneath it would
    /// leave two runs competing for one set of answers.
    /// </summary>
    [Fact]
    public async Task A_job_coming_due_while_another_run_is_going_is_skipped_rather_than_queued()
    {
        _store.Save(new SavedJob
        {
            Goal = "hourly thing",
            Trigger = TriggerKind.Schedule,
            Enabled = true,
            Period = SchedulePeriod.Hourly,
        });

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        var skipped = new List<string>();

        using var scheduler = new JobScheduler(
            _store, Policy,
            async (_, _, _) =>
            {
                Interlocked.Increment(ref runs);
                started.TrySetResult();
                await release.Task;
            },
            () => _now);

        scheduler.Skipped += (_, why) => { lock (skipped) skipped.Add(why); };

        scheduler.Reload();

        _now = _now.AddHours(1);
        scheduler.Poll();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(scheduler.IsBusy);

        // A second one comes due while the first is still going.
        _now = _now.AddHours(1);
        scheduler.Poll();

        Assert.Equal(1, Volatile.Read(ref runs));
        Assert.Contains(skipped, s => s.Contains("already going"));

        release.TrySetResult();
    }

    [Fact]
    public void Jobs_survive_a_round_trip_through_the_store()
    {
        var job = new SavedJob
        {
            Name = "Monday invoices",
            Goal = "summarise last week's invoices",
            Trigger = TriggerKind.Schedule,
            Enabled = true,
            Period = SchedulePeriod.Weekly,
            DayOfWeek = DayOfWeek.Monday,
            TimeOfDay = new TimeOnly(8, 30),
        };

        _store.Save(job);

        var loaded = Assert.Single(_store.List());

        Assert.Equal(job.Id, loaded.Id);
        Assert.Equal("Monday invoices", loaded.Name);
        Assert.Equal(TriggerKind.Schedule, loaded.Trigger);
        Assert.Equal(SchedulePeriod.Weekly, loaded.Period);
        Assert.Equal(DayOfWeek.Monday, loaded.DayOfWeek);
        Assert.Equal(new TimeOnly(8, 30), loaded.TimeOfDay);

        _store.Delete(job.Id);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void A_broken_jobs_file_schedules_nothing_rather_than_stopping_the_app()
    {
        File.WriteAllText(Path.Combine(_root, "jobs.json"), "{ this is not json");

        Assert.Empty(_store.List());
    }

    /// <summary>
    /// FileSystemWatcher delivers on its own thread with no completion signal, so a short wait
    /// is unavoidable. Kept generous rather than tight — a flaky test teaches people to ignore it.
    /// </summary>
    private static Task WaitForWatcherAsync() => Task.Delay(1_200);

    /// <summary>
    /// The scheduler starts a run on a background task and returns, because <c>Poll</c> is
    /// called from a timer and must not block. So "did it run" is a wait, and "did it not run"
    /// is a wait followed by a check.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }

        Assert.Fail($"timed out waiting for {what}");
    }

    /// <summary>Long enough for a run that was going to start to have started.</summary>
    private static Task SettleAsync() => Task.Delay(250);

    private static int Count<T>(List<T> items)
    {
        lock (items) return items.Count;
    }

    private static T[] Snapshot<T>(List<T> items)
    {
        lock (items) return [.. items];
    }
}
