namespace VibeDesk.Domain.Entities;

/// <summary>A calendar surface. Users have one primary calendar plus any number of shared/team ones.</summary>
public class Calendar
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Hex colour used to tint this calendar's events in the grid.</summary>
    public string Color { get; set; } = "#6366f1";

    public Guid OwnerId { get; set; }

    /// <summary>The auto-created calendar for a user; cannot be deleted.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>IANA id, e.g. <c>Asia/Jakarta</c>. Events are stored in UTC and rendered in this zone.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    public ExternalCalendarKind ExternalKind { get; set; } = ExternalCalendarKind.None;
    /// <summary>Remote calendar id when mirroring Google/Outlook.</summary>
    public string? ExternalId { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<CalendarEvent> Events { get; set; } = [];
    public ICollection<CalendarShare> Shares { get; set; } = [];
}

/// <summary>Grants another user access to a whole calendar (as opposed to a single event).</summary>
public class CalendarShare
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CalendarId { get; set; }
    public Calendar? Calendar { get; set; }

    public Guid UserId { get; set; }
    public PermissionRole Role { get; set; } = PermissionRole.Viewer;
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CalendarEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CalendarId { get; set; }
    public Calendar? Calendar { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }

    /// <summary>Always UTC. All-day events use midnight UTC on <see cref="StartUtc"/>'s date.</summary>
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public bool IsAllDay { get; set; }

    public EventVisibility Visibility { get; set; } = EventVisibility.Default;
    public string? ColorOverride { get; set; }

    public RecurrenceFrequency Recurrence { get; set; } = RecurrenceFrequency.None;
    /// <summary>Every N periods of <see cref="Recurrence"/>.</summary>
    public int RecurrenceInterval { get; set; } = 1;
    public DateTimeOffset? RecurrenceUntilUtc { get; set; }
    /// <summary>Comma-separated <see cref="DayOfWeek"/> ordinals for weekly recurrence.</summary>
    public string? RecurrenceByDay { get; set; }

    /// <summary>Set on a materialised exception to a recurring series.</summary>
    public Guid? RecurringSeriesId { get; set; }
    public DateTimeOffset? OriginalStartUtc { get; set; }
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Set when the event was generated from a Sheets timeline/schedule range, which is how the
    /// Sheets+Calendar project-management integration keeps the two in step.
    /// </summary>
    public Guid? SourceSpreadsheetId { get; set; }
    public string? SourceRange { get; set; }

    public Guid OrganizerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<EventAttendee> Attendees { get; set; } = [];
    public ICollection<EventReminder> Reminders { get; set; } = [];
    /// <summary>Drive files attached to the event — the Calendar+Drive integration.</summary>
    public ICollection<EventAttachment> Attachments { get; set; } = [];
}

public class EventAttendee
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CalendarEventId { get; set; }
    public CalendarEvent? CalendarEvent { get; set; }

    public Guid? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    public AttendeeResponse Response { get; set; } = AttendeeResponse.NeedsAction;
    public bool IsOptional { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}

public class EventReminder
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CalendarEventId { get; set; }
    public CalendarEvent? CalendarEvent { get; set; }

    public ReminderMethod Method { get; set; } = ReminderMethod.Notification;
    public int MinutesBefore { get; set; } = 10;

    /// <summary>Set once dispatched so the background sweeper doesn't fire twice.</summary>
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>Links a Drive item to an event so attendees get the meeting docs with the invite.</summary>
public class EventAttachment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CalendarEventId { get; set; }
    public CalendarEvent? CalendarEvent { get; set; }

    public Guid DriveItemId { get; set; }
    public DriveItem? DriveItem { get; set; }

    /// <summary>Role automatically granted to attendees when the event is saved.</summary>
    public PermissionRole GrantAttendees { get; set; } = PermissionRole.Viewer;
}
