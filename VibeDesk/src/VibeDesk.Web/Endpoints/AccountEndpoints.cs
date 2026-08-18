using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VibeDesk.Infrastructure.Identity;

namespace VibeDesk.Web.Endpoints;

/// <summary>
/// Authentication endpoints as Minimal API handlers.
/// </summary>
/// <remarks>
/// Sign-in and sign-out have to write cookies, and an interactive Blazor component cannot: by the
/// time it runs, the response headers are long gone. So the auth pages are static SSR forms that post
/// here, which is also what makes them work with JavaScript disabled.
/// </remarks>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account");

        group.MapPost("/login", LoginAsync);
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/logout", LogoutAsync);
        group.MapPost("/login-2fa", LoginTwoFactorAsync);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        [FromForm] string email,
        [FromForm] string password,
        // Nullable on purpose: an unchecked checkbox sends no field, and a required bool would turn
        // "don't keep me signed in" into a 400.
        [FromForm] bool? rememberMe,
        [FromForm] string? returnUrl,
        SignInManager<AppUser> signInManager,
        UserManager<AppUser> userManager)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Redirect("/account/login", returnUrl, "Enter your email and password.");
        }

        var user = await userManager.FindByEmailAsync(email.Trim());

        if (user is not null && user.IsDisabled)
        {
            return Redirect("/account/login", returnUrl, "This account has been disabled.");
        }

        var persist = rememberMe ?? false;

        var result = await signInManager.PasswordSignInAsync(
            email.Trim(), password, persist, lockoutOnFailure: true);

        if (result.RequiresTwoFactor)
        {
            return Results.Redirect(
                $"/account/two-factor?rememberMe={persist}&returnUrl={Uri.EscapeDataString(Safe(returnUrl))}");
        }

        if (result.IsLockedOut)
        {
            return Redirect("/account/login", returnUrl,
                "Too many attempts. Try again in a few minutes.");
        }

        if (!result.Succeeded)
        {
            // One message for both wrong-password and unknown-email: distinguishing them tells an
            // attacker which addresses have accounts here.
            return Redirect("/account/login", returnUrl, "That email and password don't match.");
        }

        if (user is not null)
        {
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await userManager.UpdateAsync(user);
        }

        return Results.Redirect(Safe(returnUrl));
    }

    private static async Task<IResult> LoginTwoFactorAsync(
        [FromForm] string code,
        [FromForm] bool? rememberMe,
        [FromForm] bool? rememberMachine,
        [FromForm] string? returnUrl,
        SignInManager<AppUser> signInManager)
    {
        var normalised = (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);

        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            normalised, rememberMe ?? false, rememberMachine ?? false);

        if (result.Succeeded) return Results.Redirect(Safe(returnUrl));

        if (result.IsLockedOut)
        {
            return Redirect("/account/login", returnUrl, "Too many attempts. Try again shortly.");
        }

        // Fall back to a recovery code so a lost authenticator is not a lockout.
        var recovery = await signInManager.TwoFactorRecoveryCodeSignInAsync(normalised);
        if (recovery.Succeeded) return Results.Redirect(Safe(returnUrl));

        return Redirect("/account/two-factor", returnUrl, "That code isn't valid. Try again.");
    }

    private static async Task<IResult> RegisterAsync(
        [FromForm] string email,
        [FromForm] string displayName,
        [FromForm] string password,
        [FromForm] string confirmPassword,
        SignInManager<AppUser> signInManager,
        UserManager<AppUser> userManager,
        VibeDesk.Application.Calendars.ICalendarService calendars)
    {
        if (password != confirmPassword)
        {
            return Redirect("/account/register", null, "Those passwords don't match.");
        }

        var user = new AppUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Trim() : displayName.Trim(),
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            var message = string.Join(" ", created.Errors.Select(e => e.Description));
            return Redirect("/account/register", null, message);
        }

        await userManager.AddToRoleAsync(user, AppRoles.Member);

        await signInManager.SignInAsync(user, isPersistent: false);

        // Every user needs a primary calendar before they can create an event, so make it now rather
        // than lazily on first use.
        try
        {
            if (calendars is VibeDesk.Infrastructure.Services.CalendarService concrete)
            {
                await concrete.EnsurePrimaryAsync(user.Id);
            }
        }
        catch (Exception)
        {
            // Not fatal: the calendar page creates one on demand as a fallback.
        }

        return Results.Redirect("/drive");
    }

    private static async Task<IResult> LogoutAsync(
        [FromForm] string? returnUrl,
        SignInManager<AppUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Redirect(Safe(returnUrl));
    }

    private static IResult Redirect(string path, string? returnUrl, string error) =>
        Results.Redirect(
            $"{path}?error={Uri.EscapeDataString(error)}&returnUrl={Uri.EscapeDataString(Safe(returnUrl))}");

    /// <summary>
    /// Only allows same-site relative redirects. Echoing an arbitrary <c>returnUrl</c> back into a
    /// redirect is an open-redirect: an attacker could send a login link that lands the user on their
    /// own page immediately after authenticating.
    /// </summary>
    private static string Safe(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return "/drive";

        // Reject absolute URLs and protocol-relative ones ("//evil.example").
        if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal))
            return "/drive";

        if (returnUrl.Contains("://", StringComparison.Ordinal)) return "/drive";

        return returnUrl;
    }
}
