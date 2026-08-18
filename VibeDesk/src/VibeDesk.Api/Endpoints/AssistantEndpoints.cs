using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VibeDesk.Application.Assistant;
using VibeDesk.Domain;

namespace VibeDesk.Api.Endpoints;

/// <summary>Mr Clippy over HTTP, so the desktop and mobile hosts get the same assistant.</summary>
public static class AssistantEndpoints
{
    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var clippy = app.MapGroup("/api/assistant").WithTags("Assistant").RequireAuthorization();

        clippy.MapGet("/status", (IClippyService service) => Results.Ok(new
        {
            configured = service.IsConfigured,
            providers = service.AvailableProviders,
            models = service.AvailableProviders.ToDictionary(p => p.ToString(), service.ModelsFor),
        })).WithSummary("Which providers and models are usable");

        clippy.MapGet("/sessions", async (IClippyService service, CancellationToken ct) =>
                Results.Ok(await service.ListSessionsAsync(ct)))
            .WithSummary("The caller's conversations");

        clippy.MapPost("/sessions", async (
            CreateSessionRequest request, IClippyService service, CancellationToken ct) =>
                Results.Ok(await service.CreateSessionAsync(request.Title, request.Context, ct)))
            .WithSummary("Start a conversation");

        clippy.MapGet("/sessions/{id:guid}", async (
            Guid id, IClippyService service, CancellationToken ct) =>
                await service.GetSessionAsync(id, ct) is { } session
                    ? Results.Ok(session)
                    : Results.NotFound())
            .WithSummary("One conversation's settings");

        clippy.MapGet("/sessions/{id:guid}/messages", async (
            Guid id, IClippyService service, CancellationToken ct) =>
                Results.Ok(await service.GetMessagesAsync(id, ct)))
            .WithSummary("Transcript");

        clippy.MapDelete("/sessions/{id:guid}", async (
            Guid id, IClippyService service, CancellationToken ct) =>
        {
            await service.DeleteSessionAsync(id, ct);
            return Results.NoContent();
        }).WithSummary("Delete a conversation and its attachments");

        clippy.MapPost("/sessions/{id:guid}/reset", async (
            Guid id, IClippyService service, CancellationToken ct) =>
        {
            // Clears the turns but keeps the session, so the user loses history without losing the
            // provider and prompt they configured.
            await service.ResetSessionAsync(id, ct);
            return Results.NoContent();
        }).WithSummary("Clear the transcript, keep the settings");

        clippy.MapPatch("/sessions/{id:guid}/title", async (
            Guid id, RenameSessionRequest request, IClippyService service, CancellationToken ct) =>
        {
            await service.RenameSessionAsync(id, request.Title, ct);
            return Results.NoContent();
        }).WithSummary("Rename a conversation");

        clippy.MapPut("/sessions/{id:guid}/settings", async (
            Guid id, SessionSettingsRequest request, IClippyService service, CancellationToken ct) =>
        {
            await service.UpdateSessionSettingsAsync(
                id, request.Provider, request.Model, request.Temperature, request.SystemPrompt, ct);

            return Results.NoContent();
        }).WithSummary("Override provider, model, temperature or system prompt for this conversation");

        clippy.MapPost("/sessions/{id:guid}/messages", async (
            Guid id,
            SendMessageRequest request,
            IClippyService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Server-sent events rather than a JSON array: a reply that takes thirty seconds should
            // start rendering immediately, and SSE survives proxies that buffer chunked JSON.
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            await foreach (var chunk in service
                .SendAsync(id, request.Message, request.Context, request.Attachments, ct)
                .WithCancellation(ct))
            {
                await http.Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(chunk, options)}\n\n", ct);

                await http.Response.Body.FlushAsync(ct);
            }
        }).WithSummary("Send a message; the reply streams back as server-sent events");

        clippy.MapPost("/attachments", async (
            IFormFile file, IClippyService service, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();

            return Results.Ok(await service.UploadAttachmentAsync(
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                stream,
                ct));
        })
            .DisableAntiforgery()
            .WithSummary("Upload an image or document to attach to a turn");

        return app;
    }

    public sealed record CreateSessionRequest(string? Title, ClippyContext? Context);
    public sealed record RenameSessionRequest(string Title);

    public sealed record SessionSettingsRequest(
        AiProvider? Provider,
        string? Model,
        double? Temperature,
        string? SystemPrompt);

    public sealed record SendMessageRequest(
        string Message,
        ClippyContext? Context,
        IReadOnlyList<ChatAttachmentInput>? Attachments);
}
