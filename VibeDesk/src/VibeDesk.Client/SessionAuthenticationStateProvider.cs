using Microsoft.AspNetCore.Components.Authorization;
using VibeDesk.Client.Services;

namespace VibeDesk.Client;

/// <summary>
/// Bridges <see cref="ApiSession"/> to Blazor's authorisation system, so <c>[Authorize]</c> on the
/// shared pages behaves the same in a desktop window as it does on the server.
/// </summary>
public sealed class SessionAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly ApiSession _session;
    private readonly ClippyApiClient _clippy;

    public SessionAuthenticationStateProvider(ApiSession session, ClippyApiClient clippy)
    {
        _session = session;
        _clippy = clippy;
        _session.Changed += OnSessionChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_session.ToPrincipal()));

    private void OnSessionChanged()
    {
        // The assistant's provider list is behind authentication, so it can only be fetched once a
        // token exists. Fire-and-forget: a failed refresh degrades the panel, it does not block sign-in.
        if (_session.IsAuthenticated) _ = _clippy.RefreshStatusAsync();

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void Dispose() => _session.Changed -= OnSessionChanged;
}
