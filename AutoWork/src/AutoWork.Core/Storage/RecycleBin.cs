using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoWork.Core.Storage;

/// <summary>One deleted thing, and where it came from.</summary>
public sealed record RecycledItem
{
    public required string Id { get; init; }

    /// <summary>Where it was when it was deleted. Empty for items recycled before this was recorded.</summary>
    public string OriginalPath { get; init; } = "";

    /// <summary>Where it lives now, inside AutoWork's recycle folder.</summary>
    public required string StoredPath { get; init; }

    public DateTimeOffset DeletedAt { get; init; } = DateTimeOffset.Now;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }

    /// <summary>The run that deleted it, so it can be traced back to a transcript.</summary>
    public string RunId { get; init; } = "";

    public string Name => Path.GetFileName(OriginalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n
        ? n
        : Path.GetFileName(StoredPath);

    /// <summary>False for anything recycled before the origin was recorded — it cannot be put back.</summary>
    [JsonIgnore]
    public bool CanRestore => OriginalPath.Length > 0;
}

public enum RestoreStatus
{
    Restored,
    /// <summary>Something is already at the original path.</summary>
    Occupied,
    /// <summary>The item is no longer in the recycle folder.</summary>
    Missing,
    /// <summary>Recycled before origins were recorded, so there is nowhere to put it back.</summary>
    OriginUnknown,
    Refused,
    Failed,
}

public sealed record RestoreResult(RestoreStatus Status, string Message, string? Path = null);

public interface IRecycleBin
{
    /// <summary>Moves <paramref name="path"/> into the bin and records where it came from.</summary>
    RecycledItem Store(string path, bool isDirectory, string runId = "");

    /// <summary>Newest first.</summary>
    IReadOnlyList<RecycledItem> List();

    RestoreResult Restore(string id, bool overwrite = false);

    /// <summary>Deletes an item for good.</summary>
    bool Purge(string id);

    int PurgeAll();
}

