using System.Text.Json.Serialization;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Core.Agents;

/// <summary>
/// The three subsystems from the architecture. Every step and every log line is attributed to
/// one of them, which is what lets the UI show the user which faculty is currently working.
/// </summary>
public enum AgentOrgan
{
    /// <summary>Think: planning, reasoning, verification, summarisation.</summary>
    Brain = 0,
    /// <summary>See: screen capture and visual understanding.</summary>
    Eyes = 1,
    /// <summary>Act: files, shell, input, documents, network.</summary>
    Hands = 2,
}

public enum RunStatus
{
    Pending,
    Planning,
    Running,
    AwaitingApproval,
    Compacting,
    Verifying,
    Succeeded,
    Failed,
    Cancelled,
}

public enum StepStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled,
}

/// <summary>How much damage a tool can do. Drives approval prompts and the UI's colour coding.</summary>
public enum ToolRisk
{
    /// <summary>Reads only. No approval needed.</summary>
    Safe = 0,
    /// <summary>Creates or modifies files.</summary>
    Write = 1,
    /// <summary>Deletes or overwrites existing data.</summary>
    Destructive = 2,
    /// <summary>Runs commands or drives input — can do anything the user can.</summary>
    System = 3,
}

/// <summary>
/// A tool, plus the metadata AutoWork needs that <see cref="AIFunction"/> does not carry:
/// which organ owns it, how dangerous it is, and what to ask the user before running it.
/// </summary>
public sealed class ToolDescriptor
{
    public required AIFunction Function { get; init; }
    public required AgentOrgan Organ { get; init; }
    public ToolRisk Risk { get; init; } = ToolRisk.Safe;

    /// <summary>Grouping for the UI, e.g. "Files", "Documents", "Screen".</summary>
    public string Category { get; init; } = "General";

    public ApprovalKind ApprovalKind { get; init; } = ApprovalKind.Other;

    public string Name => Function.Name;
}

/// <summary>Everything a tool needs in order to behave itself. Handed to tool providers per run.</summary>
public sealed class ToolContext
{
    public required PathGuard Guard { get; init; }
    public required IApprovalBroker Approvals { get; init; }
    public required IActionLog Log { get; init; }
    public required Configuration.AgentOptions Options { get; init; }
    public string RunId { get; init; } = "";

    /// <summary>Where tools put files when the user did not name a destination.</summary>
    public string WorkingDirectory { get; init; } = AppPaths.WorkspaceDirectory;

    public Configuration.WebSearchOptions Search { get; init; } = new();

    /// <summary>Speech-to-text settings. Off by default, so the tool refuses until configured.</summary>
    public Meetings.TranscriptionSettings Transcription { get; init; } = new();

    /// <summary>Signed-in browser automation. Off by default — it is every account the user has.</summary>
    public Browsing.BrowserSettings Browser { get; init; } = new();

    /// <summary>
    /// Where soft-deleted files go. Defaulted rather than required so a tool set can be built in
    /// a test without one, and shared so the Recycle page and the delete tool agree on the index.
    /// </summary>
    public Storage.IRecycleBin Recycle { get; init; } = new Storage.RecycleBin();

    /// <summary>
    /// Resolves API keys a tool needs, the same way models resolve theirs — so a key still
    /// lives in the secret store or an environment variable and never in config.json.
    /// Optional: null simply means no keyed service is reachable, and tools that can degrade
    /// without one still work.
    /// </summary>
    public Security.ISecretStore? Secrets { get; init; }
}

public interface IToolProvider
{
    /// <summary>Human-readable group name, shown in Settings when listing capabilities.</summary>
    string Name { get; }

    IEnumerable<ToolDescriptor> GetTools(ToolContext context);
}

// ── Run events ────────────────────────────────────────────────────────────────────────────
// The orchestrator reports progress as immutable events. Core stays free of UI types; the
// desktop layer turns these into observable rows on the Work Tape.

/// <summary>
/// The discriminators exist so a run can be written to disk and read back as the same events.
/// They are short and fixed: a saved transcript outlives the build that wrote it, so renaming a
/// class must not make yesterday's history unreadable.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RunStartedEvent), "run.started")]
[JsonDerivedType(typeof(PlanReadyEvent), "plan")]
[JsonDerivedType(typeof(StepStartedEvent), "step.started")]
[JsonDerivedType(typeof(StepFinishedEvent), "step.finished")]
[JsonDerivedType(typeof(ToolCallEvent), "tool")]
[JsonDerivedType(typeof(AssistantMessageEvent), "assistant")]
[JsonDerivedType(typeof(AssistantDeltaEvent), "assistant.delta")]
[JsonDerivedType(typeof(CompactionEvent), "compaction")]
[JsonDerivedType(typeof(ApprovalRequestedEvent), "approval")]
[JsonDerivedType(typeof(SubAgentEvent), "subagent")]
[JsonDerivedType(typeof(UsageEvent), "usage")]
[JsonDerivedType(typeof(RunPausedEvent), "run.paused")]
[JsonDerivedType(typeof(RunResumedEvent), "run.resumed")]
[JsonDerivedType(typeof(RunFinishedEvent), "run.finished")]
public abstract record RunEvent
{
    public string RunId { get; init; } = "";
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
}

