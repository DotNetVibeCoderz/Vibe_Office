using Microsoft.AspNetCore.Authorization;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Documents;
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

        // Export a native item to its Office equivalent. Separate from the download route because
        // nothing is stored: the file is built from the content model on each request.
        app.MapGet("/drive/export/{id:guid}", ExportItemAsync)
            .RequireAuthorization();

        // Raw storage key. Restricted to chat attachments: those are addressed by key rather than by
        // a Drive item, so there is no item to check against.
        app.MapGet("/storage/{*key}", DownloadByKeyAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ExportItemAsync(
        Guid id,
        IDriveService drive,
        IDocumentContentService content,
        IOfficeConverter office,
        IPermissionService permissions,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var role = await permissions.ResolveRoleAsync(id, currentUser.Id, ct);
        if (role == PermissionRole.None) return Results.NotFound();

        var item = await drive.GetAsync(id, ct);
        if (item is null) return Results.NotFound();

        var format = item.Type switch
        {
            DriveItemType.Spreadsheet => OfficeFormat.Excel,
            DriveItemType.Presentation => OfficeFormat.PowerPoint,
            DriveItemType.Document => OfficeFormat.Word,
            _ => (OfficeFormat?)null,
        };

        // A folder or an uploaded blob has no Office equivalent; the plain download route serves those.
        if (format is not { } target) return Results.NotFound();

        var (extension, contentType) = IOfficeConverter.Descriptor(target);

        // Built in memory: the OpenXML SDK writes its package on dispose and seeks while doing so,
        // which a response stream does not allow.
        var buffer = new MemoryStream();

        switch (target)
        {
            case OfficeFormat.Excel:
                office.WriteExcel(buffer, await content.GetTypedAsync<SpreadsheetModel>(id, ct) ?? new());
                break;

            case OfficeFormat.PowerPoint:
                office.WritePowerPoint(
                    buffer, await content.GetTypedAsync<PresentationModel>(id, ct) ?? new(), item.Name);
                break;

            default:
                office.WriteWord(
                    buffer, await content.GetTypedAsync<DocumentModel>(id, ct) ?? new(), item.Name);
                break;
        }

        buffer.Position = 0;

        return Results.File(buffer, contentType, item.Name + extension);
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
