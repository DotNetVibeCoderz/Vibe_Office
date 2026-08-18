using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using VibeDesk.Application.Assistant;
using VibeDesk.Application.Calendars;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Drive;
using VibeDesk.Domain;

namespace VibeDesk.Ai.Plugins;

/// <summary>
/// The user's own workspace: Drive search, document reading, and the calendar.
/// </summary>
/// <remarks>
/// Read-only by design. Every call goes through <see cref="IDriveService"/> and
/// <see cref="IDocumentContentService"/>, which resolve permissions for the signed-in user — so the
/// assistant can only ever read what its user could already open. Nothing here takes a user id, and
/// that is the point: there is no parameter the model could set to reach someone else's files.
/// </remarks>
public sealed partial class WorkspacePlugin(
    IDriveService drive,
    IDocumentContentService content,
    ICalendarService calendar,
    ClippyContext context,
    int maxChars)
{
    [KernelFunction("search_drive")]
    [Description("Searches the user's Drive by keyword and returns matching files with their ids, types and dates. Call this to find a document before reading it.")]
    public async Task<string> SearchDriveAsync(
        [Description("Words to match against file names and contents. Leave empty to list recent files.")] string? keyword,
        [Description("Optional filter: document, spreadsheet, presentation, folder or file.")] string? type,
        CancellationToken ct = default)
    {
        var query = new DriveQuery
        {
            Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
            Types = ParseType(type),
            Take = 20,
            Recursive = true,
        };

        var page = await drive.QueryAsync(query, ct).ConfigureAwait(false);

        if (page.Items.Count == 0) return "Tidak ada berkas yang cocok.";

        var builder = new StringBuilder();

        foreach (var item in page.Items)
        {
            builder.AppendLine(
                $"- {item.Name} [{item.Type}] id={item.Id} diubah {item.UpdatedAt:yyyy-MM-dd HH:mm} oleh {item.UpdatedByName ?? item.OwnerName}");
        }

        return builder.ToString();
    }

    [KernelFunction("read_document")]
    [Description("Reads a Docs, Sheets or Slides file from Drive as plain text. Pass the id from search_drive, or leave it empty to read the file the user currently has open.")]
    public async Task<string> ReadDocumentAsync(
        [Description("Drive item id. Empty means the currently open file.")] string? itemId,
        CancellationToken ct = default)
    {
        var id = Resolve(itemId);

        if (id is null)
        {
            return "Tidak ada berkas yang sedang dibuka. Panggil search_drive lebih dulu untuk mendapatkan id-nya.";
        }

        var payload = await content.GetAsync(id.Value, ct).ConfigureAwait(false);
        if (payload is null) return "Berkas tidak ditemukan atau tidak dapat diakses.";

        var body = payload.Type switch
        {
            DriveItemType.Document => ReadDoc(payload.Data),
            DriveItemType.Spreadsheet => ReadSheet(payload.Data),
            DriveItemType.Presentation => ReadDeck(payload.Data),
            _ => "(tipe berkas ini tidak bisa dibaca sebagai teks)",
        };

        return Truncate($"# {payload.Name} ({payload.Type})\n\n{body}", maxChars);
    }

    [KernelFunction("get_open_document")]
    [Description("Describes what the user is looking at right now: which app, which file, and the current selection. Call this first when the user says 'this', 'here' or 'the selected cells'.")]
    public string GetOpenDocument()
    {
        var builder = new StringBuilder($"App: {context.App}");

        if (context.DriveItemId is { } id)
        {
            builder.Append($"\nFile: {context.DriveItemName ?? "(tanpa nama)"} [{context.DriveItemType}] id={id}");
        }
        else
        {
            builder.Append("\nFile: (belum ada berkas yang dibuka)");
        }

        if (!string.IsNullOrWhiteSpace(context.Selection))
        {
            builder.Append($"\nSelection: {context.Selection}");
        }

        return builder.ToString();
    }

    [KernelFunction("list_calendar_events")]
    [Description("Lists the user's calendar events in a date window. Use for questions about schedules, availability and upcoming meetings.")]
    public async Task<string> ListEventsAsync(
        [Description("Days ahead to include. Use a negative number to look backwards. Default 7.")] int days = 7,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var (from, to) = days >= 0 ? (now, now.AddDays(days)) : (now.AddDays(days), now);

        var events = await calendar.GetOccurrencesAsync(from, to, null, ct).ConfigureAwait(false);

        if (events.Count == 0) return "Tidak ada acara pada rentang tersebut.";

        var builder = new StringBuilder();

        foreach (var occurrence in events.OrderBy(e => e.StartUtc))
        {
            builder.Append($"- {occurrence.StartUtc:yyyy-MM-dd HH:mm}–{occurrence.EndUtc:HH:mm} UTC  {occurrence.Title}");

            if (!string.IsNullOrWhiteSpace(occurrence.Location)) builder.Append($" @ {occurrence.Location}");

            builder.AppendLine($"  [{occurrence.CalendarName}]");
        }

        return Truncate(builder.ToString(), maxChars);
    }

    private Guid? Resolve(string? itemId) =>
        Guid.TryParse(itemId, out var parsed) ? parsed : context.DriveItemId;

    private static DriveItemType[]? ParseType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "document" or "doc" or "docs" => [DriveItemType.Document],
        "spreadsheet" or "sheet" or "sheets" => [DriveItemType.Spreadsheet],
        "presentation" or "slide" or "slides" => [DriveItemType.Presentation],
        "folder" => [DriveItemType.Folder],
        "file" => [DriveItemType.File],
        _ => null,
    };

    private static string ReadDoc(string json)
    {
        var model = ContentJson.Deserialize<DocumentModel>(json);

        // Tags out, entities decoded, blank runs collapsed — the model reads prose, not markup.
        var text = TagPattern().Replace(model.Html.Replace("</p>", "</p>\n").Replace("<br>", "\n"), string.Empty);

        return string.Join('\n', System.Net.WebUtility.HtmlDecode(text)
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0));
    }

    private static string ReadSheet(string json)
    {
        var model = ContentJson.Deserialize<SpreadsheetModel>(json);
        var builder = new StringBuilder();

        foreach (var sheet in model.Sheets)
        {
            builder.AppendLine($"## Sheet: {sheet.Name}");

            if (sheet.Cells.Count == 0)
            {
                builder.AppendLine("(kosong)").AppendLine();
                continue;
            }

            // Grouped by row so the model sees a table rather than a bag of addresses.
            var rows = sheet.Cells
                .Select(kv => (Address: kv.Key, Cell: kv.Value, Row: RowOf(kv.Key)))
                .GroupBy(x => x.Row)
                .OrderBy(g => g.Key);

            foreach (var row in rows)
            {
                var cells = row
                    .OrderBy(x => x.Address, StringComparer.OrdinalIgnoreCase)
                    .Select(x => $"{x.Address}={x.Cell.F ?? x.Cell.V ?? string.Empty}");

                builder.AppendLine(string.Join(" | ", cells));
            }

            if (sheet.Charts.Count > 0) builder.AppendLine($"(chart: {sheet.Charts.Count})");
            if (sheet.Pivots.Count > 0) builder.AppendLine($"(pivot: {sheet.Pivots.Count})");

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ReadDeck(string json)
    {
        var model = ContentJson.Deserialize<PresentationModel>(json);
        var builder = new StringBuilder();
        var number = 0;

        foreach (var slide in model.Slides)
        {
            number++;
            builder.AppendLine($"## Slide {number} ({slide.Layout})");

            foreach (var element in slide.Elements.Where(e => !string.IsNullOrWhiteSpace(e.Text)))
            {
                builder.AppendLine(System.Net.WebUtility.HtmlDecode(
                    TagPattern().Replace(element.Text!, string.Empty)).Trim());
            }

            if (!string.IsNullOrWhiteSpace(slide.Notes)) builder.AppendLine($"Catatan: {slide.Notes}");

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static int RowOf(string address)
    {
        var digits = new string(address.Where(char.IsDigit).ToArray());

        return int.TryParse(digits, out var row) ? row : 0;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "\n…(dipotong)";

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}
