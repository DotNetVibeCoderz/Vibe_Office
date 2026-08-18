using VibeDesk.Domain;

namespace VibeDesk.Application.Assistant;

public sealed record ChatSessionDto(
    Guid Id,
    string Title,
    string? AppContext,
    Guid? DriveItemId,
    AiProvider? Provider,
    string? Model,
    double? Temperature,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount);

public sealed record ChatAttachmentDto(
    Guid Id,
    ChatAttachmentKind Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Url);

public sealed record ChatMessageDto(
    Guid Id,
    ChatRole Role,
    string Content,
    IReadOnlyList<ToolCallDto> ToolCalls,
    IReadOnlyList<ChatAttachmentDto> Attachments,
    string? ModelUsed,
    AiProvider? ProviderUsed,
    int? PromptTokens,
    int? CompletionTokens,
    string? Error,
    DateTimeOffset CreatedAt);

/// <summary>
/// A kernel function the assistant invoked. Surfaced so the user can see that "searched the web" or
/// "read your spreadsheet" actually happened, rather than trusting an unaudited claim in the prose.
/// </summary>
public sealed record ToolCallDto(string Name, string? Summary, bool Succeeded);

/// <summary>An upload the user attached to a turn, already stored and given a URL.</summary>
public sealed record ChatAttachmentInput(
    ChatAttachmentKind Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    string StorageKey,
    string Url,
    string? ExtractedText = null);

/// <summary>
/// Context the assistant is grounded in: which app the user is in and which document is open.
/// Passed per message rather than stored on the session, because a user can move between documents
/// inside one conversation.
/// </summary>
public sealed record ClippyContext(
    string App,
    Guid? DriveItemId = null,
    string? DriveItemName = null,
    DriveItemType? DriveItemType = null,
    /// <summary>Selected cells, text range or slide the user is pointing at, if any.</summary>
    string? Selection = null);

/// <summary>One chunk of a streamed reply.</summary>
public sealed record ClippyChunk(string? TextDelta, ToolCallDto? ToolCall, bool IsFinal, string? Error);

/// <summary>
/// Mr Clippy. Implemented over Semantic Kernel with a runtime-selected provider.
/// </summary>
public interface IClippyService
{
    /// <summary>
    /// False when no provider is configured or reachable. The chat panel uses this to explain what to
    /// configure rather than failing on the user's first message.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Providers that have credentials configured, for the model picker.</summary>
    IReadOnlyList<AiProvider> AvailableProviders { get; }

    IReadOnlyList<string> ModelsFor(AiProvider provider);

    Task<IReadOnlyList<ChatSessionDto>> ListSessionsAsync(CancellationToken ct = default);

    Task<ChatSessionDto> CreateSessionAsync(
        string? title = null,
        ClippyContext? context = null,
        CancellationToken ct = default);

    Task<ChatSessionDto?> GetSessionAsync(Guid sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(Guid sessionId, CancellationToken ct = default);

    Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Clears the turns but keeps the session, so the user loses history without losing settings.</summary>
    Task ResetSessionAsync(Guid sessionId, CancellationToken ct = default);

    Task RenameSessionAsync(Guid sessionId, string title, CancellationToken ct = default);

    Task UpdateSessionSettingsAsync(
        Guid sessionId,
        AiProvider? provider,
        string? model,
        double? temperature,
        string? systemPromptOverride,
        CancellationToken ct = default);

    /// <summary>
    /// Streams a reply. The user turn is persisted before generation starts, so a failure mid-stream
    /// still leaves a coherent transcript rather than losing what the user asked.
    /// </summary>
    IAsyncEnumerable<ClippyChunk> SendAsync(
        Guid sessionId,
        string message,
        ClippyContext context,
        IReadOnlyList<ChatAttachmentInput>? attachments = null,
        CancellationToken ct = default);

    /// <summary>
    /// Stores an upload for use in a turn. Images come back as a URL the model can read; documents
    /// additionally carry extracted text so the model does not need a second fetch.
    /// </summary>
    Task<ChatAttachmentInput> UploadAttachmentAsync(
        string fileName,
        string contentType,
        Stream content,
        CancellationToken ct = default);
}
