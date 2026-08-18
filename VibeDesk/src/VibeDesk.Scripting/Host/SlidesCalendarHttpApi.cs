using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VibeDesk.Application.Calendars;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Drive;
using VibeDesk.Domain;

namespace VibeDesk.Scripting.Host;

// ────────────────────────────────────── Slides ──────────────────────────────────────

public sealed partial class SlidesApi(
    ScriptHost host, IDriveService drive, IDocumentContentService content)
{
    public PresentationView Read(string id)
    {
        host.Require(ScriptScope.SlidesRead, "slides.read()");

        return host.Track("slides.read", id, () =>
        {
            var payload = WorkspaceApi.Sync(content.GetAsync(WorkspaceApi.Id(id, "presentation")))
                ?? throw new ArgumentException("That presentation could not be found.");

            var model = ContentJson.Deserialize<PresentationModel>(payload.Data);

            return new PresentationView
            {
                Id = payload.Id.ToString(),
                Name = payload.Name,
                Theme = model.Theme,
                Slides = model.Slides.Select((s, i) => new SlideView
                {
                    Index = i,
                    Layout = s.Layout,
                    Text = string.Join(
                        "\n",
                        s.Elements
                            .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                            .Select(e => Strip(e.Text!))),
                    Notes = s.Notes,
                }).ToList(),
            };
        });
    }

    public FileInfoView Create(string name, string theme = "aurora", string? parentId = null)
    {
        host.Require(ScriptScope.SlidesWrite, "slides.create()");

        return host.Track("slides.create", name, () =>
        {
            var created = WorkspaceApi.Sync(drive.CreateDocumentAsync(
                DriveItemType.Presentation, name, DriveApi.Optional(parentId)));

            Mutate(created.Id, model =>
            {
                model.Theme = theme;
                // A new deck arrives with one placeholder slide; a script that is about to add its
                // own slides does not want it.
                model.Slides.Clear();
            });

            return DriveApi.Map(created);
        });
    }

    /// <summary>
    /// Appends a slide. <paramref name="layout"/> is <c>title</c>, <c>titleContent</c>,
    /// <c>sectionHeader</c>, <c>twoColumn</c>, <c>quote</c> or <c>blank</c>.
    /// </summary>
    public int AddSlide(
        string id,
        string title,
        string? body = null,
        string layout = "titleContent",
        string? notes = null)
    {
        host.Require(ScriptScope.SlidesWrite, "slides.addSlide()");

        return host.Track("slides.addSlide", title, () =>
        {
            var index = 0;

            Mutate(WorkspaceApi.Id(id, "presentation"), model =>
            {
                var slide = new Slide { Layout = layout, Notes = notes };

                slide.Elements.Add(new SlideElement
                {
                    Type = "text",
                    X = 8,
                    Y = layout == "title" ? 38 : 12,
                    W = 84,
                    H = 18,
                    Text = $"<h1>{WebUtility.HtmlEncode(title)}</h1>",
                });

                if (!string.IsNullOrWhiteSpace(body))
                {
                    slide.Elements.Add(new SlideElement
                    {
                        Type = "text",
                        X = 8,
                        Y = 36,
                        W = 84,
                        H = 52,
                        // Newlines become paragraphs: a script building a deck from data passes text,
                        // not markup.
                        Text = string.Concat(body
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(line => $"<p>{WebUtility.HtmlEncode(line.Trim())}</p>")),
                    });
                }

                model.Slides.Add(slide);
                index = model.Slides.Count - 1;
            });

            return index;
        });
    }

    /// <summary>Adds one slide per row of a frame — the "deck from a dataset" case.</summary>
    public int AddSlidesFromFrame(string id, DataFrame frame, string titleColumn, string? bodyColumn = null)
    {
        host.Require(ScriptScope.SlidesWrite, "slides.addSlidesFromFrame()");

        var added = 0;

        foreach (var row in frame.Rows)
        {
            var title = DataFrame.Text(row.GetValueOrDefault(titleColumn));

            var body = bodyColumn is null
                ? string.Join("\n", row.Where(kv => kv.Key != titleColumn)
                    .Select(kv => $"{kv.Key}: {DataFrame.Text(kv.Value)}"))
                : DataFrame.Text(row.GetValueOrDefault(bodyColumn));

            AddSlide(id, title, body);
            added++;
        }

        return added;
    }

    public void SetNotes(string id, int index, string notes)
    {
        host.Require(ScriptScope.SlidesWrite, "slides.setNotes()");

        host.Track("slides.setNotes", id, () => Mutate(WorkspaceApi.Id(id, "presentation"), model =>
        {
            if (index < 0 || index >= model.Slides.Count)
            {
                throw new ArgumentException($"This deck has no slide at index {index}.");
            }

            model.Slides[index].Notes = notes;
        }));
    }

    private void Mutate(Guid id, Action<PresentationModel> mutate)
    {
        var payload = WorkspaceApi.Sync(content.GetAsync(id))
            ?? throw new ArgumentException("That presentation could not be found.");

        var model = ContentJson.Deserialize<PresentationModel>(payload.Data);
        mutate(model);

        WorkspaceApi.Sync(content.SaveTypedAsync(id, model));
    }

    private static string Strip(string html) =>
        WebUtility.HtmlDecode(TagPattern().Replace(html, string.Empty)).Trim();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}

