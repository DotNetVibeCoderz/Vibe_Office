using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Platform;

namespace VibeDesk.Api.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "vibedesk";
    public string Audience { get; set; } = "vibedesk-api";

    /// <summary>
    /// Signing key. Left empty in configuration on purpose — a committed default would be a signing
    /// key every deployment shares, so the host generates an ephemeral one and warns instead.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;

    /// <summary>Scheme name for the API-key handler, kept next to the JWT settings it sits beside.</summary>
    public const string ApiKeyScheme = "ApiKey";
    public const string ApiKeyHeader = "X-Api-Key";
}

/// <summary>Mints access tokens. Registered as a singleton so the signing key is created once.</summary>
public sealed class TokenService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;

    public TokenService(IOptions<JwtOptions> options, ILogger<TokenService> logger)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            // Ephemeral rather than a hard-coded fallback: tokens die with the process, which is
            // annoying in development and safe everywhere. A shared default key would be neither.
            var generated = RandomNumberGenerator.GetBytes(64);
            _key = new SymmetricSecurityKey(generated);

            logger.LogWarning(
                "Jwt:SigningKey is not configured. A random key was generated for this process, so " +
                "issued tokens stop working when it restarts and will not validate on another instance.");
        }
        else
        {
            _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        }
    }

    public SymmetricSecurityKey SigningKey => _key;

    public (string Token, DateTimeOffset ExpiresAt) Issue(UserSummaryDto user)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("display_name", user.DisplayName),
        };

        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}

/// <summary>
/// Authenticates the <c>X-Api-Key</c> header against <see cref="IApiKeyService"/>, which stores only
/// SHA-256 hashes. Runs as a second scheme beside JWT so integrations do not need a login round trip.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyService apiKeys,
    IUserDirectory users) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(JwtOptions.ApiKeyHeader, out var presented) ||
            string.IsNullOrWhiteSpace(presented))
        {
            // NoResult, not Fail: the request may still carry a bearer token for the other scheme.
            return AuthenticateResult.NoResult();
        }

        var userId = await apiKeys.ValidateAsync(presented.ToString());
        if (userId is null) return AuthenticateResult.Fail("Unknown or revoked API key.");

        var user = await users.GetAsync(userId.Value);
        if (user is null) return AuthenticateResult.Fail("The key's owner no longer exists.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("display_name", user.DisplayName),
        };

        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

/// <summary>
/// Ambient identity read from the HTTP context. Unlike the Blazor host this is genuinely per-request,
/// so <see cref="IHttpContextAccessor"/> is the right source here.
/// </summary>
public sealed class ApiCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? Id =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public string? DisplayName => Principal?.FindFirstValue("display_name") ?? Email;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public Guid RequireId() => Id
        ?? throw new UnauthorizedAccessException("This operation requires an authenticated caller.");
}

/// <summary>Registration for both schemes plus the pieces they need.</summary>
public static class ApiSecurityRegistration
{
    public static IServiceCollection AddVibeDeskApiSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<TokenService>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, ApiCurrentUser>();

        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    // Default is five minutes, which lets a "revoked" token keep working past expiry.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // The key is owned by TokenService, so it is resolved at first use rather than
                // duplicated here — otherwise the ephemeral-key path would sign with one key and
                // validate with another.
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var tokens = context.HttpContext.RequestServices.GetRequiredService<TokenService>();
                        context.Options.TokenValidationParameters.IssuerSigningKey = tokens.SigningKey;

                        // SignalR cannot set an Authorization header on the WebSocket handshake, so
                        // the hub accepts the token as a query parameter instead.
                        var access = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(access) &&
                            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = access;
                        }

                        return Task.CompletedTask;
                    },
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                JwtOptions.ApiKeyScheme, _ => { });

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme,
                    JwtOptions.ApiKeyScheme)
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }
}
