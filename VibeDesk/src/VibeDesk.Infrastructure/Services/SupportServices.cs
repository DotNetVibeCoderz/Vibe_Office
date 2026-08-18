using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Infrastructure.Services;

/// <summary>
/// Ambient identity for background work (seeding, reminder sweeps, gRPC service accounts) where there
/// is no signed-in user. Hosts with a real user override this registration.
/// </summary>
public sealed class SystemCurrentUser(Guid? userId = null, string? email = null) : ICurrentUser
{
    public Guid? Id { get; } = userId;
    public string? Email { get; } = email;
    public string? DisplayName => "System";
    public bool IsAuthenticated => Id is not null;

    public bool IsInRole(string role) => role == AppRolesConstant.Admin;

    public Guid RequireId() => Id
        ?? throw new UnauthorizedAccessException("This operation requires a signed-in user.");

    /// <summary>Mirror of the Identity role names, kept here so this file has no Identity dependency.</summary>
    private static class AppRolesConstant
    {
        public const string Admin = "Admin";
    }
}

/// <summary>
/// No-op collaboration notifier. Registered by hosts that have no realtime transport (background
/// services, the gRPC-only surface) so services can depend on the interface unconditionally.
/// </summary>
public sealed class NullCollaborationNotifier : ICollaborationNotifier
{
    public Task ContentChangedAsync(
        Guid itemId, long revision, Guid byUserId, string? patchJson = null,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task CommentsChangedAsync(Guid itemId, CancellationToken ct = default) => Task.CompletedTask;

    public Task PresenceAsync(
        Guid itemId, Guid userId, string displayName, string? cursorJson,
        CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// API keys for the public REST/gRPC surface.
/// </summary>
/// <remarks>
/// Only a SHA-256 hash of the key is stored, so a database leak does not hand over working
/// credentials, and the plaintext is returned exactly once at creation. Lookup is by hash, which is
/// why the hash column carries a unique index.
/// </remarks>
public sealed class ApiKeyService(AppDbContext db, ICurrentUser currentUser, IActivityService activity)
    : IApiKeyService
{
    private const string LivePrefix = "vd_live_";

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var keys = await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.OwnerId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        return keys.Select(k => new ApiKeyDto(
            k.Id, k.Name, k.Prefix, k.Scopes, k.CreatedAt,
            k.ExpiresAt, k.LastUsedAt, k.IsActive)).ToList();
    }

    public async Task<ApiKeyIssued> CreateAsync(
        string name, string scopes, DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("An API key needs a name.");

        var userId = currentUser.RequireId();

        // 32 random bytes, URL-safe. The prefix is a non-secret label so users can identify keys in
        // the UI without us storing any part of the secret.
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var plainText = LivePrefix + secret;

        var record = new ApiKey
        {
            OwnerId = userId,
            Name = name.Trim(),
            Prefix = plainText[..Math.Min(16, plainText.Length)],
            KeyHash = Hash(plainText),
            Scopes = string.IsNullOrWhiteSpace(scopes) ? "drive.read" : scopes.Trim(),
            ExpiresAt = expiresAt,
        };

        db.ApiKeys.Add(record);
        await db.SaveChangesAsync(ct);

        await activity.LogAsync("apikey.created", null, record.Name, ct);

        var dto = new ApiKeyDto(
            record.Id, record.Name, record.Prefix, record.Scopes,
            record.CreatedAt, record.ExpiresAt, null, true);

        return new ApiKeyIssued(dto, plainText);
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var affected = await db.ApiKeys
            .Where(k => k.Id == id && k.OwnerId == userId && k.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, DateTimeOffset.UtcNow), ct);

        if (affected > 0) await activity.LogAsync("apikey.revoked", null, id.ToString(), ct);
    }

    public async Task<Guid?> ValidateAsync(string plainTextKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plainTextKey)) return null;
        if (!plainTextKey.StartsWith(LivePrefix, StringComparison.Ordinal)) return null;

        var hash = Hash(plainTextKey);
        var now = DateTimeOffset.UtcNow;

        var record = await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.KeyHash == hash && k.RevokedAt == null
                        && (k.ExpiresAt == null || k.ExpiresAt > now))
            .Select(k => new { k.Id, k.OwnerId })
            .FirstOrDefaultAsync(ct);

        if (record is null) return null;

        // Fire-and-forget style update: last-used tracking must not slow the request path down, and a
        // lost timestamp is not worth failing the call over.
        try
        {
            await db.ApiKeys
                .Where(k => k.Id == record.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct);
        }
        catch (Exception)
        {
            // Ignored deliberately.
        }

        return record.OwnerId;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>
/// Delta sync for the Drive desktop/mobile clients and offline mode.
/// </summary>
/// <remarks>
/// Deliberately a high-water-mark cursor over <see cref="DriveItem.UpdatedAt"/> rather than a change
/// log: it needs no extra write on the hot path, and it is backed by the existing
/// <c>(OwnerId, IsTrashed, UpdatedAt)</c> index. The tradeoff is that hard-deleted items cannot be
/// reported, so deletion is expressed by trashing, and a client reconciles true absences on a full sync.
/// </remarks>
public sealed class SyncService(
    AppDbContext db,
    ICurrentUser currentUser,
    IDriveService drive) : ISyncService
{
    public async Task<SyncDelta> PullAsync(
        string deviceId, DateTimeOffset since, int take = 500, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();
        var limit = Math.Clamp(take, 1, 1000);

        var changed = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.OwnerId == userId && x.UpdatedAt > since && !x.IsTrashed)
            .OrderBy(x => x.UpdatedAt)
            .Take(limit)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var deleted = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.OwnerId == userId && x.IsTrashed && x.TrashedAt > since)
            .OrderBy(x => x.TrashedAt)
            .Take(limit)
            .Select(x => x.Id)
            .ToListAsync(ct);

        // Project through the Drive service so clients see the same DTO shape as the web UI.
        var items = new List<DriveItemDto>(changed.Count);
        foreach (var id in changed)
        {
            var dto = await drive.GetAsync(id, ct);
            if (dto is not null) items.Add(dto);
        }

        // Advance to the newest thing we actually returned, never to "now": anything modified after
        // this page must still be delivered on the next pull.
        var cursor = items.Count == 0 && deleted.Count == 0
            ? since
            : items.Select(i => i.UpdatedAt).DefaultIfEmpty(since).Max();

        return new SyncDelta(items, deleted, cursor);
    }

    public async Task AcknowledgeAsync(
        string deviceId, DateTimeOffset cursor, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var state = await db.SyncStates
            .FirstOrDefaultAsync(s => s.UserId == userId && s.DeviceId == deviceId, ct);

        if (state is null) return;

        // Never move the cursor backwards: a delayed acknowledgement must not cause re-delivery.
        if (cursor > state.Cursor) state.Cursor = cursor;
        state.LastSeenAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task RegisterDeviceAsync(
        string deviceId, string? deviceName, string platform, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ValidationException("A device id is required.");

        var userId = currentUser.RequireId();

        var state = await db.SyncStates
            .FirstOrDefaultAsync(s => s.UserId == userId && s.DeviceId == deviceId, ct);

        if (state is null)
        {
            db.SyncStates.Add(new SyncState
            {
                UserId = userId,
                DeviceId = deviceId.Trim(),
                DeviceName = deviceName,
                Platform = string.IsNullOrWhiteSpace(platform) ? "web" : platform,
            });
        }
        else
        {
            state.DeviceName = deviceName ?? state.DeviceName;
            state.Platform = platform;
            state.LastSeenAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
