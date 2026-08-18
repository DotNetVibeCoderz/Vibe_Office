namespace VibeDesk.Domain.Entities;

/// <summary>
/// One Mr Clippy conversation. Sessions are per-user and remember which app and document they were
/// opened from, so the assistant can ground answers in whatever the user is actually looking at.
/// </summary>
public class ChatSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    /// <summary>Auto-derived from the first user message unless renamed.</summary>
    public string Title { get; set; } = "New chat";

    /// <summary>Which app the panel was opened in: docs | sheets | slides | calendar | drive.</summary>
    public string? AppContext { get; set; }

    /// <summary>The document the user had open, if any — used for grounding and for edit tools.</summary>
    public Guid? DriveItemId { get; set; }

    /// <summary>Per-session overrides of the appsettings defaults; null means "use configuration".</summary>
    public AiProvider? Provider { get; set; }
    public string? Model { get; set; }
    public double? Temperature { get; set; }
    public string? SystemPromptOverride { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsArchived { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = [];
}

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChatSessionId { get; set; }
    public ChatSession? ChatSession { get; set; }

    public ChatRole Role { get; set; }

    /// <summary>Markdown for user/assistant turns; rendered to HTML by the UI layer.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of the kernel functions invoked while producing this turn, so the UI can show
    /// "searched the web / read your spreadsheet" affordances and users can audit tool use.
    /// </summary>
    public string? ToolCallsJson { get; set; }

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public string? ModelUsed { get; set; }
    public AiProvider? ProviderUsed { get; set; }

    /// <summary>Set when generation failed, so the turn renders as an error rather than empty.</summary>
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ChatAttachment> Attachments { get; set; } = [];
}

/// <summary>
/// An upload attached to a chat turn. Images are re-sent to the model as image content; documents are
/// uploaded and referenced by URL in the message text, per the product spec.
/// </summary>
public class ChatAttachment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChatMessageId { get; set; }
    public ChatMessage? ChatMessage { get; set; }

    public ChatAttachmentKind Kind { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    /// <summary>Key in the configured storage provider.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Resolved public/served URL handed to the model.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Extracted text for documents, so the model can read them without a second fetch.</summary>
    public string? ExtractedText { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
