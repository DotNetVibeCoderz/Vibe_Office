using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using VibeDesk.Application.Abstractions;

namespace VibeDesk.Web.Services;

/// <summary>
/// Reads the signed-in user from the authentication state.
/// </summary>
/// <remarks>
/// <para>
/// Prefers <see cref="AuthenticationStateProvider"/> over <c>IHttpContextAccessor</c>: a Blazor Server
/// circuit outlives the HTTP request that opened it, so the accessor's context is null — or worse,
/// stale — by the time an interactive component calls a service.
/// </para>
/// <para>
/// The provider only works inside a Razor component's DI scope, though, and it throws outside one.
/// That made every minimal-API endpoint in this host fail with a 500 the moment it touched a service
/// that resolves <see cref="ICurrentUser"/> — which is every file download. Outside a circuit the
/// HTTP context is both available and authoritative, so it is the fallback.
/// </para>
/// <para>
/// Resolution is deferred to first use rather than done in the constructor, so merely constructing a
/// service that depends on this cannot throw.
/// </para>
/// </remarks>
public sealed class HttpCurrentUser(
    AuthenticationStateProvider provider,
    IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? _principal;

    private ClaimsPrincipal Principal => _principal ??= Resolve();

    private ClaimsPrincipal Resolve()
    {
        try
        {
            // Synchronous by design: the interface promises synchronous properties, and call sites
            // inside EF predicates cannot await.
            return provider.GetAuthenticationStateAsync().GetAwaiter().GetResult().User;
        }
        catch (InvalidOperationException)
        {
            // No component scope — an endpoint, a background call. There is no circuit here, so the
            // request's own principal is exactly right.
            return accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        }
    }

    public Guid? Id
    {
        get
        {
            var raw = Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Email => Principal.FindFirstValue(ClaimTypes.Email)
                            ?? Principal.FindFirstValue(ClaimTypes.Name);

    public string? DisplayName => Principal.FindFirstValue("display_name")
                                  ?? Principal.FindFirstValue(ClaimTypes.Name)
                                  ?? Email;

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => Principal.IsInRole(role);

    public Guid RequireId() => Id
        ?? throw new UnauthorizedAccessException("This operation requires a signed-in user.");
}
