using VibeDesk.Domain;

namespace VibeDesk.Application.Platform;

public sealed record UserSummaryDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? AvatarColor,
    string Initials,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles);

public sealed record NotificationDto(
    Guid Id,
    NotificationKind Kind,
    string Title,
    string? Body,
    string? Link,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record ActivityDto(
    Guid Id,
    Guid? ActorId,
    string ActorName,
    Guid? DriveItemId,
    string? ItemName,
    string Action,
    string? Detail,
    DateTimeOffset OccurredAt);

/// <summary>Lookup for turning user ids into display names without every service touching Identity.</summary>
public interface IUserDirectory
{
    Task<UserSummaryDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<UserSummaryDto?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Batch resolve — used by list projections to avoid an N+1 per row.</summary>
    Task<IReadOnlyDictionary<Guid, UserSummaryDto>> GetManyAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default);

    /// <summary>Type-ahead for the share dialog and attendee picker.</summary>
    Task<IReadOnlyList<UserSummaryDto>> SearchAsync(
        string keyword,
        int take = 10,
        CancellationToken ct = default);
}

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> ListAsync(
        bool unreadOnly = false,
        int take = 50,
        CancellationToken ct = default);

    Task<int> CountUnreadAsync(CancellationToken ct = default);

    Task NotifyAsync(
        Guid userId,
        NotificationKind kind,
        string title,
        string? body = null,
        string? link = null,
        CancellationToken ct = default);

    Task MarkReadAsync(Guid id, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);
}

public interface IActivityService
{
    /// <summary>Appends an audit entry. Never throws — logging must not fail the operation it records.</summary>
    Task LogAsync(
        string action,
        Guid? driveItemId = null,
        string? detail = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ActivityDto>> ForItemAsync(Guid itemId, int take = 50, CancellationToken ct = default);

    Task<IReadOnlyList<ActivityDto>> ForUserAsync(int take = 50, CancellationToken ct = default);
}

/// <summary>Changes a device missed while offline, for Drive file sync and offline mode.</summary>
public sealed record SyncDelta(
    IReadOnlyList<Drive.DriveItemDto> Changed,
    IReadOnlyList<Guid> Deleted,
    DateTimeOffset Cursor);

public interface ISyncService
{
    /// <summary>Everything that changed for the current user since <paramref name="since"/>.</summary>
    Task<SyncDelta> PullAsync(
        string deviceId,
        DateTimeOffset since,
        int take = 500,
        CancellationToken ct = default);

    /// <summary>Advances the device's high-water mark after it has applied a delta.</summary>
    Task AcknowledgeAsync(string deviceId, DateTimeOffset cursor, CancellationToken ct = default);

    Task RegisterDeviceAsync(
        string deviceId,
        string? deviceName,
        string platform,
        CancellationToken ct = default);
}

public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    string Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool IsActive);

/// <summary>Creation result — the only moment the plaintext key exists.</summary>
public sealed record ApiKeyIssued(ApiKeyDto Key, string PlainTextKey);

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct = default);

    Task<ApiKeyIssued> CreateAsync(
        string name,
        string scopes,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default);

    Task RevokeAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Validates a presented key and returns its owner, or null when unknown/revoked/expired.
    /// Also stamps <c>LastUsedAt</c>.
    /// </summary>
    Task<Guid?> ValidateAsync(string plainTextKey, CancellationToken ct = default);
}

/// <summary>
/// Broadcasts document changes and presence to other editors. Implemented over SignalR on the server;
/// desktop/mobile hosts use the SignalR client against the API.
/// </summary>
public interface ICollaborationNotifier
{
    /// <summary>Tells other editors of an item that its content moved to a new revision.</summary>
    Task ContentChangedAsync(
        Guid itemId,
        long revision,
        Guid byUserId,
        string? patchJson = null,
        CancellationToken ct = default);

    Task CommentsChangedAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Publishes a cursor/selection position for the presence overlay.</summary>
    Task PresenceAsync(
        Guid itemId,
        Guid userId,
        string displayName,
        string? cursorJson,
        CancellationToken ct = default);
}
