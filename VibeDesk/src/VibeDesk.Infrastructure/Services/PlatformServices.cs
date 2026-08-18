using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Platform;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Identity;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Infrastructure.Services;

/// <summary>
/// Reads user records for display. Cached because list projections resolve the same handful of
/// authors and owners over and over within a single page render.
/// </summary>
public sealed class UserDirectory(AppDbContext db, ICacheService cache) : IUserDirectory
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public Task<UserSummaryDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        cache.GetOrCreateAsync<UserSummaryDto?>($"user:{id}:summary", async token =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, token);
            return user is null ? null : await ProjectAsync(user, token);
        }, Ttl, [$"user:{id}"], ct);

    public async Task<UserSummaryDto?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalised = email.Trim().ToUpperInvariant();

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalised, ct);

        return user is null ? null : await ProjectAsync(user, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, UserSummaryDto>> GetManyAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var wanted = ids.Distinct().ToArray();
        if (wanted.Length == 0) return new Dictionary<Guid, UserSummaryDto>();

        // One query for the whole batch — this method exists specifically to avoid an N+1 per row.
        var found = await db.Users
            .AsNoTracking()
            .Where(u => wanted.Contains(u.Id))
            .ToListAsync(ct);

        var result = new Dictionary<Guid, UserSummaryDto>(found.Count);
        foreach (var user in found)
        {
            result[user.Id] = await ProjectAsync(user, ct);
        }

        return result;
    }

    public async Task<IReadOnlyList<UserSummaryDto>> SearchAsync(
        string keyword, int take = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return [];

        var pattern = $"%{keyword.Trim()}%";

        var matches = await db.Users
            .AsNoTracking()
            .Where(u => !u.IsDisabled
                        && (EF.Functions.Like(u.DisplayName, pattern)
                            || EF.Functions.Like(u.Email!, pattern)))
            .OrderBy(u => u.DisplayName)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(ct);

        var result = new List<UserSummaryDto>(matches.Count);
        foreach (var user in matches) result.Add(await ProjectAsync(user, ct));
        return result;
    }

    private async Task<UserSummaryDto> ProjectAsync(AppUser user, CancellationToken ct)
    {
        var roles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == user.Id)
            .Join(db.Roles.AsNoTracking(), ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(ct);

        return new UserSummaryDto(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.AvatarColor,
            user.Initials,
            user.TwoFactorEnabled,
            roles);
    }
}

public sealed class NotificationService(AppDbContext db, ICurrentUser currentUser) : INotificationService
{
    public async Task<IReadOnlyList<NotificationDto>> ListAsync(
        bool unreadOnly = false, int take = 50, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(n => new NotificationDto(
                n.Id, n.Kind, n.Title, n.Body, n.Link, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<int> CountUnreadAsync(CancellationToken ct = default)
    {
        var userId = currentUser.Id;
        if (userId is null) return 0;

        return await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }

    public async Task NotifyAsync(
        Guid userId,
        NotificationKind kind,
        string title,
        string? body = null,
        string? link = null,
        CancellationToken ct = default)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            Kind = kind,
            Title = title,
            Body = body,
            Link = link,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        // Scoped by user id so one account can't mark another's notifications read.
        await db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);
    }
}

/// <summary>Append-only audit log. Failures are swallowed — auditing must never break the operation.</summary>
public sealed class ActivityService(
    AppDbContext db,
    ICurrentUser currentUser,
    IUserDirectory users,
    ILogger<ActivityService> logger) : IActivityService
{
    public async Task LogAsync(
        string action,
        Guid? driveItemId = null,
        string? detail = null,
        CancellationToken ct = default)
    {
        try
        {
            db.ActivityEntries.Add(new ActivityEntry
            {
                ActorId = currentUser.Id,
                DriveItemId = driveItemId,
                Action = action,
                // The column is capped; truncate rather than letting a long detail fail the insert.
                Detail = detail is { Length: > 1000 } ? detail[..1000] : detail,
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to write activity entry {Action}", action);
        }
    }

    public async Task<IReadOnlyList<ActivityDto>> ForItemAsync(
        Guid itemId, int take = 50, CancellationToken ct = default)
    {
        var entries = await db.ActivityEntries
            .AsNoTracking()
            .Where(a => a.DriveItemId == itemId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

        return await ProjectAsync(entries, ct);
    }

    public async Task<IReadOnlyList<ActivityDto>> ForUserAsync(int take = 50, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var entries = await db.ActivityEntries
            .AsNoTracking()
            .Where(a => a.ActorId == userId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

        return await ProjectAsync(entries, ct);
    }

    private async Task<List<ActivityDto>> ProjectAsync(List<ActivityEntry> entries, CancellationToken ct)
    {
        var actorIds = entries.Where(a => a.ActorId is not null).Select(a => a.ActorId!.Value);
        var directory = await users.GetManyAsync(actorIds, ct);

        var itemIds = entries.Where(a => a.DriveItemId is not null)
            .Select(a => a.DriveItemId!.Value)
            .Distinct()
            .ToArray();

        var itemNames = itemIds.Length == 0
            ? []
            : await db.DriveItems
                .AsNoTracking()
                .Where(x => itemIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return entries.Select(a => new ActivityDto(
            a.Id,
            a.ActorId,
            a.ActorId is not null && directory.TryGetValue(a.ActorId.Value, out var actor)
                ? actor.DisplayName
                : "System",
            a.DriveItemId,
            a.DriveItemId is not null && itemNames.TryGetValue(a.DriveItemId.Value, out var name)
                ? name
                : null,
            a.Action,
            a.Detail,
            a.OccurredAt)).ToList();
    }
}
