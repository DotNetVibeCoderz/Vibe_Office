using System.Text.Json;
using System.Text.Json.Serialization;
using AutoWork.Core.Agents;

namespace AutoWork.Core.Runs;

/// <summary>
/// The headline of a past run — enough to fill a list without reading its transcript.
/// </summary>
public sealed record RunHistoryEntry
{
    public required string Id { get; init; }
    public required string Goal { get; init; }
    public string? ModelDisplayName { get; init; }
    public RunStatus Status { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public long ElapsedMs { get; init; }
    public string Summary { get; init; } = "";
    public string? Error { get; init; }
    public int TotalSteps { get; init; }
    public int ToolCalls { get; init; }

    /// <summary>Null when no verification pass ran.</summary>
    public bool? VerificationPassed { get; init; }

    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }

    /// <summary>Null when the model had no price set, which is the default.</summary>
    public decimal? Cost { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>False when a provider answered some calls without saying what they used.</summary>
    public bool UsageComplete { get; init; } = true;

    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>A past run in full: its headline plus every event the Work Tape was built from.</summary>
public sealed record RunTranscript
{
    public required RunHistoryEntry Entry { get; init; }
    public IReadOnlyList<RunEvent> Events { get; init; } = [];
}

public interface IRunHistoryStore
{
    /// <summary>Newest first. Reads headlines only, never transcripts.</summary>
    IReadOnlyList<RunHistoryEntry> List(int limit = 200);

    /// <summary>Null when the run was never saved, or has since been pruned.</summary>
    RunTranscript? Load(string runId);

    Task SaveAsync(string runId, string goal, IReadOnlyList<RunEvent> events, CancellationToken cancellationToken = default);

    void Delete(string runId);

    /// <summary>Removes runs older than <paramref name="retentionDays"/>. Returns how many went.</summary>
    int Prune(int retentionDays);
}

/// <summary>
/// One run, two files: <c>&lt;id&gt;.json</c> holds the headline and <c>&lt;id&gt;.events.json</c> the
/// transcript. Splitting them is what keeps the history list quick — showing fifty past runs
/// otherwise means parsing fifty transcripts, and a transcript carries every tool result the run
/// ever saw.
/// </summary>
public sealed class FileRunHistoryStore : IRunHistoryStore
{
    /// <summary>
    /// A single tool result can be an entire file's contents. The transcript is for the user to
    /// read back, not to replay into a model, so anything past this is cut — otherwise one run
    /// that read a large folder leaves tens of megabytes behind for the retention sweep to find.
    /// </summary>
    private const int MaxStoredResultChars = 4_000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public FileRunHistoryStore(string? directory = null)
    {
        _directory = directory ?? AppPaths.RunsDirectory;
        Directory.CreateDirectory(_directory);
    }

    public IReadOnlyList<RunHistoryEntry> List(int limit = 200)
    {
        if (!Directory.Exists(_directory)) return [];

        var entries = new List<RunHistoryEntry>();

        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            if (file.EndsWith(".events.json", StringComparison.OrdinalIgnoreCase)) continue;

            var entry = ReadEntry(file);
            if (entry is not null) entries.Add(entry);
        }

        return entries
            .OrderByDescending(e => e.StartedAt)
            .Take(Math.Max(0, limit))
            .ToList();
    }

    public RunTranscript? Load(string runId)
    {
        var id = Sanitise(runId);
        if (id is null) return null;

        var entry = ReadEntry(Path.Combine(_directory, id + ".json"));
        if (entry is null) return null;

        var events = new List<RunEvent>();
        var eventsPath = Path.Combine(_directory, id + ".events.json");

        if (File.Exists(eventsPath))
        {
            try
            {
                events = JsonSerializer.Deserialize<List<RunEvent>>(File.ReadAllText(eventsPath), SerializerOptions) ?? [];
            }
            catch (JsonException)
            {
                // A truncated transcript — the app was killed mid-write, say — should still let the
                // headline show. Losing the detail beats hiding that the run happened at all.
            }
        }

        return new RunTranscript { Entry = entry, Events = events };
    }

    public async Task SaveAsync(
        string runId,
        string goal,
        IReadOnlyList<RunEvent> events,
        CancellationToken cancellationToken = default)
    {
        var id = Sanitise(runId);
        if (id is null) return;

        Directory.CreateDirectory(_directory);

        var trimmed = events.Select(Trim).ToList();
        var entry = Summarise(id, goal, trimmed);

        await File.WriteAllTextAsync(
            Path.Combine(_directory, id + ".json"),
            JsonSerializer.Serialize(entry, SerializerOptions),
            cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(_directory, id + ".events.json"),
            JsonSerializer.Serialize(trimmed, SerializerOptions),
            cancellationToken).ConfigureAwait(false);
    }

    public void Delete(string runId)
    {
        var id = Sanitise(runId);
        if (id is null) return;

        foreach (var path in new[] { Path.Combine(_directory, id + ".json"), Path.Combine(_directory, id + ".events.json") })
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public int Prune(int retentionDays)
    {
        // Zero or negative would mean "delete everything, including the run that just finished",
        // which is never what a user means by a retention period.
        if (retentionDays <= 0 || !Directory.Exists(_directory)) return 0;

        var cutoff = DateTimeOffset.Now.AddDays(-retentionDays);
        var removed = 0;

        foreach (var entry in List(int.MaxValue))
        {
            if (entry.StartedAt >= cutoff) continue;
            Delete(entry.Id);
            removed++;
        }

        return removed;
    }

    private RunHistoryEntry? ReadEntry(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<RunHistoryEntry>(File.ReadAllText(path), SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static RunHistoryEntry Summarise(string id, string goal, IReadOnlyList<RunEvent> events)
    {
        var started = events.OfType<RunStartedEvent>().FirstOrDefault();
        var finished = events.OfType<RunFinishedEvent>().LastOrDefault();

        // The meter reports a running total, so the last one is the whole run.
        var usage = events.OfType<UsageEvent>().LastOrDefault();

        var startedAt = started?.At ?? events.FirstOrDefault()?.At ?? DateTimeOffset.Now;

        return new RunHistoryEntry
        {
            Id = id,
            Goal = string.IsNullOrWhiteSpace(started?.Goal) ? goal : started!.Goal,
            ModelDisplayName = started?.ModelDisplayName,

            // A run that was killed mid-flight never reported a status; recording it as cancelled
            // is truer than leaving it looking like it is still going.
            Status = finished?.Status ?? RunStatus.Cancelled,
            StartedAt = startedAt,
            FinishedAt = finished?.At ?? events.LastOrDefault()?.At ?? startedAt,
            ElapsedMs = finished?.ElapsedMs ?? 0,
            Summary = finished?.Summary ?? "",
            Error = finished?.Error,
            TotalSteps = finished?.TotalSteps ?? events.OfType<StepStartedEvent>().Count(),
            ToolCalls = events.OfType<ToolCallEvent>().Count(),
            VerificationPassed = finished?.Verification?.Passed,
            InputTokens = usage?.InputTokens ?? 0,
            OutputTokens = usage?.OutputTokens ?? 0,
            Cost = usage?.Cost,
            Currency = usage?.Currency ?? "USD",
            UsageComplete = usage is null || usage.CallsWithoutUsage == 0,
        };
    }

    private static RunEvent Trim(RunEvent e) => e switch
    {
        ToolCallEvent t => t with
        {
            Arguments = Truncate(t.Arguments, MaxStoredResultChars) ?? "",
            Result = Truncate(t.Result, MaxStoredResultChars),
        },
        SubAgentEvent s => s with { Result = Truncate(s.Result, MaxStoredResultChars) },
        _ => e,
    };

    private static string? Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max
            ? text
            : text[..max] + $"\n… ({text.Length - max:N0} more characters not kept)";

    /// <summary>
    /// Run ids come from the orchestrator, but this writes file paths from them, so a stray
    /// separator must never turn into a directory traversal.
    /// </summary>
    private static string? Sanitise(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId)) return null;

        var clean = new string([.. runId.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')]);
        return clean.Length == 0 ? null : clean[..Math.Min(clean.Length, 64)];
    }
}
