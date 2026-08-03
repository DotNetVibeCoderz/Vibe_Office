using AutoWork.Core.Security;

namespace AutoWork.Core.Configuration;

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public enum UiLanguage
{
    /// <summary>Follow the OS language, falling back to English.</summary>
    System = 0,
    English = 1,
    Indonesian = 2,
}

public sealed class AppearanceOptions
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public UiLanguage Language { get; set; } = UiLanguage.System;

    /// <summary>Honour the OS "reduce motion" setting, and let users force it on.</summary>
    public bool ReduceMotion { get; set; }

    public double UiScale { get; set; } = 1.0;
}

public sealed class AgentOptions
{
    /// <summary>Model used for planning and reasoning (the Brain).</summary>
    public string? PlannerModelId { get; set; }

    /// <summary>Model used for step execution. Falls back to the planner model.</summary>
    public string? ExecutorModelId { get; set; }

    /// <summary>Model used for screenshot understanding. Must have Vision capability.</summary>
    public string? VisionModelId { get; set; }

    /// <summary>Model used to embed knowledge-base entries.</summary>
    public string? EmbeddingModelId { get; set; }

    /// <summary>Hard ceiling on agent turns before a run is abandoned.</summary>
    public int MaxSteps { get; set; } = 40;

    /// <summary>Consecutive tool failures tolerated before the run gives up.</summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>How many sub-agents may run at once.</summary>
    public int MaxParallelSubAgents { get; set; } = 3;

    public bool EnableSubAgents { get; set; } = true;

    /// <summary>Run a verification pass against the plan's success criteria before reporting done.</summary>
    public bool EnableSelfVerification { get; set; } = true;

    /// <summary>Summarise older turns automatically as the context window fills.</summary>
    public bool EnableAutoCompact { get; set; } = true;

    /// <summary>Fraction of the model's context window that triggers compaction. 0.75 = compact at 75% full.</summary>
    public double AutoCompactThreshold { get; set; } = 0.75;

    /// <summary>Recent turns always kept verbatim when compacting.</summary>
    public int CompactKeepRecentTurns { get; set; } = 6;

    /// <summary>Per-tool-call wall clock limit.</summary>
    public int ToolTimeoutSeconds { get; set; } = 120;

    /// <summary>Pause and ask before any step that writes, deletes or runs a command.</summary>
    public bool ConfirmDestructiveActions { get; set; } = true;
}

/// <summary>
/// Web search. A key buys better results but is not required — without one the tool falls back
/// to keyless backends rather than disappearing, so a fresh install can still research.
/// </summary>
public sealed class WebSearchOptions
{
    /// <summary>
    /// Secret-store name or "env:VAR" for a Tavily key. Never the key itself. Defaults to the
    /// variable Tavily's own docs use, so an existing setup needs no configuration at all.
    /// </summary>
    public string ApiKeyRef { get; set; } = "env:TAVILY_API_KEY";

    /// <summary>Results requested per search. Enough to triangulate, few enough to stay cheap.</summary>
    public int MaxResults { get; set; } = 5;
}

public sealed class IntegrationSettings
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; }

    /// <summary>Non-secret settings (workspace ids, base URLs, default folders).</summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Secret-store names or "env:VAR" references. Never the secret itself.</summary>
    public Dictionary<string, string> SecretRefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The whole of AutoWork's persisted state, minus secrets. Serialised to config.json,
/// overlaid by environment variables at load time, and editable from Settings.
/// </summary>
public sealed class AutoWorkConfig
{
    /// <summary>Bumped when the shape changes so migrations have something to switch on.</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<ModelProfile> Models { get; set; } = [];

    public AgentOptions Agent { get; set; } = new();

    public PermissionPolicy Permissions { get; set; } = new();

    public AppearanceOptions Appearance { get; set; } = new();

    public WebSearchOptions Search { get; set; } = new();

    public List<IntegrationSettings> Integrations { get; set; } = [];

    /// <summary>Persist run transcripts to disk so past work can be reopened.</summary>
    public bool KeepRunHistory { get; set; } = true;

    public int RunHistoryRetentionDays { get; set; } = 30;

    public bool FirstRunCompleted { get; set; }

    public ModelProfile? FindModel(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Models.FirstOrDefault(m => m.Id == id);

    /// <summary>Planner model, or the first enabled tool-capable model as a fallback.</summary>
    public ModelProfile? ResolvePlannerModel() =>
        FindModel(Agent.PlannerModelId)
        ?? Models.FirstOrDefault(m => m.Enabled && m.Supports(ModelCapabilities.Tools));

    public ModelProfile? ResolveExecutorModel() =>
        FindModel(Agent.ExecutorModelId) ?? ResolvePlannerModel();

    public ModelProfile? ResolveVisionModel() =>
        FindModel(Agent.VisionModelId)
        ?? Models.FirstOrDefault(m => m.Enabled && m.Supports(ModelCapabilities.Vision));

    public ModelProfile? ResolveEmbeddingModel() =>
        FindModel(Agent.EmbeddingModelId)
        ?? Models.FirstOrDefault(m => m.Enabled && m.Supports(ModelCapabilities.Embeddings));

    public IntegrationSettings GetIntegration(string id)
    {
        var existing = Integrations.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var created = new IntegrationSettings { Id = id };
        Integrations.Add(created);
        return created;
    }
}
