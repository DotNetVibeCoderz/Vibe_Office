using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text.Json;
using AutoWork.Core.Configuration;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace AutoWork.Providers;

public sealed record ConnectionTestResult(bool Success, string Message, long ElapsedMs, string? ModelEcho = null);

/// <summary>
/// Turns a <see cref="ModelProfile"/> into something callable.
///
/// Nearly every vendor ships an OpenAI-compatible surface, so one code path covers OpenAI,
/// Gemini, DeepSeek, Qwen, Moonshot, OpenRouter, LM Studio and any gateway a user points at.
/// Only Anthropic and Ollama get their own clients, because their protocols genuinely differ.
///
/// Clients are cached per profile — creating an HttpClient per agent turn is a socket leak
/// waiting to happen, and profiles change rarely.
/// </summary>
public sealed class ModelClientFactory : IDisposable
{
    private readonly ISecretStore _secrets;
    private readonly Dictionary<string, IChatClient> _chatClients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _embedders = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ModelClientFactory(ISecretStore secrets) => _secrets = secrets;

    /// <summary>Drops cached clients. Call when models are edited in Settings.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            foreach (var client in _chatClients.Values) TryDispose(client);
            foreach (var embedder in _embedders.Values) TryDispose(embedder);
            _chatClients.Clear();
            _embedders.Clear();
        }
    }

    public IChatClient CreateChatClient(ModelProfile profile)
    {
        var key = CacheKey(profile);

        lock (_gate)
        {
            if (_chatClients.TryGetValue(key, out var cached)) return cached;

            // Compatibility sits closest to the wire, inside the function-invocation loop, so
            // every request the loop issues is repaired — not just the first one.
            var client = new ParameterCompatibilityChatClient(BuildChatClient(profile));

            // Function invocation is what makes the agent loop possible at all. The iteration
            // cap is a runaway guard: a model that keeps calling tools without converging would
            // otherwise burn the user's quota inside a single step.
            var configured = client
                .AsBuilder()
                .UseFunctionInvocation(configure: invoking =>
                {
                    invoking.MaximumIterationsPerRequest = 16;
                    invoking.IncludeDetailedErrors = true;
                })
                .Build();

            _chatClients[key] = configured;
            return configured;
        }
    }

    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ModelProfile? profile)
    {
        if (profile is null) return null;

        var key = CacheKey(profile) + "|embed";

        lock (_gate)
        {
            if (_embedders.TryGetValue(key, out var cached)) return cached;

            IEmbeddingGenerator<string, Embedding<float>>? generator = profile.Kind switch
            {
                ProviderKind.Ollama => new OllamaApiClient(new Uri(NormalizeEndpoint(profile)), profile.ModelId),
                ProviderKind.OpenAICompatible => BuildOpenAIClient(profile)
                    .GetEmbeddingClient(profile.ModelId)
                    .AsIEmbeddingGenerator(),

                // Anthropic does not serve an embeddings endpoint. Callers fall back to
                // keyword search, which JsonKnowledgeStore already handles.
                _ => null,
            };

            if (generator is null) return null;

            _embedders[key] = generator;
            return generator;
        }
    }

    private IChatClient BuildChatClient(ModelProfile profile) => profile.Kind switch
    {
        ProviderKind.Anthropic => new AnthropicChatClient(
            apiKey: RequireKey(profile),
            model: profile.ModelId,
            endpoint: NormalizeEndpoint(profile),
            defaultMaxTokens: profile.MaxOutputTokens,
            extraHeaders: profile.Headers),

        ProviderKind.Ollama => new OllamaApiClient(new Uri(NormalizeEndpoint(profile)), profile.ModelId),

        ProviderKind.OpenAICompatible => BuildOpenAIClient(profile)
            .GetChatClient(profile.ModelId)
            .AsIChatClient(),

        _ => throw new NotSupportedException($"Provider kind {profile.Kind} is not supported."),
    };

    private OpenAIClient BuildOpenAIClient(ModelProfile profile)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(NormalizeEndpoint(profile)) };

        if (profile.Headers.Count > 0)
            options.AddPolicy(new HeaderPolicy(profile.Headers), PipelinePosition.PerCall);

        // Local servers (Ollama's OpenAI shim, LM Studio, vLLM) usually ignore the key but the
        // SDK still requires a non-empty credential.
        var key = _secrets.Resolve(profile.ApiKeyRef);
        return new OpenAIClient(new ApiKeyCredential(string.IsNullOrWhiteSpace(key) ? "not-required" : key), options);
    }

    private string RequireKey(ModelProfile profile)
    {
        var key = _secrets.Resolve(profile.ApiKeyRef);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"\"{profile.DisplayName}\" has no API key. Add one in Settings › Models, " +
                $"or set the {DescribeKeySource(profile)} environment variable.");
        }
        return key;
    }

    private static string DescribeKeySource(ModelProfile profile) =>
        profile.ApiKeyRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? profile.ApiKeyRef[4..]
            : ProviderPresets.Find(profile.Preset)?.EnvironmentVariable ?? "provider";

    private static string NormalizeEndpoint(ModelProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Endpoint)) return NormalizeAzureEndpoint(profile.Endpoint);

        var preset = ProviderPresets.Find(profile.Preset);
        if (preset is not null && !string.IsNullOrWhiteSpace(preset.Endpoint)) return preset.Endpoint;

        throw new InvalidOperationException(
            $"\"{profile.DisplayName}\" has no endpoint. Set one in Settings › Models.");
    }

    /// <summary>
    /// Completes an Azure OpenAI resource URL into its OpenAI-compatible base.
    ///
    /// The Azure portal shows <c>https://name.openai.azure.com/</c>, which is what people paste,
    /// but chat completions live under <c>/openai/v1</c>. Left alone that is a 404 with no clue
    /// as to why, so the endpoint is completed rather than validated. Anything already carrying
    /// an <c>/openai</c> path — including the older <c>/openai/deployments/…</c> form — is left
    /// exactly as typed.
    /// </summary>
    public static string NormalizeAzureEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return endpoint;

        var isAzure = uri.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase)
                      || uri.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase);

        if (!isAzure) return endpoint;

        var path = uri.AbsolutePath.Trim('/');
        if (path.StartsWith("openai", StringComparison.OrdinalIgnoreCase)) return endpoint;

        return $"{uri.Scheme}://{uri.Authority}/openai/v1";
    }

    /// <summary>Includes the resolved key so rotating a key invalidates the cached client.</summary>
    private string CacheKey(ModelProfile profile) =>
        $"{profile.Id}|{profile.Kind}|{profile.Endpoint}|{profile.ModelId}|{_secrets.Resolve(profile.ApiKeyRef)?.GetHashCode() ?? 0}";

    // ── Diagnostics ───────────────────────────────────────────────────────────────────────

    /// <summary>Sends the smallest possible real request so Settings can show a green tick.</summary>
    public async Task<ConnectionTestResult> TestAsync(ModelProfile profile, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var client = CreateChatClient(profile);

            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Reply with the single word: ok")],
                // Not 16. A reasoning model bills its private deliberation against this cap and
                // spends it before writing a word, so a tight budget buys an empty reply at full
                // price. One word costs one word either way for a model that does not reason.
                new ChatOptions { MaxOutputTokens = 1_000, Temperature = 0 },
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            // The test deliberately sends temperature and a token cap, so a model that refuses
            // them is found here rather than three steps into someone's first real run.
            var adjustments = client.GetService(typeof(ParameterCompatibilityChatClient))
                is ParameterCompatibilityChatClient compatibility ? compatibility.Adjustments : [];

            var note = adjustments.Count == 0
                ? ""
                : $" This model does not accept {NaturalList(adjustments)}, so AutoWork works around that.";

            var text = response.Text?.Trim();

            // Connected but silent is not a working model, and calling it success would send the
            // user off to debug their prompt instead of their configuration.
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ConnectionTestResult(false,
                    "The endpoint answered but the model returned no text. Check that the model id names a " +
                    "chat model, and that its output token limit leaves room for a reply.",
                    stopwatch.ElapsedMilliseconds);
            }

            return new ConnectionTestResult(true,
                $"Connected in {stopwatch.ElapsedMilliseconds} ms.{note}", stopwatch.ElapsedMilliseconds, text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ConnectionTestResult(false, Explain(ex), stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>Turns SDK exceptions into something a user can act on.</summary>
    private static string Explain(Exception ex) => ex switch
    {
        InvalidOperationException => ex.Message,
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } =>
            "The API key was rejected. Check the key in Settings › Models.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } =>
            "The endpoint responded 404. Check the base URL and the model id.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } =>
            "Rate limited by the provider. Wait a moment and try again.",
        HttpRequestException { InnerException: System.Net.Sockets.SocketException } =>
            "Could not reach the endpoint. Check the URL, and that a local server is running.",
        ClientResultException client => $"Provider returned {client.Status}: {Shorten(client.Message)}",
        HttpRequestException http => Shorten(http.Message),
        _ => Shorten(ex.Message),
    };

    private static string Shorten(string message) =>
        message.Length <= 300 ? message : message[..300] + "…";

    private static string NaturalList(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };

    /// <summary>Lists models the endpoint actually serves, so users pick instead of typing.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(ModelProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            if (profile.Kind == ProviderKind.Ollama)
            {
                var ollama = new OllamaApiClient(new Uri(NormalizeEndpoint(profile)));
                var local = await ollama.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false);
                return local.Select(m => m.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            }

            if (profile.Kind == ProviderKind.Anthropic)
            {
                using var http = new HttpClient { BaseAddress = new Uri(NormalizeEndpoint(profile).TrimEnd('/') + "/") };
                http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", RequireKey(profile));
                http.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                return await ReadModelIdsAsync(http, "models", cancellationToken).ConfigureAwait(false);
            }

            using var openAiHttp = new HttpClient { BaseAddress = new Uri(NormalizeEndpoint(profile).TrimEnd('/') + "/") };
            var key = _secrets.Resolve(profile.ApiKeyRef);
            if (!string.IsNullOrWhiteSpace(key))
                openAiHttp.DefaultRequestHeaders.Authorization = new("Bearer", key);

            return await ReadModelIdsAsync(openAiHttp, "models", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Listing is a convenience. A provider that does not expose it is not an error;
            // the user types the model id instead.
            return [];
        }
    }

    private static async Task<IReadOnlyList<string>> ReadModelIdsAsync(
        HttpClient http, string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return [];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void TryDispose(object candidate)
    {
        if (candidate is IDisposable disposable) disposable.Dispose();
    }

    public void Dispose() => Invalidate();
}
