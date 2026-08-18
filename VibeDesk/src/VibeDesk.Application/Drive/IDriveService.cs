using VibeDesk.Domain;

namespace VibeDesk.Application.Drive;

/// <summary>
/// The single entry point for everything that lives in Drive — folders, uploads, and the
/// Docs/Sheets/Slides items too. Because all five apps store through here, permission checks,
/// activity logging and cache invalidation happen in one place instead of per app.
/// </summary>
public interface IDriveService
{
    Task<DriveItemDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lists or searches items visible to the current user according to <paramref name="query"/>.</summary>
    Task<PagedResult<DriveItemDto>> QueryAsync(DriveQuery query, CancellationToken ct = default);

    /// <summary>Root-to-item ancestor chain for the breadcrumb bar.</summary>
    Task<IReadOnlyList<BreadcrumbDto>> GetBreadcrumbsAsync(Guid id, CancellationToken ct = default);

    Task<DriveItemDto> CreateFolderAsync(string name, Guid? parentId, CancellationToken ct = default);

    /// <summary>
    /// Creates an empty Docs/Sheets/Slides item seeded with the default payload for its type.
    /// Rejects <see cref="DriveItemType.Folder"/> and <see cref="DriveItemType.File"/>.
    /// </summary>
    Task<DriveItemDto> CreateDocumentAsync(
        DriveItemType type,
        string name,
        Guid? parentId,
        string? initialData = null,
        CancellationToken ct = default);

    Task<DriveItemDto> UploadAsync(
        string fileName,
        string contentType,
        Stream content,
        Guid? parentId,
        CancellationToken ct = default);

    /// <summary>Opens the stored binary of a <see cref="DriveItemType.File"/> item.</summary>
    Task<Stream?> OpenFileAsync(Guid id, CancellationToken ct = default);

    /// <summary>A URL the browser can fetch the binary from, signed where the backend supports it.</summary>
    Task<string?> GetDownloadUrlAsync(Guid id, TimeSpan? validFor = null, CancellationToken ct = default);

    Task<DriveItemDto> RenameAsync(Guid id, string newName, CancellationToken ct = default);

    /// <summary>
    /// Reparents an item. Rewrites the materialised path of the whole subtree, and refuses to move a
    /// folder into itself or one of its own descendants.
    /// </summary>
    Task<DriveItemDto> MoveAsync(Guid id, Guid? newParentId, CancellationToken ct = default);

    /// <summary>Deep-copies an item (and, for folders, its subtree) into <paramref name="targetParentId"/>.</summary>
    Task<DriveItemDto> CopyAsync(
        Guid id,
        Guid? targetParentId,
        string? newName = null,
        CancellationToken ct = default);

    Task SetStarredAsync(Guid id, bool starred, CancellationToken ct = default);

    /// <summary>Soft delete — the item moves to trash and stays restorable.</summary>
    Task TrashAsync(Guid id, CancellationToken ct = default);

    Task RestoreAsync(Guid id, CancellationToken ct = default);

    /// <summary>Permanent delete, including stored binaries and every version snapshot.</summary>
    Task DeleteForeverAsync(Guid id, CancellationToken ct = default);

    Task<int> EmptyTrashAsync(CancellationToken ct = default);

    Task<StorageUsageDto> GetUsageAsync(CancellationToken ct = default);

    /// <summary>Records that the current user opened the item, powering the "Recent" list.</summary>
    Task TouchAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Reads and writes the JSON payload of an editable item. Split from <see cref="IDriveService"/>
/// because the editors call it on every autosave and it has different caching characteristics.
/// </summary>
public interface IDocumentContentService
{
    Task<DriveItemContentDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Persists a new payload. <paramref name="baseRevision"/> is the revision the client edited from;
    /// when the stored revision has moved on, the save is rejected with the server's copy so the
    /// client can rebase rather than silently clobbering a collaborator.
    /// Pass <c>-1</c> to force the write regardless.
    /// </summary>
    Task<SaveContentResult> SaveAsync(
        Guid id,
        string data,
        long baseRevision,
        CancellationToken ct = default);

