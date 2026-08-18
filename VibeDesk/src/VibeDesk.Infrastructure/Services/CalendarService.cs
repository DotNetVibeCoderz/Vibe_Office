using Microsoft.EntityFrameworkCore;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Calendars;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Application.Spreadsheets;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Infrastructure.Services;

/// <summary>
/// Calendars, events, invitations and the Drive/Sheets integrations.
/// </summary>
/// <remarks>
/// Recurring events are stored once as a master row and expanded into occurrences on read. Nothing is
/// materialised in the database except *exceptions* (a moved or cancelled single occurrence), which
/// keeps an event repeating "every Monday forever" from becoming an unbounded number of rows.
/// </remarks>
public sealed class CalendarService(
    AppDbContext db,
    ICurrentUser currentUser,
    IUserDirectory users,
    IPermissionService permissions,
    IDocumentContentService content,
    IActivityService activity,
    INotificationService notifications) : ICalendarService
{
    /// <summary>
    /// Hard cap on generated occurrences per series per query. A daily series over a year view is ~365;
    /// this stops a malformed series (interval 0, absurd window) from generating without bound.
    /// </summary>
    private const int MaxOccurrencesPerSeries = 1000;

    // ─────────────────────────────────────── calendars ───────────────────────────────────────

    public async Task<IReadOnlyList<CalendarDto>> ListCalendarsAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var owned = await db.Calendars
            .AsNoTracking()
            .Where(c => c.OwnerId == userId)
            .ToListAsync(ct);

        var sharedWithRoles = await db.CalendarShares
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Join(db.Calendars.AsNoTracking(), s => s.CalendarId, c => c.Id,
                (s, c) => new { Calendar = c, s.Role })
            .ToListAsync(ct);

        var ownerIds = owned.Select(c => c.OwnerId)
            .Concat(sharedWithRoles.Select(x => x.Calendar.OwnerId))
            .Distinct();
        var directory = await users.GetManyAsync(ownerIds, ct);

        var result = owned
            .Select(c => Project(c, PermissionRole.Owner, directory))
            .Concat(sharedWithRoles.Select(x => Project(x.Calendar, x.Role, directory)))
            // Primary first, then alphabetically — the order the sidebar wants.
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.Name)
            .ToList();

        return result;
    }

    private static CalendarDto Project(
        Calendar calendar, PermissionRole role, IReadOnlyDictionary<Guid, UserSummaryDto> directory) =>
        new(calendar.Id, calendar.Name, calendar.Description, calendar.Color, calendar.OwnerId,
            directory.TryGetValue(calendar.OwnerId, out var owner) ? owner.DisplayName : "Unknown",
            calendar.IsPrimary, calendar.TimeZoneId, role, calendar.ExternalKind, calendar.LastSyncedAt);

    /// <summary>
    /// Returns the user's primary calendar, creating it on first access. Every user is guaranteed one,
    /// so event creation never has to ask which calendar to use.
    /// </summary>
    public async Task<Calendar> EnsurePrimaryAsync(Guid userId, CancellationToken ct = default)
    {
        var primary = await db.Calendars.FirstOrDefaultAsync(c => c.OwnerId == userId && c.IsPrimary, ct);
        if (primary is not null) return primary;

        var timeZone = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(ct) ?? "UTC";

        primary = new Calendar
        {
            Name = "My calendar",
            OwnerId = userId,
            IsPrimary = true,
            Color = "#6366f1",
            TimeZoneId = timeZone,
        };

        db.Calendars.Add(primary);
        await db.SaveChangesAsync(ct);
        return primary;
    }

    public async Task<CalendarDto> CreateCalendarAsync(
        string name, string color, string? description = null, string? timeZoneId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("A calendar needs a name.");

        var userId = currentUser.RequireId();

        var calendar = new Calendar
        {
            Name = name.Trim(),
            Color = string.IsNullOrWhiteSpace(color) ? "#6366f1" : color,
            Description = description,
            OwnerId = userId,
            TimeZoneId = timeZoneId ?? "UTC",
        };

        db.Calendars.Add(calendar);
        await db.SaveChangesAsync(ct);

        var directory = await users.GetManyAsync([userId], ct);
        return Project(calendar, PermissionRole.Owner, directory);
    }

    public async Task<CalendarDto> UpdateCalendarAsync(
        Guid id, string name, string color, string? description, CancellationToken ct = default)
    {
        var calendar = await RequireCalendarAsync(id, PermissionRole.Editor, ct);

        if (!string.IsNullOrWhiteSpace(name)) calendar.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(color)) calendar.Color = color;
        calendar.Description = description;

        await db.SaveChangesAsync(ct);

        var directory = await users.GetManyAsync([calendar.OwnerId], ct);
        var role = await ResolveCalendarRoleAsync(calendar, currentUser.Id, ct);
        return Project(calendar, role, directory);
    }

    public async Task DeleteCalendarAsync(Guid id, CancellationToken ct = default)
    {
        var calendar = await RequireCalendarAsync(id, PermissionRole.Owner, ct);

        if (calendar.IsPrimary)
            throw new ValidationException("The primary calendar cannot be deleted.");

        db.Calendars.Remove(calendar);
        await db.SaveChangesAsync(ct);

        await activity.LogAsync("calendar.deleted", null, calendar.Name, ct);
    }

    public async Task ShareCalendarAsync(
        Guid calendarId, string email, PermissionRole role, CancellationToken ct = default)
    {
        if (role is PermissionRole.None or PermissionRole.Owner)
            throw new ValidationException("Calendar role must be Viewer, Commenter or Editor.");

        var calendar = await RequireCalendarAsync(calendarId, PermissionRole.Owner, ct);

        var grantee = await users.FindByEmailAsync(email, ct)
                      ?? throw new ValidationException($"No user found with the email {email}.");

        if (grantee.Id == calendar.OwnerId)
            throw new ValidationException("That user already owns this calendar.");

        var existing = await db.CalendarShares
            .FirstOrDefaultAsync(s => s.CalendarId == calendarId && s.UserId == grantee.Id, ct);

        if (existing is null)
        {
            db.CalendarShares.Add(new CalendarShare
            {
                CalendarId = calendarId,
                UserId = grantee.Id,
                Role = role,
            });
        }
        else
        {
            existing.Role = role;
        }

        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(
            grantee.Id,
            NotificationKind.Share,
            $"{currentUser.DisplayName ?? "Someone"} shared the calendar \"{calendar.Name}\" with you",
            $"You now have {role} access.",
            "/calendar",
            ct);
    }

    public async Task UnshareCalendarAsync(Guid calendarId, Guid userId, CancellationToken ct = default)
    {
        await RequireCalendarAsync(calendarId, PermissionRole.Owner, ct);

        await db.CalendarShares
            .Where(s => s.CalendarId == calendarId && s.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }

    // ──────────────────────────────────────── events ────────────────────────────────────────

    public async Task<IReadOnlyList<EventOccurrenceDto>> GetOccurrencesAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<Guid>? calendarIds = null,
        CancellationToken ct = default)
    {
        if (toUtc <= fromUtc) return [];

        var userId = currentUser.RequireId();
        var visible = await GetVisibleCalendarsAsync(userId, ct);

        if (calendarIds is { Count: > 0 })
        {
            var wanted = calendarIds.ToHashSet();
            visible = visible.Where(c => wanted.Contains(c.Calendar.Id)).ToList();
        }

        if (visible.Count == 0) return [];

        var calendarLookup = visible.ToDictionary(c => c.Calendar.Id);
        var ids = calendarLookup.Keys.ToArray();

        // Fetch non-recurring events overlapping the window, plus every recurring master that could
        // still be active — a master's own StartUtc may be long before the window.
        var candidates = await db.CalendarEvents
            .AsNoTracking()
            .Include(e => e.Attendees)
            .Include(e => e.Reminders)
            .Include(e => e.Attachments)
            .Where(e => ids.Contains(e.CalendarId)
                        && e.RecurringSeriesId == null
                        && ((e.Recurrence == RecurrenceFrequency.None
                             && e.StartUtc < toUtc && e.EndUtc > fromUtc)
                            || (e.Recurrence != RecurrenceFrequency.None
                                && e.StartUtc < toUtc
                                && (e.RecurrenceUntilUtc == null || e.RecurrenceUntilUtc >= fromUtc))))
            .ToListAsync(ct);

        // Exceptions: single occurrences that were edited or cancelled out of a series.
        var seriesIds = candidates
            .Where(e => e.Recurrence != RecurrenceFrequency.None)
            .Select(e => e.Id)
            .ToArray();

        var exceptions = seriesIds.Length == 0
            ? []
            : await db.CalendarEvents
                .AsNoTracking()
                .Include(e => e.Attendees)
                .Include(e => e.Reminders)
                .Include(e => e.Attachments)
                .Where(e => e.RecurringSeriesId != null && seriesIds.Contains(e.RecurringSeriesId!.Value))
                .ToListAsync(ct);

        var exceptionsBySeries = exceptions
            .Where(e => e.OriginalStartUtc is not null)
            .GroupBy(e => e.RecurringSeriesId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(e => e.OriginalStartUtc!.Value, e => e));

        var organiserIds = candidates.Select(e => e.OrganizerId)
            .Concat(exceptions.Select(e => e.OrganizerId))
            .Distinct();
        var directory = await users.GetManyAsync(organiserIds, ct);

        var attachmentNames = await LoadAttachmentNamesAsync(
            candidates.Concat(exceptions).SelectMany(e => e.Attachments), ct);

        var result = new List<EventOccurrenceDto>();

        foreach (var master in candidates)
        {
            var (calendar, role) = calendarLookup[master.CalendarId];

            if (master.Recurrence == RecurrenceFrequency.None)
            {
                if (master.IsCancelled) continue;
                result.Add(ProjectOccurrence(
                    master, master.StartUtc, master.EndUtc, calendar, role,
                    directory, attachmentNames, isRecurring: false, seriesStart: null, userId));
                continue;
            }

            exceptionsBySeries.TryGetValue(master.Id, out var seriesExceptions);

            foreach (var start in ExpandRecurrence(master, fromUtc, toUtc))
            {
                // An exception replaces the generated occurrence at that instant.
                if (seriesExceptions is not null && seriesExceptions.TryGetValue(start, out var replacement))
                {
                    if (replacement.IsCancelled) continue;
                    if (replacement.StartUtc >= toUtc || replacement.EndUtc <= fromUtc) continue;

                    result.Add(ProjectOccurrence(
                        replacement, replacement.StartUtc, replacement.EndUtc, calendar, role,
                        directory, attachmentNames, isRecurring: true, master.StartUtc, userId));
                    continue;
                }

                var end = start + (master.EndUtc - master.StartUtc);
                if (start >= toUtc || end <= fromUtc) continue;

                result.Add(ProjectOccurrence(
                    master, start, end, calendar, role,
                    directory, attachmentNames, isRecurring: true, master.StartUtc, userId));
            }
        }

        return result.OrderBy(o => o.StartUtc).ThenBy(o => o.Title).ToList();
    }

    /// <summary>
    /// Generates the start instants of a recurring series that fall inside a window.
    /// </summary>
    /// <remarks>
    /// For weekly recurrence with <see cref="CalendarEvent.RecurrenceByDay"/>, the interval applies to
    /// whole weeks and the by-day list selects days within each active week — so "every other Tue+Thu"
    /// behaves as users expect rather than advancing day by day.
    /// </remarks>
    private static IEnumerable<DateTimeOffset> ExpandRecurrence(
        CalendarEvent master, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var interval = Math.Max(1, master.RecurrenceInterval);
        var hardEnd = master.RecurrenceUntilUtc is null
            ? toUtc
            : (master.RecurrenceUntilUtc.Value < toUtc ? master.RecurrenceUntilUtc.Value : toUtc);

        if (master.Recurrence == RecurrenceFrequency.Weekly
            && !string.IsNullOrWhiteSpace(master.RecurrenceByDay))
        {
            return ExpandWeeklyByDay(master, fromUtc, hardEnd, interval);
        }

        return ExpandSimple(master, fromUtc, hardEnd, interval);
    }

    private static IEnumerable<DateTimeOffset> ExpandSimple(
        CalendarEvent master, DateTimeOffset fromUtc, DateTimeOffset hardEnd, int interval)
    {
        var current = master.StartUtc;
        var duration = master.EndUtc - master.StartUtc;
        var emitted = 0;

        while (current <= hardEnd && emitted < MaxOccurrencesPerSeries)
        {
            // Only yield once the occurrence's *end* is inside the window, so an event already in
            // progress at the window start is still returned.
            if (current + duration > fromUtc)
            {
                yield return current;
                emitted++;
            }

            current = master.Recurrence switch
            {
                RecurrenceFrequency.Daily => current.AddDays(interval),
                RecurrenceFrequency.Weekly => current.AddDays(7 * interval),
                RecurrenceFrequency.Monthly => current.AddMonths(interval),
                RecurrenceFrequency.Yearly => current.AddYears(interval),
                _ => hardEnd.AddDays(1),
            };
        }
    }

    private static IEnumerable<DateTimeOffset> ExpandWeeklyByDay(
        CalendarEvent master, DateTimeOffset fromUtc, DateTimeOffset hardEnd, int interval)
    {
        var days = master.RecurrenceByDay!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var d) ? d : -1)
            .Where(d => d is >= 0 and <= 6)
            .Distinct()
            .Order()
            .ToArray();

        if (days.Length == 0)
        {
            foreach (var start in ExpandSimple(master, fromUtc, hardEnd, interval)) yield return start;
            yield break;
        }

        var duration = master.EndUtc - master.StartUtc;
        var timeOfDay = master.StartUtc.TimeOfDay;

        // Anchor to the Sunday of the series' first week so interval counting is stable.
        var anchorWeekStart = master.StartUtc.Date.AddDays(-(int)master.StartUtc.DayOfWeek);
        var weekStart = new DateTimeOffset(anchorWeekStart, TimeSpan.Zero);
        var emitted = 0;

        while (weekStart <= hardEnd && emitted < MaxOccurrencesPerSeries)
        {
            foreach (var day in days)
            {
                var candidate = weekStart.AddDays(day).Add(timeOfDay);

                if (candidate < master.StartUtc) continue;
                if (candidate > hardEnd) continue;
                if (candidate + duration <= fromUtc) continue;

                yield return candidate;
                if (++emitted >= MaxOccurrencesPerSeries) yield break;
            }

            weekStart = weekStart.AddDays(7 * interval);
        }
    }

    private EventOccurrenceDto ProjectOccurrence(
        CalendarEvent source,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        Calendar calendar,
        PermissionRole role,
        IReadOnlyDictionary<Guid, UserSummaryDto> directory,
        IReadOnlyDictionary<Guid, (string Name, DriveItemType Type)> attachmentNames,
        bool isRecurring,
        DateTimeOffset? seriesStart,
        Guid userId)
    {
        var myResponse = source.Attendees
            .FirstOrDefault(a => a.UserId == userId)?.Response ?? AttendeeResponse.NeedsAction;

        return new EventOccurrenceDto(
            source.Id,
            calendar.Id,
            calendar.Name,
            calendar.Color,
            source.Title,
            source.Description,
            source.Location,
            startUtc,
            endUtc,
            source.IsAllDay,
            source.Visibility,
            source.ColorOverride,
            isRecurring,
            seriesStart,
            source.OrganizerId,
            directory.TryGetValue(source.OrganizerId, out var organiser) ? organiser.DisplayName : "Unknown",
            myResponse,
            source.Attendees.Select(a => new AttendeeDto(
                a.Id, a.UserId, a.Email, a.DisplayName, a.Response, a.IsOptional)).ToList(),
            source.Reminders.Select(r => new ReminderDto(r.Id, r.Method, r.MinutesBefore)).ToList(),
            source.Attachments.Select(a => new EventAttachmentDto(
                a.Id,
                a.DriveItemId,
                attachmentNames.TryGetValue(a.DriveItemId, out var info) ? info.Name : "Attachment",
                attachmentNames.TryGetValue(a.DriveItemId, out var typed) ? typed.Type : DriveItemType.File,
                a.GrantAttendees)).ToList(),
            role);
    }

    private async Task<IReadOnlyDictionary<Guid, (string Name, DriveItemType Type)>>
        LoadAttachmentNamesAsync(IEnumerable<EventAttachment> attachments, CancellationToken ct)
    {
        var ids = attachments.Select(a => a.DriveItemId).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, (string, DriveItemType)>();

        var rows = await db.DriveItems
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Type })
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.Id, x => (x.Name, x.Type));
    }

    public async Task<EventOccurrenceDto?> GetEventAsync(Guid eventId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var entity = await db.CalendarEvents
            .AsNoTracking()
            .Include(e => e.Attendees)
            .Include(e => e.Reminders)
            .Include(e => e.Attachments)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);

        if (entity is null) return null;

        var calendar = await db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == entity.CalendarId, ct);
        if (calendar is null) return null;

        var role = await ResolveCalendarRoleAsync(calendar, userId, ct);

        // An attendee can see an event even without access to the calendar it lives on.
        var isAttendee = entity.Attendees.Any(a => a.UserId == userId);
        if (role == PermissionRole.None && !isAttendee) return null;
        if (role == PermissionRole.None) role = PermissionRole.Viewer;

        var directory = await users.GetManyAsync([entity.OrganizerId], ct);
        var attachmentNames = await LoadAttachmentNamesAsync(entity.Attachments, ct);

        return ProjectOccurrence(
            entity, entity.StartUtc, entity.EndUtc, calendar, role,
            directory, attachmentNames,
            entity.Recurrence != RecurrenceFrequency.None,
            entity.Recurrence != RecurrenceFrequency.None ? entity.StartUtc : null,
            userId);
    }

    public async Task<EventOccurrenceDto> SaveEventAsync(
        EventInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            throw new ValidationException("An event needs a title.");

        if (input.EndUtc <= input.StartUtc)
            throw new ValidationException("The event must end after it starts.");

        var userId = currentUser.RequireId();
        var calendar = await RequireCalendarAsync(input.CalendarId, PermissionRole.Editor, ct);

        CalendarEvent entity;

        if (input.Id is not null)
        {
            entity = await db.CalendarEvents
                .Include(e => e.Attendees)
                .Include(e => e.Reminders)
                .Include(e => e.Attachments)
                .FirstOrDefaultAsync(e => e.Id == input.Id, ct)
                ?? throw new NotFoundException("That event was not found.");
        }
        else
        {
            entity = new CalendarEvent { OrganizerId = userId };
            db.CalendarEvents.Add(entity);
        }

        entity.CalendarId = input.CalendarId;
        entity.Title = input.Title.Trim();
        entity.Description = input.Description;
        entity.Location = input.Location;
        entity.StartUtc = input.StartUtc;
        entity.EndUtc = input.EndUtc;
        entity.IsAllDay = input.IsAllDay;
        entity.Visibility = input.Visibility;
        entity.ColorOverride = input.ColorOverride;
        entity.Recurrence = input.Recurrence;
        entity.RecurrenceInterval = Math.Max(1, input.RecurrenceInterval);
        entity.RecurrenceUntilUtc = input.RecurrenceUntilUtc;
        entity.RecurrenceByDay = input.RecurrenceByDay is { Count: > 0 }
            ? string.Join(',', input.RecurrenceByDay)
            : null;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await SyncAttendeesAsync(entity, input.AttendeeEmails, ct);
        SyncReminders(entity, input.Reminders);
        await SyncAttachmentsAsync(entity, input.AttachmentIds, input.GrantAttendees, ct);

        await db.SaveChangesAsync(ct);

        // Attaching Drive files is only useful if attendees can open them, so grant access here.
        await GrantAttachmentAccessAsync(entity, ct);

        await activity.LogAsync(
            input.Id is null ? "event.created" : "event.updated", null, entity.Title, ct);

        await NotifyAttendeesAsync(entity, calendar, input.Id is null, ct);

        return await GetEventAsync(entity.Id, ct)
               ?? throw new InvalidOperationException("Event saved but could not be re-read.");
    }

    private async Task SyncAttendeesAsync(
        CalendarEvent entity, IReadOnlyList<string> emails, CancellationToken ct)
    {
        var normalised = emails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        // Remove attendees no longer listed.
        var removed = entity.Attendees
            .Where(a => !normalised.Contains(a.Email, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var attendee in removed) entity.Attendees.Remove(attendee);

        var existing = entity.Attendees
            .Select(a => a.Email.ToLowerInvariant())
            .ToHashSet();

        foreach (var email in normalised.Where(e => !existing.Contains(e)))
        {
            // Resolve to a user id when we can, so the attendee sees the event in their own calendar.
            var user = await users.FindByEmailAsync(email, ct);

            entity.Attendees.Add(new EventAttendee
            {
                CalendarEventId = entity.Id,
                Email = email,
                UserId = user?.Id,
                DisplayName = user?.DisplayName,
                // The organiser is implicitly attending.
                Response = user?.Id == entity.OrganizerId
                    ? AttendeeResponse.Accepted
                    : AttendeeResponse.NeedsAction,
            });
        }
    }

    private static void SyncReminders(CalendarEvent entity, IReadOnlyList<ReminderInput> reminders)
    {
        entity.Reminders.Clear();

        foreach (var reminder in reminders.Take(5))
        {
            entity.Reminders.Add(new EventReminder
            {
                CalendarEventId = entity.Id,
                Method = reminder.Method,
                MinutesBefore = Math.Clamp(reminder.MinutesBefore, 0, 40_320),
            });
        }
    }

    private async Task SyncAttachmentsAsync(
        CalendarEvent entity, IReadOnlyList<Guid> attachmentIds, PermissionRole grant, CancellationToken ct)
    {
        var wanted = attachmentIds.Distinct().ToList();

        var removed = entity.Attachments.Where(a => !wanted.Contains(a.DriveItemId)).ToList();
        foreach (var attachment in removed) entity.Attachments.Remove(attachment);

        var existing = entity.Attachments.Select(a => a.DriveItemId).ToHashSet();

        foreach (var id in wanted.Where(id => !existing.Contains(id)))
        {
            // Only attach what the organiser may actually share.
            var role = await permissions.ResolveRoleAsync(id, currentUser.Id, ct);
            if (role < PermissionRole.Viewer) continue;

            entity.Attachments.Add(new EventAttachment
            {
                CalendarEventId = entity.Id,
                DriveItemId = id,
                GrantAttendees = grant,
            });
        }
    }

    /// <summary>
    /// Shares each attached Drive item with each attendee. Failures are tolerated per pair: the
    /// organiser may not own every attachment, and that should not fail saving the event.
    /// </summary>
    private async Task GrantAttachmentAccessAsync(CalendarEvent entity, CancellationToken ct)
    {
        if (entity.Attachments.Count == 0 || entity.Attendees.Count == 0) return;

        foreach (var attachment in entity.Attachments)
        {
            foreach (var attendee in entity.Attendees.Where(a => a.UserId != entity.OrganizerId))
            {
                try
                {
                    await permissions.ShareAsync(
                        attachment.DriveItemId, attendee.Email, attachment.GrantAttendees, null, ct);
                }
                catch (Exception)
                {
                    // Not fatal: the event is still valid without the grant.
                }
            }
        }
    }

    private async Task NotifyAttendeesAsync(
        CalendarEvent entity, Calendar calendar, bool isNew, CancellationToken ct)
    {
        var recipients = entity.Attendees
            .Where(a => a.UserId is not null && a.UserId != currentUser.Id)
            .Select(a => a.UserId!.Value)
            .Distinct();

        foreach (var recipient in recipients)
        {
            await notifications.NotifyAsync(
                recipient,
                NotificationKind.EventInvite,
                isNew
                    ? $"{currentUser.DisplayName ?? "Someone"} invited you to \"{entity.Title}\""
                    : $"\"{entity.Title}\" was updated",
                $"{entity.StartUtc:ddd d MMM yyyy, HH:mm} UTC · {calendar.Name}",
                $"/calendar?event={entity.Id}",
                ct);
        }
    }

    public async Task DeleteEventAsync(
        Guid eventId, DateTimeOffset? occurrenceStartUtc = null, CancellationToken ct = default)
    {
        var entity = await db.CalendarEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct)
                     ?? throw new NotFoundException("That event was not found.");

        await RequireCalendarAsync(entity.CalendarId, PermissionRole.Editor, ct);

        if (occurrenceStartUtc is null || entity.Recurrence == RecurrenceFrequency.None)
        {
            // Deleting a master takes its exceptions with it, or they would be orphaned.
            await db.CalendarEvents
                .Where(e => e.RecurringSeriesId == eventId)
                .ExecuteDeleteAsync(ct);

            db.CalendarEvents.Remove(entity);
            await db.SaveChangesAsync(ct);

            await activity.LogAsync("event.deleted", null, entity.Title, ct);
            return;
        }

        // Cancelling one occurrence is recorded as a cancelled exception row rather than by
        // materialising the rest of the series.
        var existing = await db.CalendarEvents.FirstOrDefaultAsync(
            e => e.RecurringSeriesId == eventId && e.OriginalStartUtc == occurrenceStartUtc, ct);

        if (existing is not null)
        {
            existing.IsCancelled = true;
        }
        else
        {
            db.CalendarEvents.Add(new CalendarEvent
            {
                CalendarId = entity.CalendarId,
                Title = entity.Title,
                StartUtc = occurrenceStartUtc.Value,
                EndUtc = occurrenceStartUtc.Value + (entity.EndUtc - entity.StartUtc),
                IsAllDay = entity.IsAllDay,
                OrganizerId = entity.OrganizerId,
                RecurringSeriesId = eventId,
                OriginalStartUtc = occurrenceStartUtc,
                IsCancelled = true,
            });
        }

        await db.SaveChangesAsync(ct);
        await activity.LogAsync("event.occurrenceCancelled", null, entity.Title, ct);
    }

    public async Task RespondAsync(
        Guid eventId, AttendeeResponse response, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var attendee = await db.EventAttendees
            .FirstOrDefaultAsync(a => a.CalendarEventId == eventId && a.UserId == userId, ct)
            ?? throw new NotFoundException("You are not an attendee of that event.");

        attendee.Response = response;
        attendee.RespondedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        var entity = await db.CalendarEvents
            .AsNoTracking()
            .Where(e => e.Id == eventId)
            .Select(e => new { e.Title, e.OrganizerId })
            .FirstOrDefaultAsync(ct);

        if (entity is not null && entity.OrganizerId != userId)
        {
            await notifications.NotifyAsync(
                entity.OrganizerId,
                NotificationKind.EventInvite,
                $"{currentUser.DisplayName ?? "Someone"} {response.ToString().ToLowerInvariant()} \"{entity.Title}\"",
                null,
                $"/calendar?event={eventId}",
                ct);
        }
    }

    public async Task AttachDriveItemAsync(
        Guid eventId,
        Guid driveItemId,
        PermissionRole grantAttendees = PermissionRole.Viewer,
        CancellationToken ct = default)
    {
        var entity = await db.CalendarEvents
            .Include(e => e.Attendees)
            .Include(e => e.Attachments)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct)
            ?? throw new NotFoundException("That event was not found.");

        await RequireCalendarAsync(entity.CalendarId, PermissionRole.Editor, ct);
        await permissions.RequireAsync(driveItemId, PermissionRole.Viewer, ct);

        if (entity.Attachments.Any(a => a.DriveItemId == driveItemId)) return;

        entity.Attachments.Add(new EventAttachment
        {
            CalendarEventId = eventId,
            DriveItemId = driveItemId,
            GrantAttendees = grantAttendees,
        });

        await db.SaveChangesAsync(ct);
        await GrantAttachmentAccessAsync(entity, ct);
    }

    public async Task DetachDriveItemAsync(
        Guid eventId, Guid attachmentId, CancellationToken ct = default)
    {
        var entity = await db.CalendarEvents
            .AsNoTracking()
            .Where(e => e.Id == eventId)
            .Select(e => new { e.CalendarId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("That event was not found.");

        await RequireCalendarAsync(entity.CalendarId, PermissionRole.Editor, ct);

        await db.EventAttachments
            .Where(a => a.Id == attachmentId && a.CalendarEventId == eventId)
            .ExecuteDeleteAsync(ct);
    }

    // ─────────────────────────── Sheets → Calendar integration ───────────────────────────

    /// <summary>
    /// Reads a Sheets range as a schedule and creates events from it. Columns are positional:
    /// <c>title | start | end [| location]</c>, with the first row treated as a header.
    /// Dates are read through the formula engine, so serial numbers, formulas and ISO strings all work.
    /// </summary>
    public async Task<int> ImportFromSpreadsheetAsync(
        Guid spreadsheetId,
        string sheetName,
        string range,
        Guid targetCalendarId,
        CancellationToken ct = default)
    {
        await permissions.RequireAsync(spreadsheetId, PermissionRole.Viewer, ct);
        await RequireCalendarAsync(targetCalendarId, PermissionRole.Editor, ct);

        var model = await content.GetTypedAsync<SpreadsheetModel>(spreadsheetId, ct)
                    ?? throw new NotFoundException("That spreadsheet was not found.");

        if (!CellRange.TryParse(range, out var parsed))
            throw new ValidationException($"'{range}' is not a valid range.");

        var sheet = model.Sheets.FirstOrDefault(
                        s => s.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase))
                    ?? model.Sheets.FirstOrDefault()
                    ?? throw new ValidationException("That spreadsheet has no sheets.");

        var engine = new FormulaEngine(model);
        var userId = currentUser.RequireId();
        var created = 0;

        // Skip the header row.
        for (var row = parsed.Start.Row + 1; row <= parsed.End.Row; row++)
        {
            var title = engine.GetCell(sheet.Name, new CellAddress(row, parsed.Start.Col))
                .ToDisplayString().Trim();

            if (string.IsNullOrWhiteSpace(title)) continue;

            if (parsed.ColCount < 3) continue;

            var start = ReadDate(engine, sheet.Name, row, parsed.Start.Col + 1);
            var end = ReadDate(engine, sheet.Name, row, parsed.Start.Col + 2);

            if (start is null) continue;
            // A missing or invalid end defaults to a one-hour event rather than being skipped.
            end ??= start.Value.AddHours(1);
            if (end <= start) end = start.Value.AddHours(1);

            var location = parsed.ColCount >= 4
                ? engine.GetCell(sheet.Name, new CellAddress(row, parsed.Start.Col + 3)).ToDisplayString()
                : null;

            db.CalendarEvents.Add(new CalendarEvent
            {
                CalendarId = targetCalendarId,
                Title = title.Length > 512 ? title[..512] : title,
                Location = string.IsNullOrWhiteSpace(location) ? null : location,
                StartUtc = start.Value,
                EndUtc = end.Value,
                OrganizerId = userId,
                SourceSpreadsheetId = spreadsheetId,
                SourceRange = $"{sheet.Name}!{new CellAddress(row, parsed.Start.Col).ToKey()}",
            });

            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            await activity.LogAsync(
                "calendar.importedFromSheet", spreadsheetId, $"{created} events", ct);
        }

        return created;
    }

    /// <summary>
    /// Reads a cell as an instant, accepting a spreadsheet serial number or a parseable date string.
    /// </summary>
    private static DateTimeOffset? ReadDate(FormulaEngine engine, string sheetName, int row, int col)
    {
        var value = engine.GetCell(sheetName, new CellAddress(row, col));

        var number = value.ToNumber();
        if (!number.IsError && number.Kind == FormulaValueKind.Number && number.RawNumber > 0)
        {
            // Serial numbers below 1 would resolve to 1899 and are almost certainly not dates.
            if (number.RawNumber >= 1)
            {
                var epoch = new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc);
                return new DateTimeOffset(epoch.AddDays(number.RawNumber), TimeSpan.Zero);
            }
        }

        var text = value.ToDisplayString();
        if (DateTimeOffset.TryParse(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    // ──────────────────────────────────── free/busy + reminders ────────────────────────────────────

    public async Task<IReadOnlyList<BusySlot>> GetBusyAsync(
        IReadOnlyList<string> attendeeEmails,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        currentUser.RequireId();

        var normalised = attendeeEmails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .ToList();

        if (normalised.Count == 0) return [];

        // Busy = they are an attendee of an event in the window. Titles are only exposed for events
        // that are not marked private, so free/busy does not leak private event details.
        var rows = await db.EventAttendees
            .AsNoTracking()
            .Where(a => normalised.Contains(a.Email))
            .Join(db.CalendarEvents.AsNoTracking(), a => a.CalendarEventId, e => e.Id, (a, e) => e)
            .Where(e => e.StartUtc < toUtc && e.EndUtc > fromUtc && !e.IsCancelled)
            .Select(e => new { e.StartUtc, e.EndUtc, e.Title, e.Visibility })
            .ToListAsync(ct);

        return rows
            .Select(r => new BusySlot(
                r.StartUtc,
                r.EndUtc,
                r.Visibility == EventVisibility.Private ? null : r.Title))
            .OrderBy(s => s.StartUtc)
            .ToList();
    }

    public async Task<int> DispatchDueRemindersAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Only look at a bounded window: a reminder that is hours overdue is no longer useful, and
        // scanning the whole table on every sweep would not scale.
        var horizon = now.AddHours(-2);

        var due = await db.EventReminders
            .AsNoTracking()
            .Where(r => r.SentAt == null)
            .Join(db.CalendarEvents.AsNoTracking(), r => r.CalendarEventId, e => e.Id,
                (r, e) => new { Reminder = r, Event = e })
            .Where(x => !x.Event.IsCancelled
                        && x.Event.StartUtc > horizon
                        && x.Event.StartUtc <= now.AddMinutes(x.Reminder.MinutesBefore))
            .Take(500)
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        var eventIds = due.Select(x => x.Event.Id).Distinct().ToArray();

        var attendees = await db.EventAttendees
            .AsNoTracking()
            .Where(a => eventIds.Contains(a.CalendarEventId) && a.UserId != null)
            .Select(a => new { a.CalendarEventId, UserId = a.UserId!.Value })
            .ToListAsync(ct);

        var attendeesByEvent = attendees
            .GroupBy(a => a.CalendarEventId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.UserId).Distinct().ToList());

        foreach (var item in due)
        {
            var recipients = attendeesByEvent.TryGetValue(item.Event.Id, out var list)
                ? new HashSet<Guid>(list)
                : [];

            recipients.Add(item.Event.OrganizerId);

            foreach (var recipient in recipients)
            {
                await notifications.NotifyAsync(
                    recipient,
                    NotificationKind.EventReminder,
                    $"\"{item.Event.Title}\" starts soon",
                    $"{item.Event.StartUtc:HH:mm} UTC · in {item.Reminder.MinutesBefore} minutes",
                    $"/calendar?event={item.Event.Id}",
                    ct);
            }
        }

        var reminderIds = due.Select(x => x.Reminder.Id).ToArray();

        // Stamp them all at once so a crash mid-sweep cannot double-send the ones already delivered.
        await db.EventReminders
            .Where(r => reminderIds.Contains(r.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SentAt, now), ct);

        return due.Count;
    }

    // ─────────────────────────────────────── helpers ───────────────────────────────────────

    private async Task<Calendar> RequireCalendarAsync(
        Guid calendarId, PermissionRole minimum, CancellationToken ct)
    {
        var calendar = await db.Calendars.FirstOrDefaultAsync(c => c.Id == calendarId, ct)
                       ?? throw new NotFoundException("That calendar was not found.");

        var role = await ResolveCalendarRoleAsync(calendar, currentUser.Id, ct);

        if (role == PermissionRole.None)
            throw new NotFoundException("That calendar was not found.");

        if (role < minimum)
            throw new ForbiddenException($"This action requires {minimum} access on the calendar.");

        return calendar;
    }

    private async Task<PermissionRole> ResolveCalendarRoleAsync(
        Calendar calendar, Guid? userId, CancellationToken ct)
    {
        if (userId is null) return PermissionRole.None;
        if (calendar.OwnerId == userId) return PermissionRole.Owner;

        var shared = await db.CalendarShares
            .AsNoTracking()
            .Where(s => s.CalendarId == calendar.Id && s.UserId == userId)
            .Select(s => (PermissionRole?)s.Role)
            .FirstOrDefaultAsync(ct);

        return shared ?? PermissionRole.None;
    }

    private async Task<List<(Calendar Calendar, PermissionRole Role)>> GetVisibleCalendarsAsync(
        Guid userId, CancellationToken ct)
    {
        var owned = await db.Calendars
            .AsNoTracking()
            .Where(c => c.OwnerId == userId)
            .ToListAsync(ct);

        var shared = await db.CalendarShares
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Join(db.Calendars.AsNoTracking(), s => s.CalendarId, c => c.Id,
                (s, c) => new { Calendar = c, s.Role })
            .ToListAsync(ct);

        var result = owned.Select(c => (c, PermissionRole.Owner)).ToList();
        result.AddRange(shared.Select(x => (x.Calendar, x.Role)));
        return result;
    }
}