// ────────────────────────────────────── Calendar ──────────────────────────────────────

public sealed class CalendarApi(ScriptHost host, ICalendarService calendar)
{
    public IReadOnlyList<CalendarView> List()
    {
        host.Require(ScriptScope.CalendarRead, "calendar.list()");

        return host.Track("calendar.list", null, () =>
            WorkspaceApi.Sync(calendar.ListCalendarsAsync())
                .Select(c => new CalendarView
                {
                    Id = c.Id.ToString(),
                    Name = c.Name,
                    Color = c.Color,
                    IsPrimary = c.IsPrimary,
                    CanEdit = c.CanEdit,
                })
                .ToList());
    }

    /// <summary>Occurrences in a window, recurrence already expanded.</summary>
    public IReadOnlyList<EventView> Events(string from, string to, string? calendarId = null)
    {
        host.Require(ScriptScope.CalendarRead, "calendar.events()");

        return host.Track("calendar.events", $"{from}..{to}", () =>
        {
            var ids = string.IsNullOrWhiteSpace(calendarId)
                ? null
                : new[] { WorkspaceApi.Id(calendarId, "calendar") };

            return WorkspaceApi.Sync(calendar.GetOccurrencesAsync(Instant(from), Instant(to), ids))
                .Select(Map)
                .ToList();
        });
    }

    public EventView Create(EventDraft draft)
    {
        host.Require(ScriptScope.CalendarWrite, "calendar.create()");

        return host.Track("calendar.create", draft.Title, () =>
        {
            var calendarId = string.IsNullOrWhiteSpace(draft.CalendarId)
                ? WorkspaceApi.Sync(calendar.ListCalendarsAsync())
                      .FirstOrDefault(c => c.IsPrimary)?.Id
                  ?? throw new InvalidOperationException("No calendar to write to.")
                : WorkspaceApi.Id(draft.CalendarId, "calendar");

            var input = new EventInput
            {
                Id = string.IsNullOrWhiteSpace(draft.Id) ? null : WorkspaceApi.Id(draft.Id, "event"),
                CalendarId = calendarId,
                Title = draft.Title,
                Description = draft.Description,
                Location = draft.Location,
                StartUtc = Instant(draft.Start),
                EndUtc = Instant(draft.End),
                IsAllDay = draft.AllDay,
                Recurrence = Frequency(draft.Recurrence),
                RecurrenceInterval = Math.Max(1, draft.RecurrenceInterval),
                RecurrenceUntilUtc = string.IsNullOrWhiteSpace(draft.RecurrenceUntil)
                    ? null
                    : Instant(draft.RecurrenceUntil),
                AttendeeEmails = draft.Attendees,
                Reminders = [.. draft.ReminderMinutes.Select(m => new ReminderInput(ReminderMethod.Notification, m))],
            };

            return Map(WorkspaceApi.Sync(calendar.SaveEventAsync(input)));
        });
    }

    public void Delete(string eventId, string? occurrenceStart = null)
    {
        host.Require(ScriptScope.CalendarWrite, "calendar.delete()");

        host.Track("calendar.delete", eventId, () => WorkspaceApi.Sync(calendar.DeleteEventAsync(
            WorkspaceApi.Id(eventId, "event"),
            string.IsNullOrWhiteSpace(occurrenceStart) ? null : Instant(occurrenceStart))));
    }

    /// <summary>Busy windows for a set of attendees, for scheduling scripts.</summary>
    public IReadOnlyList<EventView> Busy(IEnumerable<string> emails, string from, string to)
    {
        host.Require(ScriptScope.CalendarRead, "calendar.busy()");

        return host.Track("calendar.busy", null, () =>
            WorkspaceApi.Sync(calendar.GetBusyAsync([.. emails], Instant(from), Instant(to)))
                .Select(b => new EventView
                {
                    Title = b.Title ?? "(busy)",
                    Start = b.StartUtc,
                    End = b.EndUtc,
                })
                .ToList());
    }

    private static EventView Map(EventOccurrenceDto e) => new()
    {
        Id = e.EventId.ToString(),
        CalendarId = e.CalendarId.ToString(),
        Title = e.Title,
        Description = e.Description,
        Location = e.Location,
        Start = e.StartUtc,
        End = e.EndUtc,
        AllDay = e.IsAllDay,
        Recurring = e.IsRecurring,
        Organizer = e.OrganizerName,
        Attendees = [.. e.Attendees.Select(a => a.Email)],
    };

    private static RecurrenceFrequency Frequency(string? value) => value?.ToLowerInvariant() switch
    {
        "daily" => RecurrenceFrequency.Daily,
        "weekly" => RecurrenceFrequency.Weekly,
        "monthly" => RecurrenceFrequency.Monthly,
        "yearly" => RecurrenceFrequency.Yearly,
        _ => RecurrenceFrequency.None,
    };

