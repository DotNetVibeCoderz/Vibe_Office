namespace VibeDesk.Scripting.Host;

/// <summary>
/// The shapes scripts see. Plain classes with properties rather than dictionaries, because every
/// runtime reads CLR properties natively — a dictionary would force <c>obj["name"]</c> in JavaScript
/// and C# both.
/// </summary>
/// <remarks>
/// JavaScript sees these members in camelCase (<c>item.name</c>); Python and C# see the CLR names
/// (<c>item.Name</c>). That is one mapping in the JS engine rather than an un-idiomatic API in all
/// three languages.
/// </remarks>
public sealed class FileInfoView
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? ParentId { get; init; }
    public long Size { get; init; }
    public string Owner { get; init; } = string.Empty;
    public bool Starred { get; init; }
    public bool Shared { get; init; }
    public string Role { get; init; } = string.Empty;
    public DateTimeOffset Updated { get; init; }

    public override string ToString() => $"{Name} ({Type})";
}

public sealed class DocumentView
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Html { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public int WordCount { get; init; }
    public long Revision { get; init; }
}

public sealed class SheetView
{
    public string Name { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public int ColCount { get; init; }
}

public sealed class SlideView
{
    public int Index { get; init; }
    public string Layout { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string? Notes { get; init; }
}

public sealed class PresentationView
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Theme { get; init; } = string.Empty;
    public IReadOnlyList<SlideView> Slides { get; init; } = [];
}

public sealed class CalendarView
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }
    public bool CanEdit { get; init; }
}

public sealed class EventView
{
    public string Id { get; init; } = string.Empty;
    public string CalendarId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Location { get; init; }
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public bool AllDay { get; init; }
    public bool Recurring { get; init; }
    public string Organizer { get; init; } = string.Empty;
    public IReadOnlyList<string> Attendees { get; init; } = [];

    public override string ToString() => $"{Start:yyyy-MM-dd HH:mm} {Title}";
}

/// <summary>What a script passes to create or update an event.</summary>
public sealed class EventDraft
{
    public string? Id { get; set; }
    public string? CalendarId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }

    /// <summary>ISO 8601. Accepts a local or offset-bearing string; stored as UTC.</summary>
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;

    public bool AllDay { get; set; }

    /// <summary>none | daily | weekly | monthly | yearly</summary>
    public string? Recurrence { get; set; }
    public int RecurrenceInterval { get; set; } = 1;
    public string? RecurrenceUntil { get; set; }

    public List<string> Attendees { get; set; } = [];
    public List<int> ReminderMinutes { get; set; } = [];
}

public sealed class HttpResponseView
{
    public int Status { get; init; }
    public bool Ok { get; init; }
    public string Body { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();

    /// <summary>The body parsed as JSON, or null when it is not JSON.</summary>
    public object? Json { get; init; }
}

public sealed class UsageView
{
    public long UsedBytes { get; init; }
    public long QuotaBytes { get; init; }
    public double UsedPercent { get; init; }
    public int FileCount { get; init; }
    public int DocumentCount { get; init; }
    public int SpreadsheetCount { get; init; }
    public int PresentationCount { get; init; }
    public int FolderCount { get; init; }
}
