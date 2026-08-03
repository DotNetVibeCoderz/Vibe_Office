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

    public bool Enabled { get; set; } = true;

    public bool Supports(ModelCapabilities capability) => (Capabilities & capability) == capability;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(DisplayName) ? $"{ModelId} ({Kind})" : DisplayName;
}
