using Microsoft.AspNetCore.Mvc;
using VibeDesk.Application.Drive;
using VibeDesk.Domain;

namespace VibeDesk.Api.Endpoints;

/// <summary>Content, versions, comments and permissions — everything that hangs off a Drive item.</summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var items = app.MapGroup("/api/items/{id:guid}").WithTags("Documents").RequireAuthorization();

        // ── content ──────────────────────────────────────────────────────────
        items.MapGet("/content", async (Guid id, IDocumentContentService content, CancellationToken ct) =>
                await content.GetAsync(id, ct) is { } payload ? Results.Ok(payload) : Results.NotFound())
            .WithSummary("Read a document's JSON payload and its current revision");

        items.MapPut("/content", async (
            Guid id,
            SaveContentRequest request,
            IDocumentContentService content,
            CancellationToken ct) =>
        {
            var result = await content.SaveAsync(id, request.Data, request.BaseRevision, ct);

            // 409 with the server's copy, not a silent overwrite: the client rebases on the body.
            return result.Accepted ? Results.Ok(result) : Results.Conflict(result);
        }).WithSummary("Save a payload; rejected with 409 when the stored revision has moved on");

        // ── versions ─────────────────────────────────────────────────────────
        items.MapGet("/versions", async (Guid id, IVersionService versions, CancellationToken ct) =>
                Results.Ok(await versions.ListAsync(id, ct)))
            .WithSummary("Version history");

        items.MapPost("/versions", async (
            Guid id, SnapshotRequest request, IVersionService versions, CancellationToken ct) =>
                Results.Ok(await versions.SnapshotAsync(id, request.Label, false, ct)))
            .WithSummary("Capture the current state as a labelled snapshot");

        items.MapGet("/versions/{versionId:guid}", async (
            Guid id, Guid versionId, IVersionService versions, CancellationToken ct) =>
                await versions.GetDataAsync(id, versionId, ct) is { } data
                    ? Results.Ok(new { data })
                    : Results.NotFound())
            .WithSummary("Payload of one snapshot, for the history preview");

        items.MapPost("/versions/{versionId:guid}/restore", async (
            Guid id, Guid versionId, IVersionService versions, CancellationToken ct) =>
        {
            // Snapshots the current state first, so the restore is itself undoable.
            await versions.RestoreAsync(id, versionId, ct);
            return Results.NoContent();
        }).WithSummary("Roll back to a snapshot");

        items.MapDelete("/versions/{versionId:guid}", async (
            Guid id, Guid versionId, IVersionService versions, CancellationToken ct) =>
        {
            await versions.DeleteAsync(id, versionId, ct);
            return Results.NoContent();
        }).WithSummary("Delete a snapshot");

        // ── comments and suggestions ─────────────────────────────────────────
        items.MapGet("/comments", async (
            Guid id, [FromQuery] bool includeResolved, ICommentService comments, CancellationToken ct) =>
                Results.Ok(await comments.ListAsync(id, includeResolved, ct)))
            .WithSummary("Comment threads with replies nested");

        items.MapPost("/comments", async (
            Guid id, AddCommentRequest request, ICommentService comments, CancellationToken ct) =>
                Results.Ok(await comments.AddAsync(
                    id,
                    request.Body,
                    request.Anchor,
                    request.Kind,
                    request.SuggestedText,
                    request.OriginalText,
                    request.ParentCommentId,
                    ct)))
            .WithSummary("Add a comment, a reply, or a suggestion");

        items.MapGet("/comments/count", async (Guid id, ICommentService comments, CancellationToken ct) =>
                Results.Ok(new { open = await comments.CountOpenAsync(id, ct) }))
            .WithSummary("Number of unresolved threads");

        // ── permissions and sharing ──────────────────────────────────────────
        items.MapGet("/permissions", async (Guid id, IPermissionService permissions, CancellationToken ct) =>
                Results.Ok(await permissions.ListAsync(id, ct)))
            .WithSummary("Who has access, and how");

        items.MapPost("/permissions", async (
            Guid id, ShareRequest request, IPermissionService permissions, CancellationToken ct) =>
                Results.Ok(await permissions.ShareAsync(id, request.Email, request.Role, request.ExpiresAt, ct)))
            .WithSummary("Grant access by email; claimed automatically if that user registers later");

        items.MapDelete("/permissions/{permissionId:guid}", async (
            Guid id, Guid permissionId, IPermissionService permissions, CancellationToken ct) =>
        {
            await permissions.RevokeAsync(id, permissionId, ct);
            return Results.NoContent();
        }).WithSummary("Revoke a grant");

        items.MapPut("/link-sharing", async (
            Guid id, LinkSharingRequest request, IPermissionService permissions, CancellationToken ct) =>
        {
            await permissions.SetLinkSharingAsync(id, request.Scope, request.LinkRole, ct);
            return Results.NoContent();
        }).WithSummary("Set link scope and the role the link grants");

        items.MapPost("/transfer-ownership", async (
            Guid id, TransferRequest request, IPermissionService permissions, CancellationToken ct) =>
        {
            await permissions.TransferOwnershipAsync(id, request.NewOwnerId, ct);
            return Results.NoContent();
        }).WithSummary("Transfer ownership; the previous owner becomes an Editor");

        // ── comment-scoped routes (no item id in the path) ───────────────────
        var comments = app.MapGroup("/api/comments").WithTags("Documents").RequireAuthorization();

        comments.MapPatch("/{commentId:guid}", async (
            Guid commentId, EditCommentRequest request, ICommentService service, CancellationToken ct) =>
                Results.Ok(await service.EditAsync(commentId, request.Body, ct)))
            .WithSummary("Edit a comment's body");

        comments.MapPatch("/{commentId:guid}/resolved", async (
            Guid commentId, ResolveRequest request, ICommentService service, CancellationToken ct) =>
        {
            await service.ResolveAsync(commentId, request.Resolved, ct);
            return Results.NoContent();
        }).WithSummary("Resolve or reopen a thread");

        comments.MapPost("/{commentId:guid}/accept", async (
            Guid commentId, ICommentService service, CancellationToken ct) =>
                Results.Ok(new { revision = await service.AcceptSuggestionAsync(commentId, ct) }))
            .WithSummary("Apply a suggestion to the document and return the new revision");

        comments.MapPost("/{commentId:guid}/reject", async (
            Guid commentId, ICommentService service, CancellationToken ct) =>
        {
            await service.RejectSuggestionAsync(commentId, ct);
            return Results.NoContent();
        }).WithSummary("Reject a suggestion");

        comments.MapDelete("/{commentId:guid}", async (
            Guid commentId, ICommentService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(commentId, ct);
            return Results.NoContent();
        }).WithSummary("Delete a comment");

        // ── anonymous share links ────────────────────────────────────────────
        app.MapGet("/api/share/{token}", async (
            string token, IPermissionService permissions, CancellationToken ct) =>
                await permissions.ResolveShareTokenAsync(token, ct) is { } item
                    ? Results.Ok(item)
                    : Results.NotFound())
            .WithTags("Documents")
            .AllowAnonymous()
            .WithSummary("Resolve a share link to the item it grants access to");

        return app;
    }

    /// <summary><c>BaseRevision: -1</c> forces the write, bypassing the conflict check.</summary>
    public sealed record SaveContentRequest(string Data, long BaseRevision);

    public sealed record SnapshotRequest(string? Label);

    public sealed record AddCommentRequest(
        string Body,
        string? Anchor,
        CommentKind Kind = CommentKind.Comment,
        string? SuggestedText = null,
        string? OriginalText = null,
        Guid? ParentCommentId = null);

    public sealed record EditCommentRequest(string Body);
    public sealed record ResolveRequest(bool Resolved);
    public sealed record ShareRequest(string Email, PermissionRole Role, DateTimeOffset? ExpiresAt);
    public sealed record LinkSharingRequest(ShareScope Scope, PermissionRole LinkRole);
    public sealed record TransferRequest(Guid NewOwnerId);
}
