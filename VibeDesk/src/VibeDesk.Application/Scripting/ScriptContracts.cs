using VibeDesk.Domain;

namespace VibeDesk.Application.Scripting;

public sealed record ScriptDto(
    Guid Id,
    string Name,
    string? Description,
    ScriptLanguage Language,
    ScriptScope Scopes,
    string? AllowedHosts,
    int? TimeoutSeconds,
    bool IsEnabled,
    string? TemplateId,
    int VersionNumber,
    Guid OwnerId,
    string OwnerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastRunAt,
    ScriptRunStatus? LastRunStatus,
    int TriggerCount)
{
    public string LanguageLabel => Language switch
    {
        ScriptLanguage.Python => "Python",
        ScriptLanguage.CSharp => "C#",
        _ => "JavaScript",
    };

    /// <summary>File extension used by the editor and the CLI when a script is written to disk.</summary>
    public string Extension => Language switch
    {
        ScriptLanguage.Python => ".py",
        ScriptLanguage.CSharp => ".csx",
        _ => ".js",
    };
}

public sealed record ScriptDetailDto(ScriptDto Script, string Code, IReadOnlyList<ScriptTriggerDto> Triggers);

public sealed record ScriptTriggerDto(
    Guid Id,
    TriggerKind Kind,
    string? EventName,
    Guid? DriveItemId,
    string? DriveItemName,
    string? CronExpression,
    string TimeZoneId,
    bool IsEnabled,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastFiredAt);

public sealed record ScriptVersionDto(
    Guid Id,
    int VersionNumber,
    string? Note,
    Guid AuthorId,
    string AuthorName,
    DateTimeOffset CreatedAt);

public sealed record ScriptRunDto(
    Guid Id,
    Guid ScriptId,
    string ScriptName,
    ScriptRunTrigger TriggeredBy,
    string? TriggerDetail,
    ScriptRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int DurationMs,
    string? Output,
    string? ResultJson,
    string? Error,
    IReadOnlyList<ScriptApiCallDto> ApiCalls,
    int VersionNumber);

/// <summary>One host API call a script made, so a run can be audited for what it touched.</summary>
public sealed record ScriptApiCallDto(string Method, string? Target, bool Succeeded, int DurationMs);

public sealed record ScriptInput
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = "Untitled script";
    public string? Description { get; init; }
    public ScriptLanguage Language { get; init; } = ScriptLanguage.JavaScript;
    public string Code { get; init; } = string.Empty;
    public ScriptScope Scopes { get; init; } = ScriptScope.None;
    public string? AllowedHosts { get; init; }
    public int? TimeoutSeconds { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string? TemplateId { get; init; }
    public string? VersionNote { get; init; }
}

public sealed record ScriptTriggerInput
{
    public Guid? Id { get; init; }
    public TriggerKind Kind { get; init; } = TriggerKind.Manual;
    public string? EventName { get; init; }
    public Guid? DriveItemId { get; init; }
    public string? CronExpression { get; init; }
    public string TimeZoneId { get; init; } = "UTC";
    public bool IsEnabled { get; init; } = true;
}

// ───────────────────────────────── execution ─────────────────────────────────

/// <summary>Everything one execution needs. Kept separate from the entity so the CLI and the
/// "run this unsaved buffer" path in the editor can execute code that was never persisted.</summary>
public sealed record ScriptExecutionRequest
{
    public Guid? ScriptId { get; init; }
    public string Name { get; init; } = "ad-hoc";
    public required ScriptLanguage Language { get; init; }
    public required string Code { get; init; }
    public ScriptScope Scopes { get; init; } = ScriptScope.None;
    public string? AllowedHosts { get; init; }
    public int? TimeoutSeconds { get; init; }

    /// <summary>Values exposed to the script as <c>input</c>.</summary>
    public IReadOnlyDictionary<string, string>? Input { get; init; }

    public ScriptRunTrigger TriggeredBy { get; init; } = ScriptRunTrigger.Manual;
    public string? TriggerDetail { get; init; }

    /// <summary>Persist a <c>ScriptRun</c> row. False for the editor's throwaway "test run".</summary>
    public bool Record { get; init; } = true;
}

public sealed record ScriptExecutionResult(
    ScriptRunStatus Status,
    string Output,
    string? ResultJson,
    string? Error,
    int DurationMs,
    IReadOnlyList<ScriptApiCallDto> ApiCalls)
{
    public bool Succeeded => Status == ScriptRunStatus.Succeeded;

    public static ScriptExecutionResult Refused(string reason) =>
        new(ScriptRunStatus.Refused, string.Empty, null, reason, 0, []);
}

/// <summary>Runs script source. One implementation per language, selected by <see cref="Language"/>.</summary>
public interface IScriptRuntime
{
    ScriptLanguage Language { get; }

    /// <summary>
    /// Rejects source that cannot be allowed to run, before any of it executes. Returns null when the
    /// source is acceptable. This is where the C# runtime refuses file and process access — for a
    /// compiled language, refusing at analysis time is the only point where refusal is cheap.
    /// </summary>
    string? Validate(string code);

