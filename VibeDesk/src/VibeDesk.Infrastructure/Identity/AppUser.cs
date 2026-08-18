using Microsoft.AspNetCore.Identity;

namespace VibeDesk.Infrastructure.Identity;

/// <summary>
/// The application user. Derives from <see cref="IdentityUser{Guid}"/> so 2FA, lockout and the TOTP
/// authenticator come from ASP.NET Core Identity rather than being reimplemented.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Hex colour for the generated avatar, assigned at registration.</summary>
    public string? AvatarColor { get; set; }

    /// <summary>Storage key of an uploaded avatar, when the user has replaced the generated one.</summary>
    public string? AvatarKey { get; set; }

    public string? JobTitle { get; set; }

    /// <summary>IANA time zone used to render calendars and timestamps for this user.</summary>
    public string TimeZoneId { get; set; } = "Asia/Jakarta";

    /// <summary>UI language: <c>en</c> or <c>id</c>.</summary>
    public string Locale { get; set; } = "id";

    /// <summary>light | dark | system</summary>
    public string ThemePreference { get; set; } = "system";

    /// <summary>Per-user quota in bytes. Defaults to 15 GB, matching the spec's "large capacity".</summary>
    public long QuotaBytes { get; set; } = 15L * 1024 * 1024 * 1024;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    public bool IsDisabled { get; set; }

    /// <summary>Two initials for the avatar fallback.</summary>
    public string Initials
    {
        get
        {
            var source = string.IsNullOrWhiteSpace(DisplayName) ? Email ?? "?" : DisplayName;
            var parts = source.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
        }
    }
}

public class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
}

/// <summary>Role names used in policies and seeding.</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Member = "Member";

    public static readonly string[] All = [Admin, Member];
}
