namespace VibeDesk.Domain.Entities;

/// <summary>
/// A comment or suggestion thread anchored into a document. Shared by Docs, Sheets and Slides —
/// <see cref="Anchor"/> is interpreted per app (text range, cell address, slide element id).
/// </summary>
public class Comment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid DriveItemId { get; set; }
    public DriveItem? DriveItem { get; set; }

    /// <summary>Null for a root thread; set for replies.</summary>
    public Guid? ParentCommentId { get; set; }
    public Comment? ParentComment { get; set; }
    public ICollection<Comment> Replies { get; set; } = [];

    public CommentKind Kind { get; set; } = CommentKind.Comment;
    public CommentStatus Status { get; set; } = CommentStatus.Open;

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// App-specific location, e.g. <c>{"from":120,"to":168}</c> for Docs, <c>{"sheet":0,"a1":"C4"}</c>
    /// for Sheets, <c>{"slide":2,"element":"tx1"}</c> for Slides.
    /// </summary>
    public string? Anchor { get; set; }

    /// <summary>For <see cref="CommentKind.Suggestion"/>: the replacement the author proposes.</summary>
    public string? SuggestedText { get; set; }
    /// <summary>For <see cref="CommentKind.Suggestion"/>: the text the suggestion replaces.</summary>
    public string? OriginalText { get; set; }

    public Guid AuthorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? ResolvedById { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>User ids mentioned with @, used to fan out notifications.</summary>
    public string? MentionedUserIds { get; set; }
}
