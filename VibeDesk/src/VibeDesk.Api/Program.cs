using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using VibeDesk.Ai;
using VibeDesk.Api.Endpoints;
using VibeDesk.Api.Grpc;
using VibeDesk.Api.Hubs;
using VibeDesk.Api.Security;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Platform;
using VibeDesk.Infrastructure;
using VibeDesk.Infrastructure.Persistence;
using VibeDesk.Office;
using VibeDesk.Scripting;

var builder = WebApplication.CreateBuilder(args);

// ── the same infrastructure the web host uses, from the same configuration keys ──
builder.Services.AddVibeDeskInfrastructure(builder.Configuration);
builder.Services.AddVibeDeskAssistant(builder.Configuration);

// ── the scripting engine ────────────────────────────────────────────────────
// The runtimes, the executor and the background workers. The trigger decorators are registered at
// the bottom of this file instead, where nothing can replace them.
builder.Services.AddVibeDeskScripting(builder.Configuration);

// Office import and export. Stateless, so one instance serves every request.
builder.Services.AddVibeDeskOffice();

// Identity is registered for password verification only — the API issues bearer tokens rather than
// cookies, so none of the interactive sign-in machinery is wired up.
builder.Services.AddVibeDeskIdentity();

builder.Services.AddVibeDeskApiSecurity(builder.Configuration);

// SignalR is the transport, so this host can deliver real notifications where the default
// implementation does nothing. Registered *after* infrastructure so it replaces the TryAdd fallback.
builder.Services.AddSignalR();
builder.Services.AddScoped<ICollaborationNotifier, SignalRCollaborationNotifier>();

builder.Services.AddGrpc();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Enums as names, not ordinals. A contract that says "type": 0 forces every client to hard-code
    // the ordering of a C# enum, and reordering it silently breaks them all.
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddOpenApi();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    // The desktop and mobile hosts are not browsers, but a browser-based client needs this and the
    // origins have to be explicit — AllowAnyOrigin cannot be combined with credentials.
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];

    if (origins.Length > 0) policy.WithOrigins(origins).AllowCredentials();
    else policy.SetIsOriginAllowed(_ => false);

    policy.AllowAnyHeader().AllowAnyMethod();
}));

// ── scripting triggers ──────────────────────────────────────────────────────
// Last, deliberately. These wrap the activity log, the collaboration notifier and the current user;
// anything registered after a decorator replaces it silently, and this host registers its own
// ICurrentUser and ICollaborationNotifier above.
builder.Services.AddVibeDeskScriptingTriggers();

var app = builder.Build();

// ── database ready before the first request, same as the web host ──
await BootstrapDatabaseAsync(app);

// Domain exceptions carry meaning; translating them here keeps every endpoint free of try/catch.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;

    var (status, message) = error switch
    {
        NotFoundException e => (StatusCodes.Status404NotFound, e.Message),
        // Forbidden is deliberate here: the caller can see the item but lacks the role. Where the
        // item itself is invisible, the services throw NotFoundException instead.
        ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),
        ValidationException e => (StatusCodes.Status400BadRequest, e.Message),
        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Authentication required."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };

    if (status == StatusCodes.Status500InternalServerError && error is not null)
    {
        app.Logger.LogError(error, "Unhandled exception on {Path}", context.Request.Path);
    }

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = message });
}));

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("VibeDesk API")
        .WithTheme(ScalarTheme.BluePlanet));
}

app.MapAuthEndpoints();
app.MapDriveEndpoints();
app.MapDocumentEndpoints();
app.MapCalendarEndpoints();
app.MapPlatformEndpoints();
app.MapAssistantEndpoints();
app.MapScriptEndpoints();

app.MapHub<CollaborationHub>("/hubs/collaboration");
app.MapGrpcService<DriveGrpcService>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous().ExcludeFromDescription();
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

app.Run();

// Runs before the pipeline so the first request never races a migration.
static async Task BootstrapDatabaseAsync(WebApplication app)
{
    var settings = app.Configuration.GetSection("Database");

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (settings.GetValue("MigrateOnStartup", true))
    {
        await db.Database.MigrateAsync();
    }

    if (settings.GetValue("SeedSampleData", false) && app.Environment.IsDevelopment())
    {
        await scope.ServiceProvider.GetRequiredService<SampleDataSeeder>().SeedAsync();
    }
}
