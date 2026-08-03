using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoWork.Core.Automation;

public enum TriggerKind
{
    /// <summary>Only ever started by hand.</summary>
    Manual = 0,
    /// <summary>Repeats on a clock.</summary>
    Schedule = 1,
    /// <summary>Fires when a watched folder changes.</summary>
    FolderChange = 2,
}

public enum SchedulePeriod
{
    Hourly = 0,
    Daily = 1,
    Weekly = 2,
}

/// <summary>
/// A job the user has saved: a goal, and when it should run.
///
/// Deliberately not a cron expression. The point of this feature is "every Monday, summarise
/// last week's invoices" — a person describing a routine, not an operator writing a crontab, and
/// a mis-typed cron field that silently runs something hourly is a bad way to learn the syntax.
/// </summary>
public sealed class SavedJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    public string Name { get; set; } = "";

    /// <summary>Exactly what would be typed into the Work view.</summary>
    public string Goal { get; set; } = "";

    public TriggerKind Trigger { get; set; } = TriggerKind.Manual;

    /// <summary>Off until the user turns it on, whatever the trigger says.</summary>
    public bool Enabled { get; set; }

    // ── Schedule ──────────────────────────────────────────────────────────────────────────

    public SchedulePeriod Period { get; set; } = SchedulePeriod.Daily;

    /// <summary>Local time of day for a daily or weekly job. Ignored when hourly.</summary>
    public TimeOnly TimeOfDay { get; set; } = new(9, 0);

    /// <summary>Day for a weekly job.</summary>
    public DayOfWeek DayOfWeek { get; set; } = System.DayOfWeek.Monday;

    // ── Folder trigger ────────────────────────────────────────────────────────────────────

    /// <summary>Folder to watch. Must be one the sandbox already grants.</summary>
    public string WatchFolder { get; set; } = "";

    public string WatchFilter { get; set; } = "*";
    public bool WatchSubfolders { get; set; }

    /// <summary>
    /// How long the folder must be quiet before the job runs. A copy of fifty files raises fifty
    /// events; without a settling period the job starts on the first one and reads a half-copied
    /// folder.
    /// </summary>
    public int QuietSeconds { get; set; } = 20;

    // ── History ───────────────────────────────────────────────────────────────────────────

    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastRunId { get; set; }
    public string? LastOutcome { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? (Goal.Length <= 60 ? Goal : Goal[..60] + "…")
        : Name;

    /// <summary>Null when the job is usable; otherwise why it is not.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Goal)) return "A job needs something to do.";

        if (Trigger == TriggerKind.FolderChange && string.IsNullOrWhiteSpace(WatchFolder))
            return "A folder trigger needs a folder to watch.";

        return null;
    }

    /// <summary>
    /// The next time this job is due, from <paramref name="after"/>. Null when it never is.
    ///
    /// Uses the clock rather than a stored countdown, so an app that was closed over the weekend
    /// does not wake up owing three runs — it simply runs at the next due time.
    /// </summary>
    public DateTimeOffset? NextDueAfter(DateTimeOffset after)
    {
        if (!Enabled || Trigger != TriggerKind.Schedule) return null;

        var local = after.LocalDateTime;

        switch (Period)
        {
            case SchedulePeriod.Hourly:
            {
                var next = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Local).AddHours(1);
                return new DateTimeOffset(next);
            }

            case SchedulePeriod.Daily:
            {
                var today = local.Date.Add(TimeOfDay.ToTimeSpan());
                return new DateTimeOffset(today > local ? today : today.AddDays(1));
            }

            case SchedulePeriod.Weekly:
            {
                var candidate = local.Date.Add(TimeOfDay.ToTimeSpan());
                var days = ((int)DayOfWeek - (int)local.DayOfWeek + 7) % 7;

                candidate = candidate.AddDays(days);
                if (candidate <= local) candidate = candidate.AddDays(7);

                return new DateTimeOffset(candidate);
            }

            default:
                return null;
        }
    }

    public SavedJob Clone() => (SavedJob)MemberwiseClone();
}

public interface IJobStore
{
    IReadOnlyList<SavedJob> List();
    void Save(SavedJob job);
    void Delete(string id);
}

/// <summary>One JSON file holding every saved job. Small, hand-editable, easy to back up.</summary>
public sealed class FileJobStore : IJobStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    public FileJobStore(string? path = null) =>
        _path = path ?? Path.Combine(AppPaths.Root, "jobs.json");

    /// <summary>Raised after any change, so a running scheduler can pick it up.</summary>
    public event Action? Changed;

    public IReadOnlyList<SavedJob> List()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return [];

            try
            {
                return JsonSerializer.Deserialize<List<SavedJob>>(File.ReadAllText(_path), SerializerOptions) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // A broken jobs file must not stop the app starting. Nothing is scheduled until
                // it is fixed, which is the safe direction to fail in.
                return [];
            }
        }
    }

    public void Save(SavedJob job)
    {
        lock (_gate)
        {
            var jobs = ListLocked();
            var index = jobs.FindIndex(j => j.Id == job.Id);

            if (index >= 0) jobs[index] = job;
            else jobs.Add(job);

            WriteLocked(jobs);
        }

        Changed?.Invoke();
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            var jobs = ListLocked();
            jobs.RemoveAll(j => j.Id == id);
            WriteLocked(jobs);
        }

        Changed?.Invoke();
    }

    private List<SavedJob> ListLocked()
    {
        if (!File.Exists(_path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<SavedJob>>(File.ReadAllText(_path), SerializerOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    private void WriteLocked(List<SavedJob> jobs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path, JsonSerializer.Serialize(jobs, SerializerOptions));
    }
}
