using VibeDesk.Application.Calendars;
using VibeDesk.Application.Platform;
using VibeDesk.Domain;

namespace VibeDesk.Client.Services;

public sealed class CalendarApiClient(HttpClient http) : ApiClientBase(http), ICalendarService
{
    public async Task<IReadOnlyList<CalendarDto>> ListCalendarsAsync(CancellationToken ct = default) =>
        await GetAsync<List<CalendarDto>>("/api/calendars", ct).ConfigureAwait(false) ?? [];

    public async Task<CalendarDto> CreateCalendarAsync(
        string name,
        string color,
        string? description = null,
        string? timeZoneId = null,
        CancellationToken ct = default) =>
        await SendAsync<CalendarDto>(
            HttpMethod.Post, "/api/calendars", new { name, color, description, timeZoneId }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the calendar.");

    public async Task<CalendarDto> UpdateCalendarAsync(
        Guid id, string name, string color, string? description, CancellationToken ct = default) =>
        await SendAsync<CalendarDto>(HttpMethod.Put, $"/api/calendars/{id}", new { name, color, description }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the calendar.");

    public Task DeleteCalendarAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/calendars/{id}", null, ct);

    public Task ShareCalendarAsync(Guid calendarId, string email, PermissionRole role, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/calendars/{calendarId}/share", new { email, role }, ct);

    public Task UnshareCalendarAsync(Guid calendarId, Guid userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/calendars/{calendarId}/share/{userId}", null, ct);

    public async Task<IReadOnlyList<EventOccurrenceDto>> GetOccurrencesAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<Guid>? calendarIds = null,
        CancellationToken ct = default)
    {
        var url = "/api/events" + Query(("from", fromUtc), ("to", toUtc));

        if (calendarIds is { Count: > 0 })
        {
            // Repeated key rather than a comma list: that is what the model binder expects for an array.
            url += "&" + string.Join('&', calendarIds.Select(id => $"calendarId={id}"));
        }

        return await GetAsync<List<EventOccurrenceDto>>(url, ct).ConfigureAwait(false) ?? [];
    }

    public Task<EventOccurrenceDto?> GetEventAsync(Guid eventId, CancellationToken ct = default) =>
        GetAsync<EventOccurrenceDto>($"/api/events/{eventId}", ct);

    public async Task<EventOccurrenceDto> SaveEventAsync(EventInput input, CancellationToken ct = default) =>
        await SendAsync<EventOccurrenceDto>(HttpMethod.Post, "/api/events", input, ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the event.");

    public Task DeleteEventAsync(
        Guid eventId, DateTimeOffset? occurrenceStartUtc = null, CancellationToken ct = default) =>
        SendAsync(
            HttpMethod.Delete,
            $"/api/events/{eventId}" + Query(("occurrenceStartUtc", occurrenceStartUtc)),
            null,
            ct);

    public Task RespondAsync(Guid eventId, AttendeeResponse response, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/events/{eventId}/respond", new { response }, ct);

    public Task AttachDriveItemAsync(
        Guid eventId,
        Guid driveItemId,
        PermissionRole grantAttendees = PermissionRole.Viewer,
        CancellationToken ct = default) =>
        SendAsync(
            HttpMethod.Post,
            $"/api/events/{eventId}/attachments",
            new { driveItemId, grantAttendees },
            ct);

    public Task DetachDriveItemAsync(Guid eventId, Guid attachmentId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/events/{eventId}/attachments/{attachmentId}", null, ct);

    public async Task<int> ImportFromSpreadsheetAsync(
        Guid spreadsheetId,
        string sheetName,
        string range,
        Guid targetCalendarId,
        CancellationToken ct = default) =>
        (await SendAsync<CreatedResponse>(
            HttpMethod.Post,
            "/api/events/import",
            new { spreadsheetId, sheetName, range, targetCalendarId },
            ct).ConfigureAwait(false))?.Created ?? 0;

    public async Task<IReadOnlyList<BusySlot>> GetBusyAsync(
        IReadOnlyList<string> attendeeEmails,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default) =>
        await SendAsync<List<BusySlot>>(
            HttpMethod.Post, "/api/events/free-busy", new { attendeeEmails, fromUtc, toUtc }, ct)
            .ConfigureAwait(false) ?? [];

    /// <summary>
    /// The reminder sweeper runs on the server. A client calling it would be both useless and a way
    /// to make every device compete to dispatch the same reminders.
    /// </summary>
    public Task<int> DispatchDueRemindersAsync(CancellationToken ct = default) => Task.FromResult(0);

    private sealed record CreatedResponse(int Created);
}

public sealed class UserDirectoryApiClient(HttpClient http) : ApiClientBase(http), IUserDirectory
{
    public Task<UserSummaryDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<UserSummaryDto>($"/api/users/{id}", ct);

    public async Task<UserSummaryDto?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        (await SearchAsync(email, 1, ct).ConfigureAwait(false))
        .FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyDictionary<Guid, UserSummaryDto>> GetManyAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, UserSummaryDto>();

        // No batch route: the server-side implementation batches to avoid an N+1 against the database,
        // but over HTTP the round trips are the cost and these lists are short (attendees, sharers).
        foreach (var id in ids.Distinct())
        {
            if (await GetAsync(id, ct).ConfigureAwait(false) is { } user) result[id] = user;
        }

        return result;
    }

    public async Task<IReadOnlyList<UserSummaryDto>> SearchAsync(
        string keyword, int take = 10, CancellationToken ct = default) =>
        await GetAsync<List<UserSummaryDto>>(
            "/api/users/search" + Query(("q", keyword), ("take", take)), ct).ConfigureAwait(false) ?? [];
}

public sealed class NotificationApiClient(HttpClient http) : ApiClientBase(http), INotificationService
{
    public async Task<IReadOnlyList<NotificationDto>> ListAsync(
        bool unreadOnly = false, int take = 50, CancellationToken ct = default) =>
        await GetAsync<List<NotificationDto>>(
            "/api/notifications" + Query(("unreadOnly", unreadOnly), ("take", take)), ct).ConfigureAwait(false) ?? [];

    public async Task<int> CountUnreadAsync(CancellationToken ct = default) =>
        (await GetAsync<CountResponse>("/api/notifications/unread-count", ct).ConfigureAwait(false))?.Count ?? 0;

    /// <summary>
    /// Not available to a client: notifying another user is a server-side action, and exposing it
    /// over the API would let any signed-in caller push arbitrary notifications to anyone.
    /// </summary>
    public Task NotifyAsync(
        Guid userId,
        NotificationKind kind,
        string title,
        string? body = null,
        string? link = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Notifications are raised by the server, not by clients.");

    public Task MarkReadAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/notifications/{id}/read", null, ct);

    public Task MarkAllReadAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "/api/notifications/read-all", null, ct);

    private sealed record CountResponse(int Count);
}

public sealed class ActivityApiClient(HttpClient http) : ApiClientBase(http), IActivityService
{
    /// <summary>The server writes its own audit entries; a client-supplied one would be unverifiable.</summary>
    public Task LogAsync(
        string action, Guid? driveItemId = null, string? detail = null, CancellationToken ct = default) =>
        Task.CompletedTask;

    public async Task<IReadOnlyList<ActivityDto>> ForItemAsync(
        Guid itemId, int take = 50, CancellationToken ct = default) =>
        await GetAsync<List<ActivityDto>>(
            $"/api/activity/items/{itemId}" + Query(("take", take)), ct).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ActivityDto>> ForUserAsync(int take = 50, CancellationToken ct = default) =>
        await GetAsync<List<ActivityDto>>("/api/activity" + Query(("take", take)), ct).ConfigureAwait(false) ?? [];
}
