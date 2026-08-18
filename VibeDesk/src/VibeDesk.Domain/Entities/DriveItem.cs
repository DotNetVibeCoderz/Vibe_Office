namespace VibeDesk.Domain.Entities;

/// <summary>
/// A single node in the Drive tree — folder, document, spreadsheet, presentation or uploaded file.
/// Metadata only: the editable payload lives in <see cref="DriveItemContent"/> and binaries live in
/// the blob store under <see cref="StorageKey"/>, so listing a folder never loads document bodies.
/// </summary>
public class DriveItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DriveItemType Type { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Null for items at the root of a user's drive.</summary>
    public Guid? ParentId { get; set; }
    public DriveItem? Parent { get; set; }
    public ICollection<DriveItem> Children { get; set; } = [];

    public Guid OwnerId { get; set; }

    /// <summary>
    /// Materialised ancestor path (<c>/{guid}/{guid}/</c>) so subtree queries are a single
    /// index-backed <c>LIKE</c> instead of a recursive walk.
    /// </summary>
    public string Path { get; set; } = "/";

    /// <summary>Only set for <see cref="DriveItemType.File"/>: key into the configured storage provider.</summary>
    public string? StorageKey { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Set when the binary was written through the encrypting storage decorator.</summary>
    public bool IsEncrypted { get; set; }

    public ShareScope Scope { get; set; } = ShareScope.Private;
    /// <summary>Opaque token used by anonymous link sharing; regenerated when link access is revoked.</summary>
    public string? ShareToken { get; set; }
    /// <summary>Role granted to anyone arriving through <see cref="ShareToken"/>.</summary>
    public PermissionRole LinkRole { get; set; } = PermissionRole.Viewer;

    public bool IsStarred { get; set; }
    public bool IsTrashed { get; set; }
    public DateTimeOffset? TrashedAt { get; set; }

    /// <summary>Space-separated keywords maintained on save to back Drive's search box.</summary>
    public string? SearchText { get; set; }

    /// <summary>Free-form colour tag used by the Drive UI for folders.</summary>
    public string? Color { get; set; }

    public int VersionNumber { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedById { get; set; }
    public DateTimeOffset? LastViewedAt { get; set; }

    /// <summary>Optimistic-concurrency guard; also used to reject stale collaborative saves.</summary>
    public byte[]? RowVersion { get; set; }

    public DriveItemContent? Content { get; set; }
    public ICollection<DriveItemVersion> Versions { get; set; } = [];
    public ICollection<DriveItemPermission> Permissions { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];

    public bool IsEditableDocument =>
        Type is DriveItemType.Document or DriveItemType.Spreadsheet or DriveItemType.Presentation;
}
