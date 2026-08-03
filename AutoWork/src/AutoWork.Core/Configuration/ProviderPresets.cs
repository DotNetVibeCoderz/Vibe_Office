namespace AutoWork.Core.Configuration;

/// <summary>
/// A starting point for adding a model: the endpoint, the environment variable the vendor
/// documents, and a sensible default model id.
///
/// Model ids move faster than any shipped app can track, so these are seeds the user edits
/// in Settings — not a supported-model list. Anything with an OpenAI-compatible endpoint
/// works via the "Custom" preset even if it is not named here.
/// </summary>
public sealed record ProviderPreset
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required ProviderKind Kind { get; init; }
    public required string Endpoint { get; init; }

    /// <summary>The variable name the vendor's own docs use, so existing setups just work.</summary>
    public string EnvironmentVariable { get; init; } = "";

    /// <summary>
    /// Variable holding the endpoint, for providers where it is per-account rather than
    /// per-vendor. Azure gives every resource its own hostname, so <see cref="Endpoint"/> can
    /// only be a placeholder and this is the one that carries the real value.
    /// </summary>
    public string EndpointEnvironmentVariable { get; init; } = "";

    /// <summary>
    /// Variable holding the model id, where the vendor lets the user name it. An Azure
    /// deployment name is chosen at deployment time, so no default can be right.
    /// </summary>
    public string ModelEnvironmentVariable { get; init; } = "";

    /// <summary>
    /// True when <see cref="Endpoint"/> is a placeholder rather than a working URL. Such a
    /// preset is never seeded from a key alone — an auto-added model that cannot be called
    /// would be picked as the default planner and break every run.
    /// </summary>
    public bool EndpointIsPlaceholder { get; init; }

    public string DefaultModelId { get; init; } = "";
    public string DefaultEmbeddingModelId { get; init; } = "";
    public int ContextWindow { get; init; } = 128_000;
    public ModelCapabilities Capabilities { get; init; } = ModelCapabilities.Tools;

    /// <summary>Runs on the user's own machine, so no key is required.</summary>
    public bool IsLocal { get; init; }

    /// <summary>Where to get a key. Shown as a link in Settings.</summary>
    public string? ConsoleUrl { get; init; }
}

