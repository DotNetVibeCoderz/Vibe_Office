using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Cli;

/// <summary>Raised when the server answered, but with a refusal the user needs to read.</summary>
public sealed class CliException(string message) : Exception(message);

public sealed record TemplateCard(
    string Id, string Name, string Summary, string Category, ScriptLanguage Language,
    ScriptScope Scopes, string? AllowedHosts, IReadOnlyList<string> Tags);

public sealed record TemplateListing(IReadOnlyList<string> Categories, IReadOnlyList<TemplateCard> Templates);

/// <summary>The slice of the VibeDesk API the CLI needs.</summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public ApiClient(CliConfig config)
    {
        _http = new HttpClient { BaseAddress = new Uri(config.Endpoint), Timeout = TimeSpan.FromMinutes(6) };

        // An API key outlives the session and is the right credential for automation; the bearer
        // token from `login` is the fallback for interactive use.
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _http.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);
        }
        else if (config.AccessToken is { } token)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<(string Token, DateTimeOffset Expires, string Name)> LoginAsync(
        string email, string password, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(
            "/api/auth/token", new { email, password }, Json, ct);

        await ThrowIfFailed(response, ct);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);

        return (
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("expiresAt").GetDateTimeOffset(),
            body.GetProperty("user").GetProperty("displayName").GetString() ?? email);
    }

    public Task<IReadOnlyList<ScriptDto>> ListAsync(CancellationToken ct) =>
        GetAsync<IReadOnlyList<ScriptDto>>("/api/scripts", ct);

    public Task<ScriptDetailDto> GetAsync(Guid id, CancellationToken ct) =>
        GetAsync<ScriptDetailDto>($"/api/scripts/{id}", ct);

    public Task<IReadOnlyList<ScriptRunDto>> RunsAsync(Guid? scriptId, int take, CancellationToken ct) =>
        GetAsync<IReadOnlyList<ScriptRunDto>>(
            scriptId is { } id ? $"/api/script-runs?scriptId={id}&take={take}" : $"/api/script-runs?take={take}",
            ct);

    /// <summary>The gallery listing omits each template's code, so it deserializes to its own shape.</summary>
    public async Task<IReadOnlyList<TemplateCard>> TemplatesAsync(string? keyword, CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(keyword)
            ? "/api/script-templates"
            : $"/api/script-templates?q={Uri.EscapeDataString(keyword)}";

        var body = await GetAsync<TemplateListing>(path, ct);

        return body.Templates;
    }

    public Task<ScriptTemplateDto> TemplateAsync(string id, CancellationToken ct) =>
        GetAsync<ScriptTemplateDto>($"/api/script-templates/{Uri.EscapeDataString(id)}", ct);

    public Task<ScriptDto> SaveAsync(ScriptInput input, CancellationToken ct) =>
        PostAsync<ScriptInput, ScriptDto>("/api/scripts", input, ct);

    public Task<ScriptRunDto> RunSavedAsync(
        Guid id, IReadOnlyDictionary<string, string>? input, CancellationToken ct) =>
        PostAsync<object, ScriptRunDto>($"/api/scripts/{id}/run", new { input }, ct);

    public Task<ScriptExecutionResult> RunSourceAsync(object request, CancellationToken ct) =>
        PostAsync<object, ScriptExecutionResult>("/api/scripts/run", request, ct);

    public async Task<string?> ValidateAsync(ScriptLanguage language, string code, CancellationToken ct)
    {
        var body = await PostAsync<object, JsonElement>(
            "/api/scripts/validate", new { language, code }, ct);

        return body.GetProperty("problem").GetString();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        await ThrowIfFailed(response, ct);

        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task<TOut> PostAsync<TIn, TOut>(string path, TIn body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, Json, ct);
        await ThrowIfFailed(response, ct);

        return (await response.Content.ReadFromJsonAsync<TOut>(Json, ct))!;
    }

    /// <summary>Turns an HTTP failure into a sentence, because a status code is not an explanation.</summary>
    private static async Task ThrowIfFailed(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var raw = await response.Content.ReadAsStringAsync(ct);
        var detail = Extract(raw);

        throw new CliException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Not signed in, or the token has expired. Run 'vibedesk login'.",
            HttpStatusCode.Forbidden => detail ?? "You do not have access to that.",
            HttpStatusCode.NotFound => detail ?? "Not found.",
            _ => detail ?? $"The server returned {(int)response.StatusCode} {response.ReasonPhrase}.",
        });
    }

    private static string? Extract(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            var body = JsonDocument.Parse(raw).RootElement;

            foreach (var name in (string[])["error", "detail", "title", "message"])
            {
                if (body.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text)
                {
                    return text;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON. The raw text is still better than nothing, within reason.
        }

        return raw.Length <= 400 ? raw.Trim() : null;
    }

    public void Dispose() => _http.Dispose();
}
