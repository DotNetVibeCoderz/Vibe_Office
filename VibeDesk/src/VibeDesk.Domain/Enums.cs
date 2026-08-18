namespace VibeDesk.Domain;

/// <summary>
/// Every node in Drive is a <see cref="Entities.DriveItem"/>; this discriminates what it is.
/// Docs/Sheets/Slides are *not* separate trees — they are Drive items with a content payload,
/// which is what makes sharing, search, trash and version history work uniformly across apps.
/// </summary>
public enum DriveItemType
{
    Folder = 0,
    Document = 1,
    Spreadsheet = 2,
    Presentation = 3,
    /// <summary>An uploaded binary (pdf, image, video, …) stored in the blob store.</summary>
    File = 4,
}

/// <summary>Ordered least→most privilege. Comparisons rely on the numeric order.</summary>
public enum PermissionRole
{
    None = 0,
    Viewer = 1,
    Commenter = 2,
    Editor = 3,
    Owner = 4,
}

public enum ShareScope
{
    /// <summary>Only explicitly granted principals.</summary>
    Private = 0,
    /// <summary>Anyone signed in who has the link.</summary>
    AnyoneWithLinkInternal = 1,
    /// <summary>Anyone with the link, no sign-in required.</summary>
    AnyoneWithLink = 2,
}

public enum CommentKind
{
    Comment = 0,
    /// <summary>A proposed edit that does not change the body until accepted.</summary>
    Suggestion = 1,
}

public enum CommentStatus
{
    Open = 0,
    Resolved = 1,
    Accepted = 2,
    Rejected = 3,
}

public enum ChatRole
{
    User = 0,
    Assistant = 1,
    System = 2,
    Tool = 3,
}

public enum AiProvider
{
    OpenAI = 0,
    Anthropic = 1,
    Google = 2,
    Ollama = 3,
}

public enum ChatAttachmentKind
{
    Image = 0,
    Document = 1,
}

public enum AttendeeResponse
{
    NeedsAction = 0,
    Accepted = 1,
    Declined = 2,
    Tentative = 3,
}

public enum ReminderMethod
{
    Notification = 0,
    Email = 1,
}

public enum EventVisibility
{
    Default = 0,
    Public = 1,
    Private = 2,
}

public enum RecurrenceFrequency
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4,
}

/// <summary>Which external calendar an event mirrors, for the email/calendar sync feature.</summary>
public enum ExternalCalendarKind
{
    None = 0,
    Google = 1,
    Outlook = 2,
}

public enum NotificationKind
{
    Share = 0,
    Comment = 1,
    Mention = 2,
    EventReminder = 3,
    EventInvite = 4,
    System = 5,
}

// ───────────────────────────────────── Scripting ─────────────────────────────────────

public enum ScriptLanguage
{
    JavaScript = 0,
    Python = 1,
    CSharp = 2,
}

/// <summary>
/// What a script may touch, as flags so one column carries the whole grant. Read and write are
/// separate for every app: "read my Sheets" is a normal ask, "rewrite my Sheets" is not.
/// </summary>
[Flags]
public enum ScriptScope
{
    None = 0,

    DriveRead = 1 << 0,
    DriveWrite = 1 << 1,
    DocsRead = 1 << 2,
    DocsWrite = 1 << 3,
    SheetsRead = 1 << 4,
    SheetsWrite = 1 << 5,
    SlidesRead = 1 << 6,
    SlidesWrite = 1 << 7,
    CalendarRead = 1 << 8,
    CalendarWrite = 1 << 9,

    /// <summary>Outbound HTTP, still limited to the script's allowed hosts.</summary>
    Network = 1 << 10,

    /// <summary>Send notifications to the script's own owner.</summary>
    Notify = 1 << 11,

    ReadOnly = DriveRead | DocsRead | SheetsRead | SlidesRead | CalendarRead,
    All = ReadOnly | DriveWrite | DocsWrite | SheetsWrite | SlidesWrite | CalendarWrite | Network | Notify,
}

public enum TriggerKind
{
    Manual = 0,
    Event = 1,
    Schedule = 2,
}

public enum ScriptRunTrigger
{
    Manual = 0,
    Event = 1,
    Schedule = 2,
    Api = 3,
    Cli = 4,
}

public enum ScriptRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,

    /// <summary>Exceeded its wall-clock or memory ceiling and was stopped.</summary>
    TimedOut = 3,

    /// <summary>Refused before execution — usually a scope the script does not hold.</summary>
    Refused = 4,
}