    /// <summary>
    /// Parses an ISO timestamp. A string with no offset is read as UTC rather than as server-local:
    /// a script that runs on a schedule must not change meaning with the host's timezone.
    /// </summary>
    internal static DateTimeOffset Instant(string value)
    {
        if (DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        throw new ArgumentException($"'{value}' is not a date/time this API can read. Use ISO 8601.");
    }
}

// ────────────────────────────────────── HTTP ──────────────────────────────────────

/// <summary>
/// Outbound HTTP for scripts.
/// </summary>
/// <remarks>
/// Two independent gates. The script's <c>AllowedHosts</c> list says which hosts it may talk to at
/// all — a weather script has no business calling an internal service. Then the address itself is
/// checked after DNS resolution, because a name that looks external can still resolve to loopback or
/// a cloud metadata endpoint, and that is exactly the request a hostile script would make.
/// </remarks>
public sealed class HttpApi(
    ScriptHost host, ScriptingOptions options, HttpClient http, string? allowedHosts)
{
    private readonly string[] _allowed = (allowedHosts ?? string.Empty)
        .Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToArray();

    private int _calls;

    public HttpResponseView Get(string url, Dictionary<string, string>? headers = null) =>
        Send("GET", url, null, null, headers);

    public HttpResponseView Post(
        string url, string? body = null, string? contentType = null, Dictionary<string, string>? headers = null) =>
        Send("POST", url, body, contentType, headers);

    public HttpResponseView PostJson(string url, object? body, Dictionary<string, string>? headers = null) =>
        Send("POST", url, JsonSerializer.Serialize(body, JsonHelper.Options), "application/json", headers);

    /// <summary>Fetches and parses JSON in one call — by far the most common script use.</summary>
    public object? GetJson(string url, Dictionary<string, string>? headers = null)
    {
        var response = Get(url, headers);

        if (!response.Ok)
        {
            throw new InvalidOperationException($"GET {url} returned {response.Status}.");
        }

        return response.Json ?? JsonHelper.ToPlain(response.Body);
    }

    private HttpResponseView Send(
        string method, string url, string? body, string? contentType, Dictionary<string, string>? headers)
    {
        host.Require(ScriptScope.Network, $"http.{method.ToLowerInvariant()}()");

        if (++_calls > options.MaxHttpCalls)
        {
            throw new InvalidOperationException(
                $"This script has made its {options.MaxHttpCalls} allowed outbound calls.");
        }

        var uri = Validate(url);

        return host.Track($"http.{method.ToLowerInvariant()}", uri.Host, () =>
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);

            if (body is not null)
            {
                request.Content = new StringContent(
                    body, Encoding.UTF8, contentType ?? "application/json");
            }

            foreach (var (key, value) in headers ?? [])
            {
                // Hop-by-hop and content headers are set above; anything else is the script's.
                request.Headers.TryAddWithoutValidation(key, value);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.HttpTimeoutSeconds));
            using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            using var stream = response.Content.ReadAsStream(cts.Token);
            using var reader = new StreamReader(stream);

            var buffer = new char[Math.Min(options.MaxHttpResponseKb, 8192) * 1024];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, read);

            return new HttpResponseView
            {
                Status = (int)response.StatusCode,
                Ok = response.IsSuccessStatusCode,
                Body = text,
                Headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
                Json = JsonHelper.TryToPlain(text),
            };
        });
    }

    private Uri Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"'{url}' is not an absolute URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Only http and https URLs can be requested.");
        }

        if (_allowed.Length == 0)
        {
            throw new ScriptPermissionException(
                "This script has no allowed hosts. Add the hostnames it needs in its settings.");
        }

        if (!_allowed.Any(pattern => HostMatches(uri.Host, pattern)))
        {
            throw new ScriptPermissionException(
                $"'{uri.Host}' is not in this script's allowed hosts ({string.Join(", ", _allowed)}).");
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host, out var literal)
                ? [literal]
                : Dns.GetHostAddresses(uri.Host);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not resolve {uri.Host}: {ex.Message}");
        }

        if (addresses.Length == 0 || addresses.Any(IsPrivate))
        {
            throw new ScriptPermissionException(
                $"'{uri.Host}' resolves to a private address, which scripts cannot reach.");
        }

        return uri;
    }

    private static bool HostMatches(string host, string pattern)
    {
        if (pattern == "*") return true;

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var b = address.GetAddressBytes();

        return b[0] switch
        {
            0 or 10 or 127 => true,
            172 => b[1] >= 16 && b[1] <= 31,
            169 => b[1] == 254,
            192 => b[1] == 168,
            _ => false,
        };
    }
}

internal static class JsonHelper
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static object? ToPlain(string json) => TryToPlain(json)
        ?? throw new ArgumentException("That string is not valid JSON.");

    /// <summary>
    /// Converts JSON to dictionaries, lists and primitives rather than <c>JsonElement</c>. Every
    /// runtime can walk those natively; a JsonElement would need a different accessor in each.
    /// </summary>
    public static object? TryToPlain(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return Convert(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object? Convert(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => Convert(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(Convert).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
