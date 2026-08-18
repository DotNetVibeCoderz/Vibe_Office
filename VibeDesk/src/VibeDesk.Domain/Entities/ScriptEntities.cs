namespace VibeDesk.Domain.Entities;

/// <summary>
/// A user-authored automation. Scripts are workspace objects rather than Drive items: they are code,
/// not documents, and giving them their own table keeps versioning, scopes and run history from
/// having to be bolted onto <see cref="DriveItem"/>.
/// </summary>
public class Script
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = "Untitled script";
    public string? Description { get; set; }

    public ScriptLanguage Language { get; set; } = ScriptLanguage.JavaScript;

    /// <summary>Current source. Superseded versions live in <see cref="Versions"/>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// What the script is allowed to touch. A script that only reads Sheets should not be able to
    /// delete from Drive, and the scope is what makes that enforceable rather than aspirational.
    /// </summary>
    public ScriptScope Scopes { get; set; } = ScriptScope.None;

    /// <summary>Hosts the script may call out to. Empty means no outbound network at all.</summary>
    public string? AllowedHosts { get; set; }

    /// <summary>Wall-clock ceiling for one run; falls back to the configured default when null.</summary>
    public int? TimeoutSeconds { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Set when the script was created from a gallery template, for provenance.</summary>
    public string? TemplateId { get; set; }

    public int VersionNumber { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastRunAt { get; set; }
    public ScriptRunStatus? LastRunStatus { get; set; }

    public ICollection<ScriptVersion> Versions { get; set; } = [];
    public ICollection<ScriptTrigger> Triggers { get; set; } = [];
    public ICollection<ScriptRun> Runs { get; set; } = [];
}

/// <summary>An immutable snapshot of a script's source, written on every save.</summary>
public class ScriptVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ScriptId { get; set; }
    public Script? Script { get; set; }

    public int VersionNumber { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Note { get; set; }

    public Guid AuthorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// What causes a script to run. A script with no triggers is manual-only, which is the default and
/// the safe one.
/// </summary>
public class ScriptTrigger
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ScriptId { get; set; }
    public Script? Script { get; set; }

    public TriggerKind Kind { get; set; }

    /// <summary>
    /// For <see cref="TriggerKind.Event"/>: the activity action to match, e.g. <c>item.created</c> or
    /// <c>content.saved</c>. Supports a trailing <c>*</c>, so <c>item.*</c> matches every Drive event.
    /// </summary>
    public string? EventName { get; set; }

    /// <summary>Restricts an event trigger to one Drive item or folder subtree.</summary>
    public Guid? DriveItemId { get; set; }

    /// <summary>For <see cref="TriggerKind.Schedule"/>: a five-field cron expression.</summary>
    public string? CronExpression { get; set; }

    public string TimeZoneId { get; set; } = "UTC";

    public bool IsEnabled { get; set; } = true;

    /// <summary>Next fire time for a schedule, recomputed after each run so the sweeper stays cheap.</summary>
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One execution. This is the audit log: every run is recorded, including the ones that were refused
/// before any code ran, because "the script never executed" is exactly what you need to know when
/// something did not happen.
/// </summary>
public class ScriptRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ScriptId { get; set; }
    public Script? Script { get; set; }

    /// <summary>Who the run acted as. Scheduled runs act as the script's owner.</summary>
    public Guid ActorId { get; set; }

    public ScriptRunTrigger TriggeredBy { get; set; }

    /// <summary>The event that fired it, for an event-triggered run.</summary>
    public string? TriggerDetail { get; set; }

    public ScriptRunStatus Status { get; set; } = ScriptRunStatus.Running;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public int DurationMs { get; set; }

    /// <summary>Everything the script wrote to its console, capped and stored as text.</summary>
    public string? Output { get; set; }

    /// <summary>JSON of the script's return value, when it returned one.</summary>
    public string? ResultJson { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// Host API calls made during the run, as JSON. The point of an audit log for automation is being
    /// able to answer "what did it touch", not just "did it finish".
    /// </summary>
    public string? ApiCallsJson { get; set; }

    public int VersionNumber { get; set; }
}

/// <summary>
/// A script shared to the marketplace. A copy of the source, not a reference: installing a shared
/// script must not let its author change what already runs in someone else's workspace.
/// </summary>
public class ScriptPublication
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ScriptId { get; set; }
    public Guid AuthorId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Category { get; set; }

    public ScriptLanguage Language { get; set; }
    public string Code { get; set; } = string.Empty;
    public ScriptScope Scopes { get; set; }
    public string? AllowedHosts { get; set; }

    public int InstallCount { get; set; }

    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsListed { get; set; } = true;
}
