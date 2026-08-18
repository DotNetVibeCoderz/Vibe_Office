using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using VibeDesk.Application.Assistant;
using VibeDesk.Domain;

namespace VibeDesk.Client.Services;

/// <summary>
/// Mr Clippy over HTTP. Provider availability is fetched once at start-up because the chat panel
/// reads <see cref="IsConfigured"/> synchronously during render, and a render path cannot await.
/// </summary>
public sealed class ClippyApiClient(HttpClient http) : ApiClientBase(http), IClippyService
{
    private StatusResponse? _status;

    public bool IsConfigured => _status?.Configured ?? false;

    public IReadOnlyList<AiProvider> AvailableProviders => _status?.Providers ?? [];

    public IReadOnlyList<string> ModelsFor(AiProvider provider) =>
        _status?.Models is { } models && models.TryGetValue(provider.ToString(), out var list) ? list : [];

    /// <summary>Called by the host once the user is signed in; the endpoint requires a token.</summary>
    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        try
        {
            _status = await GetAsync<StatusResponse>("/api/assistant/status", ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A missing assistant is a degraded panel, not a broken app.
            _status = null;
        }
    }

    public async Task<IReadOnlyList<ChatSessionDto>> ListSessionsAsync(CancellationToken ct = default) =>
        await GetAsync<List<ChatSessionDto>>("/api/assistant/sessions", ct).ConfigureAwait(false) ?? [];

    public async Task<ChatSessionDto> CreateSessionAsync(
        string? title = null, ClippyContext? context = null, CancellationToken ct = default) =>
        await SendAsync<ChatSessionDto>(HttpMethod.Post, "/api/assistant/sessions", new { title, context }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the session.");

    public Task<ChatSessionDto?> GetSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        GetAsync<ChatSessionDto>($"/api/assistant/sessions/{sessionId}", ct);

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        Guid sessionId, CancellationToken ct = default) =>
        await GetAsync<List<ChatMessageDto>>($"/api/assistant/sessions/{sessionId}/messages", ct)
            .ConfigureAwait(false) ?? [];

    public Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/assistant/sessions/{sessionId}", null, ct);

    public Task ResetSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/assistant/sessions/{sessionId}/reset", null, ct);

    public Task RenameSessionAsync(Guid sessionId, string title, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"/api/assistant/sessions/{sessionId}/title", new { title }, ct);

    public Task UpdateSessionSettingsAsync(
        Guid sessionId,
        AiProvider? provider,
        string? model,
        double? temperature,
        string? systemPromptOverride,
        CancellationToken ct = default) =>
        SendAsync(
            HttpMethod.Put,
            $"/api/assistant/sessions/{sessionId}/settings",
            new { provider, model, temperature, systemPrompt = systemPromptOverride },
            ct);

    /// <summary>Consumes the endpoint's server-sent events and re-emits them as chunks.</summary>
    public async IAsyncEnumerable<ClippyChunk> SendAsync(
        Guid sessionId,
        string message,
        ClippyContext? context,
        IReadOnlyList<ChatAttachmentInput>? attachments = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/assistant/sessions/{sessionId}/messages")
        {
            Content = JsonContent.Create(
                new { message, context, attachments }, options: ApiJson.Options),
        };

        HttpResponseMessage? response = null;
        string? failure = null;

        try
        {
            // ResponseHeadersRead, or HttpClient buffers the whole reply and nothing streams.
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        if (response is null)
        {
            yield return new ClippyChunk(null, null, true, failure);
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await ApiSession.ReadErrorAsync(response, ct).ConfigureAwait(false);
                yield return new ClippyChunk(null, null, true, error ?? response.ReasonPhrase);
                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                // SSE separates events with a blank line; only `data:` carries a payload.
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

                ClippyChunk? chunk = null;

                try
                {
                    chunk = JsonSerializer.Deserialize<ClippyChunk>(line[6..], ApiJson.Options);
                }
                catch (JsonException)
                {
                    // A malformed frame is not worth ending the reply over.
                }

                if (chunk is not null) yield return chunk;
            }
        }
    }

    public async Task<ChatAttachmentInput> UploadAttachmentAsync(
        string fileName,
        string contentType,
        Stream content,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", fileName);

        var response = await Http.PostAsync("/api/assistant/attachments", form, ct).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<ChatAttachmentInput>(ApiJson.Options, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The server did not return the attachment.");
    }

    private sealed record StatusResponse(
        bool Configured,
        List<AiProvider> Providers,
        Dictionary<string, List<string>> Models);
}
