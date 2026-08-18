using Microsoft.EntityFrameworkCore;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Infrastructure.Services;

/// <summary>
/// Comment and suggestion threads, shared by Docs, Sheets and Slides.
/// </summary>
/// <remarks>
/// Commenting requires only <see cref="PermissionRole.Commenter"/>, which is the point of that role:
/// a reviewer can propose an edit without being able to change the document. Accepting a suggestion
/// is what actually mutates the body, and that requires Editor.
/// </remarks>
public sealed class CommentService(
    AppDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IDocumentContentService content,
    IUserDirectory users,
    IActivityService activity,
    INotificationService notifications,
    ICollaborationNotifier collaboration) : ICommentService
{
    public async Task<IReadOnlyList<CommentDto>> ListAsync(
        Guid itemId, bool includeResolved = false, CancellationToken ct = default)
    {
        await permissions.RequireAsync(itemId, PermissionRole.Viewer, ct);

        var query = db.Comments.AsNoTracking().Where(c => c.DriveItemId == itemId);

        // Filter on root threads only: a resolved thread hides its replies with it, and an open thread
        // must keep showing replies regardless of their own status.
        if (!includeResolved)
        {
            query = query.Where(c => c.ParentCommentId != null || c.Status == CommentStatus.Open);
        }

        var all = await query.OrderBy(c => c.CreatedAt).ToListAsync(ct);
        if (all.Count == 0) return [];

        var directory = await users.GetManyAsync(all.Select(c => c.AuthorId), ct);

        var repliesByParent = all
            .Where(c => c.ParentCommentId is not null)
            .GroupBy(c => c.ParentCommentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return all
            .Where(c => c.ParentCommentId is null)
            .Select(root => Project(root, repliesByParent, directory))
            .ToList();
    }

    private static CommentDto Project(
        Comment comment,
        Dictionary<Guid, List<Comment>> repliesByParent,
        IReadOnlyDictionary<Guid, UserSummaryDto> directory)
    {
        var replies = repliesByParent.TryGetValue(comment.Id, out var children)
            ? children.Select(child => Project(child, repliesByParent, directory)).ToList()
            : [];

        return new CommentDto(
            comment.Id,
            comment.ParentCommentId,
            comment.Kind,
            comment.Status,
            comment.Body,
            comment.Anchor,
            comment.SuggestedText,
            comment.OriginalText,
            comment.AuthorId,
            directory.TryGetValue(comment.AuthorId, out var author) ? author.DisplayName : "Unknown",
            comment.CreatedAt,
            comment.UpdatedAt,
            replies);
    }

    public async Task<CommentDto> AddAsync(
        Guid itemId,
        string body,
        string? anchor,
        CommentKind kind = CommentKind.Comment,
        string? suggestedText = null,
        string? originalText = null,
        Guid? parentCommentId = null,
        CancellationToken ct = default)
    {
        await permissions.RequireAsync(itemId, PermissionRole.Commenter, ct);

        if (string.IsNullOrWhiteSpace(body) && kind == CommentKind.Comment)
            throw new ValidationException("A comment needs a body.");

        if (kind == CommentKind.Suggestion && suggestedText is null)
            throw new ValidationException("A suggestion needs replacement text.");

        var userId = currentUser.RequireId();

        if (parentCommentId is not null)
        {
            // A reply must belong to the same document, or one item's thread could be grafted onto another.
            var parentItemId = await db.Comments
                .AsNoTracking()
                .Where(c => c.Id == parentCommentId)
                .Select(c => (Guid?)c.DriveItemId)
                .FirstOrDefaultAsync(ct);

            if (parentItemId is null)
                throw new NotFoundException("The comment being replied to was not found.");

            if (parentItemId != itemId)
                throw new ValidationException("A reply must be on the same item as its parent.");
        }

        var mentioned = ExtractMentions(body);

        var comment = new Comment
        {
            DriveItemId = itemId,
            ParentCommentId = parentCommentId,
            Kind = kind,
            Status = CommentStatus.Open,
            Body = body.Trim(),
            Anchor = anchor,
            SuggestedText = suggestedText,
            OriginalText = originalText,
            AuthorId = userId,
            MentionedUserIds = mentioned.Count == 0 ? null : string.Join(',', mentioned),
        };

        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);

        await collaboration.CommentsChangedAsync(itemId, ct);
        await activity.LogAsync(
            kind == CommentKind.Suggestion ? "suggestion.added" : "comment.added",
            itemId,
            Excerpt(body),
            ct);

        await NotifyParticipantsAsync(itemId, comment, mentioned, ct);

        var author = await users.GetAsync(userId, ct);

        return new CommentDto(
            comment.Id, comment.ParentCommentId, comment.Kind, comment.Status,
            comment.Body, comment.Anchor, comment.SuggestedText, comment.OriginalText,
            userId, author?.DisplayName ?? "Unknown", comment.CreatedAt, null, []);
    }

    /// <summary>
    /// Notifies @-mentioned users, the document owner, and everyone else already in the thread —
    /// minus the author, who does not need to be told about their own comment.
    /// </summary>
    private async Task NotifyParticipantsAsync(
        Guid itemId, Comment comment, List<Guid> mentioned, CancellationToken ct)
    {
        var item = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == itemId)
            .Select(x => new { x.Name, x.OwnerId, x.Type })
            .FirstOrDefaultAsync(ct);

        if (item is null) return;

        var recipients = new HashSet<Guid>(mentioned) { item.OwnerId };

        if (comment.ParentCommentId is not null)
        {
            var threadAuthors = await db.Comments
                .AsNoTracking()
                .Where(c => c.Id == comment.ParentCommentId || c.ParentCommentId == comment.ParentCommentId)
                .Select(c => c.AuthorId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var authorId in threadAuthors) recipients.Add(authorId);
        }

        recipients.Remove(comment.AuthorId);
        if (recipients.Count == 0) return;

        var actorName = currentUser.DisplayName ?? "Someone";
        var link = LinkFor(item.Type, itemId);

        foreach (var recipient in recipients)
        {
            var isMention = mentioned.Contains(recipient);

            await notifications.NotifyAsync(
                recipient,
                isMention ? NotificationKind.Mention : NotificationKind.Comment,
                isMention
                    ? $"{actorName} mentioned you in \"{item.Name}\""
                    : $"{actorName} commented on \"{item.Name}\"",
                Excerpt(comment.Body),
                link,
                ct);
        }
    }

    public async Task<CommentDto> EditAsync(Guid commentId, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ValidationException("A comment needs a body.");

        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct)
                      ?? throw new NotFoundException("That comment was not found.");

        var userId = currentUser.RequireId();

        // Only the author edits their own words; an Editor on the document cannot rewrite them.
        if (comment.AuthorId != userId)
            throw new ForbiddenException("You can only edit your own comments.");

        await permissions.RequireAsync(comment.DriveItemId, PermissionRole.Commenter, ct);

        comment.Body = body.Trim();
        comment.UpdatedAt = DateTimeOffset.UtcNow;

        var mentioned = ExtractMentions(body);
        comment.MentionedUserIds = mentioned.Count == 0 ? null : string.Join(',', mentioned);

        await db.SaveChangesAsync(ct);
        await collaboration.CommentsChangedAsync(comment.DriveItemId, ct);

        var author = await users.GetAsync(userId, ct);

        return new CommentDto(
            comment.Id, comment.ParentCommentId, comment.Kind, comment.Status,
            comment.Body, comment.Anchor, comment.SuggestedText, comment.OriginalText,
            userId, author?.DisplayName ?? "Unknown", comment.CreatedAt, comment.UpdatedAt, []);
    }

    public async Task ResolveAsync(Guid commentId, bool resolved, CancellationToken ct = default)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct)
                      ?? throw new NotFoundException("That comment was not found.");

        await permissions.RequireAsync(comment.DriveItemId, PermissionRole.Commenter, ct);

        comment.Status = resolved ? CommentStatus.Resolved : CommentStatus.Open;
        comment.ResolvedById = resolved ? currentUser.Id : null;
        comment.ResolvedAt = resolved ? DateTimeOffset.UtcNow : null;

        await db.SaveChangesAsync(ct);

        await collaboration.CommentsChangedAsync(comment.DriveItemId, ct);
        await activity.LogAsync(
            resolved ? "comment.resolved" : "comment.reopened", comment.DriveItemId, null, ct);
    }

    public async Task<long> AcceptSuggestionAsync(Guid commentId, CancellationToken ct = default)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct)
                      ?? throw new NotFoundException("That suggestion was not found.");

        if (comment.Kind != CommentKind.Suggestion)
            throw new ValidationException("That comment is not a suggestion.");

        if (comment.Status == CommentStatus.Accepted)
            throw new ValidationException("That suggestion has already been accepted.");

        // Applying a suggestion changes the document, so it needs Editor rather than Commenter.
        await permissions.RequireAsync(comment.DriveItemId, PermissionRole.Editor, ct);

        var current = await content.GetAsync(comment.DriveItemId, ct)
                      ?? throw new NotFoundException("The document content was not found.");

        var revision = current.Type switch
        {
            DriveItemType.Document => await ApplyToDocumentAsync(comment, current, ct),
            DriveItemType.Spreadsheet => await ApplyToSpreadsheetAsync(comment, current, ct),
            DriveItemType.Presentation => await ApplyToPresentationAsync(comment, current, ct),
            _ => throw new ValidationException("Suggestions are not supported on this item type."),
        };

        comment.Status = CommentStatus.Accepted;
        comment.ResolvedById = currentUser.Id;
        comment.ResolvedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await collaboration.CommentsChangedAsync(comment.DriveItemId, ct);
        await activity.LogAsync("suggestion.accepted", comment.DriveItemId, Excerpt(comment.Body), ct);

        if (comment.AuthorId != currentUser.Id)
        {
            await notifications.NotifyAsync(
                comment.AuthorId,
                NotificationKind.Comment,
                $"{currentUser.DisplayName ?? "Someone"} accepted your suggestion",
                Excerpt(comment.SuggestedText ?? comment.Body),
                LinkFor(current.Type, comment.DriveItemId),
                ct);
        }

        return revision;
    }

    /// <summary>
    /// Replaces the suggestion's original text with the proposed text in the document HTML.
    /// Only the first occurrence is replaced — a suggestion targets one spot, and replacing every
    /// match would silently rewrite unrelated parts of the document.
    /// </summary>
    private async Task<long> ApplyToDocumentAsync(
        Comment comment, DriveItemContentDto current, CancellationToken ct)
    {
        var model = ContentJson.Deserialize<DocumentModel>(current.Data);

        if (string.IsNullOrEmpty(comment.OriginalText))
        {
            throw new ValidationException(
                "This suggestion has no original text recorded, so it cannot be applied automatically.");
        }

        var index = model.Html.IndexOf(comment.OriginalText, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new ValidationException(
                "The text this suggestion targets has changed, so it can no longer be applied.");
        }

        model.Html = string.Concat(
            model.Html.AsSpan(0, index),
            comment.SuggestedText ?? string.Empty,
            model.Html.AsSpan(index + comment.OriginalText.Length));

        model.AppliedSuggestions.Add(comment.Id.ToString("N"));

        return await content.SaveTypedAsync(comment.DriveItemId, model, ct);
    }

    /// <summary>Writes the suggested value into the cell the suggestion is anchored to.</summary>
    private async Task<long> ApplyToSpreadsheetAsync(
        Comment comment, DriveItemContentDto current, CancellationToken ct)
    {
        var model = ContentJson.Deserialize<SpreadsheetModel>(current.Data);
        var anchor = CommentAnchor.Parse(comment.Anchor);

        if (anchor.A1 is null)
            throw new ValidationException("This suggestion is not anchored to a cell.");

        var sheet = model.Sheets.ElementAtOrDefault(anchor.SheetIndex ?? 0)
                    ?? throw new ValidationException("The sheet this suggestion targets no longer exists.");

        var suggested = comment.SuggestedText ?? string.Empty;
        var cell = sheet.Cells.TryGetValue(anchor.A1, out var existing) ? existing : new Cell();

        // A suggestion starting with '=' is a formula proposal; otherwise it's a literal value.
        if (suggested.StartsWith('='))
        {
            cell.F = suggested;
        }
        else
        {
            cell.F = null;
            cell.V = suggested;
        }

        sheet.Cells[anchor.A1] = cell;

        return await content.SaveTypedAsync(comment.DriveItemId, model, ct);
    }

    /// <summary>Replaces the text of the slide element the suggestion is anchored to.</summary>
    private async Task<long> ApplyToPresentationAsync(
        Comment comment, DriveItemContentDto current, CancellationToken ct)
    {
        var model = ContentJson.Deserialize<PresentationModel>(current.Data);
        var anchor = CommentAnchor.Parse(comment.Anchor);

        if (anchor.ElementId is null)
            throw new ValidationException("This suggestion is not anchored to a slide element.");

        var element = model.Slides
            .SelectMany(s => s.Elements)
            .FirstOrDefault(e => e.Id == anchor.ElementId)
            ?? throw new ValidationException("The element this suggestion targets no longer exists.");

        element.Text = comment.SuggestedText;

        return await content.SaveTypedAsync(comment.DriveItemId, model, ct);
    }

    public async Task RejectSuggestionAsync(Guid commentId, CancellationToken ct = default)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct)
                      ?? throw new NotFoundException("That suggestion was not found.");

        if (comment.Kind != CommentKind.Suggestion)
            throw new ValidationException("That comment is not a suggestion.");

        await permissions.RequireAsync(comment.DriveItemId, PermissionRole.Editor, ct);

        comment.Status = CommentStatus.Rejected;
        comment.ResolvedById = currentUser.Id;
        comment.ResolvedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        await collaboration.CommentsChangedAsync(comment.DriveItemId, ct);
        await activity.LogAsync("suggestion.rejected", comment.DriveItemId, null, ct);
    }

    public async Task DeleteAsync(Guid commentId, CancellationToken ct = default)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct)
                      ?? throw new NotFoundException("That comment was not found.");

        var userId = currentUser.RequireId();
        var role = await permissions.ResolveRoleAsync(comment.DriveItemId, userId, ct);

        // The author can always delete their own; the document owner can moderate anyone's.
        if (comment.AuthorId != userId && role < PermissionRole.Owner)
            throw new ForbiddenException("You can only delete your own comments.");

        // Replies have a Restrict FK to their parent, so they must go first.
        var replies = await db.Comments.Where(c => c.ParentCommentId == commentId).ToListAsync(ct);
        db.Comments.RemoveRange(replies);
        db.Comments.Remove(comment);

        await db.SaveChangesAsync(ct);

        await collaboration.CommentsChangedAsync(comment.DriveItemId, ct);
    }

    public async Task<int> CountOpenAsync(Guid itemId, CancellationToken ct = default)
    {
        var role = await permissions.ResolveRoleAsync(itemId, currentUser.Id, ct);
        if (role == PermissionRole.None) return 0;

        return await db.Comments.CountAsync(
            c => c.DriveItemId == itemId && c.ParentCommentId == null && c.Status == CommentStatus.Open,
            ct);
    }

    // ─────────────────────────────────────── helpers ───────────────────────────────────────

    /// <summary>
    /// Pulls user ids out of <c>@[guid]</c> markers. The editor inserts these when a mention is picked
    /// from the type-ahead, so parsing ids is reliable in a way that parsing display names is not.
    /// </summary>
    private static List<Guid> ExtractMentions(string body)
    {
        var result = new List<Guid>();
        if (string.IsNullOrEmpty(body)) return result;

        var index = 0;
        while ((index = body.IndexOf("@[", index, StringComparison.Ordinal)) >= 0)
        {
            var close = body.IndexOf(']', index + 2);
            if (close < 0) break;

            var candidate = body[(index + 2)..close];
            if (Guid.TryParse(candidate, out var id) && !result.Contains(id))
            {
                result.Add(id);
            }

            index = close + 1;
        }

        return result;
    }

    private static string Excerpt(string? text, int max = 140)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }

    private static string LinkFor(DriveItemType type, Guid id) => type switch
    {
        DriveItemType.Document => $"/docs/{id}",
        DriveItemType.Spreadsheet => $"/sheets/{id}",
        DriveItemType.Presentation => $"/slides/{id}",
        _ => $"/drive/file/{id}",
    };
}
