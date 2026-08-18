using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Configuration;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Infrastructure.Services;

/// <summary>
/// The Drive implementation every app stores through. Because Docs/Sheets/Slides are Drive items,
/// this one class owns naming, foldering, trash, copy, quota and search for all of them.
/// </summary>
public sealed class DriveService(
    AppDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IStorageProvider storage,
    ICacheService cache,
    IUserDirectory users,
    IActivityService activity,
    IOptions<StorageOptions> storageOptions,
    IOfficeConverter? office = null) : IDriveService
{
    private readonly StorageOptions _storageOptions = storageOptions.Value;

    // ─────────────────────────────────────── reads ───────────────────────────────────────

    public async Task<DriveItemDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var role = await permissions.ResolveRoleAsync(id, currentUser.Id, ct);
        if (role == PermissionRole.None) return null;

        var item = await db.DriveItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return null;

        return await ProjectOneAsync(item, role, ct);
    }

    public async Task<PagedResult<DriveItemDto>> QueryAsync(
        DriveQuery query, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var source = db.DriveItems.AsNoTracking();

        // Visibility first: owned by me, granted to me, or reachable through a shared ancestor.
        source = ApplyVisibility(source, userId);

        source = query.TrashedOnly
            ? source.Where(x => x.IsTrashed)
            : source.Where(x => !x.IsTrashed);

        if (query.SharedWithMeOnly)
        {
            source = source.Where(x => x.OwnerId != userId);
        }

        if (query.ParentId is not null)
        {
            if (query.Recursive)
            {
                // Materialised path turns "everything under this folder" into a prefix match.
                var prefix = await GetPathPrefixAsync(query.ParentId.Value, ct);
                if (prefix is null) return PagedResult<DriveItemDto>.Empty;
                source = source.Where(x => x.Path.StartsWith(prefix));
            }
            else
            {
                source = source.Where(x => x.ParentId == query.ParentId);
            }
        }
        else if (!query.Recursive && !query.StarredOnly && !query.TrashedOnly
                 && !query.SharedWithMeOnly && string.IsNullOrWhiteSpace(query.Keyword))
        {
            // "My Drive" root: only items with no parent that I own.
            source = source.Where(x => x.ParentId == null && x.OwnerId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = $"%{query.Keyword.Trim()}%";
            source = source.Where(x =>
                EF.Functions.Like(x.Name, pattern)
                || (x.SearchText != null && EF.Functions.Like(x.SearchText, pattern)));
        }

        if (query.Types is { Length: > 0 })
        {
            var types = query.Types;
            source = source.Where(x => types.Contains(x.Type));
        }

        if (query.ModifiedAfter is not null)
            source = source.Where(x => x.UpdatedAt >= query.ModifiedAfter);

        if (query.ModifiedBefore is not null)
            source = source.Where(x => x.UpdatedAt <= query.ModifiedBefore);

        if (query.OwnerId is not null)
            source = source.Where(x => x.OwnerId == query.OwnerId);

        if (query.StarredOnly)
            source = source.Where(x => x.IsStarred);

        var total = await source.CountAsync(ct);

        source = ApplySort(source, query);

        var page = await source
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 500))
            .ToListAsync(ct);

        var projected = await ProjectManyAsync(page, userId, ct);

        return new PagedResult<DriveItemDto>(projected, total, query.Skip, query.Take);
    }

    /// <summary>
    /// Restricts a query to items the user may see. Folders shared with the user make their whole
    /// subtree visible, which is expressed as a path prefix match against the shared folder ids.
    /// </summary>
    private IQueryable<DriveItem> ApplyVisibility(IQueryable<DriveItem> source, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;

        var grantedIds = db.DriveItemPermissions
            .Where(p => p.UserId == userId && (p.ExpiresAt == null || p.ExpiresAt > now))
            .Select(p => p.DriveItemId);

        return source.Where(x =>
            x.OwnerId == userId
            || grantedIds.Contains(x.Id)
            // Inherited: some ancestor on the path was shared with me.
            || db.DriveItemPermissions.Any(p =>
                p.UserId == userId
                && (p.ExpiresAt == null || p.ExpiresAt > now)
                && x.Path.Contains(p.DriveItemId.ToString())));
    }

    private static IQueryable<DriveItem> ApplySort(IQueryable<DriveItem> source, DriveQuery query)
    {
        // Folders always lead, matching what every file manager does.
        var ordered = source.OrderBy(x => x.Type == DriveItemType.Folder ? 0 : 1);

        return query.SortBy.ToLowerInvariant() switch
        {
            "name" => query.Descending
                ? ordered.ThenByDescending(x => x.Name)
                : ordered.ThenBy(x => x.Name),
            "created" => query.Descending
                ? ordered.ThenByDescending(x => x.CreatedAt)
                : ordered.ThenBy(x => x.CreatedAt),
            "size" => query.Descending
                ? ordered.ThenByDescending(x => x.SizeBytes)
                : ordered.ThenBy(x => x.SizeBytes),
            "type" => query.Descending
                ? ordered.ThenByDescending(x => x.Type).ThenBy(x => x.Name)
                : ordered.ThenBy(x => x.Type).ThenBy(x => x.Name),
            _ => query.Descending
                ? ordered.ThenByDescending(x => x.UpdatedAt)
                : ordered.ThenBy(x => x.UpdatedAt),
        };
    }

    public async Task<IReadOnlyList<BreadcrumbDto>> GetBreadcrumbsAsync(
        Guid id, CancellationToken ct = default)
    {
        var item = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Name, x.Path })
            .FirstOrDefaultAsync(ct);

        if (item is null) return [];

        var ancestorIds = item.Path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty && g != id)
            .ToList();

        var names = ancestorIds.Count == 0
            ? []
            : await db.DriveItems
                .AsNoTracking()
                .Where(x => ancestorIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        // Path order is root → parent, so preserve it rather than sorting.
        var crumbs = ancestorIds
            .Where(names.ContainsKey)
            .Select(g => new BreadcrumbDto(g, names[g]))
            .ToList();

        crumbs.Add(new BreadcrumbDto(item.Id, item.Name));
        return crumbs;
    }

    // ─────────────────────────────────────── writes ───────────────────────────────────────

    public async Task<DriveItemDto> CreateFolderAsync(
        string name, Guid? parentId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();
        await RequireParentWritableAsync(parentId, ct);

        var item = new DriveItem
        {
            Type = DriveItemType.Folder,
            Name = await UniqueNameAsync(SanitiseName(name, "Untitled folder"), parentId, ct),
            ParentId = parentId,
            OwnerId = userId,
            UpdatedById = userId,
        };

        item.Path = await BuildPathAsync(parentId, item.Id, ct);
        item.SearchText = item.Name;

        db.DriveItems.Add(item);
        await db.SaveChangesAsync(ct);

        await InvalidateListingsAsync(parentId, userId, ct);
        await activity.LogAsync("item.created", item.Id, $"Folder \"{item.Name}\"", ct);

        return await ProjectOneAsync(item, PermissionRole.Owner, ct);
    }

    public async Task<DriveItemDto> CreateDocumentAsync(
        DriveItemType type,
        string name,
        Guid? parentId,
        string? initialData = null,
        CancellationToken ct = default)
    {
        if (type is DriveItemType.Folder or DriveItemType.File)
        {
            throw new ValidationException(
                "CreateDocumentAsync handles Document, Spreadsheet and Presentation only.");
        }

        var userId = currentUser.RequireId();
        await RequireParentWritableAsync(parentId, ct);

        var defaultName = type switch
        {
            DriveItemType.Spreadsheet => "Untitled spreadsheet",
            DriveItemType.Presentation => "Untitled presentation",
            _ => "Untitled document",
        };

        var item = new DriveItem
        {
            Type = type,
            Name = await UniqueNameAsync(SanitiseName(name, defaultName), parentId, ct),
            ParentId = parentId,
            OwnerId = userId,
            UpdatedById = userId,
        };

        item.Path = await BuildPathAsync(parentId, item.Id, ct);
        item.SearchText = item.Name;

        var data = initialData ?? DefaultPayload(type);

        db.DriveItems.Add(item);
        db.DriveItemContents.Add(new DriveItemContent
        {
            DriveItemId = item.Id,
            Data = data,
            Revision = 1,
            UpdatedById = userId,
        });

        await db.SaveChangesAsync(ct);

        await InvalidateListingsAsync(parentId, userId, ct);
        await activity.LogAsync("item.created", item.Id, $"{type} \"{item.Name}\"", ct);

        return await ProjectOneAsync(item, PermissionRole.Owner, ct);
    }

    /// <summary>Seed payload so a newly created item opens in a usable state rather than blank JSON.</summary>
    private static string DefaultPayload(DriveItemType type) => type switch
    {
        DriveItemType.Spreadsheet => ContentJson.Serialize(new SpreadsheetModel()),
        DriveItemType.Presentation => ContentJson.Serialize(new PresentationModel()),
        _ => ContentJson.Serialize(new DocumentModel()),
    };

    public async Task<DriveItemDto> UploadAsync(
        string fileName,
        string contentType,
        Stream content,
        Guid? parentId,
        CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();
        await RequireParentWritableAsync(parentId, ct);

        // An Office file becomes a real editable item rather than an opaque attachment. Done before
        // the quota check because a converted document stores its text, not the original package.
        if (office?.Detect(fileName, contentType) is { } format)
        {
            if (await TryImportOfficeAsync(office, format, fileName, content, parentId, ct) is { } imported)
            {
                return imported;
            }

            // Conversion failed — a corrupt or password-protected package. Falling through keeps the
            // bytes as an attachment, which is far better than refusing the upload outright.
        }

        var usage = await GetUsageAsync(ct);
        if (content.CanSeek && content.Length > _storageOptions.MaxUploadBytes)
        {
            throw new ValidationException(
                $"That file is larger than the {_storageOptions.MaxUploadBytes / (1024 * 1024)} MB upload limit.");
        }

        var safeName = SanitiseName(fileName, "upload");
        var item = new DriveItem
        {
            Type = DriveItemType.File,
            Name = await UniqueNameAsync(safeName, parentId, ct),
            ParentId = parentId,
            OwnerId = userId,
            UpdatedById = userId,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            IsEncrypted = _storageOptions.EncryptAtRest,
        };

        // Key includes the item id so two files with the same name never collide in the store.
        item.StorageKey = $"drive/{userId:N}/{item.Id:N}/{safeName}";
        item.Path = await BuildPathAsync(parentId, item.Id, ct);
        item.SearchText = item.Name;

        var stored = await storage.PutAsync(item.StorageKey, content, item.ContentType, ct);
        item.SizeBytes = stored.SizeBytes;

        if (usage.QuotaBytes > 0 && usage.UsedBytes + item.SizeBytes > usage.QuotaBytes)
        {
            // Roll the blob back so a rejected upload doesn't leave an orphan behind.
            await storage.DeleteAsync(item.StorageKey, ct);
            throw new ValidationException("This upload would exceed your storage quota.");
        }

        db.DriveItems.Add(item);
        await db.SaveChangesAsync(ct);

        await InvalidateListingsAsync(parentId, userId, ct);
        await activity.LogAsync("item.uploaded", item.Id, $"{item.Name} ({item.SizeBytes} bytes)", ct);

        return await ProjectOneAsync(item, PermissionRole.Owner, ct);
    }

    /// <summary>
    /// Converts an uploaded Office file into a native item, or returns null when the package cannot
    /// be read.
    /// </summary>
    /// <remarks>
    /// The stream is buffered first: the OpenXML SDK seeks all over a package, and an upload stream
    /// off the wire generally cannot. Failure is swallowed on purpose — a file the converter chokes
    /// on is stored as an attachment by the caller, which is the outcome the user wants far more than
    /// a rejected upload.
    /// </remarks>
    private async Task<DriveItemDto?> TryImportOfficeAsync(
        IOfficeConverter converter,
        OfficeFormat format,
        string fileName,
        Stream content,
        Guid? parentId,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        string payload;

        try
        {
            payload = format switch
            {
                OfficeFormat.Excel => ContentJson.Serialize(converter.ReadExcel(buffer)),
                OfficeFormat.PowerPoint => ContentJson.Serialize(converter.ReadPowerPoint(buffer)),
                _ => ContentJson.Serialize(converter.ReadWord(buffer)),
            };
        }
        catch (Exception)
        {
            // Rewind so the caller can still store the original bytes.
            if (content.CanSeek) content.Position = 0;
            return null;
        }

        // The extension is dropped: the item is a VibeDesk document now, not a .docx.
        var name = Path.GetFileNameWithoutExtension(fileName);

        var created = await CreateDocumentAsync(
            IOfficeConverter.TargetType(format),
            string.IsNullOrWhiteSpace(name) ? fileName : name,
            parentId,
            payload,
            ct);

        await activity.LogAsync("item.imported", created.Id, $"{fileName} → {format}", ct);

        return created;
    }

    public async Task<Stream?> OpenFileAsync(Guid id, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Viewer, ct);

        var key = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.StorageKey)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrEmpty(key) ? null : await storage.GetAsync(key, ct);
    }

    public async Task<string?> GetDownloadUrlAsync(
        Guid id, TimeSpan? validFor = null, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Viewer, ct);

        var key = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.StorageKey)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrEmpty(key) ? null : await storage.GetUrlAsync(key, validFor, ct);
    }

    public async Task<DriveItemDto> RenameAsync(
        Guid id, string newName, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Editor, ct);

        var item = await db.DriveItems.FirstOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw new NotFoundException($"Item {id} was not found.");

        var previous = item.Name;
        item.Name = await UniqueNameAsync(SanitiseName(newName, previous), item.ParentId, ct, excludeId: id);
        item.SearchText = item.Name;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedById = currentUser.Id;

        await db.SaveChangesAsync(ct);

        await InvalidateListingsAsync(item.ParentId, item.OwnerId, ct);
        await activity.LogAsync("item.renamed", id, $"\"{previous}\" → \"{item.Name}\"", ct);

        var role = await permissions.ResolveRoleAsync(id, currentUser.Id, ct);
        return await ProjectOneAsync(item, role, ct);
    }

    public async Task<DriveItemDto> MoveAsync(
        Guid id, Guid? newParentId, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Editor, ct);
        await RequireParentWritableAsync(newParentId, ct);

        var item = await db.DriveItems.FirstOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw new NotFoundException($"Item {id} was not found.");

        if (newParentId == id)
            throw new ValidationException("An item cannot be moved into itself.");

        if (newParentId is not null)
        {
            var targetPath = await GetPathPrefixAsync(newParentId.Value, ct)
                             ?? throw new NotFoundException("Destination folder was not found.");

            // Moving a folder inside its own subtree would detach that subtree from the root.
            if (targetPath.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("A folder cannot be moved into one of its own subfolders.");
        }

        var oldParentId = item.ParentId;
        var oldPath = item.Path;

        item.ParentId = newParentId;
        item.Path = await BuildPathAsync(newParentId, item.Id, ct);
        item.Name = await UniqueNameAsync(item.Name, newParentId, ct, excludeId: id);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedById = currentUser.Id;

        await db.SaveChangesAsync(ct);

        // Descendants carry the old prefix; rewrite it so inheritance and subtree queries stay correct.
        if (item.Type == DriveItemType.Folder && oldPath != item.Path)
        {
            await RewriteSubtreePathsAsync(oldPath, item.Path, ct);
        }

        await InvalidateListingsAsync(oldParentId, item.OwnerId, ct);
        await InvalidateListingsAsync(newParentId, item.OwnerId, ct);
        await cache.RemoveByTagAsync($"role:{id}", ct);

        await activity.LogAsync("item.moved", id, $"To {newParentId?.ToString() ?? "My Drive"}", ct);

        var role = await permissions.ResolveRoleAsync(id, currentUser.Id, ct);
        return await ProjectOneAsync(item, role, ct);
    }

    /// <summary>
    /// Rewrites the path prefix of every descendant in one statement. Doing this row by row would be
    /// O(subtree) round trips; a single <c>ExecuteUpdate</c> keeps a deep move cheap.
    /// </summary>
    private async Task RewriteSubtreePathsAsync(string oldPrefix, string newPrefix, CancellationToken ct)
    {
        await db.DriveItems
            .Where(x => x.Path.StartsWith(oldPrefix) && x.Path != oldPrefix)
            .ExecuteUpdateAsync(s => s.SetProperty(
                x => x.Path,
                x => newPrefix + x.Path.Substring(oldPrefix.Length)), ct);
    }

    public async Task<DriveItemDto> CopyAsync(
        Guid id, Guid? targetParentId, string? newName = null, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Viewer, ct);
        await RequireParentWritableAsync(targetParentId, ct);

        var userId = currentUser.RequireId();

        var source = await db.DriveItems
            .AsNoTracking()
            .Include(x => x.Content)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException($"Item {id} was not found.");

        var copy = await CopyNodeAsync(source, targetParentId, newName, userId, ct);

        if (source.Type == DriveItemType.Folder)
        {
            await CopyChildrenAsync(source.Id, copy.Id, userId, ct);
        }

        await db.SaveChangesAsync(ct);

        await InvalidateListingsAsync(targetParentId, userId, ct);
        await activity.LogAsync("item.copied", copy.Id, $"Copied from {source.Name}", ct);

        return await ProjectOneAsync(copy, PermissionRole.Owner, ct);
    }

    private async Task<DriveItem> CopyNodeAsync(
        DriveItem source, Guid? parentId, string? newName, Guid userId, CancellationToken ct)
    {
        var name = newName ?? $"Copy of {source.Name}";

        var copy = new DriveItem
        {
            Type = source.Type,
            Name = await UniqueNameAsync(SanitiseName(name, source.Name), parentId, ct),
            ParentId = parentId,
            OwnerId = userId,
            UpdatedById = userId,
            ContentType = source.ContentType,
            SizeBytes = source.SizeBytes,
            Color = source.Color,
            IsEncrypted = source.IsEncrypted,
        };

        copy.Path = await BuildPathAsync(parentId, copy.Id, ct);
        copy.SearchText = copy.Name;

        // Copies never inherit the original's sharing — a copy is private until shared again.
        copy.Scope = ShareScope.Private;
        copy.ShareToken = null;

        if (source.Type == DriveItemType.File && !string.IsNullOrEmpty(source.StorageKey))
        {
            copy.StorageKey = $"drive/{userId:N}/{copy.Id:N}/{copy.Name}";
            await storage.CopyAsync(source.StorageKey, copy.StorageKey, ct);
        }

        db.DriveItems.Add(copy);

        if (source.Content is not null)
        {
            db.DriveItemContents.Add(new DriveItemContent
            {
                DriveItemId = copy.Id,
                Data = source.Content.Data,
                PlainText = source.Content.PlainText,
                Revision = 1,
                UpdatedById = userId,
            });
        }

        return copy;
    }

    private async Task CopyChildrenAsync(
        Guid sourceParentId, Guid targetParentId, Guid userId, CancellationToken ct)
    {
        var children = await db.DriveItems
            .AsNoTracking()
            .Include(x => x.Content)
            .Where(x => x.ParentId == sourceParentId && !x.IsTrashed)
            .ToListAsync(ct);

        foreach (var child in children)
        {
            var copy = await CopyNodeAsync(child, targetParentId, child.Name, userId, ct);

            if (child.Type == DriveItemType.Folder)
            {
                await CopyChildrenAsync(child.Id, copy.Id, userId, ct);
            }
        }
    }

    public async Task SetStarredAsync(Guid id, bool starred, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Viewer, ct);

        await db.DriveItems
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsStarred, starred), ct);

        await cache.RemoveByTagAsync($"user:{currentUser.Id}", ct);
    }

    public async Task TrashAsync(Guid id, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Editor, ct);

        var item = await db.DriveItems.FirstOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw new NotFoundException($"Item {id} was not found.");

        var now = DateTimeOffset.UtcNow;
        item.IsTrashed = true;
        item.TrashedAt = now;

        // Trashing a folder trashes its subtree, so nothing is left reachable but orphaned.
        if (item.Type == DriveItemType.Folder)
        {
            await db.DriveItems
                .Where(x => x.Path.StartsWith(item.Path) && x.Id != id && !x.IsTrashed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsTrashed, true)
                    .SetProperty(x => x.TrashedAt, now), ct);
        }

        await db.SaveChangesAsync(ct);

        await InvalidateListingsAsync(item.ParentId, item.OwnerId, ct);
        await activity.LogAsync("item.trashed", id, item.Name, ct);
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Editor, ct);

        var item = await db.DriveItems.FirstOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw new NotFoundException($"Item {id} was not found.");

        item.IsTrashed = false;
        item.TrashedAt = null;

        // If the parent is still in the trash, restore to the root instead of into a hidden folder.
        if (item.ParentId is not null)
        {
            var parentTrashed = await db.DriveItems
                .AnyAsync(x => x.Id == item.ParentId && x.IsTrashed, ct);

            if (parentTrashed)
            {
                item.ParentId = null;
                item.Path = await BuildPathAsync(null, item.Id, ct);
            }
        }

        if (item.Type == DriveItemType.Folder)
        {
            await db.DriveItems
                .Where(x => x.Path.StartsWith(item.Path) && x.Id != id && x.IsTrashed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsTrashed, false)
                    .SetProperty(x => x.TrashedAt, (DateTimeOffset?)null), ct);
        }

        await db.SaveChangesAsync(ct);

        await InvalidateListingsAsync(item.ParentId, item.OwnerId, ct);
        await activity.LogAsync("item.restored", id, item.Name, ct);
    }

    public async Task DeleteForeverAsync(Guid id, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Owner, ct);

        var item = await db.DriveItems.FirstOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw new NotFoundException($"Item {id} was not found.");

        // Delete the deepest nodes first: the parent FK is Restrict, so a top-down delete would fail.
        var subtree = item.Type == DriveItemType.Folder
            ? await db.DriveItems
                .Where(x => x.Path.StartsWith(item.Path))
                .OrderByDescending(x => x.Path.Length)
                .ToListAsync(ct)
            : [item];

        foreach (var node in subtree)
        {
            await PurgeBlobsAsync(node, ct);
        }

        db.DriveItems.RemoveRange(subtree);
        await db.SaveChangesAsync(ct);

        await InvalidateListingsAsync(item.ParentId, item.OwnerId, ct);
        await cache.RemoveByTagAsync($"role:{id}", ct);
        await activity.LogAsync("item.deleted", null, $"Deleted \"{item.Name}\" permanently", ct);
    }

    /// <summary>Removes the item's blob and every version blob, so nothing is left paying for storage.</summary>
    private async Task PurgeBlobsAsync(DriveItem node, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(node.StorageKey))
        {
            await storage.DeleteAsync(node.StorageKey, ct);
        }

        var versionKeys = await db.DriveItemVersions
            .Where(v => v.DriveItemId == node.Id && v.StorageKey != null)
            .Select(v => v.StorageKey!)
            .ToListAsync(ct);

        foreach (var key in versionKeys)
        {
            await storage.DeleteAsync(key, ct);
        }
    }

    public async Task<int> EmptyTrashAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var trashed = await db.DriveItems
            .Where(x => x.OwnerId == userId && x.IsTrashed)
            .OrderByDescending(x => x.Path.Length)
            .ToListAsync(ct);

        if (trashed.Count == 0) return 0;

        foreach (var node in trashed)
        {
            await PurgeBlobsAsync(node, ct);
        }

        db.DriveItems.RemoveRange(trashed);
        await db.SaveChangesAsync(ct);

        await cache.RemoveByTagAsync($"user:{userId}", ct);
        await activity.LogAsync("trash.emptied", null, $"{trashed.Count} items", ct);

        return trashed.Count;
    }

    public async Task<StorageUsageDto> GetUsageAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        return await cache.GetOrCreateAsync($"usage:{userId}", async token =>
        {
            // One grouped query instead of six counts.
            var byType = await db.DriveItems
                .AsNoTracking()
                .Where(x => x.OwnerId == userId && !x.IsTrashed)
                .GroupBy(x => x.Type)
                .Select(g => new { Type = g.Key, Count = g.Count(), Bytes = g.Sum(x => x.SizeBytes) })
                .ToListAsync(token);

            var quota = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.QuotaBytes)
                .FirstOrDefaultAsync(token);

            int CountOf(DriveItemType type) =>
                byType.FirstOrDefault(x => x.Type == type)?.Count ?? 0;

            return new StorageUsageDto(
                byType.Sum(x => x.Bytes),
                quota,
                CountOf(DriveItemType.File),
                CountOf(DriveItemType.Document),
                CountOf(DriveItemType.Spreadsheet),
                CountOf(DriveItemType.Presentation),
                CountOf(DriveItemType.Folder));
        }, TimeSpan.FromMinutes(1), [$"user:{userId}"], ct);
    }

    public async Task TouchAsync(Guid id, CancellationToken ct = default)
    {
        var role = await permissions.ResolveRoleAsync(id, currentUser.Id, ct);
        if (role == PermissionRole.None) return;

        // Deliberately not SaveChanges on the tracked entity: this fires on every open and must not
        // bump UpdatedAt or the concurrency token.
        await db.DriveItems
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastViewedAt, DateTimeOffset.UtcNow), ct);
    }

    // ─────────────────────────────────────── helpers ───────────────────────────────────────

    private async Task RequireParentWritableAsync(Guid? parentId, CancellationToken ct)
    {
        if (parentId is null) return;

        var parentType = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == parentId)
            .Select(x => (DriveItemType?)x.Type)
            .FirstOrDefaultAsync(ct);

        if (parentType is null)
            throw new NotFoundException("Destination folder was not found.");

        if (parentType != DriveItemType.Folder)
            throw new ValidationException("Items can only be placed inside a folder.");

        await permissions.RequireAsync(parentId.Value, PermissionRole.Editor, ct);
    }

    /// <summary>Builds the materialised path <c>/{ancestor}/…/{self}/</c> for a new or moved item.</summary>
    private async Task<string> BuildPathAsync(Guid? parentId, Guid selfId, CancellationToken ct)
    {
        if (parentId is null) return $"/{selfId}/";

        var parentPath = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == parentId)
            .Select(x => x.Path)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(parentPath))
            throw new NotFoundException("Destination folder was not found.");

        return $"{parentPath}{selfId}/";
    }

    private async Task<string?> GetPathPrefixAsync(Guid id, CancellationToken ct) =>
        await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.Path)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Strips path separators and control characters. Names are shown in the UI and used to build
    /// storage keys, so a name containing <c>/</c> or <c>..</c> must never reach either.
    /// </summary>
    private static string SanitiseName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        var cleaned = new string(name
            .Trim()
            .Where(c => !char.IsControl(c) && c is not ('/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
            .ToArray())
            .Trim(' ', '.');

        if (cleaned.Length == 0) return fallback;
        return cleaned.Length > 255 ? cleaned[..255] : cleaned;
    }

    /// <summary>
    /// Appends " (2)", " (3)" … when a sibling already uses the name, the way a file manager does.
    /// </summary>
    private async Task<string> UniqueNameAsync(
        string name, Guid? parentId, CancellationToken ct, Guid? excludeId = null)
    {
        var siblings = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.ParentId == parentId && !x.IsTrashed && (excludeId == null || x.Id != excludeId))
            .Select(x => x.Name)
            .ToListAsync(ct);

        if (!siblings.Contains(name, StringComparer.OrdinalIgnoreCase)) return name;

        var extension = Path.GetExtension(name);
        var stem = string.IsNullOrEmpty(extension) ? name : name[..^extension.Length];

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{stem} ({i}){extension}";
            if (!siblings.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }

        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }

    private async Task InvalidateListingsAsync(Guid? parentId, Guid ownerId, CancellationToken ct)
    {
        await cache.RemoveByTagAsync($"user:{ownerId}", ct);
        if (parentId is not null) await cache.RemoveByTagAsync($"folder:{parentId}", ct);
    }

    private async Task<DriveItemDto> ProjectOneAsync(
        DriveItem item, PermissionRole role, CancellationToken ct)
    {
        var directory = await users.GetManyAsync(
            new[] { item.OwnerId, item.UpdatedById ?? item.OwnerId }, ct);

        var openComments = item.IsEditableDocument
            ? await db.Comments.CountAsync(
                c => c.DriveItemId == item.Id && c.Status == CommentStatus.Open, ct)
            : 0;

        var isShared = await db.DriveItemPermissions.AnyAsync(p => p.DriveItemId == item.Id, ct)
                       || item.Scope != ShareScope.Private;

        return new DriveItemDto(
            item.Id, item.Type, item.Name, item.ParentId, item.OwnerId,
            directory.TryGetValue(item.OwnerId, out var owner) ? owner.DisplayName : "Unknown",
            item.SizeBytes, item.ContentType, item.IsStarred, item.IsTrashed,
            item.Scope, role, item.VersionNumber, item.CreatedAt, item.UpdatedAt,
            item.UpdatedById is not null && directory.TryGetValue(item.UpdatedById.Value, out var editor)
                ? editor.DisplayName
                : null,
            item.Color, openComments, isShared);
    }

    /// <summary>
    /// Projects a page of items with batched lookups. Roles are resolved per item because a listing
    /// can mix owned, directly shared and inherited items.
    /// </summary>
    private async Task<List<DriveItemDto>> ProjectManyAsync(
        List<DriveItem> items, Guid userId, CancellationToken ct)
    {
        if (items.Count == 0) return [];

        var ids = items.Select(x => x.Id).ToArray();

        var userIds = items.Select(x => x.OwnerId)
            .Concat(items.Where(x => x.UpdatedById is not null).Select(x => x.UpdatedById!.Value))
            .Distinct();
        var directory = await users.GetManyAsync(userIds, ct);

        var commentCounts = await db.Comments
            .AsNoTracking()
            .Where(c => ids.Contains(c.DriveItemId) && c.Status == CommentStatus.Open)
            .GroupBy(c => c.DriveItemId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var sharedIds = await db.DriveItemPermissions
            .AsNoTracking()
            .Where(p => ids.Contains(p.DriveItemId))
            .Select(p => p.DriveItemId)
            .Distinct()
            .ToListAsync(ct);

        var result = new List<DriveItemDto>(items.Count);

        foreach (var item in items)
        {
            var role = item.OwnerId == userId
                ? PermissionRole.Owner
                : await permissions.ResolveRoleAsync(item.Id, userId, ct);

            result.Add(new DriveItemDto(
                item.Id, item.Type, item.Name, item.ParentId, item.OwnerId,
                directory.TryGetValue(item.OwnerId, out var owner) ? owner.DisplayName : "Unknown",
                item.SizeBytes, item.ContentType, item.IsStarred, item.IsTrashed,
                item.Scope, role, item.VersionNumber, item.CreatedAt, item.UpdatedAt,
                item.UpdatedById is not null && directory.TryGetValue(item.UpdatedById.Value, out var editor)
                    ? editor.DisplayName
                    : null,
                item.Color,
                commentCounts.GetValueOrDefault(item.Id),
                sharedIds.Contains(item.Id) || item.Scope != ShareScope.Private));
        }

        return result;
    }
}
