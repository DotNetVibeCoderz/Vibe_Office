using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using VibeDesk.Infrastructure.Identity;

namespace VibeDesk.Web.Services;

/// <summary>
/// Adds the display name to the auth cookie.
/// </summary>
/// <remarks>
/// Without this, rendering the user's name in the topbar would mean a database read on every circuit
/// start. The name is low-cardinality, non-sensitive and changes rarely, so carrying it in the cookie
/// is the right trade. Anything that can change permissions is deliberately *not* stored here.
/// </remarks>
public sealed class VibeDeskClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim("display_name", user.DisplayName));

        if (!string.IsNullOrEmpty(user.AvatarColor))
        {
            identity.AddClaim(new Claim("avatar_color", user.AvatarColor));
        }

        // The base factory adds email only when it is the username, which it need not be.
        if (!string.IsNullOrEmpty(user.Email) && identity.FindFirst(ClaimTypes.Email) is null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        }

        return identity;
    }
}
