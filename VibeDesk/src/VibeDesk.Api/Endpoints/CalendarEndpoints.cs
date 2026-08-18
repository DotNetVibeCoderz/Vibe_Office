using Microsoft.AspNetCore.Mvc;
using VibeDesk.Application.Calendars;
using VibeDesk.Domain;

namespace VibeDesk.Api.Endpoints;

public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var calendars = app.MapGroup("/api/calendars").WithTags("Calendar").RequireAuthorization();

        calendars.MapGet("/", async (ICalendarService service, CancellationToken ct) =>
                Results.Ok(await service.ListCalendarsAsync(ct)))
            .WithSummary("Calendars the caller can see");

        calendars.MapPost("/", async (
            CreateCalendarRequest request, ICalendarService service, CancellationToken ct) =>
                Results.Ok(await service.CreateCalendarAsync(
                    request.Name, request.Color, request.Description, request.TimeZoneId, ct)))
            .WithSummary("Create a calendar");

        calendars.MapPut("/{id:guid}", async (
            Guid id, UpdateCalendarRequest request, ICalendarService service, CancellationToken ct) =>
                Results.Ok(await service.UpdateCalendarAsync(
                    id, request.Name, request.Color, request.Description, ct)))
            .WithSummary("Update a calendar");

        calendars.MapDelete("/{id:guid}", async (Guid id, ICalendarService service, CancellationToken ct) =>
        {
            // The primary calendar is refused by the service; deleting it would leave a user with
            // nowhere to put an event.
            await service.DeleteCalendarAsync(id, ct);
            return Results.NoContent();
        }).WithSummary("Delete a calendar and its events");

        calendars.MapPost("/{id:guid}/share", async (
            Guid id, ShareCalendarRequest request, ICalendarService service, CancellationToken ct) =>
        {
            await service.ShareCalendarAsync(id, request.Email, request.Role, ct);
            return Results.NoContent();
        }).WithSummary("Share a calendar with another user");

        calendars.MapDelete("/{id:guid}/share/{userId:guid}", async (
            Guid id, Guid userId, ICalendarService service, CancellationToken ct) =>
        {
            await service.UnshareCalendarAsync(id, userId, ct);
            return Results.NoContent();
        }).WithSummary("Remove a calendar share");

        var events = app.MapGroup("/api/events").WithTags("Calendar").RequireAuthorization();

        events.MapGet("/", async (
            [FromQuery] DateTimeOffset from,
            [FromQuery] DateTimeOffset to,
            [FromQuery] Guid[]? calendarId,
            ICalendarService service,
            CancellationToken ct) =>
        {
            // Recurrence is already expanded by the service, so a client never has to understand
            // RRULE — it receives one object per occurrence in the window.
            if (to <= from) return Results.BadRequest(new { error = "'to' must be after 'from'." });

            // A year is generous for any real view and stops an unbounded expansion request.
            if (to - from > TimeSpan.FromDays(370))
            {
                return Results.BadRequest(new { error = "The window may not exceed 370 days." });
            }

            var ids = calendarId is { Length: > 0 } ? calendarId : null;

            return Results.Ok(await service.GetOccurrencesAsync(from, to, ids, ct));
        }).WithSummary("Occurrences in a window, with recurrence expanded");

        events.MapGet("/{eventId:guid}", async (
            Guid eventId, ICalendarService service, CancellationToken ct) =>
                await service.GetEventAsync(eventId, ct) is { } found
                    ? Results.Ok(found)
                    : Results.NotFound())
            .WithSummary("Get one event");

        events.MapPost("/", async (EventInput input, ICalendarService service, CancellationToken ct) =>
                Results.Ok(await service.SaveEventAsync(input, ct)))
            .WithSummary("Create or update an event; set Id to update");

        events.MapDelete("/{eventId:guid}", async (
            Guid eventId,
            [FromQuery] DateTimeOffset? occurrenceStartUtc,
            ICalendarService service,
            CancellationToken ct) =>
        {
            // With an occurrence start this cancels just that instance; without it, the whole series.
            await service.DeleteEventAsync(eventId, occurrenceStartUtc, ct);
            return Results.NoContent();
        }).WithSummary("Delete an event, or one occurrence of a series");

        events.MapPost("/{eventId:guid}/respond", async (
            Guid eventId, RespondRequest request, ICalendarService service, CancellationToken ct) =>
        {
            await service.RespondAsync(eventId, request.Response, ct);
            return Results.NoContent();
        }).WithSummary("Accept, decline or tentatively accept an invitation");

        events.MapPost("/{eventId:guid}/attachments", async (
            Guid eventId, AttachRequest request, ICalendarService service, CancellationToken ct) =>
        {
            await service.AttachDriveItemAsync(eventId, request.DriveItemId, request.GrantAttendees, ct);
            return Results.NoContent();
        }).WithSummary("Attach a Drive item; attendees are granted access");

        events.MapDelete("/{eventId:guid}/attachments/{attachmentId:guid}", async (
            Guid eventId, Guid attachmentId, ICalendarService service, CancellationToken ct) =>
        {
            await service.DetachDriveItemAsync(eventId, attachmentId, ct);
            return Results.NoContent();
        }).WithSummary("Remove an attachment from an event");

        events.MapPost("/import", async (
            ImportRequest request, ICalendarService service, CancellationToken ct) =>
                Results.Ok(new
                {
                    created = await service.ImportFromSpreadsheetAsync(
                        request.SpreadsheetId, request.SheetName, request.Range, request.TargetCalendarId, ct),
                }))
            .WithSummary("Create events from a Sheets range of title | start | end [| location]");

        events.MapPost("/free-busy", async (
            FreeBusyRequest request, ICalendarService service, CancellationToken ct) =>
                Results.Ok(await service.GetBusyAsync(request.AttendeeEmails, request.FromUtc, request.ToUtc, ct)))
            .WithSummary("Busy windows for a set of attendees; titles are hidden for private events");

        return app;
    }

    public sealed record ImportRequest(Guid SpreadsheetId, string SheetName, string Range, Guid TargetCalendarId);

    public sealed record FreeBusyRequest(
        IReadOnlyList<string> AttendeeEmails,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc);

    public sealed record CreateCalendarRequest(string Name, string Color, string? Description, string? TimeZoneId);
    public sealed record UpdateCalendarRequest(string Name, string Color, string? Description);
    public sealed record ShareCalendarRequest(string Email, PermissionRole Role);
    public sealed record RespondRequest(AttendeeResponse Response);
    public sealed record AttachRequest(
        Guid DriveItemId,
        PermissionRole GrantAttendees = PermissionRole.Viewer);
}
