using Microsoft.AspNetCore.Mvc;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Drive;
using VibeDesk.Domain;

namespace VibeDesk.Api.Endpoints;

public static class DriveEndpoints
{
    public static IEndpointRouteBuilder MapDriveEndpoints(this IEndpointRouteBuilder app)
    {
        var drive = app.MapGroup("/api/drive").WithTags("Drive").RequireAuthorization();

        drive.MapGet("/", async (
            [AsParameters] DriveQueryRequest request,
            IDriveService service,
            CancellationToken ct) =>
                Results.Ok(await service.QueryAsync(request.ToQuery(), ct)))
            .WithSummary("List or search Drive items visible to the caller");

        drive.MapGet("/{id:guid}", async (Guid id, IDriveService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound())
            .WithSummary("Get one item's metadata");

        drive.MapGet("/{id:guid}/breadcrumbs", async (Guid id, IDriveService service, CancellationToken ct) =>
                Results.Ok(await service.GetBreadcrumbsAsync(id, ct)))
            .WithSummary("Root-to-item ancestor chain");

        drive.MapPost("/folders", async (
            CreateFolderRequest request,
            IDriveService service,
            CancellationToken ct) =>
        {
            var created = await service.CreateFolderAsync(request.Name, request.ParentId, ct);
            return Results.Created($"/api/drive/{created.Id}", created);
        }).WithSummary("Create a folder");

        drive.MapPost("/documents", async (
            CreateDocumentRequest request,
            IDriveService service,
            CancellationToken ct) =>
        {
            var created = await service.CreateDocumentAsync(
                request.Type, request.Name, request.ParentId, request.InitialData, ct);

            return Results.Created($"/api/drive/{created.Id}", created);
        }).WithSummary("Create an empty Docs, Sheets or Slides item");

        drive.MapPost("/upload", async (
            IFormFile file,
            [FromQuery] Guid? parentId,
            IDriveService service,
            CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();

            var created = await service.UploadAsync(
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                stream,
                parentId,
                ct);

            return Results.Created($"/api/drive/{created.Id}", created);
        })
            .DisableAntiforgery()
            .WithSummary("Upload a file");

        drive.MapGet("/{id:guid}/download", async (Guid id, IDriveService service, CancellationToken ct) =>
        {
            var item = await service.GetAsync(id, ct);
            if (item is null) return Results.NotFound();

            var stream = await service.OpenFileAsync(id, ct);
            if (stream is null) return Results.NotFound();

            // Always an attachment. Serving an uploaded SVG or HTML inline would be stored XSS on
            // our own origin, and a per-type allow-list is one forgotten entry away from the same bug.
            return Results.File(stream, item.ContentType ?? "application/octet-stream", item.Name);
        }).WithSummary("Download a file's binary");

        drive.MapGet("/{id:guid}/export", async (
            Guid id,
            IDriveService service,
            IDocumentContentService content,
            IOfficeConverter office,
            CancellationToken ct) =>
        {
            var item = await service.GetAsync(id, ct);
            if (item is null) return Results.NotFound();

            var format = item.Type switch
            {
                DriveItemType.Spreadsheet => OfficeFormat.Excel,
                DriveItemType.Presentation => OfficeFormat.PowerPoint,
                DriveItemType.Document => OfficeFormat.Word,
                _ => (OfficeFormat?)null,
            };

            if (format is not { } target)
            {
                return Results.Problem(
                    "Only documents, spreadsheets and presentations can be exported to Office formats.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var (extension, contentType) = IOfficeConverter.Descriptor(target);

            // Built in memory rather than streamed: the OpenXML SDK writes its package on dispose and
            // seeks while doing it, so it needs a stream it owns until the last byte is written.
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
        }).WithSummary("Export a document, spreadsheet or presentation as .docx, .xlsx or .pptx");

        drive.MapGet("/{id:guid}/download-url", async (
            Guid id,
            [FromQuery] int? minutes,
            IDriveService service,
            CancellationToken ct) =>
        {
            var url = await service.GetDownloadUrlAsync(
                id,
                minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null,
                ct);

            return url is null ? Results.NotFound() : Results.Ok(new { url });
        }).WithSummary("A time-limited URL for the binary, signed where the backend supports it");

        drive.MapPatch("/{id:guid}/name", async (
            Guid id, RenameRequest request, IDriveService service, CancellationToken ct) =>
                Results.Ok(await service.RenameAsync(id, request.Name, ct)))
            .WithSummary("Rename");

        drive.MapPatch("/{id:guid}/parent", async (
            Guid id, MoveRequest request, IDriveService service, CancellationToken ct) =>
                Results.Ok(await service.MoveAsync(id, request.ParentId, ct)))
            .WithSummary("Move; rewrites the whole subtree's path");

        drive.MapPost("/{id:guid}/copy", async (
            Guid id, CopyRequest request, IDriveService service, CancellationToken ct) =>
                Results.Ok(await service.CopyAsync(id, request.TargetParentId, request.NewName, ct)))
            .WithSummary("Deep-copy an item or subtree");

        drive.MapPatch("/{id:guid}/starred", async (
            Guid id, StarRequest request, IDriveService service, CancellationToken ct) =>
        {
            await service.SetStarredAsync(id, request.Starred, ct);
            return Results.NoContent();
        }).WithSummary("Star or unstar");

        drive.MapDelete("/{id:guid}", async (
            Guid id, [FromQuery] bool permanent, IDriveService service, CancellationToken ct) =>
        {
            if (permanent) await service.DeleteForeverAsync(id, ct);
            else await service.TrashAsync(id, ct);

            return Results.NoContent();
        }).WithSummary("Move to trash, or delete permanently with ?permanent=true");

        drive.MapPost("/{id:guid}/restore", async (Guid id, IDriveService service, CancellationToken ct) =>
        {
            await service.RestoreAsync(id, ct);
            return Results.NoContent();
        }).WithSummary("Restore from trash");

        drive.MapPost("/trash/empty", async (IDriveService service, CancellationToken ct) =>
                Results.Ok(new { deleted = await service.EmptyTrashAsync(ct) }))
            .WithSummary("Empty the trash");

        drive.MapGet("/usage", async (IDriveService service, CancellationToken ct) =>
                Results.Ok(await service.GetUsageAsync(ct)))
            .WithSummary("Storage usage and quota");

        drive.MapPost("/{id:guid}/touch", async (Guid id, IDriveService service, CancellationToken ct) =>
        {
            await service.TouchAsync(id, ct);
            return Results.NoContent();
        }).WithSummary("Record that the caller opened the item, for the Recent list");

        return app;
    }

    /// <summary>Query-string form of <see cref="DriveQuery"/>, kept separate so the DTO stays clean.</summary>
    public sealed record DriveQueryRequest(
        Guid? ParentId,
        string? Keyword,
        DriveItemType? Type,
        bool? Starred,
        bool? Trashed,
        bool? SharedWithMe,
        string? SortBy,
        bool? Descending,
        int? Skip,
        int? Take,
        bool? Recursive)
    {
        public DriveQuery ToQuery() => new()
        {
            ParentId = ParentId,
            Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword,
            Types = Type is { } t ? [t] : null,
            StarredOnly = Starred ?? false,
            TrashedOnly = Trashed ?? false,
            SharedWithMeOnly = SharedWithMe ?? false,
            SortBy = string.IsNullOrWhiteSpace(SortBy) ? "updated" : SortBy,
            Descending = Descending ?? true,
            Skip = Skip ?? 0,
            // Capped: an unbounded Take is a denial-of-service vector on a shared deployment.
            Take = Math.Clamp(Take ?? 100, 1, 500),
            Recursive = Recursive ?? false,
        };
    }

    public sealed record CreateFolderRequest(string Name, Guid? ParentId);
    public sealed record CreateDocumentRequest(DriveItemType Type, string Name, Guid? ParentId, string? InitialData);
    public sealed record RenameRequest(string Name);
    public sealed record MoveRequest(Guid? ParentId);
    public sealed record CopyRequest(Guid? TargetParentId, string? NewName);
    public sealed record StarRequest(bool Starred);
}
