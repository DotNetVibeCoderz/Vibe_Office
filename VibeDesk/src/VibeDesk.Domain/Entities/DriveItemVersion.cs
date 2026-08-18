namespace VibeDesk.Domain.Entities;

/// <summary>
/// An immutable snapshot of an item, written on explicit save-version and on a debounce timer while
/// editing. Restoring pushes a new snapshot of the current state first, so restore is never lossy.
/// </summary>
public class DriveItemVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid DriveItemId { get; set; }
    public DriveItem? DriveItem { get; set; }

    public int VersionNumber { get; set; }

    /// <summary>Snapshot of <see cref="DriveItemContent.Data"/> for editable documents.</summary>
    public string? Data { get; set; }

    /// <summary>Snapshot storage key for binary files (each version is a separate blob).</summary>
    public string? StorageKey { get; set; }

    public long SizeBytes { get; set; }

    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>User-supplied label, e.g. "before Q3 numbers".</summary>
    public string? Label { get; set; }

    /// <summary>True when this snapshot was taken automatically rather than by the user.</summary>
    public bool IsAutoSnapshot { get; set; }
}
