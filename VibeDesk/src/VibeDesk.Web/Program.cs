using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeDesk.Ai;
using VibeDesk.Office;
using VibeDesk.Scripting;
using VibeDesk.Application.Abstractions;
using VibeDesk.Infrastructure;
using VibeDesk.Infrastructure.Configuration;
using VibeDesk.Infrastructure.Identity;
using VibeDesk.Infrastructure.Persistence;
using VibeDesk.Ui.Services;
using VibeDesk.Web.Components;
using VibeDesk.Web.Endpoints;
using VibeDesk.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor ──────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // Surfacing the real exception in the browser is only safe in development.
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });

builder.Services.AddCascadingAuthenticationState();

// ── infrastructure: database, storage, cache, all application services ──────
builder.Services.AddVibeDeskInfrastructure(builder.Configuration);

// ── Mr Clippy: Semantic Kernel + the configured AI providers ────────────────
builder.Services.AddVibeDeskAssistant(builder.Configuration);

// ── Script & Automation engine ──────────────────────────────────────────────
builder.Services.AddVibeDeskScripting(builder.Configuration);

// Office import and export. Stateless, so one instance serves every request.
builder.Services.AddVibeDeskOffice();

// ── identity + cookie authentication ────────────────────────────────────────
builder.Services.AddVibeDeskIdentity();

builder.Services.AddScoped<SignInManager<AppUser>>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, VibeDeskClaimsPrincipalFactory>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/denied";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Cookie.Name = "vibedesk.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Always HTTPS in production; Lax + Secure is what lets the cookie survive OAuth redirects
        // if external providers are added later.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    })
    // The two-factor flows use their own short-lived cookies, kept separate from the session cookie
    // so a half-completed sign-in can never be mistaken for a full one.
    .AddCookie(IdentityConstants.TwoFactorUserIdScheme, options =>
    {
        options.Cookie.Name = "vibedesk.2fa.userid";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, options =>
    {
        options.Cookie.Name = "vibedesk.2fa.remember";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
    });

builder.Services.AddAuthorization();

// ── UI services ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<UiState>();
builder.Services.AddSingleton<MarkdownRenderer>();

builder.Services.AddHttpContextAccessor();

// ── scripting triggers ──────────────────────────────────────────────────────
// Last, deliberately. These wrap the activity log, the collaboration notifier and the current user;
// anything registered after a decorator replaces it silently, and this host registers its own
// ICurrentUser and ICollaborationNotifier above.
builder.Services.AddVibeDeskScriptingTriggers();

var app = builder.Build();

// ── database bootstrap ──────────────────────────────────────────────────────
await BootstrapDatabaseAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// Drive is the hub, so the root lands there. A redirect rather than a duplicate page keeps one
// canonical URL per view.
app.MapGet("/", () => Results.Redirect("/drive"));

app.MapAccountEndpoints();
app.MapStorageEndpoints();

app.MapRazorComponents<App>()
    // The five app pages live in VibeDesk.Ui so desktop and mobile render the same ones. Routing is
    // resolved by the endpoint system, not just by <Router>, so the assembly has to be declared here
    // too — declaring it in only one of the two places yields a 404 with no other symptom.
    .AddAdditionalAssemblies(typeof(VibeDesk.Ui.Components.Layout.AppShell).Assembly)
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>
/// Applies migrations and seeds sample data at startup when configured to.
/// </summary>
/// <remarks>
/// Runs before the request pipeline is built so a failed migration stops the app rather than
/// serving requests against a half-created schema.
/// </remarks>
static async Task BootstrapDatabaseAsync(WebApplication app)
{
    var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    if (!options.MigrateOnStartup && !options.SeedSampleData) return;

    await using var scope = app.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (options.MigrateOnStartup)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            logger.LogInformation("Applying migrations for {Provider}…", options.Provider);
            await db.Database.MigrateAsync();
        }

        if (options.SeedSampleData)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<SampleDataSeeder>();
            await seeder.SeedAsync(app.Environment.IsDevelopment());
        }
    }
    catch (Exception e)
    {
        logger.LogCritical(e, "Database bootstrap failed.");
        throw;
    }
}
