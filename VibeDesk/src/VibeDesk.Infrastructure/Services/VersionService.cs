using Microsoft.EntityFrameworkCore;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Infrastructure.Services;

/// <summary>
/// Version history for both editable documents and uploaded binaries.
/// </summary>
/// <remarks>
/// Restore is non-destructive: the current state is snapshotted first, so rolling back is itself
/// undoable. That property is the whole point of the feature — a user who restores the wrong version
/// must not have destroyed their work.
/// </remarks>
public sealed class VersionService(
    AppDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IStorageProvider storage,
    IUserDirectory users,
    IActivityService activity) : IVersionService
{
    /// <summary>
    /// Cap on retained snapshots per item. Auto-snapshots are pruned oldest-first past this point;
    /// user-labelled ones are kept, since those were deliberate.
    /// </summary>
    private const int MaxAutoSnapshots = 50;

    public async Task<IReadOnlyList<VersionDto>> ListAsync(Guid itemId, CancellationToken ct = default)
    {
        await permissions.RequireAsync(itemId, PermissionRole.Viewer, ct);

        var records = await db.DriveItemVersions
            .AsNoTracking()
            .Where(v => v.DriveItemId == itemId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);

        if (records.Count == 0) return [];

        var directory = await users.GetManyAsync(records.Select(v => v.CreatedById), ct);

        return records.Select(v => new VersionDto(
            v.Id,
            v.VersionNumber,
            v.CreatedById,
            directory.TryGetValue(v.CreatedById, out var author) ? author.DisplayName : "Unknown",
            v.CreatedAt,
            v.SizeBytes,
            v.Label,
            v.IsAutoSnapshot)).ToList();
    }

    public async Task<VersionDto> SnapshotAsync(
        Guid itemId, string? label = null, bool isAuto = false, CancellationToken ct = default)
    {
        await permissions.RequireAsync(itemId, PermissionRole.Editor, ct);

        var item = await db.DriveItems
            .AsNoTracking()
            .Include(x => x.Content)
            .FirstOrDefaultAsync(x => x.Id == itemId, ct)
            ?? throw new NotFoundException($"Item {itemId} was not found.");

        if (item.Type == DriveItemType.Folder)
            throw new ValidationException("Folders have no version history.");

        var userId = currentUser.RequireId();

        // VersionNumber is per item and must be gap-free for the UI's "Version N" labels.
        var nextNumber = await db.DriveItemVersions
            .Where(v => v.DriveItemId == itemId)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct) ?? 0;
        nextNumber++;

        var snapshot = new DriveItemVersion
        {
            DriveItemId = itemId,
            VersionNumber = nextNumber,
            CreatedById = userId,
            Label = label,
            IsAutoSnapshot = isAuto,
            SizeBytes = item.SizeBytes,
        };

        if (item.IsEditableDocument)
        {
            snapshot.Data = item.Content?.Data;
            snapshot.SizeBytes = snapshot.Data?.Length ?? 0;
        }
        else if (item.Type == DriveItemType.File && !string.IsNullOrEmpty(item.StorageKey))
        {
            // Each binary version is its own blob; copying server-side avoids streaming through us.
            snapshot.StorageKey = $"versions/{itemId:N}/{nextNumber:D5}";
            await storage.CopyAsync(item.StorageKey, snapshot.StorageKey, ct);
        }

        db.DriveItemVersions.Add(snapshot);
        await db.SaveChangesAsync(ct);

        await PruneAsync(itemId, ct);

        if (!isAuto)
        {
            await activity.LogAsync("version.saved", itemId, label ?? $"Version {nextNumber}", ct);
        }

        var author = await users.GetAsync(userId, ct);

        return new VersionDto(
            snapshot.Id, snapshot.VersionNumber, userId,
            author?.DisplayName ?? "Unknown", snapshot.CreatedAt,
            snapshot.SizeBytes, snapshot.Label, snapshot.IsAutoSnapshot);
    }

    /// <summary>Drops the oldest automatic snapshots once an item exceeds <see cref="MaxAutoSnapshots"/>.</summary>
    private async Task PruneAsync(Guid itemId, CancellationToken ct)
    {
        var autoCount = await db.DriveItemVersions
            .CountAsync(v => v.DriveItemId == itemId && v.IsAutoSnapshot, ct);

        if (autoCount <= MaxAutoSnapshots) return;

        var excess = await db.DriveItemVersions
            .Where(v => v.DriveItemId == itemId && v.IsAutoSnapshot)
            .OrderBy(v => v.VersionNumber)
            .Take(autoCount - MaxAutoSnapshots)
            .ToListAsync(ct);

        foreach (var version in excess)
        {
            // Delete the blob too, or pruning would only reclaim database rows.
            if (!string.IsNullOrEmpty(version.StorageKey))
            {
                await storage.DeleteAsync(version.StorageKey, ct);
            }
        }

        db.DriveItemVersions.RemoveRange(excess);
        await db.SaveChangesAsync(ct);
    }

    public async Task RestoreAsync(Guid itemId, Guid versionId, CancellationToken ct = default)
    {
        await permissions.RequireAsync(itemId, PermissionRole.Editor, ct);

        var version = await db.DriveItemVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId && v.DriveItemId == itemId, ct)
            ?? throw new NotFoundException("That version was not found.");

        // Capture the current state first so the restore can itself be undone.
        await SnapshotAsync(itemId, $"Before restoring version {version.VersionNumber}", isAuto: true, ct);

        var item = await db.DriveItems
            .Include(x => x.Content)
            .FirstOrDefaultAsync(x => x.Id == itemId, ct)
            ?? throw new NotFoundException($"Item {itemId} was not found.");

        var userId = currentUser.RequireId();
        var now = DateTimeOffset.UtcNow;

        if (item.IsEditableDocument)
        {
            if (version.Data is null)
                throw new ValidationException("That version has no stored content.");

            item.Content ??= new DriveItemContent { DriveItemId = itemId };
            item.Content.Data = version.Data;
            item.Content.Revision++;
            item.Content.UpdatedAt = now;
            item.Content.UpdatedById = userId;

            if (db.Entry(item.Content).State == EntityState.Detached)
            {
                db.DriveItemContents.Add(item.Content);
            }
        }
        else if (item.Type == DriveItemType.File)
        {
            if (string.IsNullOrEmpty(version.StorageKey))
                throw new ValidationException("That version has no stored file.");

            if (string.IsNullOrEmpty(item.StorageKey))
                throw new ValidationException("This item has no storage location to restore into.");

            await storage.CopyAsync(version.StorageKey, item.StorageKey, ct);
            item.SizeBytes = version.SizeBytes;
        }

        item.UpdatedAt = now;
        item.UpdatedById = userId;

        await db.SaveChangesAsync(ct);

        await activity.LogAsync("version.restored", itemId, $"Restored version {version.VersionNumber}", ct);
    }

    public async Task<string?> GetDataAsync(Guid itemId, Guid versionId, CancellationToken ct = default)
    {
        await permissions.RequireAsync(itemId, PermissionRole.Viewer, ct);

        return await db.DriveItemVersions
            .AsNoTracking()
            .Where(v => v.Id == versionId && v.DriveItemId == itemId)
            .Select(v => v.Data)
            .FirstOrDefaultAsync(ct);
    }

    public async Task DeleteAsync(Guid itemId, Guid versionId, CancellationToken ct = default)
    {
        await permissions.RequireAsync(itemId, PermissionRole.Owner, ct);

        var version = await db.DriveItemVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.DriveItemId == itemId, ct);

        if (version is null) return;

        if (!string.IsNullOrEmpty(version.StorageKey))
        {
            await storage.DeleteAsync(version.StorageKey, ct);
        }

        db.DriveItemVersions.Remove(version);
        await db.SaveChangesAsync(ct);
    }
}
