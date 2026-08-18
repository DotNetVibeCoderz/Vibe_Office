using VibeDesk.Domain;

namespace VibeDesk.Application.Calendars;

public sealed record CalendarDto(
    Guid Id,
    string Name,
    string? Description,
    string Color,
    Guid OwnerId,
    string OwnerName,
    bool IsPrimary,
    string TimeZoneId,
    PermissionRole MyRole,
    ExternalCalendarKind ExternalKind,
    DateTimeOffset? LastSyncedAt)
{
    public bool CanEdit => MyRole >= PermissionRole.Editor;
}

/// <summary>
/// A single occurrence on the grid. Recurring series are expanded into one DTO per occurrence by
/// <see cref="ICalendarService.GetOccurrencesAsync"/>, so the UI never has to understand recurrence.
/// </summary>
public sealed record EventOccurrenceDto(
    Guid EventId,
    Guid CalendarId,
    string CalendarName,
    string CalendarColor,
    string Title,
    string? Description,
    string? Location,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsAllDay,
    EventVisibility Visibility,
    string? ColorOverride,
    bool IsRecurring,
    /// <summary>Start of the series master this occurrence was expanded from, for edit-series actions.</summary>
    DateTimeOffset? SeriesStartUtc,
    Guid OrganizerId,
    string OrganizerName,
    AttendeeResponse MyResponse,
    IReadOnlyList<AttendeeDto> Attendees,
    IReadOnlyList<ReminderDto> Reminders,
    IReadOnlyList<EventAttachmentDto> Attachments,
    PermissionRole MyRole)
{
    public string Color => ColorOverride ?? CalendarColor;
    public bool CanEdit => MyRole >= PermissionRole.Editor;
    public TimeSpan Duration => EndUtc - StartUtc;
}

public sealed record AttendeeDto(
    Guid Id,
    Guid? UserId,
    string Email,
    string? DisplayName,
    AttendeeResponse Response,
    bool IsOptional);

public sealed record ReminderDto(Guid Id, ReminderMethod Method, int MinutesBefore);

public sealed record EventAttachmentDto(
    Guid Id,
    Guid DriveItemId,
    string Name,
    DriveItemType Type,
    PermissionRole GrantAttendees)
{
    public string OpenRoute => Type switch
    {
        DriveItemType.Document => $"/docs/{DriveItemId}",
        DriveItemType.Spreadsheet => $"/sheets/{DriveItemId}",
        DriveItemType.Presentation => $"/slides/{DriveItemId}",
        DriveItemType.Folder => $"/drive/{DriveItemId}",
        _ => $"/drive/file/{DriveItemId}",
    };
}

/// <summary>Write model for creating and updating events, shared by the UI form and the REST API.</summary>
public sealed record EventInput
{
    public Guid? Id { get; init; }
    public Guid CalendarId { get; init; }

    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Location { get; init; }

    public DateTimeOffset StartUtc { get; init; }
    public DateTimeOffset EndUtc { get; init; }
    public bool IsAllDay { get; init; }

    public EventVisibility Visibility { get; init; } = EventVisibility.Default;
    public string? ColorOverride { get; init; }

    public RecurrenceFrequency Recurrence { get; init; } = RecurrenceFrequency.None;
    public int RecurrenceInterval { get; init; } = 1;
    public DateTimeOffset? RecurrenceUntilUtc { get; init; }
    /// <summary>Ordinals of <see cref="DayOfWeek"/> for weekly recurrence.</summary>
    public IReadOnlyList<int>? RecurrenceByDay { get; init; }

    /// <summary>Emails to invite. Existing attendees not in this list are removed.</summary>
    public IReadOnlyList<string> AttendeeEmails { get; init; } = [];
    public IReadOnlyList<ReminderInput> Reminders { get; init; } = [];

    /// <summary>Drive items to attach; attendees are granted access when the event is saved.</summary>
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];
    public PermissionRole GrantAttendees { get; init; } = PermissionRole.Viewer;
}

public sealed record ReminderInput(ReminderMethod Method, int MinutesBefore);

/// <summary>Free/busy probe result used by the "find a time" helper.</summary>
public sealed record BusySlot(DateTimeOffset StartUtc, DateTimeOffset EndUtc, string? Title);

public interface ICalendarService
{
    Task<IReadOnlyList<CalendarDto>> ListCalendarsAsync(CancellationToken ct = default);

    Task<CalendarDto> CreateCalendarAsync(
        string name,
        string color,
        string? description = null,
        string? timeZoneId = null,
        CancellationToken ct = default);

    Task<CalendarDto> UpdateCalendarAsync(
        Guid id,
        string name,
        string color,
        string? description,
        CancellationToken ct = default);

    /// <summary>Deletes a calendar and its events. The primary calendar cannot be deleted.</summary>
    Task DeleteCalendarAsync(Guid id, CancellationToken ct = default);

    Task ShareCalendarAsync(Guid calendarId, string email, PermissionRole role, CancellationToken ct = default);
    Task UnshareCalendarAsync(Guid calendarId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Every occurrence overlapping [from, to) across the given calendars (all visible ones when
    /// <paramref name="calendarIds"/> is null), with recurrence already expanded and cancelled
    /// exceptions removed.
    /// </summary>
    Task<IReadOnlyList<EventOccurrenceDto>> GetOccurrencesAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<Guid>? calendarIds = null,
        CancellationToken ct = default);

    Task<EventOccurrenceDto?> GetEventAsync(Guid eventId, CancellationToken ct = default);

    Task<EventOccurrenceDto> SaveEventAsync(EventInput input, CancellationToken ct = default);

    /// <summary>
    /// Deletes an event. For a recurring series, <paramref name="occurrenceStartUtc"/> cancels just
    /// that occurrence; null deletes the whole series.
    /// </summary>
    Task DeleteEventAsync(
        Guid eventId,
        DateTimeOffset? occurrenceStartUtc = null,
        CancellationToken ct = default);

    Task RespondAsync(Guid eventId, AttendeeResponse response, CancellationToken ct = default);

    Task AttachDriveItemAsync(
        Guid eventId,
        Guid driveItemId,
        PermissionRole grantAttendees = PermissionRole.Viewer,
        CancellationToken ct = default);

    Task DetachDriveItemAsync(Guid eventId, Guid attachmentId, CancellationToken ct = default);

    /// <summary>
    /// Creates events from a Sheets range — the Sheets+Calendar project-management integration.
    /// The range is read as <c>title | start | end [| location]</c> with a header row.
    /// </summary>
    Task<int> ImportFromSpreadsheetAsync(
        Guid spreadsheetId,
        string sheetName,
        string range,
        Guid targetCalendarId,
        CancellationToken ct = default);

    Task<IReadOnlyList<BusySlot>> GetBusyAsync(
        IReadOnlyList<string> attendeeEmails,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default);

    /// <summary>Reminders whose fire time has passed and that have not been dispatched yet.</summary>
    Task<int> DispatchDueRemindersAsync(CancellationToken ct = default);
}
