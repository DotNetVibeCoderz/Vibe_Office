using AutoWork.Core.Security;

namespace AutoWork.Core.Automation;

public sealed record JobFired(SavedJob Job, string Reason);

/// <summary>
/// Watches the clock and the folders, and asks for a job to be run when one comes due.
///
/// It does not know what running a job means — that is a delegate — so the whole thing can be
/// tested against a fake clock and a real folder without an agent, a provider or a network.
///
/// Three rules shape it:
///
/// <list type="bullet">
/// <item><b>One at a time.</b> A run holds the Work view and the approval broker, so a second one
/// starting underneath it would produce two runs competing for one set of consent prompts. A job
/// that comes due while another is running is skipped, not queued — "summarise yesterday" run
/// twice in quick succession is worse than run once.</item>
/// <item><b>Missed time does not accumulate.</b> Due times come from the clock, not a countdown,
/// so an app closed over a weekend wakes owing nothing.</item>
/// <item><b>A folder trigger can only watch what the sandbox already grants.</b> Otherwise saving
/// a job would be a way to make AutoWork read a folder it was never given.</item>
/// </list>
/// </summary>
public sealed class JobScheduler : IDisposable
{
    private readonly IJobStore _store;
    private readonly Func<PermissionPolicy> _policy;
    private readonly Func<SavedJob, string, CancellationToken, Task> _run;
    private readonly Func<DateTimeOffset> _now;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _due = new(StringComparer.Ordinal);

    private Timer? _timer;
    private int _running;

    public JobScheduler(
        IJobStore store,
        Func<PermissionPolicy> policy,
        Func<SavedJob, string, CancellationToken, Task> run,
        Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _policy = policy;
        _run = run;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>Raised when a job is about to run, and when one is skipped. For the UI and the log.</summary>
    public event Action<JobFired>? Fired;
    public event Action<SavedJob, string>? Skipped;

    /// <summary>True while a job started by the scheduler is in flight.</summary>
    public bool IsBusy => Volatile.Read(ref _running) != 0;

    public void Start(TimeSpan? tick = null)
    {
        Reload();

        var interval = tick ?? TimeSpan.FromSeconds(30);
        _timer = new Timer(_ => Poll(), null, interval, interval);
    }

    /// <summary>Re-reads the jobs and rebuilds the folder watchers. Call after any edit.</summary>
    public void Reload()
    {
        var jobs = _store.List();

        lock (_gate)
        {
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
            _pending.Clear();

            // Due times are recomputed from the clock, so an edit takes effect immediately
            // rather than at the end of whatever interval was already counting.
            _due.Clear();

            foreach (var job in jobs.Where(j => j.Enabled && j.Validate() is null))
            {
                if (job.Trigger == TriggerKind.Schedule)
                {
                    if (job.NextDueAfter(_now()) is { } due) _due[job.Id] = due;
                }
                else if (job.Trigger == TriggerKind.FolderChange)
                {
                    TryWatch(job);
                }
            }
        }
    }

    /// <summary>Checks for anything due. Exposed so a test can drive it without waiting.</summary>
    public void Poll()
    {
        var now = _now();
        SavedJob? toRun = null;
        var reason = "";

        var jobs = _store.List().ToDictionary(j => j.Id, StringComparer.Ordinal);

        lock (_gate)
        {
            foreach (var (id, due) in _due.ToList())
            {
                if (due > now || !jobs.TryGetValue(id, out var job)) continue;

                _due[id] = job.NextDueAfter(now) ?? DateTimeOffset.MaxValue;

                toRun ??= job;
                if (ReferenceEquals(toRun, job)) reason = $"scheduled for {due.LocalDateTime:t}";
            }

            if (toRun is null)
            {
                foreach (var (id, quietFrom) in _pending.ToList())
                {
                    if (!jobs.TryGetValue(id, out var job)) { _pending.Remove(id); continue; }

                    if (now < quietFrom.AddSeconds(Math.Max(1, job.QuietSeconds))) continue;

                    _pending.Remove(id);
                    toRun = job;
                    reason = $"{PathGuard.Describe(job.WatchFolder)} changed";
                    break;
                }
            }
        }

        if (toRun is not null) Fire(toRun, reason);
    }

    private void Fire(SavedJob job, string reason)
    {
        // Interlocked rather than the lock, because the run itself is long and must not hold it.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            Skipped?.Invoke(job, "another run was already going");
            return;
        }

        Fired?.Invoke(new JobFired(job, reason));

        _ = Task.Run(async () =>
        {
            try
            {
                await _run(job, reason, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Skipped?.Invoke(job, ex.Message);
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        });
    }

    private void TryWatch(SavedJob job)
    {
        // The check that keeps a saved job from becoming a way to read an ungranted folder.
        var guard = new PathGuard(_policy());

        string folder;
        try
        {
            folder = guard.EnsureReadable(job.WatchFolder);
        }
        catch (SandboxViolationException)
        {
            Skipped?.Invoke(job, $"{PathGuard.Describe(job.WatchFolder)} is not a folder you have granted, so it is not being watched");
            return;
        }

        if (!Directory.Exists(folder))
        {
            Skipped?.Invoke(job, $"{PathGuard.Describe(folder)} does not exist, so it is not being watched");
            return;
        }

        var watcher = new FileSystemWatcher(folder)
        {
            Filter = string.IsNullOrWhiteSpace(job.WatchFilter) ? "*" : job.WatchFilter,
            IncludeSubdirectories = job.WatchSubfolders,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        void Touch(object? sender, FileSystemEventArgs e) => Touched(job.Id);

        watcher.Created += Touch;
        watcher.Changed += Touch;
        watcher.Renamed += (_, _) => Touched(job.Id);
        watcher.Deleted += Touch;

        // A watcher that dies silently is worse than one that never started.
        watcher.Error += (_, e) => Skipped?.Invoke(job, $"watching {PathGuard.Describe(folder)} failed: {e.GetException().Message}");

        watcher.EnableRaisingEvents = true;
        _watchers[job.Id] = watcher;
    }

    /// <summary>
    /// Restarts the quiet period. Copying fifty files raises fifty events; the job should run
    /// once, after the copying stops, not once per file and not on the first one.
    /// </summary>
    private void Touched(string jobId)
    {
        lock (_gate) _pending[jobId] = _now();
    }

    public void Dispose()
    {
        _timer?.Dispose();

        lock (_gate)
        {
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
        }
    }
}
