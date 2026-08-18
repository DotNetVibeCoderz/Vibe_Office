using System.Security.Claims;
using VibeDesk.Application.Abstractions;

namespace VibeDesk.Web.Services;

/// <summary>
/// Reads the signed-in user from the authentication state.
/// </summary>
/// <remarks>
/// Backed by <see cref="AuthenticationStateProvider"/> rather than <c>IHttpContextAccessor</c>:
/// a Blazor Server circuit outlives the HTTP request that opened it, so the accessor's context is
/// null (or worse, stale) by the time an interactive component calls a service.
/// </remarks>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly ClaimsPrincipal _principal;

    public HttpCurrentUser(Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider provider)
    {
        // Constructor-time resolution keeps every property synchronous, which is what the interface
        // promises and what call sites in EF predicates require.
        _principal = provider.GetAuthenticationStateAsync()
            .GetAwaiter()
            .GetResult()
            .User;
    }

    public Guid? Id
    {
        get
        {
            var raw = _principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Email => _principal.FindFirstValue(ClaimTypes.Email)
                            ?? _principal.FindFirstValue(ClaimTypes.Name);

    public string? DisplayName => _principal.FindFirstValue("display_name")
                                  ?? _principal.FindFirstValue(ClaimTypes.Name)
                                  ?? Email;

    public bool IsAuthenticated => _principal.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => _principal.IsInRole(role);

    public Guid RequireId() => Id
        ?? throw new UnauthorizedAccessException("This operation requires a signed-in user.");
}