/// <summary>
/// AutoWork's own recycle folder, and the index that makes it recoverable.
///
/// Soft delete has always moved files here — inside <see cref="AppPaths.Root"/>, which
/// <c>PathGuard</c> refuses unconditionally, so the agent cannot read a recycled file back out.
/// What it never did was record <em>where the file came from</em>, which meant "recoverable" was
/// only true for someone willing to work out the original path themselves. The index fixes that.
///
/// It is append-only JSONL, the same shape as the action log: a half-written line costs one
/// entry rather than the whole index, and there is no rewrite step to lose data in.
/// </summary>
public sealed class RecycleBin : IRecycleBin
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;
    private readonly string _indexPath;
    private readonly Lock _gate = new();

    public RecycleBin(string? directory = null)
    {
        _directory = directory ?? AppPaths.RecycleDirectory;
        _indexPath = Path.Combine(_directory, "index.jsonl");
    }

    public RecycledItem Store(string path, bool isDirectory, string runId = "")
    {
        var bin = Path.Combine(_directory, DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(bin);

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var id = $"{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("n")[..6]}";
        var destination = Path.Combine(bin, $"{id}-{name}");

        var size = isDirectory ? DirectorySize(path) : SafeLength(path);

        if (isDirectory) Directory.Move(path, destination);
        else File.Move(path, destination);

        var item = new RecycledItem
        {
            Id = id,
            OriginalPath = path,
            StoredPath = destination,
            IsDirectory = isDirectory,
            SizeBytes = size,
            RunId = runId,
        };

        Append(item);
        return item;
    }

    public IReadOnlyList<RecycledItem> List()
    {
        var indexed = ReadIndex();

        // Anything on disk that the index does not know about — recycled by an older build, or
        // its index line lost. Listed anyway: the user should see everything that is taking up
        // space, even the part that cannot be put back.
        var known = indexed.Select(i => i.StoredPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = new List<RecycledItem>();

        if (Directory.Exists(_directory))
        {
            foreach (var day in Directory.EnumerateDirectories(_directory))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(day))
                {
                    if (known.Contains(entry)) continue;

                    var isDirectory = Directory.Exists(entry);

                    orphans.Add(new RecycledItem
                    {
                        Id = "orphan:" + Path.GetFileName(entry),
                        OriginalPath = "",
                        StoredPath = entry,
                        DeletedAt = File.GetLastWriteTime(entry),
                        IsDirectory = isDirectory,
                        SizeBytes = isDirectory ? DirectorySize(entry) : SafeLength(entry),
                    });
                }
            }
        }

        return indexed
            .Where(i => File.Exists(i.StoredPath) || Directory.Exists(i.StoredPath))
            .Concat(orphans)
            .OrderByDescending(i => i.DeletedAt)
            .ToList();
    }

    public RestoreResult Restore(string id, bool overwrite = false)
    {
        var item = List().FirstOrDefault(i => i.Id == id);

        if (item is null) return new RestoreResult(RestoreStatus.Missing, "That item is no longer in the recycle folder.");
        if (!item.CanRestore)
            return new RestoreResult(RestoreStatus.OriginUnknown,
                "This was recycled before AutoWork recorded where files came from, so there is nowhere to put it back. " +
                "You can still find it in the recycle folder.");

        if (!File.Exists(item.StoredPath) && !Directory.Exists(item.StoredPath))
            return new RestoreResult(RestoreStatus.Missing, "That item is no longer in the recycle folder.");

        string destination;
        try
        {
            destination = Path.GetFullPath(item.OriginalPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new RestoreResult(RestoreStatus.Failed, $"Its original path is unusable: {ex.Message}");
        }

        // The one hard rule. Restoring is a user action on the user's own data, so it is not
        // bound by the agent's folder grants — a file deleted from a folder since un-granted must
        // still be recoverable. But nothing may be written into AutoWork's own directory, or a
        // doctored index line would be a way to drop a file on top of config.json or secrets.json.
        if (IsInsideAppRoot(destination))
            return new RestoreResult(RestoreStatus.Refused,
                "That path is inside AutoWork's own folder, which nothing is allowed to write to.");

        var exists = File.Exists(destination) || Directory.Exists(destination);
        if (exists && !overwrite)
            return new RestoreResult(RestoreStatus.Occupied,
                $"Something is already at {PathDisplay(destination)}. Restoring would replace it.", destination);

        try
        {
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            if (exists)
            {
                if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
                else File.Delete(destination);
            }

            if (item.IsDirectory) Directory.Move(item.StoredPath, destination);
            else File.Move(item.StoredPath, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RestoreResult(RestoreStatus.Failed, ex.Message, destination);
        }

        Forget(item.Id);
        return new RestoreResult(RestoreStatus.Restored, $"Restored to {PathDisplay(destination)}.", destination);
    }

    public bool Purge(string id)
    {
        var item = List().FirstOrDefault(i => i.Id == id);
        if (item is null) return false;

        var removed = Erase(item);
        Forget(item.Id);
        return removed;
    }

    public int PurgeAll()
    {
        var count = 0;
        foreach (var item in List())
            if (Erase(item)) count++;

        // Cheaper and less error-prone than rewriting the index minus every purged line.
        lock (_gate)
        {
            try { File.Delete(_indexPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        return count;
    }

    // ── Index ─────────────────────────────────────────────────────────────────────────────

    private void Append(RecycledItem item) => AppendLine(JsonSerializer.Serialize(item, SerializerOptions));

    /// <summary>
    /// Appends one record, repairing a torn tail first.
    ///
    /// A process killed mid-append leaves a line with no terminator. Appending straight onto that
    /// concatenates the new record with the fragment and loses <em>both</em> — so one interrupted
    /// write would quietly cost the next deletion its record too. Closing the line first means a
    /// tear costs exactly the entry it happened to.
    /// </summary>
    private void AppendLine(string json)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);

            if (NeedsTerminator())
                File.AppendAllText(_indexPath, Environment.NewLine);

            File.AppendAllText(_indexPath, json + Environment.NewLine);
        }
    }

    private bool NeedsTerminator()
    {
        try
        {
            var info = new FileInfo(_indexPath);
            if (!info.Exists || info.Length == 0) return false;

            using var stream = File.OpenRead(_indexPath);
            stream.Seek(-1, SeekOrigin.End);

            return stream.ReadByte() is not ((byte)'\n');
        }
        catch (IOException)
        {
            return false;
        }
    }

    private List<RecycledItem> ReadIndex()
    {
        lock (_gate)
        {
            if (!File.Exists(_indexPath)) return [];

            var items = new List<RecycledItem>();

            foreach (var line in File.ReadLines(_indexPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var item = JsonSerializer.Deserialize<RecycledItem>(line, SerializerOptions);
                    if (item is not null) items.Add(item);
                }
                catch (JsonException)
                {
                    // One torn line, from a process killed mid-append. Skip it and keep the rest.
                }
            }

            // A restored item is forgotten by appending a tombstone, so later wins.
            var live = new Dictionary<string, RecycledItem>(StringComparer.Ordinal);

            foreach (var item in items)
            {
                if (item.StoredPath == "!forgotten") live.Remove(item.Id);
                else live[item.Id] = item;
            }

            return [.. live.Values];
        }
    }

    /// <summary>Appends a tombstone rather than rewriting the file. Append-only stays append-only.</summary>
    private void Forget(string id)
    {
        if (id.StartsWith("orphan:", StringComparison.Ordinal)) return;

        AppendLine(JsonSerializer.Serialize(new RecycledItem { Id = id, StoredPath = "!forgotten" }, SerializerOptions));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static bool Erase(RecycledItem item)
    {
        try
        {
            if (item.IsDirectory && Directory.Exists(item.StoredPath))
            {
                Directory.Delete(item.StoredPath, recursive: true);
                return true;
            }

            if (File.Exists(item.StoredPath))
            {
                File.Delete(item.StoredPath);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    private static bool IsInsideAppRoot(string path)
    {
        var root = Path.GetFullPath(AppPaths.Root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(path);

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // The separator check is what stops "…/AutoWorkOther" matching "…/AutoWork".
        return full.Equals(root, comparison)
            || full.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch (IOException) { return 0; }
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(SafeLength);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string PathDisplay(string path) => Security.PathGuard.Describe(path);
}