    Task<ScriptExecutionResult> ExecuteAsync(
        ScriptExecutionRequest request,
        IScriptHost host,
        CancellationToken ct = default);
}

/// <summary>
/// The surface a script sees. Implemented once and handed to every runtime, so JavaScript, Python and
/// C# get the same API and the same permission checks rather than three subtly different ones.
/// </summary>
public interface IScriptHost
{
    void Log(string message);

    /// <summary>Throws when the script's scopes do not include <paramref name="required"/>.</summary>
    void Require(ScriptScope required, string operation);

    void RecordCall(string method, string? target, bool succeeded, int durationMs);

    string Output { get; }
    IReadOnlyList<ScriptApiCallDto> Calls { get; }
}

public interface IScriptExecutor
{
    IReadOnlyList<ScriptLanguage> AvailableLanguages { get; }

    /// <summary>Static checks only — used by the editor to show problems before a run.</summary>
    string? Validate(ScriptLanguage language, string code);

    Task<ScriptExecutionResult> ExecuteAsync(ScriptExecutionRequest request, CancellationToken ct = default);
}

// ───────────────────────────────── templates ─────────────────────────────────

public sealed record ScriptTemplateDto(
    string Id,
    string Name,
    string Summary,
    string Category,
    ScriptLanguage Language,
    ScriptScope Scopes,
    string? AllowedHosts,
    string Code,
    IReadOnlyList<string> Tags);

public interface IScriptTemplateGallery
{
    IReadOnlyList<ScriptTemplateDto> All { get; }

    IReadOnlyList<string> Categories { get; }

    ScriptTemplateDto? Find(string id);

    IReadOnlyList<ScriptTemplateDto> Search(string? keyword, string? category, ScriptLanguage? language);
}

// ───────────────────────────────── persistence ─────────────────────────────────

public sealed record ScriptPublicationDto(
    Guid Id,
    Guid ScriptId,
    string Name,
    string? Summary,
    string? Category,
    ScriptLanguage Language,
    ScriptScope Scopes,
    string? AllowedHosts,
    Guid AuthorId,
    string AuthorName,
    int InstallCount,
    DateTimeOffset PublishedAt);

public interface IScriptService
{
    Task<IReadOnlyList<ScriptDto>> ListAsync(CancellationToken ct = default);

    Task<ScriptDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<ScriptDto> SaveAsync(ScriptInput input, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ScriptVersionDto>> ListVersionsAsync(Guid scriptId, CancellationToken ct = default);

    Task<string?> GetVersionCodeAsync(Guid scriptId, Guid versionId, CancellationToken ct = default);

    Task<ScriptTriggerDto> SaveTriggerAsync(
        Guid scriptId, ScriptTriggerInput input, CancellationToken ct = default);

    Task DeleteTriggerAsync(Guid scriptId, Guid triggerId, CancellationToken ct = default);

    /// <summary>Runs a saved script as the current user, recording the run.</summary>
    Task<ScriptRunDto> RunAsync(
        Guid scriptId,
        IReadOnlyDictionary<string, string>? input = null,
        ScriptRunTrigger triggeredBy = ScriptRunTrigger.Manual,
        CancellationToken ct = default);

    Task<IReadOnlyList<ScriptRunDto>> ListRunsAsync(
        Guid? scriptId = null, int take = 50, CancellationToken ct = default);

    Task<ScriptRunDto?> GetRunAsync(Guid runId, CancellationToken ct = default);

    // ── marketplace ──
    Task<IReadOnlyList<ScriptPublicationDto>> BrowseAsync(
        string? keyword = null, string? category = null, CancellationToken ct = default);

    Task<ScriptPublicationDto> PublishAsync(
        Guid scriptId, string? summary, string? category, CancellationToken ct = default);

    Task UnpublishAsync(Guid publicationId, CancellationToken ct = default);

    /// <summary>Copies a published script into the caller's workspace, disabled until they review it.</summary>
    Task<ScriptDto> InstallAsync(Guid publicationId, CancellationToken ct = default);
}

/// <summary>Which scripts want an event, and who owns them.</summary>
public interface IScriptTriggerLookup
{
    Task<IReadOnlyList<TriggerMatch>> MatchAsync(
        string eventName, Guid? driveItemId, CancellationToken ct = default);

    /// <summary>Schedule triggers whose next run time has passed.</summary>
    Task<IReadOnlyList<TriggerMatch>> DueAsync(DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>Advances a schedule trigger past the firing that was just taken.</summary>
    Task AdvanceAsync(Guid triggerId, DateTimeOffset nextUtc, CancellationToken ct = default);
}

public sealed record TriggerMatch(
    Guid TriggerId, Guid ScriptId, Guid OwnerId, string? CronExpression, string TimeZoneId);

/// <summary>
/// Fires event triggers. Implemented in the scripting layer and called by decorators around the
/// existing activity and collaboration services, so no feature service has to know scripts exist.
/// </summary>
public interface IScriptEventDispatcher
{
    Task DispatchAsync(string eventName, Guid? driveItemId, string? detail, CancellationToken ct = default);
}