public static class ProviderPresets
{
    public static IReadOnlyList<ProviderPreset> All { get; } =
    [
        new()
        {
            Id = "openai",
            DisplayName = "OpenAI",
            Kind = ProviderKind.OpenAICompatible,
            Endpoint = "https://api.openai.com/v1",
            EnvironmentVariable = "OPENAI_API_KEY",
            DefaultModelId = "gpt-4o",
            DefaultEmbeddingModelId = "text-embedding-3-small",
            ContextWindow = 128_000,
            Capabilities = ModelCapabilities.Tools | ModelCapabilities.Vision | ModelCapabilities.Embeddings,
            ConsoleUrl = "https://platform.openai.com/api-keys",
        },
        new()
        {
            Id = "azure-openai",
            DisplayName = "Azure OpenAI",
            Kind = ProviderKind.OpenAICompatible,

            // Azure's newer /openai/v1 surface speaks plain OpenAI, so it needs no separate
            // client. Users paste the resource URL from the portal and NormalizeAzureEndpoint
            // completes it — asking them to remember the suffix would only produce 404s.
            Endpoint = "https://YOUR-RESOURCE.openai.azure.com/openai/v1",
            EndpointIsPlaceholder = true,
            EnvironmentVariable = "AZURE_OPENAI_API_KEY",
            EndpointEnvironmentVariable = "AZURE_OPENAI_ENDPOINT",
            ModelEnvironmentVariable = "AZURE_OPENAI_DEPLOYMENT",
            DefaultModelId = "gpt-5-mini",
            DefaultEmbeddingModelId = "text-embedding-3-small",
            ContextWindow = 272_000,
            Capabilities = ModelCapabilities.Tools | ModelCapabilities.Vision
                           | ModelCapabilities.Embeddings | ModelCapabilities.Reasoning,
            ConsoleUrl = "https://portal.azure.com",
        },
        new()
        {
            Id = "anthropic",
            DisplayName = "Anthropic",
            Kind = ProviderKind.Anthropic,
            Endpoint = "https://api.anthropic.com/v1",
            EnvironmentVariable = "ANTHROPIC_API_KEY",
            DefaultModelId = "claude-sonnet-5",
            ContextWindow = 200_000,
            Capabilities = ModelCapabilities.Tools | ModelCapabilities.Vision | ModelCapabilities.Reasoning,
            ConsoleUrl = "https://console.anthropic.com/settings/keys",
        },
        new()
        {
            Id = "gemini",
            DisplayName = "Google Gemini",
            Kind = ProviderKind.OpenAICompatible,
            Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/",
            EnvironmentVariable = "GEMINI_API_KEY",
            DefaultModelId = "gemini-2.5-pro",
            DefaultEmbeddingModelId = "text-embedding-004",
            ContextWindow = 1_000_000,
            Capabilities = ModelCapabilities.Tools | ModelCapabilities.Vision | ModelCapabilities.Embeddings,
            ConsoleUrl = "https://aistudio.google.com/apikey",
        },
        new()
        {
            Id = "ollama",
            DisplayName = "Ollama (local)",
            Kind = ProviderKind.Ollama,
            Endpoint = "http://localhost:11434",
            EnvironmentVariable = "OLLAMA_HOST",
            DefaultModelId = "qwen2.5:14b",
            DefaultEmbeddingModelId = "nomic-embed-text",
            ContextWindow = 32_000,
            Capabilities = ModelCapabilities.Tools | ModelCapabilities.Embeddings,
            IsLocal = true,
            ConsoleUrl = "https://ollama.com/download",
        },
        new()
        {
            Id = "deepseek",
            DisplayName = "DeepSeek",
            Kind = ProviderKind.OpenAICompatible,
            Endpoint = "https://api.deepseek.com/v1",
            EnvironmentVariable = "DEEPSEEK_API_KEY",
            DefaultModelId = "deepseek-chat",
            ContextWindow = 128_000,
            Capabilities = ModelCapabilities.Tools | ModelCapabilities.Reasoning,
            ConsoleUrl = "https://platform.deepseek.com/api_keys",
        },
        new()
        {
            Id = "qwen",
            DisplayName = "Qwen (DashScope)",
            Kind = ProviderKind.OpenAICompatible,
            Endpoint = "https://dashscope-intl.aliyuncs.com/compatible-mode/v1",
            EnvironmentVariable = "DASHSCOPE_API_KEY",
            DefaultModelId = "qwen-max",
            DefaultEmbeddingModelId = "text-embedding-v3",
            ContextWindow = 131_000,
            Capabilities = ModelCapabilities.Tools | ModelCapabilities.Vision | ModelCapabilities.Embeddings,
            ConsoleUrl = "https://dashscope.console.aliyun.com/apiKey",
        },
        new()
        {
            Id = "moonshot",
            DisplayName = "Moonshot (Kimi)",
            Kind = ProviderKind.OpenAICompatible,
            Endpoint = "https://api.moonshot.ai/v1",
            EnvironmentVariable = "MOONSHOT_API_KEY",
            DefaultModelId = "kimi-k2-0905-preview",
            ContextWindow = 256_000,
            Capabilities = ModelCapabilities.Tools,
            ConsoleUrl = "https://platform.moonshot.ai/console/api-keys",
        },
        new()
        {
            Id = "openrouter",
            DisplayName = "OpenRouter",
            Kind = ProviderKind.OpenAICompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            EnvironmentVariable = "OPENROUTER_API_KEY",
            DefaultModelId = "anthropic/claude-sonnet-4.5",
            ContextWindow = 200_000,
            Capabilities = ModelCapabilities.Tools | ModelCapabilities.Vision,
            ConsoleUrl = "https://openrouter.ai/keys",
        },
        new()
        {
            Id = "lmstudio",
            DisplayName = "LM Studio (local)",
            Kind = ProviderKind.OpenAICompatible,
            Endpoint = "http://localhost:1234/v1",
            DefaultModelId = "local-model",
            ContextWindow = 32_000,
            Capabilities = ModelCapabilities.Tools | ModelCapabilities.Embeddings,
            IsLocal = true,
            ConsoleUrl = "https://lmstudio.ai",
        },
        new()
        {
            Id = "custom",
            DisplayName = "Custom OpenAI-compatible endpoint",
            Kind = ProviderKind.OpenAICompatible,
            Endpoint = "",
            DefaultModelId = "",
            ContextWindow = 128_000,
            Capabilities = ModelCapabilities.Tools,
        },
    ];

    public static ProviderPreset? Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds an unsaved profile from a preset, ready for the user to fill in a key.</summary>
    public static ModelProfile CreateProfile(ProviderPreset preset, string? modelId = null) => new()
    {
        DisplayName = string.IsNullOrWhiteSpace(modelId)
            ? $"{preset.DisplayName} — {preset.DefaultModelId}"
            : $"{preset.DisplayName} — {modelId}",
        Preset = preset.Id,
        Kind = preset.Kind,
        Endpoint = preset.Endpoint,
        ModelId = modelId ?? preset.DefaultModelId,
        ApiKeyRef = preset.IsLocal || string.IsNullOrEmpty(preset.EnvironmentVariable)
            ? ""
            : $"env:{preset.EnvironmentVariable}",
        ContextWindow = preset.ContextWindow,
        Capabilities = preset.Capabilities,
    };
}
