using Cuan.Components;
using Cuan.Data;
using Cuan.Models;
using Cuan.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Threading.RateLimiting;
using ApexCharts;

var builder = WebApplication.CreateBuilder(args);

var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString(provider) ?? builder.Configuration.GetConnectionString("Sqlite");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
    {
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
    }
    else
    {
        options.UseSqlite(connectionString);
    }

    options.EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
});

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/access-denied";
    options.SlidingExpiration = true;
});

// AuthZ + auth state untuk Blazor components
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 64 * 1024;
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = true;
        // Entitas EF saling menunjuk (COA punya Parent dan Children), jadi tanpa
        // ini serialisasi berputar dan endpoint balas 500.
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("ApiPolicy", config =>
    {
        config.PermitLimit = 100;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 10;
    });
});

// Antiforgery service untuk form POST SSR
builder.Services.AddAntiforgery();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("djp");

builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ReportExportService>();

// Parameter sistem dibaca sekali lalu dipakai bersama seluruh circuit.
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddScoped<DocumentNumberService>();
builder.Services.AddScoped<PostingService>();
builder.Services.AddScoped<TaxService>();
builder.Services.AddScoped<EfakturExportService>();
builder.Services.AddScoped<DjpClient>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<MasterImportPlans>();

builder.Services.AddApexCharts();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}
await DataSeeder.SeedAsync(app.Services);

// Ensure default API key exists (for Swagger auto-fill)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var defaultApiKey = builder.Configuration["AppSettings:DefaultApiKey"];
    if (!string.IsNullOrWhiteSpace(defaultApiKey))
    {
        var exists = await db.ApiKeys.AnyAsync(k => k.KeyValue == defaultApiKey);
        if (!exists)
        {
            db.ApiKeys.Add(new ApiKey
            {
                KeyName = "Default API Key",
                KeyValue = defaultApiKey,
                IsActive = true,
                ExpiresAt = DateTime.UtcNow.AddYears(1)
            });
            await db.SaveChangesAsync();
        }
    }
}

// Lengkapi tabel SystemSettings dengan parameter baru dari katalog, lalu muat
// ke cache. Basis data lama tetap terpakai — parameter yang belum ada
// ditambahkan dengan nilai bawaannya.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var settings = app.Services.GetRequiredService<SettingsService>();
    await settings.SyncCatalogAsync(db);

    // Angka dan tanggal mengikuti locale dari Pengaturan (bawaan id-ID → Rp 1.500.000).
    var culture = settings.Culture;
    CultureInfo.DefaultThreadCurrentCulture = culture;
    CultureInfo.DefaultThreadCurrentUICulture = culture;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
else
{
    var defaultApiKey = builder.Configuration["AppSettings:DefaultApiKey"] ?? string.Empty;
    var apiKeyHeader = builder.Configuration["AppSettings:ApiKeyHeader"] ?? "X-Api-Key";

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CUAN API v1");
        c.RoutePrefix = "swagger";
        c.HeadContent = $"<script>window.__DEFAULT_API_KEY='{defaultApiKey}';window.__API_KEY_HEADER='{apiKeyHeader}';</script>";
        c.InjectJavascript("/js/swagger-auth.js");
    });
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Endpoint login khusus supaya Set-Cookie tidak error di Blazor event
app.MapPost("/account/login", async (
    [FromForm] LoginRequest login,
    SignInManager<ApplicationUser> signInManager) =>
{
    var result = await signInManager.PasswordSignInAsync(
        login.Email, login.Password, isPersistent: false, lockoutOnFailure: true);

    if (result.Succeeded)
    {
        var target = string.IsNullOrWhiteSpace(login.ReturnUrl) ? "/" : login.ReturnUrl;
        return Results.LocalRedirect(target);
    }

    var error = result.IsLockedOut ? "locked" : "invalid";
    var returnUrl = string.IsNullOrWhiteSpace(login.ReturnUrl) ? "/" : login.ReturnUrl;
    return Results.LocalRedirect($"/login?error={Uri.EscapeDataString(error)}&ReturnUrl={Uri.EscapeDataString(returnUrl)}");
});

app.MapControllers();
app.MapHub<NotificationHub>("/notificationHub");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}
