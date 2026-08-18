using Microsoft.AspNetCore.Mvc;
using VibeDesk.Application.Platform;

namespace VibeDesk.Api.Endpoints;

/// <summary>Users, notifications, activity, offline sync and API keys.</summary>
public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/users").WithTags("Platform").RequireAuthorization();

        users.MapGet("/search", async (
            [FromQuery] string q, [FromQuery] int? take, IUserDirectory directory, CancellationToken ct) =>
                Results.Ok(await directory.SearchAsync(q, Math.Clamp(take ?? 10, 1, 50), ct)))
            .WithSummary("Type-ahead for the share dialog and attendee picker");

        users.MapGet("/{id:guid}", async (Guid id, IUserDirectory directory, CancellationToken ct) =>
                await directory.GetAsync(id, ct) is { } user ? Results.Ok(user) : Results.NotFound())
            .WithSummary("One user's public summary");

        var notifications = app.MapGroup("/api/notifications").WithTags("Platform").RequireAuthorization();

        notifications.MapGet("/", async (
            [FromQuery] bool unreadOnly, [FromQuery] int? take, INotificationService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(unreadOnly, Math.Clamp(take ?? 50, 1, 200), ct)))
            .WithSummary("The caller's notifications");

        notifications.MapGet("/unread-count", async (INotificationService service, CancellationToken ct) =>
                Results.Ok(new { count = await service.CountUnreadAsync(ct) }))
            .WithSummary("Badge count");

        notifications.MapPost("/{id:guid}/read", async (
            Guid id, INotificationService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(id, ct);
            return Results.NoContent();
        }).WithSummary("Mark one as read");

        notifications.MapPost("/read-all", async (INotificationService service, CancellationToken ct) =>
        {
            await service.MarkAllReadAsync(ct);
            return Results.NoContent();
        }).WithSummary("Mark everything as read");

        var activity = app.MapGroup("/api/activity").WithTags("Platform").RequireAuthorization();

        activity.MapGet("/", async (
            [FromQuery] int? take, IActivityService service, CancellationToken ct) =>
                Results.Ok(await service.ForUserAsync(Math.Clamp(take ?? 50, 1, 200), ct)))
            .WithSummary("Recent activity for the caller");

        activity.MapGet("/items/{itemId:guid}", async (
            Guid itemId, [FromQuery] int? take, IActivityService service, CancellationToken ct) =>
                Results.Ok(await service.ForItemAsync(itemId, Math.Clamp(take ?? 50, 1, 200), ct)))
            .WithSummary("Audit trail for one Drive item");

        var sync = app.MapGroup("/api/sync").WithTags("Platform").RequireAuthorization();

        sync.MapPost("/devices", async (
            RegisterDeviceRequest request, ISyncService service, CancellationToken ct) =>
        {
            await service.RegisterDeviceAsync(request.DeviceId, request.DeviceName, request.Platform, ct);
            return Results.NoContent();
        }).WithSummary("Register a device before its first pull");

        sync.MapGet("/pull", async (
            [FromQuery] string deviceId,
            [FromQuery] DateTimeOffset since,
            [FromQuery] int? take,
            ISyncService service,
            CancellationToken ct) =>
                Results.Ok(await service.PullAsync(deviceId, since, Math.Clamp(take ?? 500, 1, 1000), ct)))
            .WithSummary("Everything that changed for the caller since a cursor");

        sync.MapPost("/acknowledge", async (
            AcknowledgeRequest request, ISyncService service, CancellationToken ct) =>
        {
            // Advancing the cursor is a separate call on purpose: a device that crashed while
            // applying a delta must be able to receive it again.
            await service.AcknowledgeAsync(request.DeviceId, request.Cursor, ct);
            return Results.NoContent();
        }).WithSummary("Advance a device's high-water mark after it applied a delta");

        var keys = app.MapGroup("/api/api-keys").WithTags("Platform").RequireAuthorization();

        keys.MapGet("/", async (IApiKeyService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .WithSummary("The caller's API keys — metadata only, never the secret");

        keys.MapPost("/", async (
            CreateApiKeyRequest request, IApiKeyService service, CancellationToken ct) =>
        {
            // The one moment the plaintext exists. Only the SHA-256 hash is stored, so a lost key
            // has to be replaced rather than recovered.
            var issued = await service.CreateAsync(request.Name, request.Scopes, request.ExpiresAt, ct);
            return Results.Ok(issued);
        }).WithSummary("Issue a key; the plaintext is returned exactly once");

        keys.MapDelete("/{id:guid}", async (Guid id, IApiKeyService service, CancellationToken ct) =>
        {
            await service.RevokeAsync(id, ct);
            return Results.NoContent();
        }).WithSummary("Revoke a key");

        return app;
    }

    public sealed record RegisterDeviceRequest(string DeviceId, string? DeviceName, string Platform);
    public sealed record AcknowledgeRequest(string DeviceId, DateTimeOffset Cursor);
    public sealed record CreateApiKeyRequest(string Name, string Scopes, DateTimeOffset? ExpiresAt);
}
