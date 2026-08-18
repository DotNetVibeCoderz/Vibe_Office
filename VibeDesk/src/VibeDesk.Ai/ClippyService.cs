using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using VibeDesk.Ai.Backends;
using VibeDesk.Ai.Plugins;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Assistant;
using VibeDesk.Application.Calendars;
using VibeDesk.Application.Drive;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Ai;

/// <summary>
/// Mr Clippy. Owns the conversation store and the per-turn assembly of prompt, history, tools and
/// provider; the actual generation belongs to an <see cref="IChatBackend"/>.
/// </summary>
public sealed class ClippyService(
    AppDbContext db,
    ICurrentUser currentUser,
    KernelFactory kernels,
    IStorageProvider storage,
    IDriveService drive,
    IDocumentContentService content,
    ICalendarService calendar,
    IHttpClientFactory httpFactory,
    ILogger<ClippyService> logger) : IClippyService
{
    private readonly AssistantOptions _options = kernels.Options;

    public bool IsConfigured => kernels.IsConfigured;

    public IReadOnlyList<AiProvider> AvailableProviders => kernels.AvailableProviders;

    public IReadOnlyList<string> ModelsFor(AiProvider provider) => kernels.ModelsFor(provider);

    // ────────────────────────────────── sessions ──────────────────────────────────

    public async Task<IReadOnlyList<ChatSessionDto>> ListSessionsAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        return await db.ChatSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsArchived)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new ChatSessionDto(
                s.Id, s.Title, s.AppContext, s.DriveItemId, s.Provider, s.Model, s.Temperature,
                s.CreatedAt, s.UpdatedAt, s.Messages.Count))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ChatSessionDto> CreateSessionAsync(
        string? title = null,
        ClippyContext? context = null,
        CancellationToken ct = default)
    {
        var session = new ChatSession
        {
            UserId = currentUser.RequireId(),
            Title = string.IsNullOrWhiteSpace(title) ? "Obrolan baru" : title.Trim(),
            AppContext = context?.App,
            DriveItemId = context?.DriveItemId,
        };

        db.ChatSessions.Add(session);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Project(session, 0);
    }

    public async Task<ChatSessionDto?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await FindAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return null;

        var count = await db.ChatMessages.CountAsync(m => m.ChatSessionId == sessionId, ct).ConfigureAwait(false);

        return Project(session, count);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        if (await FindAsync(sessionId, ct).ConfigureAwait(false) is null) return [];

        var messages = await db.ChatMessages
            .AsNoTracking()
            .Include(m => m.Attachments)
            .Where(m => m.ChatSessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return messages.Select(ToDto).ToList();
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await FindAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return;

        await DeleteAttachmentBlobsAsync(sessionId, ct).ConfigureAwait(false);

        db.ChatSessions.Remove(session);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ResetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await FindAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return;

        await DeleteAttachmentBlobsAsync(sessionId, ct).ConfigureAwait(false);

        await db.ChatMessages
            .Where(m => m.ChatSessionId == sessionId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RenameSessionAsync(Guid sessionId, string title, CancellationToken ct = default)
    {
        var session = await FindAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return;

        session.Title = string.IsNullOrWhiteSpace(title) ? session.Title : title.Trim();
        session.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateSessionSettingsAsync(
        Guid sessionId,
        AiProvider? provider,
        string? model,
        double? temperature,
        string? systemPromptOverride,
        CancellationToken ct = default)
    {
        var session = await FindAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return;

        session.Provider = provider;
        session.Model = string.IsNullOrWhiteSpace(model) ? null : model;
        session.Temperature = temperature is null ? null : Math.Clamp(temperature.Value, 0, 2);
        session.SystemPromptOverride = string.IsNullOrWhiteSpace(systemPromptOverride) ? null : systemPromptOverride;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ────────────────────────────────── generation ──────────────────────────────────

    public async IAsyncEnumerable<ClippyChunk> SendAsync(
        Guid sessionId,
        string message,
        ClippyContext context,
        IReadOnlyList<ChatAttachmentInput>? attachments = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await FindAsync(sessionId, ct).ConfigureAwait(false);

        if (session is null)
        {
            yield return new ClippyChunk(null, null, true, "Sesi obrolan tidak ditemukan.");
            yield break;
        }

        if (!kernels.IsConfigured)
        {
            yield return new ClippyChunk(
                null, null, true,
                "Belum ada penyedia AI yang dikonfigurasi. Isi Assistant:OpenAI:ApiKey (atau Anthropic/Google), " +
                "atau jalankan Ollama secara lokal, lalu mulai ulang aplikasi.");
            yield break;
        }

        // Persisted before generation: a mid-stream failure then leaves a transcript that still shows
        // what the user asked, rather than losing the turn entirely.
        var userTurn = await PersistUserTurnAsync(session, message, attachments, ct).ConfigureAwait(false);

        var provider = kernels.ResolveProvider(session.Provider);
        var model = kernels.ResolveModel(provider, session.Model);

        var kernel = kernels.Build(provider, model);
        RegisterPlugins(kernel, context);

        var settings = new ChatRunSettings(
            model,
            session.Temperature ?? _options.Temperature,
            _options.MaxTokens,
            _options.MaxToolIterations,
            _options.MaxToolResultChars);

        var history = await BuildHistoryAsync(session, context, userTurn, ct).ConfigureAwait(false);
        var backend = kernels.BackendFor(provider);

        var reply = new StringBuilder();
        var toolCalls = new List<ToolCallDto>();
        string? error = null;

        await foreach (var chunk in backend.StreamAsync(kernel, history, settings, ct).ConfigureAwait(false))
        {
            if (chunk.TextDelta is { Length: > 0 } delta) reply.Append(delta);
            if (chunk.ToolCall is { } call) toolCalls.Add(call);
            if (chunk.Error is { Length: > 0 } message2) error = message2;

            yield return chunk;
        }

        await PersistAssistantTurnAsync(session, reply.ToString(), toolCalls, provider, model, error, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Every tool the assistant can reach, bound to this user and this open document. Built per call
    /// so the closure can never outlive the request it belongs to.
    /// </summary>
    private void RegisterPlugins(Kernel kernel, ClippyContext context)
    {
        kernel.Plugins.AddFromObject(new TimePlugin(_options.TimeZoneId), "time");
        kernel.Plugins.AddFromObject(new MathPlugin(), "math");
        kernel.Plugins.AddFromObject(
            new WebPlugin(httpFactory.CreateClient(HttpClientName), _options), "web");
        kernel.Plugins.AddFromObject(
            new WorkspacePlugin(drive, content, calendar, context, _options.MaxToolResultChars), "workspace");
    }

    private async Task<ChatHistory> BuildHistoryAsync(
        ChatSession session,
        ClippyContext context,
        ChatMessage userTurn,
        CancellationToken ct)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(BuildSystemPrompt(session, context));

        var previous = await db.ChatMessages
            .AsNoTracking()
            .Include(m => m.Attachments)
            .Where(m => m.ChatSessionId == session.Id && m.Id != userTurn.Id && m.Error == null)
            .OrderByDescending(m => m.CreatedAt)
            .Take(_options.MaxHistoryTurns)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        previous.Reverse();

        foreach (var message in previous)
        {
            if (string.IsNullOrWhiteSpace(message.Content)) continue;

            if (message.Role == ChatRole.Assistant) history.AddAssistantMessage(message.Content);
            else history.AddUserMessage(message.Content);
        }

        history.Add(await ComposeUserMessageAsync(userTurn, ct).ConfigureAwait(false));

        return history;
    }

    /// <summary>
    /// The current turn, with its attachments inlined: images as bytes so vision-capable models can
    /// actually see them, documents as their extracted text so no second fetch is needed.
    /// </summary>
    private async Task<ChatMessageContent> ComposeUserMessageAsync(ChatMessage turn, CancellationToken ct)
    {
        var items = new ChatMessageContentItemCollection();
        var text = new StringBuilder(turn.Content);

        foreach (var attachment in turn.Attachments)
        {
            if (attachment.Kind == ChatAttachmentKind.Image)
            {
                var bytes = await ReadBlobAsync(attachment.StorageKey, ct).ConfigureAwait(false);

                if (bytes is { } data)
                {
                    items.Add(new ImageContent(data, attachment.ContentType));
                    continue;
                }
            }

            text.AppendLine()
                .AppendLine($"[Lampiran: {attachment.FileName} ({attachment.ContentType})]");

            if (!string.IsNullOrWhiteSpace(attachment.ExtractedText))
            {
                text.AppendLine(Truncate(attachment.ExtractedText, _options.MaxToolResultChars));
            }
        }

        items.Insert(0, new TextContent(text.ToString()));

        return new ChatMessageContent(AuthorRole.User, items);
    }

    private string BuildSystemPrompt(ChatSession session, ClippyContext context)
    {
        var prompt = new StringBuilder(
            string.IsNullOrWhiteSpace(session.SystemPromptOverride)
                ? _options.SystemPrompt
                : session.SystemPromptOverride);

        prompt.AppendLine().AppendLine();
        prompt.AppendLine($"Konteks saat ini — aplikasi: {context.App}.");

        if (context.DriveItemId is not null)
        {
            prompt.AppendLine(
                $"Berkas yang dibuka: \"{context.DriveItemName}\" ({context.DriveItemType}), id {context.DriveItemId}.");
        }

        if (!string.IsNullOrWhiteSpace(context.Selection))
        {
            prompt.AppendLine($"Yang sedang dipilih pengguna: {context.Selection}.");
        }

        prompt.AppendLine(
            "Gunakan tool untuk membaca berkas, mencari di web, menghitung, dan mengecek tanggal — " +
            "jangan menebak isi berkas atau melakukan aritmetika sendiri. " +
            "Jawab dalam bahasa yang dipakai pengguna. Tulis dalam Markdown.");

        prompt.Append("VibeDesk dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil.");

        return prompt.ToString();
    }

    // ────────────────────────────────── attachments ──────────────────────────────────

    public async Task<ChatAttachmentInput> UploadAttachmentAsync(
        string fileName,
        string contentType,
        Stream content2,
        CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "lampiran";

        var extension = Path.GetExtension(safeName);
        var key = $"chat/{userId:N}/{Guid.CreateVersion7():N}{extension}";

        using var buffer = new MemoryStream();
        await content2.CopyToAsync(buffer, ct).ConfigureAwait(false);
        buffer.Position = 0;

        var stored = await storage.PutAsync(key, buffer, contentType, ct).ConfigureAwait(false);
        var url = await storage.GetUrlAsync(key, TimeSpan.FromHours(12), ct).ConfigureAwait(false);

        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        string? extracted = null;

        if (!isImage && IsTextLike(contentType, safeName))
        {
            buffer.Position = 0;
            using var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: true);
            extracted = Truncate(await reader.ReadToEndAsync(ct).ConfigureAwait(false), _options.MaxToolResultChars);
        }

        return new ChatAttachmentInput(
            isImage ? ChatAttachmentKind.Image : ChatAttachmentKind.Document,
            safeName,
            contentType,
            stored.SizeBytes,
            key,
            url,
            extracted);
    }

    private async Task<ReadOnlyMemory<byte>?> ReadBlobAsync(string key, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.GetAsync(key, ct).ConfigureAwait(false);
            if (stream is null) return null;

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);

            return buffer.ToArray();
        }
        catch (Exception ex)
        {
            // A missing blob must not take the whole turn down — the text still has value.
            logger.LogWarning(ex, "Could not read chat attachment {Key}", key);
            return null;
        }
    }

    private async Task DeleteAttachmentBlobsAsync(Guid sessionId, CancellationToken ct)
    {
        var keys = await db.ChatAttachments
            .AsNoTracking()
            .Where(a => a.ChatMessage!.ChatSessionId == sessionId)
            .Select(a => a.StorageKey)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(key, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete chat attachment {Key}", key);
            }
        }
    }

    // ────────────────────────────────── persistence helpers ──────────────────────────────────

    private async Task<ChatMessage> PersistUserTurnAsync(
        ChatSession session,
        string message,
        IReadOnlyList<ChatAttachmentInput>? attachments,
        CancellationToken ct)
    {
        var turn = new ChatMessage
        {
            ChatSessionId = session.Id,
            Role = ChatRole.User,
            Content = message,
        };

        foreach (var attachment in attachments ?? [])
        {
            turn.Attachments.Add(new ChatAttachment
            {
                Kind = attachment.Kind,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes,
                StorageKey = attachment.StorageKey,
                Url = attachment.Url,
                ExtractedText = attachment.ExtractedText,
            });
        }

        db.ChatMessages.Add(turn);

        // The first user message names the conversation, so the sidebar is readable without the user
        // having to rename anything.
        if (session.Title is "Obrolan baru" or "New chat" && !string.IsNullOrWhiteSpace(message))
        {
            var title = message.Trim().ReplaceLineEndings(" ");
            session.Title = title.Length > 60 ? title[..60] + "…" : title;
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        session.AppContext ??= null;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return turn;
    }

    private async Task PersistAssistantTurnAsync(
        ChatSession session,
        string reply,
        List<ToolCallDto> toolCalls,
        AiProvider provider,
        string model,
        string? error,
        CancellationToken ct)
    {
        db.ChatMessages.Add(new ChatMessage
        {
            ChatSessionId = session.Id,
            Role = ChatRole.Assistant,
            Content = reply,
            ToolCallsJson = toolCalls.Count > 0 ? JsonSerializer.Serialize(toolCalls) : null,
            ModelUsed = model,
            ProviderUsed = provider,
            Error = error,
        });

        session.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private Task<ChatSession?> FindAsync(Guid sessionId, CancellationToken ct)
    {
        var userId = currentUser.RequireId();

        return db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
    }

    private static ChatSessionDto Project(ChatSession s, int messageCount) => new(
        s.Id, s.Title, s.AppContext, s.DriveItemId, s.Provider, s.Model, s.Temperature,
        s.CreatedAt, s.UpdatedAt, messageCount);

    private static ChatMessageDto ToDto(ChatMessage m) => new(
        m.Id,
        m.Role,
        m.Content,
        ParseToolCalls(m.ToolCallsJson),
        m.Attachments
            .Select(a => new ChatAttachmentDto(a.Id, a.Kind, a.FileName, a.ContentType, a.SizeBytes, a.Url))
            .ToList(),
        m.ModelUsed,
        m.ProviderUsed,
        m.PromptTokens,
        m.CompletionTokens,
        m.Error,
        m.CreatedAt);

    private static IReadOnlyList<ToolCallDto> ParseToolCalls(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<ToolCallDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsTextLike(string contentType, string fileName)
    {
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return true;

        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("csv", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] extensions = [".txt", ".md", ".csv", ".json", ".xml", ".log", ".yaml", ".yml"];

        return extensions.Any(e => fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "\n…(dipotong)";

    /// <summary>Named client so retry/timeout policy is configured once at registration.</summary>
    internal const string HttpClientName = "vibedesk-clippy";
}