    /// <summary>Deserialises the payload into the typed model for its item type.</summary>
    Task<T?> GetTypedAsync<T>(Guid id, CancellationToken ct = default) where T : new();

    /// <summary>Serialises and stores a typed model, bypassing revision checks.</summary>
    Task<long> SaveTypedAsync<T>(Guid id, T model, CancellationToken ct = default);
}

/// <summary>Resolves and mutates access control for Drive items.</summary>
public interface IPermissionService
{
    /// <summary>
    /// Effective role of a user on an item: the highest of ownership, any explicit grant on the item
    /// or an ancestor, and the item's link scope. Returns <see cref="PermissionRole.None"/> when
    /// the user has no access at all.
    /// </summary>
    Task<PermissionRole> ResolveRoleAsync(Guid itemId, Guid? userId, CancellationToken ct = default);

    /// <summary>Throws <see cref="ForbiddenException"/> when the current user is below <paramref name="minimum"/>.</summary>
    Task RequireAsync(Guid itemId, PermissionRole minimum, CancellationToken ct = default);

    Task<IReadOnlyList<PermissionDto>> ListAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Grants a role by email; the grant is claimed automatically if that user registers later.</summary>
    Task<PermissionDto> ShareAsync(
        Guid itemId,
        string email,
        PermissionRole role,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default);

    Task RevokeAsync(Guid itemId, Guid permissionId, CancellationToken ct = default);

    Task SetLinkSharingAsync(
        Guid itemId,
        ShareScope scope,
        PermissionRole linkRole,
        CancellationToken ct = default);

    /// <summary>Resolves an anonymous share link to the item it grants access to.</summary>
    Task<DriveItemDto?> ResolveShareTokenAsync(string token, CancellationToken ct = default);

    /// <summary>Transfers ownership; the previous owner is downgraded to Editor.</summary>
    Task TransferOwnershipAsync(Guid itemId, Guid newOwnerId, CancellationToken ct = default);
}

public interface IVersionService
{
    Task<IReadOnlyList<VersionDto>> ListAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Captures the current state as a new snapshot.</summary>
    Task<VersionDto> SnapshotAsync(
        Guid itemId,
        string? label = null,
        bool isAuto = false,
        CancellationToken ct = default);

    /// <summary>
    /// Rolls the item back to a snapshot. Snapshots the *current* state first, so a restore is itself
    /// undoable and no work is ever lost.
    /// </summary>
    Task RestoreAsync(Guid itemId, Guid versionId, CancellationToken ct = default);

    /// <summary>Payload of a snapshot, for the version-history preview pane.</summary>
    Task<string?> GetDataAsync(Guid itemId, Guid versionId, CancellationToken ct = default);

    Task DeleteAsync(Guid itemId, Guid versionId, CancellationToken ct = default);
}

public interface ICommentService
{
    /// <summary>Root threads with their replies nested, ordered oldest first.</summary>
    Task<IReadOnlyList<CommentDto>> ListAsync(
        Guid itemId,
        bool includeResolved = false,
        CancellationToken ct = default);

    Task<CommentDto> AddAsync(
        Guid itemId,
        string body,
        string? anchor,
        CommentKind kind = CommentKind.Comment,
        string? suggestedText = null,
        string? originalText = null,
        Guid? parentCommentId = null,
        CancellationToken ct = default);

    Task<CommentDto> EditAsync(Guid commentId, string body, CancellationToken ct = default);

    Task ResolveAsync(Guid commentId, bool resolved, CancellationToken ct = default);

    /// <summary>
    /// Applies a suggestion to the document body and marks it accepted. Returns the new revision so
    /// the editor can refresh without a round trip.
    /// </summary>
    Task<long> AcceptSuggestionAsync(Guid commentId, CancellationToken ct = default);

    Task RejectSuggestionAsync(Guid commentId, CancellationToken ct = default);

    Task DeleteAsync(Guid commentId, CancellationToken ct = default);

    Task<int> CountOpenAsync(Guid itemId, CancellationToken ct = default);
}