public sealed record RunStartedEvent : RunEvent
{
    public required string Goal { get; init; }
    public string? ModelDisplayName { get; init; }
}

public sealed record PlanReadyEvent : RunEvent
{
    public required AgentPlan Plan { get; init; }
}

public sealed record StepStartedEvent : RunEvent
{
    public required int Index { get; init; }
    public required string Title { get; init; }
    public AgentOrgan Organ { get; init; } = AgentOrgan.Brain;
}

public sealed record StepFinishedEvent : RunEvent
{
    public required int Index { get; init; }
    public required StepStatus Status { get; init; }
    public string? Detail { get; init; }
    public long ElapsedMs { get; init; }
}

public sealed record ToolCallEvent : RunEvent
{
    public required string Tool { get; init; }
    public required AgentOrgan Organ { get; init; }
    public string Arguments { get; init; } = "";
    public string? Result { get; init; }
    public bool Failed { get; init; }
    public long ElapsedMs { get; init; }
}

public sealed record AssistantMessageEvent : RunEvent
{
    public required string Text { get; init; }
}

/// <summary>
/// A fragment of the assistant's reply, as it arrives. <see cref="Text"/> carries everything so
/// far, so a consumer that misses one update — or joins late — still shows the right thing.
///
/// A closing <see cref="AssistantMessageEvent"/> always follows, so anything that only cares
/// about the finished reply can ignore these entirely.
/// </summary>
public sealed record AssistantDeltaEvent : RunEvent
{
    public required string Delta { get; init; }
    public required string Text { get; init; }
    public int StepIndex { get; init; }
}

/// <summary>Emitted when the context window filled up and older turns were summarised away.</summary>
public sealed record CompactionEvent : RunEvent
{
    public required int TokensBefore { get; init; }
    public required int TokensAfter { get; init; }
    public required int TurnsSummarised { get; init; }
    public int ContextWindow { get; init; }
}

public sealed record ApprovalRequestedEvent : RunEvent
{
    public required ApprovalRequest Request { get; init; }
    public ApprovalDecision? Decision { get; init; }
}

public sealed record SubAgentEvent : RunEvent
{
    public required string SubAgentId { get; init; }
    public required string Task { get; init; }
    public required StepStatus Status { get; init; }
    public string? Result { get; init; }
}

/// <summary>
/// What the run has spent so far, as the provider reported it. Emitted at step boundaries rather
/// than per request — a meter that flickers on every tool round trip is noise, not information.
/// </summary>
public sealed record UsageEvent : RunEvent
{
    public required long InputTokens { get; init; }
    public required long OutputTokens { get; init; }
    public int Calls { get; init; }

    /// <summary>
    /// Calls the provider answered without reporting usage. When this is above zero the token
    /// counts are a floor, and the UI shows them as "at least" rather than as a total.
    /// </summary>
    public int CallsWithoutUsage { get; init; }

    /// <summary>Null unless the model has both prices filled in. Never inferred.</summary>
    public decimal? Cost { get; init; }

    public string Currency { get; init; } = "USD";

    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>The run stopped between steps at the user's request and is waiting to be resumed.</summary>
public sealed record RunPausedEvent : RunEvent
{
    /// <summary>Which step the run is holding in front of, 1-based. 0 before the plan exists.</summary>
    public int BeforeStep { get; init; }
}

/// <summary>The run picked up again, carrying whatever correction the user typed while it waited.</summary>
public sealed record RunResumedEvent : RunEvent
{
    /// <summary>Null when the user simply resumed without changing anything.</summary>
    public string? Correction { get; init; }
}

public sealed record RunFinishedEvent : RunEvent
{
    public required RunStatus Status { get; init; }
    public string Summary { get; init; } = "";
    public string? Error { get; init; }
    public int TotalSteps { get; init; }
    public long ElapsedMs { get; init; }

    /// <summary>Result of the verification pass, when one ran.</summary>
    public VerificationResult? Verification { get; init; }
}

// ── Plans ─────────────────────────────────────────────────────────────────────────────────

public sealed record PlanStep
{
    public required int Index { get; init; }
    public required string Title { get; init; }

    /// <summary>What this step is for, in the model's own words. Fed back as step instructions.</summary>
    public string Intent { get; init; } = "";

    public AgentOrgan Organ { get; init; } = AgentOrgan.Hands;

    /// <summary>Steps that must finish first. Enables parallel dispatch of independent work.</summary>
    public IReadOnlyList<int> DependsOn { get; init; } = [];

    /// <summary>Checked by the verification pass.</summary>
    public IReadOnlyList<string> SuccessCriteria { get; init; } = [];
}

public sealed record AgentPlan
{
    public required string Goal { get; init; }
    public IReadOnlyList<PlanStep> Steps { get; init; } = [];
    public IReadOnlyList<string> SuccessCriteria { get; init; } = [];

    /// <summary>Anything the model wants the user to know before work starts.</summary>
    public string? Notes { get; init; }

    public static AgentPlan SingleStep(string goal) => new()
    {
        Goal = goal,
        Steps = [new PlanStep { Index = 1, Title = goal, Intent = goal, Organ = AgentOrgan.Hands }],
    };
}

public sealed record VerificationResult
{
    public required bool Passed { get; init; }
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> UnmetCriteria { get; init; } = [];
}
