using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using VibeDesk.Ai.Backends;
using VibeDesk.Domain;

namespace VibeDesk.Ai;

/// <summary>
/// Builds a <see cref="Kernel"/> for a chosen provider and model, and says which providers are
/// usable at all.
/// </summary>
/// <remarks>
/// A kernel is built per request rather than cached, because the plugins are stateful: they close
/// over the signed-in user's services and the document currently open. Caching a kernel would
/// therefore cache one user's access — the one bug in this area that actually matters.
/// </remarks>
public sealed class KernelFactory(IOptions<AssistantOptions> options)
{
    private readonly AssistantOptions _options = options.Value;

    public AssistantOptions Options => _options;

    /// <summary>Providers with enough configuration to be worth showing in the picker.</summary>
    public IReadOnlyList<AiProvider> AvailableProviders { get; } = Discover(options.Value);

    public bool IsConfigured => AvailableProviders.Count > 0;

    /// <summary>Falls back to the first configured provider when the requested one has no credentials.</summary>
    public AiProvider ResolveProvider(AiProvider? requested)
    {
        var provider = requested ?? _options.Provider;

        if (AvailableProviders.Contains(provider)) return provider;

        return AvailableProviders.Count > 0 ? AvailableProviders[0] : provider;
    }

    public string ResolveModel(AiProvider provider, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested;

        return provider switch
        {
            AiProvider.Anthropic => _options.Anthropic.Model,
            AiProvider.Google => _options.Google.Model,
            AiProvider.Ollama => _options.Ollama.Model,
            _ => _options.OpenAI.Model,
        };
    }

    public IReadOnlyList<string> ModelsFor(AiProvider provider)
    {
        var (configured, fallback) = provider switch
        {
            AiProvider.Anthropic => (_options.Anthropic.Models, _options.Anthropic.Model),
            AiProvider.Google => (_options.Google.Models, _options.Google.Model),
            AiProvider.Ollama => (_options.Ollama.Models, _options.Ollama.Model),
            _ => (_options.OpenAI.Models, _options.OpenAI.Model),
        };

        return configured.Count > 0 ? configured : [fallback];
    }

    /// <summary>
    /// A kernel wired to one provider. Anthropic gets no chat service registered — its backend talks
    /// to the SDK directly and uses the kernel only as the plugin catalogue.
    /// </summary>
    public Kernel Build(AiProvider provider, string model)
    {
        var builder = Kernel.CreateBuilder();

        switch (provider)
        {
            case AiProvider.Anthropic:
                break;

            case AiProvider.Google:
                builder.AddGoogleAIGeminiChatCompletion(model, _options.Google.ApiKey);
                break;

            case AiProvider.Ollama:
                builder.AddOllamaChatCompletion(model, new Uri(_options.Ollama.Endpoint));
                break;

            default:
                if (string.IsNullOrWhiteSpace(_options.OpenAI.Endpoint))
                {
                    builder.AddOpenAIChatCompletion(model, _options.OpenAI.ApiKey);
                }
                else
                {
                    // An explicit endpoint covers Azure-style gateways and OpenAI-compatible proxies.
                    builder.AddOpenAIChatCompletion(
                        model,
                        new Uri(_options.OpenAI.Endpoint),
                        _options.OpenAI.ApiKey);
                }

                break;
        }

        return builder.Build();
    }

    public IChatBackend BackendFor(AiProvider provider) => provider == AiProvider.Anthropic
        ? new AnthropicBackend(_options.Anthropic.ApiKey)
        : new SemanticKernelBackend();

    private static List<AiProvider> Discover(AssistantOptions options)
    {
        var providers = new List<AiProvider>();

        if (!string.IsNullOrWhiteSpace(options.OpenAI.ApiKey)) providers.Add(AiProvider.OpenAI);
        if (!string.IsNullOrWhiteSpace(options.Anthropic.ApiKey)) providers.Add(AiProvider.Anthropic);
        if (!string.IsNullOrWhiteSpace(options.Google.ApiKey)) providers.Add(AiProvider.Google);

        // Ollama is local and needs no key, so a configured endpoint is the whole requirement. It is
        // listed last so a cloud provider stays the default when both are set up.
        if (!string.IsNullOrWhiteSpace(options.Ollama.Endpoint)) providers.Add(AiProvider.Ollama);

        // Put the configured default first so the picker opens on what the admin chose.
        var index = providers.IndexOf(options.Provider);
        if (index > 0)
        {
            providers.RemoveAt(index);
            providers.Insert(0, options.Provider);
        }

        return providers;
    }
}
