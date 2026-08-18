using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Platform;

namespace VibeDesk.Client;

public sealed class ApiClientOptions
{
    public const string SectionName = "Api";

    public string BaseAddress { get; set; } = "https://localhost:7299";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// The signed-in session for a desktop or mobile host: holds the bearer token, exposes the current
/// user, and is the single place any request gets its Authorization header.
/// </summary>
/// <remarks>
/// A singleton, unlike the server's per-request <c>ICurrentUser</c>. A desktop app has exactly one
/// user for the life of the process, and pretending otherwise would mean re-authenticating on every
/// component render.
/// </remarks>
public sealed class ApiSession : ICurrentUser
{
    private readonly HttpClient _http;
    private UserSummaryDto? _user;

    public ApiSession(HttpClient http)
    {
        _http = http;
    }

    public string? AccessToken { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public UserSummaryDto? User => _user;

    /// <summary>Raised when the session is established or cleared, so the shell can swap views.</summary>
    public event Action? Changed;

    public Guid? Id => _user?.Id;
    public string? Email => _user?.Email;
    public string? DisplayName => _user?.DisplayName;
    public bool IsAuthenticated => _user is not null && AccessToken is not null;

    public bool IsInRole(string role) =>
        _user?.Roles.Contains(role, StringComparer.OrdinalIgnoreCase) == true;

    public Guid RequireId() => Id
        ?? throw new UnauthorizedAccessException("Sign in before using this feature.");

    /// <summary>Exchanges credentials for a bearer token. Returns null on success, or the reason.</summary>
    public async Task<string?> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        HttpResponseMessage response;

        try
        {
            response = await _http.PostAsJsonAsync(
                "/api/auth/token",
                new { email, password },
                ApiJson.Options,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"Could not reach the server ({ex.Message}).";
        }

        if (!response.IsSuccessStatusCode)
        {
            var problem = await ReadErrorAsync(response, ct).ConfigureAwait(false);
            return problem ?? "Sign-in failed.";
        }

        var payload = await response.Content
            .ReadFromJsonAsync<TokenResponse>(ApiJson.Options, ct)
            .ConfigureAwait(false);

        if (payload?.AccessToken is null || payload.User is null) return "The server returned an unusable response.";

        AccessToken = payload.AccessToken;
        ExpiresAt = payload.ExpiresAt;
        _user = payload.User;

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        Changed?.Invoke();
        return null;
    }

    public void SignOut()
    {
        AccessToken = null;
        ExpiresAt = null;
        _user = null;
        _http.DefaultRequestHeaders.Authorization = null;

        Changed?.Invoke();
    }

    /// <summary>A principal for <c>CascadingAuthenticationState</c>, built from the token's user.</summary>
    public ClaimsPrincipal ToPrincipal()
    {
        if (_user is null) return new ClaimsPrincipal(new ClaimsIdentity());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _user.Id.ToString()),
            new(ClaimTypes.Email, _user.Email),
            new(ClaimTypes.Name, _user.DisplayName),
            new("display_name", _user.DisplayName),
        };

        claims.AddRange(_user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "vibedesk-api"));
    }

    internal static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJson.Options, ct)
                .ConfigureAwait(false);

            return body?.Error;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record TokenResponse(string? AccessToken, DateTimeOffset ExpiresAt, UserSummaryDto? User);

    internal sealed record ErrorResponse(string? Error);
}

/// <summary>
/// Shared serializer settings. Must mirror the API host exactly: camelCase names and enums as names,
/// because a mismatch here shows up as a silently empty object rather than an error.
/// </summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// Turns HTTP failures back into the domain exceptions the UI already handles, so a page written
/// against the server-side services behaves identically when it is talking to the API.
/// </summary>
public abstract class ApiClientBase(HttpClient http)
{
    protected HttpClient Http { get; } = http;

    protected async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound) return default;

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<T>(ApiJson.Options, ct).ConfigureAwait(false);
    }

    protected async Task<T> GetRequiredAsync<T>(string url, CancellationToken ct) =>
        await GetAsync<T>(url, ct).ConfigureAwait(false)
        ?? throw new NotFoundException($"{url} returned nothing.");

    protected async Task<TResult?> SendAsync<TResult>(
        HttpMethod method,
        string url,
        object? body,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: ApiJson.Options);
        }

        var response = await Http.SendAsync(request, ct).ConfigureAwait(false);

        // A stale save is an expected outcome, not a failure — the caller reads the body to rebase.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return await response.Content.ReadFromJsonAsync<TResult>(ApiJson.Options, ct).ConfigureAwait(false);
        }

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent) return default;

        return await response.Content.ReadFromJsonAsync<TResult>(ApiJson.Options, ct).ConfigureAwait(false);
    }

    protected async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken ct) =>
        await SendAsync<object>(method, url, body, ct).ConfigureAwait(false);

    protected static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var message = await ApiSession.ReadErrorAsync(response, ct).ConfigureAwait(false)
                      ?? response.ReasonPhrase
                      ?? "The request failed.";

        throw response.StatusCode switch
        {
            HttpStatusCode.NotFound => new NotFoundException(message),
            HttpStatusCode.Forbidden => new ForbiddenException(message),
            HttpStatusCode.BadRequest => new ValidationException(message),
            HttpStatusCode.Unauthorized => new UnauthorizedAccessException(message),
            _ => new InvalidOperationException(message),
        };
    }

    protected static string Query(params (string Key, object? Value)[] parameters)
    {
        var pairs = parameters
            .Where(p => p.Value is not null && p.Value.ToString() is { Length: > 0 })
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(Format(p.Value!))}")
            .ToList();

        return pairs.Count == 0 ? string.Empty : "?" + string.Join('&', pairs);
    }

    private static string Format(object value) => value switch
    {
        bool b => b ? "true" : "false",
        DateTimeOffset d => d.ToString("O"),
        _ => value.ToString() ?? string.Empty,
    };
}
