using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoWork.Core.Agents;
using Microsoft.Extensions.AI;

namespace AutoWork.Integrations;

/// <summary>
/// Shared HTTP plumbing for the connectors.
///
/// One <see cref="HttpClient"/> is shared across all of them: connectors are called from agent
/// steps, and creating a client per call is the classic way to exhaust sockets under load.
/// Per-request auth headers keep that safe.
/// </summary>
public abstract class RestConnector : IIntegration
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(15),
    })
    {
        Timeout = TimeSpan.FromSeconds(45),
    };

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract string DocsUrl { get; }
    public abstract IReadOnlyList<IntegrationField> Fields { get; }

    public abstract Task<IntegrationStatus> TestAsync(
        IntegrationCredentials credentials, CancellationToken cancellationToken = default);

    public abstract IEnumerable<ToolDescriptor> GetTools(ToolContext context, IntegrationCredentials credentials);

    // ── Requests ──────────────────────────────────────────────────────────────────────────

    protected static async Task<JsonNode?> SendAsync(
        HttpMethod method,
        string url,
        Action<HttpRequestMessage> authorize,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, url);
        authorize(request);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new IntegrationException(Explain(response.StatusCode, payload));

        return string.IsNullOrWhiteSpace(payload) ? null : JsonNode.Parse(payload);
    }

    protected static Task<JsonNode?> GetAsync(string url, Action<HttpRequestMessage> authorize,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, url, authorize, null, cancellationToken);

    protected static Task<JsonNode?> PostAsync(string url, Action<HttpRequestMessage> authorize, object body,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, url, authorize, body, cancellationToken);

    /// <summary>Form-encoded POST, which is what OAuth token endpoints expect.</summary>
    protected static async Task<JsonNode?> PostFormAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> form,
        Action<HttpRequestMessage>? authorize = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form),
        };

        authorize?.Invoke(request);

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new IntegrationException(Explain(response.StatusCode, payload));

        return string.IsNullOrWhiteSpace(payload) ? null : JsonNode.Parse(payload);
    }

    protected static Action<HttpRequestMessage> Bearer(string token, params (string Name, string Value)[] extra) =>
        request =>
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd("AutoWork/0.1");
            foreach (var (name, value) in extra) request.Headers.TryAddWithoutValidation(name, value);
        };

    /// <summary>Turns an HTTP failure into something a user can act on.</summary>
    private static string Explain(System.Net.HttpStatusCode status, string payload)
    {
        var detail = ExtractMessage(payload);

        return status switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "The credentials were rejected. Check the token under Settings › Integrations.",
            System.Net.HttpStatusCode.Forbidden =>
                $"Access denied — the token is valid but lacks the required scope. {detail}".TrimEnd(),
            System.Net.HttpStatusCode.NotFound =>
                $"Not found. {detail}".TrimEnd(),
            System.Net.HttpStatusCode.TooManyRequests =>
                "Rate limited by the service. Wait a moment and try again.",
            _ => $"The service returned {(int)status}. {detail}".TrimEnd(),
        };
    }

    private static string ExtractMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return "";

        try
        {
            var node = JsonNode.Parse(payload);
            var message = node?["message"]?.GetValue<string>()
                          ?? node?["error_description"]?.GetValue<string>()
                          ?? node?["error"]?["message"]?.GetValue<string>()
                          ?? node?["errors"]?[0]?["message"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(message)) return message;
        }
        catch (JsonException) { }

        return payload.Length <= 200 ? payload : payload[..200] + "…";
    }

    // ── Tool helpers ──────────────────────────────────────────────────────────────────────

    protected ToolDescriptor Tool(AIFunction function, ToolRisk risk = ToolRisk.Safe) => new()
    {
        Function = function,
        Organ = AgentOrgan.Hands,
        Risk = risk,
        Category = DisplayName,
        ApprovalKind = risk == ToolRisk.Safe
            ? Core.Security.ApprovalKind.NetworkAccess
            : Core.Security.ApprovalKind.Other,
    };

    /// <summary>
    /// Wraps a connector call so a network or auth failure comes back as readable text the
    /// model can act on, instead of ending the agent's turn with an exception.
    /// </summary>
    protected static async Task<string> SafelyAsync(Func<Task<string>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (IntegrationException ex)
        {
            return $"ERROR: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            return $"REFUSED: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"ERROR: Could not reach the service. {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return "ERROR: The service did not respond in time.";
        }
    }

    protected static string Trim(string? text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..max] + "…";
}

public sealed class IntegrationException : Exception
{
    public IntegrationException(string message) : base(message) { }
}
