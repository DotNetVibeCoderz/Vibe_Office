namespace VibeDesk.Domain.Entities;

/// <summary>Append-only audit trail; also powers the "recent activity" panels in Drive and editors.</summary>
public class ActivityEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? ActorId { get; set; }
    public Guid? DriveItemId { get; set; }

    /// <summary>Stable verb, e.g. <c>item.created</c>, <c>item.shared</c>, <c>comment.resolved</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Human-readable summary rendered in activity feeds.</summary>
    public string? Detail { get; set; }

    public string? IpAddress { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Notification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }
    public NotificationKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }

    /// <summary>In-app route to open when the notification is clicked.</summary>
    public string? Link { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>
/// A key for the public REST/gRPC surface. Only the hash is stored; the plaintext is shown once at
/// creation time and cannot be recovered.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Short non-secret prefix (<c>vd_live_xxxx</c>) shown in the UI to identify the key.</summary>
    public string Prefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Space-separated scopes, e.g. <c>drive.read docs.write</c>.</summary>
    public string Scopes { get; set; } = "drive.read";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);
}

/// <summary>
/// A third-party add-on registration. Kept deliberately thin: an add-on is a name, an icon, the
/// scopes it asked for and a webhook we POST events to.
/// </summary>
public class AddOn
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? IconEmoji { get; set; }

    /// <summary>Which apps the add-on surfaces in, comma-separated.</summary>
    public string Surfaces { get; set; } = "docs";

    public string Scopes { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }
    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Per-user, per-device sync bookmark for the Drive file-sync and offline features. Clients send the
/// cursor they last saw and receive everything that changed since.
/// </summary>
public class SyncState
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string Platform { get; set; } = "web";

    /// <summary>High-water mark: the newest <see cref="DriveItem.UpdatedAt"/> the device has seen.</summary>
    public DateTimeOffset Cursor { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
