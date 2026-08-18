namespace VibeDesk.Domain.Entities;

/// <summary>
/// An explicit grant of a role on one item to one principal. Grants are *inherited down the tree*:
/// resolving a user's effective role walks <see cref="DriveItem.Path"/> and takes the highest match,
/// so sharing a folder shares everything under it without fanning out rows.
/// </summary>
public class DriveItemPermission
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid DriveItemId { get; set; }
    public DriveItem? DriveItem { get; set; }

    /// <summary>Grantee. Null when the grant targets <see cref="Email"/> for a not-yet-registered user.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Invite target when the grantee has no account yet; claimed on first sign-in.</summary>
    public string? Email { get; set; }

    public PermissionRole Role { get; set; } = PermissionRole.Viewer;

    public Guid GrantedById { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional expiry; the resolver ignores grants past this instant.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
