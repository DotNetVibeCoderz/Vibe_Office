namespace AutoWork.Core.Configuration;

/// <summary>
/// How AutoWork talks to a provider on the wire. Most vendors ship an OpenAI-compatible
/// surface, so <see cref="OpenAICompatible"/> is the workhorse and the rest exist only
/// where the protocol genuinely differs.
/// </summary>
public enum ProviderKind
{
    /// <summary>Anything speaking the OpenAI /v1/chat/completions shape.</summary>
    OpenAICompatible = 0,

    /// <summary>Anthropic's native Messages API.</summary>
    Anthropic = 1,

    /// <summary>Ollama's native API (local models).</summary>
    Ollama = 2,
}

/// <summary>What a model can actually do. Drives tool exposure and routing.</summary>
[Flags]
public enum ModelCapabilities
{
    None = 0,
    /// <summary>Supports function/tool calling — required for the agent loop.</summary>
    Tools = 1 << 0,
    /// <summary>Accepts images — required for the Eyes subsystem.</summary>
    Vision = 1 << 1,
    /// <summary>Produces text embeddings.</summary>
    Embeddings = 1 << 2,
    /// <summary>Emits extended reasoning; the planner prefers these.</summary>
    Reasoning = 1 << 3,
}

/// <summary>
/// One configured, callable model. Users add these from Settings; the installer seeds a few.
/// The API key is never stored here — <see cref="ApiKeyRef"/> names an entry in the secret
/// store or an environment variable, so config.json stays safe to copy around.
/// </summary>
public sealed class ModelProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    /// <summary>What the user sees in the model picker.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Preset this was created from, e.g. "openai", "anthropic". Informational.</summary>
    public string Preset { get; set; } = "custom";

    public ProviderKind Kind { get; set; } = ProviderKind.OpenAICompatible;

    /// <summary>Base URL, e.g. https://api.openai.com/v1. Required except for presets that supply one.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>The provider's own model id, e.g. "gpt-4o", "claude-opus-4", "qwen2.5:14b".</summary>
    public string ModelId { get; set; } = "";

    /// <summary>
    /// Where to find the key. Either a secret-store name, or "env:VAR_NAME" to read the
    /// environment at call time (nothing is persisted in that case).
    /// </summary>
    public string ApiKeyRef { get; set; } = "";

    /// <summary>Total context window in tokens. Drives auto-compaction thresholds.</summary>
    public int ContextWindow { get; set; } = 128_000;

    public int MaxOutputTokens { get; set; } = 8_192;

    public float Temperature { get; set; } = 0.2f;

    public ModelCapabilities Capabilities { get; set; } = ModelCapabilities.Tools;

    /// <summary>Extra headers some gateways require (e.g. OpenRouter attribution).</summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Price per million input tokens, and per million output tokens.
    ///
    /// Left empty on purpose. AutoWork ships no price table: model ids drift, prices drift
    /// faster, and a stale built-in figure that quietly under-reports what a run cost is worse
    /// than no figure at all. Fill these in from your provider's pricing page and the meter
    /// shows money; leave them and it shows tokens, which are true whatever the price is.
    /// </summary>
    public decimal? InputPricePerMillion { get; set; }

    public decimal? OutputPricePerMillion { get; set; }

    /// <summary>Whatever the prices above are quoted in. Only ever displayed, never converted.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>True when both prices are known, which is the only case a cost can be shown for.</summary>
    public bool HasPricing => InputPricePerMillion is not null && OutputPricePerMillion is not null;

    /// <summary>Null when this model has no price set — the caller must not substitute zero.</summary>
    public decimal? CostOf(long inputTokens, long outputTokens) =>
        HasPricing
            ? inputTokens / 1_000_000m * InputPricePerMillion!.Value
              + outputTokens / 1_000_000m * OutputPricePerMillion!.Value
            : null;

    public bool Enabled { get; set; } = true;

    public bool Supports(ModelCapabilities capability) => (Capabilities & capability) == capability;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(DisplayName) ? $"{ModelId} ({Kind})" : DisplayName;
}
