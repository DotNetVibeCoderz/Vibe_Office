using System.Text.Json;
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
/// Reads and writes the JSON payload of Docs/Sheets/Slides items.
/// </summary>
/// <remarks>
/// Concurrency uses a revision counter rather than last-write-wins: a client sends the revision it
/// edited from, and a save based on a stale revision is rejected with the server's current copy so the
/// client can rebase. That is what stops two people editing the same document from silently
/// overwriting each other, and it is why every editor autosave goes through <see cref="SaveAsync"/>.
/// </remarks>
public sealed class DocumentContentService(
    AppDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    ICollaborationNotifier collaboration,
    IVersionService versions) : IDocumentContentService
{
    /// <summary>
    /// How long to wait between automatic version snapshots while a document is being edited.
    /// Frequent enough to be a safety net, rare enough not to bloat the version list.
    /// </summary>
    private static readonly TimeSpan AutoSnapshotInterval = TimeSpan.FromMinutes(10);

    public async Task<DriveItemContentDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var role = await permissions.ResolveRoleAsync(id, currentUser.Id, ct);
        if (role == PermissionRole.None) return null;

        var record = await db.DriveItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.Type,
                x.Name,
                x.UpdatedAt,
                Data = x.Content != null ? x.Content.Data : null,
                Revision = x.Content != null ? x.Content.Revision : 0,
            })
            .FirstOrDefaultAsync(ct);

        if (record is null) return null;

        if (!IsEditableType(record.Type))
            throw new ValidationException($"{record.Type} items have no editable payload.");

        return new DriveItemContentDto(
            record.Id,
            record.Type,
            record.Name,
            // A missing content row means the item predates its payload; hand back a valid default
            // rather than null so the editor still opens.
            record.Data ?? DefaultPayload(record.Type),
            record.Revision,
            role,
            record.UpdatedAt);
    }

    public async Task<SaveContentResult> SaveAsync(
        Guid id, string data, long baseRevision, CancellationToken ct = default)
    {
        await permissions.RequireAsync(id, PermissionRole.Editor, ct);

        if (string.IsNullOrWhiteSpace(data))
            throw new ValidationException("Content payload must not be empty.");

        // Reject malformed JSON here: storing it would make the document unopenable later.
        if (!IsValidJson(data))
            throw new ValidationException("Content payload is not valid JSON.");

        var item = await db.DriveItems
            .Include(x => x.Content)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException($"Item {id} was not found.");

        if (!IsEditableType(item.Type))
            throw new ValidationException($"{item.Type} items have no editable payload.");

        var userId = currentUser.RequireId();
        var now = DateTimeOffset.UtcNow;

        if (item.Content is null)
        {
            item.Content = new DriveItemContent { DriveItemId = item.Id };
            db.DriveItemContents.Add(item.Content);
        }

        // baseRevision < 0 is the explicit "overwrite regardless" signal (restore, AI edit, import).
        if (baseRevision >= 0 && item.Content.Revision != baseRevision)
        {
            return SaveContentResult.Stale(item.Content.Revision, item.Content.Data);
        }

        // Snapshot before overwriting, so the pre-edit state is recoverable. Throttled by interval to
        // avoid one snapshot per keystroke-batch.
        await MaybeAutoSnapshotAsync(item, now, ct);

        item.Content.Data = data;
        item.Content.PlainText = ExtractPlainText(item.Type, data);
        item.Content.Revision++;
        item.Content.UpdatedAt = now;
        item.Content.UpdatedById = userId;

        item.UpdatedAt = now;
        item.UpdatedById = userId;
        item.VersionNumber = (int)Math.Min(int.MaxValue, item.Content.Revision);

        // Keep search working against document bodies, not just names.
        item.SearchText = BuildSearchText(item.Name, item.Content.PlainText);

        await db.SaveChangesAsync(ct);

        await collaboration.ContentChangedAsync(id, item.Content.Revision, userId, null, ct);

        return SaveContentResult.Ok(item.Content.Revision);
    }

    private async Task MaybeAutoSnapshotAsync(DriveItem item, DateTimeOffset now, CancellationToken ct)
    {
        if (item.Content is null || item.Content.Revision == 0) return;

        var lastSnapshot = await db.DriveItemVersions
            .AsNoTracking()
            .Where(v => v.DriveItemId == item.Id)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => (DateTimeOffset?)v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastSnapshot is not null && now - lastSnapshot.Value < AutoSnapshotInterval) return;

        try
        {
            await versions.SnapshotAsync(item.Id, label: null, isAuto: true, ct);
        }
        catch (Exception)
        {
            // A failed safety-net snapshot must not block the user's actual save.
        }
    }

    public async Task<T?> GetTypedAsync<T>(Guid id, CancellationToken ct = default) where T : new()
    {
        var content = await GetAsync(id, ct);
        return content is null ? default : ContentJson.Deserialize<T>(content.Data);
    }

    public async Task<long> SaveTypedAsync<T>(Guid id, T model, CancellationToken ct = default)
    {
        var json = ContentJson.Serialize(model);
        // Typed writes come from server-side code that already holds the whole model, so there is no
        // base revision to rebase against — force the write.
        var result = await SaveAsync(id, json, baseRevision: -1, ct);
        return result.Revision;
    }

    // ─────────────────────────────────────── helpers ───────────────────────────────────────

    private static bool IsEditableType(DriveItemType type) =>
        type is DriveItemType.Document or DriveItemType.Spreadsheet or DriveItemType.Presentation;

    private static string DefaultPayload(DriveItemType type) => type switch
    {
        DriveItemType.Spreadsheet => ContentJson.Serialize(new SpreadsheetModel()),
        DriveItemType.Presentation => ContentJson.Serialize(new PresentationModel()),
        _ => ContentJson.Serialize(new DocumentModel()),
    };

    private static bool IsValidJson(string data)
    {
        try
        {
            using var _ = JsonDocument.Parse(data);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Flattens a payload to plain text for search and for grounding Mr Clippy's answers.
    /// Each type needs its own extraction: HTML for Docs, cell values for Sheets, element text for Slides.
    /// </summary>
    private static string? ExtractPlainText(DriveItemType type, string data)
    {
        try
        {
            return type switch
            {
                DriveItemType.Document => StripHtml(ContentJson.Deserialize<DocumentModel>(data).Html),
                DriveItemType.Spreadsheet => ExtractSpreadsheetText(data),
                DriveItemType.Presentation => ExtractPresentationText(data),
                _ => null,
            };
        }
        catch (Exception)
        {
            // Extraction is best-effort: a payload we can't parse still saves, just without search text.
            return null;
        }
    }

    private static string ExtractSpreadsheetText(string data)
    {
        var model = ContentJson.Deserialize<SpreadsheetModel>(data);
        var parts = new List<string>();

        foreach (var sheet in model.Sheets)
        {
            parts.Add(sheet.Name);

            foreach (var cell in sheet.Cells.Values)
            {
                // Index the displayed value, not the formula — that's what a user searches for.
                if (!string.IsNullOrWhiteSpace(cell.V)) parts.Add(cell.V);
            }
        }

        return Truncate(string.Join(' ', parts));
    }

    private static string ExtractPresentationText(string data)
    {
        var model = ContentJson.Deserialize<PresentationModel>(data);
        var parts = new List<string>();

        foreach (var slide in model.Slides)
        {
            foreach (var element in slide.Elements)
            {
                if (!string.IsNullOrWhiteSpace(element.Text)) parts.Add(StripHtml(element.Text));
                if (!string.IsNullOrWhiteSpace(element.Alt)) parts.Add(element.Alt);
            }

            if (!string.IsNullOrWhiteSpace(slide.Notes)) parts.Add(slide.Notes);
        }

        return Truncate(string.Join(' ', parts));
    }

    /// <summary>
    /// Removes tags and decodes entities. Not a parser — it only has to produce searchable words,
    /// and the output is never rendered as markup.
    /// </summary>
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var sb = new System.Text.StringBuilder(html.Length);
        var insideTag = false;

        foreach (var ch in html)
        {
            switch (ch)
            {
                case '<':
                    insideTag = true;
                    // A tag boundary is a word boundary: "<p>a</p><p>b</p>" must not become "ab".
                    sb.Append(' ');
                    break;
                case '>':
                    insideTag = false;
                    break;
                default:
                    if (!insideTag) sb.Append(ch);
                    break;
            }
        }

        var text = System.Net.WebUtility.HtmlDecode(sb.ToString());
        return Truncate(CollapseWhitespace(text));
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>The PlainText column is capped, and search does not benefit from a whole novel.</summary>
    private static string Truncate(string text, int max = 100_000) =>
        text.Length <= max ? text : text[..max];

    private static string BuildSearchText(string name, string? plainText)
    {
        // SearchText is limited to 4000 chars in the schema; the name must always survive.
        const int limit = 4000;
        if (string.IsNullOrWhiteSpace(plainText)) return name;

        var combined = $"{name} {plainText}";
        return combined.Length <= limit ? combined : combined[..limit];
    }
}
