using VibeDesk.Domain;

namespace VibeDesk.Application.Drive;

/// <summary>
/// Flat projection of a Drive item for lists and grids. Deliberately excludes the content payload —
/// this is the shape every listing query returns, so it must stay cheap.
/// </summary>
public sealed record DriveItemDto(
    Guid Id,
    DriveItemType Type,
    string Name,
    Guid? ParentId,
    Guid OwnerId,
    string OwnerName,
    long SizeBytes,
    string? ContentType,
    bool IsStarred,
    bool IsTrashed,
    ShareScope Scope,
    PermissionRole MyRole,
    int VersionNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? UpdatedByName,
    string? Color,
    int CommentCount = 0,
    bool IsShared = false)
{
    /// <summary>Route the UI navigates to when the item is opened.</summary>
    public string OpenRoute => Type switch
    {
        DriveItemType.Folder => $"/drive/{Id}",
        DriveItemType.Document => $"/docs/{Id}",
        DriveItemType.Spreadsheet => $"/sheets/{Id}",
        DriveItemType.Presentation => $"/slides/{Id}",
        _ => $"/drive/file/{Id}",
    };

    public string Icon => Type switch
    {
        DriveItemType.Folder => "folder",
        DriveItemType.Document => "description",
        DriveItemType.Spreadsheet => "table",
        DriveItemType.Presentation => "slideshow",
        _ => ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ? "image"
            : ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true ? "video"
            : "file",
    };

    public bool CanEdit => MyRole >= PermissionRole.Editor;
    public bool CanComment => MyRole >= PermissionRole.Commenter;
    public bool CanManage => MyRole >= PermissionRole.Owner;

    /// <summary>True for the three types that have an editable payload, comments and version history.</summary>
    public bool IsEditableDocument =>
        Type is DriveItemType.Document or DriveItemType.Spreadsheet or DriveItemType.Presentation;
}

/// <summary>Search and listing filter. All properties are optional and combine with AND.</summary>
public sealed record DriveQuery
{
    public Guid? ParentId { get; init; }

    /// <summary>Free-text match against name and extracted document text.</summary>
    public string? Keyword { get; init; }

    public DriveItemType[]? Types { get; init; }

    public DateTimeOffset? ModifiedAfter { get; init; }
    public DateTimeOffset? ModifiedBefore { get; init; }

    /// <summary>Restrict to items owned by this user; null means "anything I can see".</summary>
    public Guid? OwnerId { get; init; }

    public bool StarredOnly { get; init; }
    public bool TrashedOnly { get; init; }

    /// <summary>Items shared *with* me — owned by someone else but granted to me.</summary>
    public bool SharedWithMeOnly { get; init; }

    /// <summary>name | updated | created | size | type</summary>
    public string SortBy { get; init; } = "updated";
    public bool Descending { get; init; } = true;

    public int Skip { get; init; }
    public int Take { get; init; } = 100;

    /// <summary>Search the whole subtree under <see cref="ParentId"/> rather than direct children.</summary>
    public bool Recursive { get; init; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Skip, int Take)
{
    public bool HasMore => Skip + Items.Count < TotalCount;
    public static PagedResult<T> Empty => new([], 0, 0, 0);
}

public sealed record BreadcrumbDto(Guid Id, string Name);

/// <summary>Aggregate storage figures for the Drive quota widget.</summary>
public sealed record StorageUsageDto(
    long UsedBytes,
    long QuotaBytes,
    int FileCount,
    int DocumentCount,
    int SpreadsheetCount,
    int PresentationCount,
    int FolderCount)
{
    public double UsedPercent => QuotaBytes <= 0 ? 0 : Math.Min(100, UsedBytes * 100d / QuotaBytes);
}

public sealed record PermissionDto(
    Guid Id,
    Guid? UserId,
    string? Email,
    string DisplayName,
    PermissionRole Role,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    bool IsOwner);

public sealed record VersionDto(
    Guid Id,
    int VersionNumber,
    Guid CreatedById,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    string? Label,
    bool IsAutoSnapshot);

public sealed record CommentDto(
    Guid Id,
    Guid? ParentCommentId,
    CommentKind Kind,
    CommentStatus Status,
    string Body,
    string? Anchor,
    string? SuggestedText,
    string? OriginalText,
    Guid AuthorId,
    string AuthorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<CommentDto> Replies)
{
    public bool IsResolved => Status != CommentStatus.Open;
}

/// <summary>Result of a collaborative save, so the client knows whether it must rebase.</summary>
public sealed record SaveContentResult(bool Accepted, long Revision, string? ServerData, string? Reason)
{
    public static SaveContentResult Ok(long revision) => new(true, revision, null, null);

    public static SaveContentResult Stale(long revision, string serverData) =>
        new(false, revision, serverData, "The document changed since your last sync.");
}

public sealed record DriveItemContentDto(
    Guid Id,
    DriveItemType Type,
    string Name,
    string Data,
    long Revision,
    PermissionRole MyRole,
    DateTimeOffset UpdatedAt);
