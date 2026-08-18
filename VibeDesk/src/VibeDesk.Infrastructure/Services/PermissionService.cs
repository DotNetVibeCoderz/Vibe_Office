using Microsoft.EntityFrameworkCore;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Infrastructure.Services;

/// <summary>
/// Resolves and mutates Drive access control.
/// </summary>
/// <remarks>
/// Effective role is the maximum of four sources: ownership, an explicit grant on the item, an
/// explicit grant on any ancestor (inheritance), and link sharing on the item or an ancestor.
/// Ancestors come from the materialised <see cref="DriveItem.Path"/>, so the whole resolution is a
/// single indexed query over an <c>IN</c> list rather than a walk up the tree.
/// </remarks>
public sealed class PermissionService(
    AppDbContext db,
    ICurrentUser currentUser,
    ICacheService cache,
    IUserDirectory users,
    IActivityService activity,
    INotificationService notifications) : IPermissionService
{
    /// <summary>Short TTL: long enough to collapse the repeated checks in one page render.</summary>
    private static readonly TimeSpan RoleCacheTtl = TimeSpan.FromSeconds(20);

    public async Task<PermissionRole> ResolveRoleAsync(
        Guid itemId, Guid? userId, CancellationToken ct = default)
    {
        var item = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == itemId)
            .Select(x => new { x.Id, x.OwnerId, x.Path, x.Scope, x.LinkRole })
            .FirstOrDefaultAsync(ct);

        if (item is null) return PermissionRole.None;

        if (userId is not null && item.OwnerId == userId) return PermissionRole.Owner;

        var cacheKey = $"role:{itemId}:{userId?.ToString() ?? "anon"}";

        return await cache.GetOrCreateAsync(cacheKey, async token =>
        {
            var lineage = BuildLineage(item.Id, item.Path);
            var best = PermissionRole.None;

            // Link sharing applies to the item and everything under a link-shared ancestor.
            var linkRole = await ResolveLinkRoleAsync(lineage, userId, token);
            if (linkRole > best) best = linkRole;

            if (userId is null) return best;

            var now = DateTimeOffset.UtcNow;

            var granted = await db.DriveItemPermissions
                .AsNoTracking()
                .Where(p => p.UserId == userId
                            && lineage.Contains(p.DriveItemId)
                            && (p.ExpiresAt == null || p.ExpiresAt > now))
                .Select(p => p.Role)
                .ToListAsync(token);

            foreach (var role in granted)
            {
                if (role > best) best = role;
            }

            // Owning an ancestor folder confers ownership of everything inside it.
            var ownsAncestor = await db.DriveItems
                .AsNoTracking()
                .AnyAsync(x => lineage.Contains(x.Id) && x.OwnerId == userId, token);

            if (ownsAncestor) best = PermissionRole.Owner;

            return best;
        }, RoleCacheTtl, [$"role:{itemId}", userId is null ? "role:anon" : $"user:{userId}"], ct);
    }

    private async Task<PermissionRole> ResolveLinkRoleAsync(
        List<Guid> lineage, Guid? userId, CancellationToken ct)
    {
        var shared = await db.DriveItems
            .AsNoTracking()
            .Where(x => lineage.Contains(x.Id) && x.Scope != ShareScope.Private)
            .Select(x => new { x.Scope, x.LinkRole })
            .ToListAsync(ct);

        var best = PermissionRole.None;
        foreach (var entry in shared)
        {
            // "Anyone signed in with the link" grants nothing to an anonymous visitor.
            if (entry.Scope == ShareScope.AnyoneWithLinkInternal && userId is null) continue;
            if (entry.LinkRole > best) best = entry.LinkRole;
        }

        return best;
    }

    /// <summary>
    /// The item plus every ancestor id. <see cref="DriveItem.Path"/> holds the ancestor chain as
    /// <c>/{guid}/{guid}/</c>, so this is pure string work — no database round trips per level.
    /// </summary>
    private static List<Guid> BuildLineage(Guid itemId, string path)
    {
        var lineage = new List<Guid> { itemId };

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Guid.TryParse(segment, out var ancestorId) && ancestorId != itemId)
            {
                lineage.Add(ancestorId);
            }
        }

        return lineage;
    }

    public async Task RequireAsync(Guid itemId, PermissionRole minimum, CancellationToken ct = default)
    {
        var role = await ResolveRoleAsync(itemId, currentUser.Id, ct);

        if (role == PermissionRole.None)
        {
            // Don't distinguish "doesn't exist" from "not allowed" — that difference leaks whether a
            // given id is a real document.
            throw new NotFoundException($"Item {itemId} was not found.");
        }

        if (role < minimum)
        {
            throw new ForbiddenException(
                $"This action requires {minimum} access; you have {role}.");
        }
    }

    public async Task<IReadOnlyList<PermissionDto>> ListAsync(Guid itemId, CancellationToken ct = default)
    {
        await RequireAsync(itemId, PermissionRole.Viewer, ct);

        var item = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == itemId)
            .Select(x => new { x.OwnerId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Item {itemId} was not found.");

        var grants = await db.DriveItemPermissions
            .AsNoTracking()
            .Where(p => p.DriveItemId == itemId)
            .OrderByDescending(p => p.Role)
            .ToListAsync(ct);

        var userIds = grants.Where(g => g.UserId is not null)
            .Select(g => g.UserId!.Value)
            .Append(item.OwnerId)
            .Distinct();

        var directory = await users.GetManyAsync(userIds, ct);

        var result = new List<PermissionDto>();

        // The owner is not stored as a grant row, so surface it explicitly and first.
        if (directory.TryGetValue(item.OwnerId, out var owner))
        {
            result.Add(new PermissionDto(
                Guid.Empty, owner.Id, owner.Email, owner.DisplayName,
                PermissionRole.Owner, DateTimeOffset.MinValue, null, true));
        }

        foreach (var grant in grants)
        {
            var name = grant.UserId is not null && directory.TryGetValue(grant.UserId.Value, out var user)
                ? user.DisplayName
                : grant.Email ?? "Unknown";

            result.Add(new PermissionDto(
                grant.Id, grant.UserId, grant.Email, name,
                grant.Role, grant.GrantedAt, grant.ExpiresAt, false));
        }

        return result;
    }

    public async Task<PermissionDto> ShareAsync(
        Guid itemId,
        string email,
        PermissionRole role,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default)
    {
        if (role is PermissionRole.None or PermissionRole.Owner)
            throw new ValidationException("Share role must be Viewer, Commenter or Editor.");

        if (string.IsNullOrWhiteSpace(email))
            throw new ValidationException("An email address is required.");

        await RequireAsync(itemId, PermissionRole.Owner, ct);

        var normalisedEmail = email.Trim().ToLowerInvariant();
        var grantee = await users.FindByEmailAsync(normalisedEmail, ct);

        var item = await db.DriveItems.FirstOrDefaultAsync(x => x.Id == itemId, ct)
                   ?? throw new NotFoundException($"Item {itemId} was not found.");

        if (grantee is not null && grantee.Id == item.OwnerId)
            throw new ValidationException("That user already owns this item.");

        // Re-sharing with a different role updates the existing grant rather than stacking rows.
        // Split by branch rather than using a ternary inside the predicate: EF would have to
        // translate a conditional over a possibly-null closure and would dereference it while
        // extracting parameters.
        DriveItemPermission? existing;
        if (grantee is not null)
        {
            var granteeId = grantee.Id;
            existing = await db.DriveItemPermissions.FirstOrDefaultAsync(
                p => p.DriveItemId == itemId && p.UserId == granteeId, ct);
        }
        else
        {
            existing = await db.DriveItemPermissions.FirstOrDefaultAsync(
                p => p.DriveItemId == itemId && p.Email == normalisedEmail, ct);
        }

        if (existing is not null)
        {
            existing.Role = role;
            existing.ExpiresAt = expiresAt;
            existing.GrantedById = currentUser.RequireId();
            existing.GrantedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            existing = new DriveItemPermission
            {
                DriveItemId = itemId,
                UserId = grantee?.Id,
                Email = grantee is null ? normalisedEmail : null,
                Role = role,
                ExpiresAt = expiresAt,
                GrantedById = currentUser.RequireId(),
            };
            db.DriveItemPermissions.Add(existing);
        }

        await db.SaveChangesAsync(ct);
        await InvalidateAsync(itemId, grantee?.Id, ct);

        await activity.LogAsync("item.shared", itemId,
            $"Shared with {normalisedEmail} as {role}", ct);

        if (grantee is not null)
        {
            await notifications.NotifyAsync(
                grantee.Id,
                NotificationKind.Share,
                $"{currentUser.DisplayName ?? "Someone"} shared \"{item.Name}\" with you",
                $"You now have {role} access.",
                LinkFor(item),
                ct);
        }

        return new PermissionDto(
            existing.Id,
            existing.UserId,
            existing.Email,
            grantee?.DisplayName ?? normalisedEmail,
            existing.Role,
            existing.GrantedAt,
            existing.ExpiresAt,
            false);
    }

    public async Task RevokeAsync(Guid itemId, Guid permissionId, CancellationToken ct = default)
    {
        await RequireAsync(itemId, PermissionRole.Owner, ct);

        var grant = await db.DriveItemPermissions
            .FirstOrDefaultAsync(p => p.Id == permissionId && p.DriveItemId == itemId, ct);

        if (grant is null) return;

        var affectedUser = grant.UserId;
        db.DriveItemPermissions.Remove(grant);
        await db.SaveChangesAsync(ct);

        await InvalidateAsync(itemId, affectedUser, ct);
        await activity.LogAsync("item.unshared", itemId, grant.Email ?? affectedUser?.ToString(), ct);
    }

    public async Task SetLinkSharingAsync(
        Guid itemId,
        ShareScope scope,
        PermissionRole linkRole,
        CancellationToken ct = default)
    {
        if (linkRole is PermissionRole.None or PermissionRole.Owner)
            throw new ValidationException("Link role must be Viewer, Commenter or Editor.");

        await RequireAsync(itemId, PermissionRole.Owner, ct);

        var item = await db.DriveItems.FirstOrDefaultAsync(x => x.Id == itemId, ct)
                   ?? throw new NotFoundException($"Item {itemId} was not found.");

        item.Scope = scope;
        item.LinkRole = linkRole;

        if (scope == ShareScope.Private)
        {
            // Dropping the token is what actually revokes previously circulated links.
            item.ShareToken = null;
        }
        else
        {
            item.ShareToken ??= GenerateShareToken();
        }

        await db.SaveChangesAsync(ct);

        // Scope changes affect everyone, including anonymous visitors.
        await cache.RemoveByTagAsync($"role:{itemId}", ct);
        await cache.RemoveByTagAsync("role:anon", ct);

        await activity.LogAsync("item.linkSharing", itemId, $"{scope} / {linkRole}", ct);
    }

    private static string GenerateShareToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public async Task<DriveItemDto?> ResolveShareTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var item = await db.DriveItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShareToken == token && !x.IsTrashed, ct);

        if (item is null) return null;

        if (item.Scope == ShareScope.AnyoneWithLinkInternal && !currentUser.IsAuthenticated)
            return null;

        var owner = await users.GetAsync(item.OwnerId, ct);

        return new DriveItemDto(
            item.Id, item.Type, item.Name, item.ParentId, item.OwnerId,
            owner?.DisplayName ?? "Unknown", item.SizeBytes, item.ContentType,
            item.IsStarred, item.IsTrashed, item.Scope, item.LinkRole,
            item.VersionNumber, item.CreatedAt, item.UpdatedAt, null, item.Color);
    }

    public async Task TransferOwnershipAsync(Guid itemId, Guid newOwnerId, CancellationToken ct = default)
    {
        await RequireAsync(itemId, PermissionRole.Owner, ct);

        var item = await db.DriveItems.FirstOrDefaultAsync(x => x.Id == itemId, ct)
                   ?? throw new NotFoundException($"Item {itemId} was not found.");

        if (item.OwnerId == newOwnerId) return;

        if (await users.GetAsync(newOwnerId, ct) is null)
            throw new ValidationException("The new owner does not exist.");

        var previousOwner = item.OwnerId;
        item.OwnerId = newOwnerId;

        // The outgoing owner keeps Editor so they don't lose access to their own work.
        var existing = await db.DriveItemPermissions
            .FirstOrDefaultAsync(p => p.DriveItemId == itemId && p.UserId == previousOwner, ct);

        if (existing is null)
        {
            db.DriveItemPermissions.Add(new DriveItemPermission
            {
                DriveItemId = itemId,
                UserId = previousOwner,
                Role = PermissionRole.Editor,
                GrantedById = previousOwner,
            });
        }
        else
        {
            existing.Role = PermissionRole.Editor;
        }

        // Any grant to the incoming owner is now redundant.
        var redundant = await db.DriveItemPermissions
            .Where(p => p.DriveItemId == itemId && p.UserId == newOwnerId)
            .ToListAsync(ct);
        db.DriveItemPermissions.RemoveRange(redundant);

        await db.SaveChangesAsync(ct);

        await InvalidateAsync(itemId, previousOwner, ct);
        await InvalidateAsync(itemId, newOwnerId, ct);

        await activity.LogAsync("item.ownershipTransferred", itemId, newOwnerId.ToString(), ct);
    }

    private async Task InvalidateAsync(Guid itemId, Guid? userId, CancellationToken ct)
    {
        await cache.RemoveByTagAsync($"role:{itemId}", ct);
        if (userId is not null) await cache.RemoveByTagAsync($"user:{userId}", ct);
    }

    private static string LinkFor(DriveItem item) => item.Type switch
    {
        DriveItemType.Document => $"/docs/{item.Id}",
        DriveItemType.Spreadsheet => $"/sheets/{item.Id}",
        DriveItemType.Presentation => $"/slides/{item.Id}",
        DriveItemType.Folder => $"/drive/{item.Id}",
        _ => $"/drive/file/{item.Id}",
    };
}
