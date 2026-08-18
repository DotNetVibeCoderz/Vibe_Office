using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Spreadsheets;
using VibeDesk.Domain;

namespace VibeDesk.Ai.Plugins;

/// <summary>
/// Creating and editing the workspace: documents, spreadsheets, presentations and folders.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="WorkspacePlugin"/>, kept separate so the read surface stays
/// obviously read-only and a deployment can register one without the other.
/// </para>
/// <para>
/// <b>Nothing here deletes.</b> There is no trash, no delete and no empty-trash function, by design:
/// a model that misreads "clear out the old drafts" should at worst leave a stray file, never remove
/// work. Renaming and moving are reversible and are allowed; destroying is not offered at all, so
/// there is no call for the model to get wrong.
/// </para>
/// <para>
/// Every call goes through <see cref="IDriveService"/> and <see cref="IDocumentContentService"/>,
/// which resolve permissions for the signed-in user. Nothing takes a user id — the assistant can
/// only write where its user could already write.
/// </para>
/// </remarks>
public sealed partial class WorkspaceWritePlugin(
    IDriveService drive,
    IDocumentContentService content)
{
    // ─────────────────────────────────── Docs ───────────────────────────────────

    [KernelFunction("create_document")]
    [Description("Creates a new text document in the user's Drive and returns its id. Use HTML for the body: h1, h2, p, ul/li, b, i, table.")]
    public async Task<string> CreateDocumentAsync(
        [Description("Title of the document.")] string name,
        [Description("Body as HTML. Leave empty for a blank document.")] string? html = null,
        [Description("Optional id of the folder to create it in. Leave empty for the Drive root.")] string? folderId = null,
        CancellationToken ct = default)
    {
        var created = await drive
            .CreateDocumentAsync(DriveItemType.Document, Clean(name, "Untitled document"), Parse(folderId), ct: ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(html))
        {
            await WriteDocumentAsync(created.Id, _ => html, ct).ConfigureAwait(false);
        }

        return $"Dokumen \"{created.Name}\" dibuat. id={created.Id}";
    }

    [KernelFunction("append_to_document")]
    [Description("Adds HTML to the end of an existing document, leaving what is already there untouched.")]
    public async Task<string> AppendToDocumentAsync(
        [Description("Id of the document, from search_drive.")] string id,
        [Description("HTML to append.")] string html,
        CancellationToken ct = default)
    {
        if (Parse(id) is not { } target) return "Id dokumen tidak valid.";

        await WriteDocumentAsync(target, existing => existing + html, ct).ConfigureAwait(false);

        return "Isi ditambahkan ke akhir dokumen.";
    }

    [KernelFunction("replace_document_content")]
    [Description("Replaces a document's entire body with new HTML. The previous text is kept in version history. Prefer append_to_document unless the user asked for a rewrite.")]
    public async Task<string> ReplaceDocumentAsync(
        [Description("Id of the document, from search_drive.")] string id,
        [Description("The new body as HTML.")] string html,
        CancellationToken ct = default)
    {
        if (Parse(id) is not { } target) return "Id dokumen tidak valid.";

        await WriteDocumentAsync(target, _ => html, ct).ConfigureAwait(false);

        return "Isi dokumen diganti. Versi sebelumnya tersimpan di riwayat versi.";
    }

    // ─────────────────────────────────── Sheets ───────────────────────────────────

    [KernelFunction("create_spreadsheet")]
    [Description("Creates a new spreadsheet from CSV-style rows and returns its id. The first row becomes the header.")]
    public async Task<string> CreateSpreadsheetAsync(
        [Description("Title of the spreadsheet.")] string name,
        [Description("Rows, one per line, cells separated by commas. Wrap a cell in double quotes when it contains a comma.")] string? rows = null,
        [Description("Optional id of the folder to create it in.")] string? folderId = null,
        CancellationToken ct = default)
    {
        var created = await drive
            .CreateDocumentAsync(DriveItemType.Spreadsheet, Clean(name, "Untitled spreadsheet"), Parse(folderId), ct: ct)
            .ConfigureAwait(false);

        var written = string.IsNullOrWhiteSpace(rows)
            ? 0
            : await WriteRowsAsync(created.Id, rows, ct).ConfigureAwait(false);

        return $"Spreadsheet \"{created.Name}\" dibuat dengan {written} baris. id={created.Id}";
    }

    [KernelFunction("append_spreadsheet_rows")]
    [Description("Adds rows to the end of an existing spreadsheet's first sheet.")]
    public async Task<string> AppendSpreadsheetRowsAsync(
        [Description("Id of the spreadsheet, from search_drive.")] string id,
        [Description("Rows, one per line, cells separated by commas.")] string rows,
        CancellationToken ct = default)
    {
        if (Parse(id) is not { } target) return "Id spreadsheet tidak valid.";

        var written = await WriteRowsAsync(target, rows, ct).ConfigureAwait(false);

        return $"{written} baris ditambahkan.";
    }

    // ─────────────────────────────────── Slides ───────────────────────────────────

    [KernelFunction("create_presentation")]
    [Description("Creates a new presentation and returns its id. Separate slides with a blank line; within a slide the first line is the title and the rest is the body.")]
    public async Task<string> CreatePresentationAsync(
        [Description("Title of the presentation.")] string name,
        [Description("Slides separated by a blank line.")] string? slides = null,
        [Description("Optional id of the folder to create it in.")] string? folderId = null,
        CancellationToken ct = default)
    {
        var created = await drive
            .CreateDocumentAsync(DriveItemType.Presentation, Clean(name, "Untitled presentation"), Parse(folderId), ct: ct)
            .ConfigureAwait(false);

        var blocks = Blocks(slides);

        if (blocks.Count > 0)
        {
            var model = await content.GetTypedAsync<PresentationModel>(created.Id, ct).ConfigureAwait(false)
                        ?? new PresentationModel();

            // A new deck arrives with one placeholder slide, which is not what the caller asked for.
            model.Slides.Clear();

            foreach (var block in blocks) model.Slides.Add(BuildSlide(block, model.Slides.Count == 0));

            await content.SaveTypedAsync(created.Id, model, ct).ConfigureAwait(false);
        }

        return $"Presentasi \"{created.Name}\" dibuat dengan {blocks.Count} slide. id={created.Id}";
    }

    [KernelFunction("add_slides")]
    [Description("Adds slides to the end of an existing presentation. Separate slides with a blank line.")]
    public async Task<string> AddSlidesAsync(
        [Description("Id of the presentation, from search_drive.")] string id,
        [Description("Slides separated by a blank line.")] string slides,
        CancellationToken ct = default)
    {
        if (Parse(id) is not { } target) return "Id presentasi tidak valid.";

        var blocks = Blocks(slides);
        if (blocks.Count == 0) return "Tidak ada slide untuk ditambahkan.";

        var model = await content.GetTypedAsync<PresentationModel>(target, ct).ConfigureAwait(false)
                    ?? new PresentationModel();

        foreach (var block in blocks) model.Slides.Add(BuildSlide(block, isFirst: false));

        await content.SaveTypedAsync(target, model, ct).ConfigureAwait(false);

        return $"{blocks.Count} slide ditambahkan.";
    }

    // ─────────────────────────────────── Drive ───────────────────────────────────

    [KernelFunction("create_folder")]
    [Description("Creates a folder in the user's Drive and returns its id.")]
    public async Task<string> CreateFolderAsync(
        [Description("Folder name.")] string name,
        [Description("Optional id of the parent folder. Leave empty for the Drive root.")] string? parentId = null,
        CancellationToken ct = default)
    {
        var created = await drive
            .CreateFolderAsync(Clean(name, "Untitled folder"), Parse(parentId), ct)
            .ConfigureAwait(false);

        return $"Folder \"{created.Name}\" dibuat. id={created.Id}";
    }

    [KernelFunction("rename_item")]
    [Description("Renames a file or folder. Reversible, and the contents are untouched.")]
    public async Task<string> RenameAsync(
        [Description("Id of the file or folder, from search_drive.")] string id,
        [Description("The new name.")] string newName,
        CancellationToken ct = default)
    {
        if (Parse(id) is not { } target) return "Id tidak valid.";
        if (string.IsNullOrWhiteSpace(newName)) return "Nama baru tidak boleh kosong.";

        var renamed = await drive.RenameAsync(target, newName.Trim(), ct).ConfigureAwait(false);

        return $"Diganti nama menjadi \"{renamed.Name}\".";
    }

    [KernelFunction("move_item")]
    [Description("Moves a file or folder into another folder. Reversible, and nothing is deleted.")]
    public async Task<string> MoveAsync(
        [Description("Id of the file or folder to move.")] string id,
        [Description("Id of the destination folder. Leave empty to move it to the Drive root.")] string? folderId = null,
        CancellationToken ct = default)
    {
        if (Parse(id) is not { } target) return "Id tidak valid.";

        var moved = await drive.MoveAsync(target, Parse(folderId), ct).ConfigureAwait(false);

        return $"\"{moved.Name}\" dipindahkan.";
    }

    // ─────────────────────────────────── helpers ───────────────────────────────────

    private static Guid? Parse(string? id) =>
        Guid.TryParse(id?.Trim(), out var parsed) ? parsed : null;

    private static string Clean(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    /// <summary>
    /// Reads, transforms and stores a document body. The write is forced rather than revision-checked:
    /// the assistant edits from what it just read, and a conflict here would reach the user as an
    /// opaque failure instead of something they can rebase.
    /// </summary>
    private async Task WriteDocumentAsync(Guid id, Func<string, string> transform, CancellationToken ct)
    {
        var model = await content.GetTypedAsync<DocumentModel>(id, ct).ConfigureAwait(false)
                    ?? new DocumentModel();

        model.Html = transform(model.Html ?? string.Empty);
        model.WordCount = WordCount(model.Html);

        await content.SaveTypedAsync(id, model, ct).ConfigureAwait(false);
    }

    private async Task<int> WriteRowsAsync(Guid id, string rows, CancellationToken ct)
    {
        var model = await content.GetTypedAsync<SpreadsheetModel>(id, ct).ConfigureAwait(false)
                    ?? new SpreadsheetModel();

        var tab = model.Sheets.FirstOrDefault();
        if (tab is null) return 0;

        // Append below whatever is already there, so this never overwrites existing rows.
        var nextRow = tab.Cells.Keys
            .Select(k => CellAddress.TryParse(k, out var a) ? a.Row : -1)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        var written = 0;

        foreach (var line in Lines(rows))
        {
            var cells = SplitCsv(line);

            for (var c = 0; c < cells.Count; c++)
            {
                tab.Cells[new CellAddress(nextRow, c).ToA1()] = new Cell { V = cells[c] };
            }

            nextRow++;
            written++;
        }

        await content.SaveTypedAsync(id, model, ct).ConfigureAwait(false);

        return written;
    }

    private static List<string> Lines(string? text) =>
        [.. (text ?? string.Empty)
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Splits slide text into blocks on a blank line.</summary>
    private static List<string> Blocks(string? text) =>
        [.. (text ?? string.Empty)
            .ReplaceLineEndings("\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Splits one CSV line, honouring quoted cells. Written out rather than split on commas because a
    /// model asked for "Jakarta, Indonesia" in a cell will quote it, and splitting naively would put
    /// half of it in the next column.
    /// </summary>
    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (quoted)
            {
                // A doubled quote inside a quoted cell is one literal quote.
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(ch);
        }

        cells.Add(cell.ToString().Trim());

        return cells;
    }

    private static Slide BuildSlide(string block, bool isFirst)
    {
        var lines = Lines(block);
        var title = lines.Count > 0 ? lines[0] : "Slide";
        var body = lines.Skip(1).ToList();

        // The opening slide is a title card; everything after it carries content.
        var layout = isFirst && body.Count == 0 ? "title" : "titleContent";

        var slide = new Slide { Layout = layout };

        slide.Elements.Add(new SlideElement
        {
            Type = "text",
            X = 8,
            Y = layout == "title" ? 38 : 12,
            W = 84,
            H = 18,
            Text = $"<h1>{WebUtility.HtmlEncode(title)}</h1>",
        });

        if (body.Count > 0)
        {
            slide.Elements.Add(new SlideElement
            {
                Type = "text",
                X = 8,
                Y = 36,
                W = 84,
                H = 52,
                // Lines become paragraphs: the model passes text, not markup.
                Text = string.Concat(body.Select(l => $"<p>{WebUtility.HtmlEncode(l)}</p>")),
            });
        }

        return slide;
    }

    private static int WordCount(string html) =>
        WebUtility.HtmlDecode(TagPattern().Replace(html, " "))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}
