using System.Collections.ObjectModel;
using AutoWork.Core.Automation;
using AutoWork.Core.Security;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>One saved job, edited in place.</summary>
public sealed partial class JobViewModel : ObservableObject
{
    public JobViewModel(SavedJob job, Strings strings)
    {
        Job = job;
        L = strings;

        _name = job.Name;
        _goal = job.Goal;
        _enabled = job.Enabled;
        _triggerIndex = (int)job.Trigger;
        _periodIndex = (int)job.Period;
        _hour = job.TimeOfDay.Hour;
        _minute = job.TimeOfDay.Minute;
        _dayIndex = (int)job.DayOfWeek;
        _watchFolder = job.WatchFolder;
        _watchFilter = job.WatchFilter;
        _watchSubfolders = job.WatchSubfolders;
        _quietSeconds = job.QuietSeconds;
    }

    public SavedJob Job { get; }

    /// <summary>Held on the row: a ComboBox is an ItemsControl, so an ancestor binding written
    /// inside one of its items resolves to the ComboBox and renders blank.</summary>
    public Strings L { get; }

    public string Id => Job.Id;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _goal = "";
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _triggerIndex;
    [ObservableProperty] private int _periodIndex;
    [ObservableProperty] private int _hour;
    [ObservableProperty] private int _minute;
    [ObservableProperty] private int _dayIndex;
    [ObservableProperty] private string _watchFolder = "";
    [ObservableProperty] private string _watchFilter = "*";
    [ObservableProperty] private bool _watchSubfolders;
    [ObservableProperty] private int _quietSeconds = 20;

    public IReadOnlyList<string> Triggers => [L["jobs.trigger.manual"], L["jobs.trigger.schedule"], L["jobs.trigger.folder"]];
    public IReadOnlyList<string> Periods => [L["jobs.period.hourly"], L["jobs.period.daily"], L["jobs.period.weekly"]];

    public IReadOnlyList<string> Days =>
        [.. Enumerable.Range(0, 7).Select(d => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat
            .GetDayName((DayOfWeek)d))];

    public bool IsScheduled => TriggerIndex == (int)TriggerKind.Schedule;
    public bool IsFolder => TriggerIndex == (int)TriggerKind.FolderChange;
    public bool NeedsTime => IsScheduled && PeriodIndex != (int)SchedulePeriod.Hourly;
    public bool NeedsDay => IsScheduled && PeriodIndex == (int)SchedulePeriod.Weekly;

    public string? Problem => Snapshot().Validate();
    public bool HasProblem => Problem is not null;

    public string LastRun => Job.LastRunAt is { } at
        ? $"{at.LocalDateTime:d MMM HH:mm} — {Job.LastOutcome}"
        : "";

    public bool HasLastRun => !string.IsNullOrWhiteSpace(LastRun);

    /// <summary>Shown so a schedule can be checked at a glance rather than reasoned about.</summary>
    public string NextRun
    {
        get
        {
            var next = Snapshot().NextDueAfter(DateTimeOffset.Now);
            return next is null ? "" : string.Format(L["jobs.next"], next.Value.LocalDateTime.ToString("ddd d MMM HH:mm"));
        }
    }

    public bool HasNextRun => !string.IsNullOrWhiteSpace(NextRun);

    partial void OnTriggerIndexChanged(int value) => Revalidate();
    partial void OnPeriodIndexChanged(int value) => Revalidate();
    partial void OnEnabledChanged(bool value) => Revalidate();
    partial void OnGoalChanged(string value) => Revalidate();
    partial void OnWatchFolderChanged(string value) => Revalidate();
    partial void OnHourChanged(int value) => Revalidate();
    partial void OnMinuteChanged(int value) => Revalidate();
    partial void OnDayIndexChanged(int value) => Revalidate();

    private void Revalidate()
    {
        OnPropertyChanged(nameof(IsScheduled));
        OnPropertyChanged(nameof(IsFolder));
        OnPropertyChanged(nameof(NeedsTime));
        OnPropertyChanged(nameof(NeedsDay));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(NextRun));
        OnPropertyChanged(nameof(HasNextRun));
    }

    public SavedJob Snapshot()
    {
        var job = Job.Clone();

        job.Name = Name.Trim();
        job.Goal = Goal.Trim();
        job.Enabled = Enabled;
        job.Trigger = (TriggerKind)TriggerIndex;
        job.Period = (SchedulePeriod)PeriodIndex;
        job.TimeOfDay = new TimeOnly(Math.Clamp(Hour, 0, 23), Math.Clamp(Minute, 0, 59));
        job.DayOfWeek = (DayOfWeek)Math.Clamp(DayIndex, 0, 6);
        job.WatchFolder = WatchFolder.Trim();
        job.WatchFilter = string.IsNullOrWhiteSpace(WatchFilter) ? "*" : WatchFilter.Trim();
        job.WatchSubfolders = WatchSubfolders;
        job.QuietSeconds = Math.Clamp(QuietSeconds, 1, 3600);

        return job;
    }
}

/// <summary>
/// Saved jobs: a goal plus when it should run.
///
/// Every edit is written straight through to the store and the scheduler reloaded, rather than
/// collected behind a Save button. A schedule the user believes they set and did not is the
/// failure this page most needs to avoid.
/// </summary>
public sealed partial class JobsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public JobsViewModel(AppServices services)
    {
        _services = services;
        _services.Jobs.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(Reload);
    }

    public Strings L => _services.Strings;

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    [ObservableProperty] private string? _status;

    public bool IsEmpty => Jobs.Count == 0;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    [RelayCommand]
    public void Reload()
    {
        Jobs.Clear();
        foreach (var job in _services.Jobs.List().OrderByDescending(j => j.CreatedAt))
            Jobs.Add(new JobViewModel(job, L));

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Add()
    {
        // Starts disabled and manual: a new row must not begin scheduling anything by itself.
        _services.Jobs.Save(new SavedJob { Goal = "", Trigger = TriggerKind.Manual, Enabled = false });
        Reload();
    }

    [RelayCommand]
    private void Apply(JobViewModel? row)
    {
        if (row is null) return;

        var job = row.Snapshot();

        if (job.Validate() is { } problem)
        {
            Status = problem;
            OnPropertyChanged(nameof(HasStatus));
            return;
        }

        _services.Jobs.Save(job);
        _services.Scheduler.Reload();

        Status = string.Format(L["jobs.saved"], job.DisplayName);
        OnPropertyChanged(nameof(HasStatus));
        Reload();
    }

    [RelayCommand]
    private void Remove(JobViewModel? row)
    {
        if (row is null) return;

        _services.Jobs.Delete(row.Id);
        _services.Scheduler.Reload();
        Reload();
    }

    [RelayCommand]
    private async Task PickFolderAsync(JobViewModel? row)
    {
        if (row is null || PickFolder is null) return;

        var picked = await PickFolder().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked)) row.WatchFolder = picked;
    }

    /// <summary>Set by the view, which is the only thing that can reach a TopLevel.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    [RelayCommand]
    private void RunNow(JobViewModel? row)
    {
        if (row is null || _services.RunJob is null) return;

        _ = _services.RunJob(row.Snapshot(), L["jobs.reason.manual"], CancellationToken.None);
    }

    /// <summary>
    /// A folder trigger can only watch a folder the sandbox already grants, so the page says so
    /// before the scheduler quietly declines to watch it.
    /// </summary>
    public bool IsGranted(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;

        try
        {
            new PathGuard(_services.Config.Current.Permissions).EnsureReadable(folder);
            return true;
        }
        catch (SandboxViolationException)
        {
            return false;
        }
    }
}
