using Microsoft.AspNetCore.Authorization;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Drive;
using VibeDesk.Domain;

namespace VibeDesk.Web.Endpoints;

/// <summary>
/// File download routes.
/// </summary>
/// <remarks>
/// The filesystem and encrypting storage backends cannot hand out signed URLs — one has no signing
/// mechanism, the other would hand the browser ciphertext. Both return a path into these endpoints
/// instead, which means authorisation happens here rather than being baked into the URL.
/// </remarks>
public static class StorageEndpoints
{
    public static IEndpointRouteBuilder MapStorageEndpoints(this IEndpointRouteBuilder app)
    {
        // Download by Drive item id — the path every in-app link uses, because it can be
        // permission-checked against the item.
        app.MapGet("/drive/download/{id:guid}", DownloadItemAsync)
            .RequireAuthorization();

        // Raw storage key. Restricted to chat attachments: those are addressed by key rather than by
        // a Drive item, so there is no item to check against.
        app.MapGet("/storage/{*key}", DownloadByKeyAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> DownloadItemAsync(
        Guid id,
        bool? inline,
        IDriveService drive,
        IPermissionService permissions,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var role = await permissions.ResolveRoleAsync(id, currentUser.Id, ct);
        if (role == PermissionRole.None) return Results.NotFound();

        var item = await drive.GetAsync(id, ct);
        if (item is null) return Results.NotFound();

        var stream = await drive.OpenFileAsync(id, ct);
        if (stream is null) return Results.NotFound();

        var contentType = item.ContentType ?? "application/octet-stream";

        // Inline only for types a browser renders safely. Serving arbitrary uploads inline turns an
        // uploaded .html or .svg into stored XSS on our own origin.
        var renderInline = inline == true && IsInlineSafe(contentType);

        return Results.File(
            stream,
            contentType,
            fileDownloadName: renderInline ? null : item.Name,
            enableRangeProcessing: true);
    }

    private static async Task<IResult> DownloadByKeyAsync(
        string key,
        IStorageProvider storage,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var decoded = Uri.UnescapeDataString(key);

        // Only chat attachments are reachable by raw key, and only the owner's own folder. Without
        // this the route would read any object in the bucket for any signed-in user.
        var prefix = $"chat/{currentUser.RequireId():N}/";
        if (!decoded.StartsWith(prefix, StringComparison.Ordinal)) return Results.Forbid();

        var stat = await storage.StatAsync(decoded, ct);
        if (stat is null) return Results.NotFound();

        var stream = await storage.GetAsync(decoded, ct);
        if (stream is null) return Results.NotFound();

        var fileName = decoded.Split('/')[^1];
        var inline = IsInlineSafe(stat.ContentType);

        return Results.File(
            stream,
            stat.ContentType,
            fileDownloadName: inline ? null : fileName,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Types safe to render in the browsing context. SVG is excluded on purpose: it can carry script,
    /// and inline SVG from an upload would execute on our origin.
    /// </summary>
    private static bool IsInlineSafe(string contentType)
    {
        var type = contentType.ToLowerInvariant();

        if (type is "image/svg+xml" or "text/html" or "application/xhtml+xml") return false;

        return type.StartsWith("image/", StringComparison.Ordinal)
               || type.StartsWith("video/", StringComparison.Ordinal)
               || type.StartsWith("audio/", StringComparison.Ordinal)
               || type is "application/pdf" or "text/plain";
    }
}
