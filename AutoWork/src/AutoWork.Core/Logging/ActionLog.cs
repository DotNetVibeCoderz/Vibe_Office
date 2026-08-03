using System.Text.Json;
using System.Text.Json.Serialization;
using AutoWork.Core.Agents;

namespace AutoWork.Core.Logging;

public enum ActionOutcome
{
    Started,
    Succeeded,
    Failed,
    Denied,
    Cancelled,
}

/// <summary>
/// One line in the transparency log. Everything the agent does that touches the machine —
/// every file read, every command, every approval answer — produces one of these.
/// </summary>
public sealed record ActionLogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n")[..12];
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;

    public string RunId { get; init; } = "";
    public int StepIndex { get; init; }

    public AgentOrgan Organ { get; init; } = AgentOrgan.Hands;

    /// <summary>Tool or subsystem responsible, e.g. "files.move".</summary>
    public string Action { get; init; } = "";

    /// <summary>One line a person can read without expanding anything.</summary>
    public string Summary { get; init; } = "";

    public ActionOutcome Outcome { get; init; } = ActionOutcome.Succeeded;

    /// <summary>Paths touched, already trimmed for display.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    public string? Detail { get; init; }
    public string? Error { get; init; }
    public long ElapsedMs { get; init; }
}

public interface IActionLog
{
    void Append(ActionLogEntry entry);

    /// <summary>Fires on the appending thread. Subscribers must marshal to their own context.</summary>
    event Action<ActionLogEntry>? Appended;

    /// <summary>Newest-first snapshot of the in-memory tail.</summary>
    IReadOnlyList<ActionLogEntry> Recent { get; }

    /// <summary>Reads the durable log back, newest first.</summary>
    IEnumerable<ActionLogEntry> Read(int limit = 500, string? runId = null);
}

/// <summary>Discards everything. For tests and for tools constructed outside a run.</summary>
public sealed class NullActionLog : IActionLog
{
    public static readonly NullActionLog Instance = new();

    public void Append(ActionLogEntry entry) { }
    public event Action<ActionLogEntry>? Appended { add { } remove { } }
    public IReadOnlyList<ActionLogEntry> Recent => [];
    public IEnumerable<ActionLogEntry> Read(int limit = 500, string? runId = null) => [];
}

/// <summary>
/// Append-only JSON Lines log. One line per action, so it survives a crash mid-write, can be
/// tailed with standard tools, and never needs the whole file in memory to append.
/// </summary>
public sealed class JsonlActionLog : IActionLog, IDisposable
{
    private const int MemoryTailSize = 400;
    private const long RotateAtBytes = 8L * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly Queue<ActionLogEntry> _tail = new(MemoryTailSize);
    private StreamWriter? _writer;
    private bool _disposed;

    public JsonlActionLog(string? path = null)
    {
        _path = path ?? AppPaths.ActionLogFile;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public event Action<ActionLogEntry>? Appended;

    public IReadOnlyList<ActionLogEntry> Recent
    {
        get { lock (_gate) return _tail.Reverse().ToArray(); }
    }

    public void Append(ActionLogEntry entry)
    {
        lock (_gate)
        {
            if (_disposed) return;

            _tail.Enqueue(entry);

            while (_tail.Count > MemoryTailSize) _tail.Dequeue();

            try
            {
                RotateIfNeeded();
                _writer ??= new StreamWriter(new FileStream(
                    _path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };

                _writer.WriteLine(JsonSerializer.Serialize(entry, SerializerOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written must not take the app down with it.
                // The in-memory tail still feeds the Activity view.
            }
        }

        // Raised outside the lock: a slow subscriber must not stall the agent mid-action.
        Appended?.Invoke(entry);
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < RotateAtBytes) return;

        _writer?.Dispose();
        _writer = null;

        var archived = Path.Combine(
            Path.GetDirectoryName(_path)!,
            $"{Path.GetFileNameWithoutExtension(_path)}-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");

        try { File.Move(_path, archived); }
        catch (IOException) { /* another process holds it; keep appending */ }
    }

    public IEnumerable<ActionLogEntry> Read(int limit = 500, string? runId = null)
    {
        if (!File.Exists(_path)) yield break;

        // Read forward, keep a bounded window, then reverse — avoids loading a large log.
        var window = new Queue<ActionLogEntry>(limit);

        using var reader = new StreamReader(new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            ActionLogEntry? entry;
            try { entry = JsonSerializer.Deserialize<ActionLogEntry>(line, SerializerOptions); }
            catch (JsonException) { continue; }

            if (entry is null) continue;
            if (runId is not null && !string.Equals(entry.RunId, runId, StringComparison.Ordinal)) continue;

            window.Enqueue(entry);
            while (window.Count > limit) window.Dequeue();
        }

        foreach (var entry in window.Reverse())
            yield return entry;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}

/// <summary>Convenience helpers so tools do not each hand-roll entry construction.</summary>
public static class ActionLogExtensions
{
    public static void Success(this IActionLog log, string runId, AgentOrgan organ, string action,
        string summary, IReadOnlyList<string>? paths = null, long elapsedMs = 0, string? detail = null) =>
        log.Append(new ActionLogEntry
        {
            RunId = runId,
            Organ = organ,
            Action = action,
            Summary = summary,
            Outcome = ActionOutcome.Succeeded,
            Paths = paths ?? [],
            ElapsedMs = elapsedMs,
            Detail = detail,
        });

    public static void Failure(this IActionLog log, string runId, AgentOrgan organ, string action,
        string summary, string error, IReadOnlyList<string>? paths = null) =>
        log.Append(new ActionLogEntry
        {
            RunId = runId,
            Organ = organ,
            Action = action,
            Summary = summary,
            Outcome = ActionOutcome.Failed,
            Error = error,
            Paths = paths ?? [],
        });

    public static void Denied(this IActionLog log, string runId, AgentOrgan organ, string action,
        string summary, string reason, IReadOnlyList<string>? paths = null) =>
        log.Append(new ActionLogEntry
        {
            RunId = runId,
            Organ = organ,
            Action = action,
            Summary = summary,
            Outcome = ActionOutcome.Denied,
            Error = reason,
            Paths = paths ?? [],
        });
}
