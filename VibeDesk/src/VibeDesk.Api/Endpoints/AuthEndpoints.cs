using Microsoft.AspNetCore.Identity;
using VibeDesk.Api.Security;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Platform;
using VibeDesk.Infrastructure.Identity;

namespace VibeDesk.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/token", async (
            LoginRequest request,
            UserManager<AppUser> users,
            IUserDirectory directory,
            TokenService tokens,
            CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(request.Email ?? string.Empty);

            // One message for "no such account" and "wrong password". Distinguishing them turns the
            // endpoint into an account-enumeration oracle.
            const string denied = "Invalid email or password.";

            if (user is null || !await users.CheckPasswordAsync(user, request.Password ?? string.Empty))
            {
                if (user is not null) await users.AccessFailedAsync(user);
                return Results.Json(new { error = denied }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (await users.IsLockedOutAsync(user))
            {
                return Results.Json(
                    new { error = "This account is temporarily locked. Try again later." },
                    statusCode: StatusCodes.Status423Locked);
            }

            // Two-factor is enforced by the interactive host; the API refuses rather than silently
            // issuing a token that skips the second factor.
            if (await users.GetTwoFactorEnabledAsync(user))
            {
                return Results.Json(
                    new { error = "This account uses two-factor authentication. Sign in on the web app and use an API key for automation." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await users.ResetAccessFailedCountAsync(user);

            var summary = await directory.GetAsync(user.Id, ct);
            if (summary is null) return Results.Problem("The account could not be loaded.");

            var (token, expires) = tokens.Issue(summary);

            return Results.Ok(new
            {
                accessToken = token,
                tokenType = "Bearer",
                expiresAt = expires,
                user = summary,
            });
        })
            .AllowAnonymous()
            .WithSummary("Exchange email and password for a bearer token");

        auth.MapGet("/me", async (ICurrentUser current, IUserDirectory directory, CancellationToken ct) =>
                await directory.GetAsync(current.RequireId(), ct) is { } me
                    ? Results.Ok(me)
                    : Results.NotFound())
            .RequireAuthorization()
            .WithSummary("The authenticated caller");

        return app;
    }

    public sealed record LoginRequest(string? Email, string? Password);
}
