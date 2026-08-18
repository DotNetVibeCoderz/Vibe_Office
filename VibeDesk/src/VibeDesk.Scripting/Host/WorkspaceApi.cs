using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Calendars;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Application.Spreadsheets;
using VibeDesk.Domain;

namespace VibeDesk.Scripting.Host;

/// <summary>
/// Everything a script can reach, in one object. Exposed as <c>api</c> in every language.
/// </summary>
/// <remarks>
/// Two rules hold throughout. First, every call goes through the same <see cref="IDriveService"/> and
/// friends the UI uses, so a script's reach is exactly its user's reach — there is no parameter a
/// script can set to touch someone else's files. Second, every method is <b>synchronous</b>: neither
/// the JavaScript nor the Python engine can await a <c>Task</c>, and a host API that only worked from
/// C# would not be a unified API.
/// </remarks>
public sealed partial class WorkspaceApi
{
    private readonly ScriptHost _host;
    private readonly ScriptingOptions _options;

    internal WorkspaceApi(
        ScriptHost host,
        ScriptingOptions options,
        IDriveService drive,
        IDocumentContentService content,
        ICalendarService calendar,
        INotificationService notifications,
        ICurrentUser currentUser,
        HttpClient http,
        string? allowedHosts)
    {
        _host = host;
        _options = options;
        _currentUser = currentUser;

        Drive = new DriveApi(host, options, drive);
        Docs = new DocsApi(host, drive, content);
        Sheets = new SheetsApi(host, options, drive, content);
        Slides = new SlidesApi(host, drive, content);
        Calendar = new CalendarApi(host, calendar);
        Http = new HttpApi(host, options, http, allowedHosts);

        _notifications = notifications;
    }

    private readonly INotificationService _notifications;
    private readonly ICurrentUser _currentUser;

    public DriveApi Drive { get; }
    public DocsApi Docs { get; }
    public SheetsApi Sheets { get; }
    public SlidesApi Slides { get; }
    public CalendarApi Calendar { get; }
    public HttpApi Http { get; }

    /// <summary>Values passed in by the caller — the CLI's <c>--input</c>, or a trigger's detail.</summary>
    public Dictionary<string, string> Input { get; internal set; } = [];

    public void Log(object? message) => _host.Log(Stringify(message));

    /// <summary>Sends a notification to the script's own owner. Never to anyone else.</summary>
    public void Notify(string title, string? body = null)
    {
        _host.Require(ScriptScope.Notify, "notify()");

        // Addressed to the running user, never to an id the script chose: a script that could pick
        // its recipient would be a way to spam every account in the workspace.
        _host.Track("notify", title, () => Sync(_notifications.NotifyAsync(
            _currentUser.RequireId(), NotificationKind.System, title, body)));
    }

    public string Now() => DateTimeOffset.UtcNow.ToString("O");

    public string Uuid() => Guid.CreateVersion7().ToString();

    /// <summary>
    /// Builds a <see cref="DataFrame"/> from computed data, so a script can write a table it worked
    /// out itself and not only one it read from a sheet.
    /// </summary>
    /// <remarks>
    /// Rows are accepted as sequences rather than arrays because that is what arrives from JavaScript
    /// and Python: Jint hands over an object implementing <c>IEnumerable</c>, and an IronPython list
    /// is not an <c>object[]</c>. Requiring an array would make this method C#-only.
    /// </remarks>
    public DataFrame Frame(IEnumerable<object?> columns, IEnumerable<object?> rows)
    {
        var names = columns.Select(c => c?.ToString() ?? string.Empty).ToList();

        var materialised = rows
            .Select(row => row is System.Collections.IEnumerable cells and not string
                ? cells.Cast<object?>().ToArray()
                : new[] { row })
            .Take(_options.MaxRows)
            .ToList();

        return new DataFrame(names, materialised);
    }

    /// <summary>JSON in, object out. Present so every language has one parser with one behaviour.</summary>
    public object? ParseJson(string json) => JsonHelper.ToPlain(json);

    public string ToJson(object? value) => JsonSerializer.Serialize(value, JsonHelper.Options);

    internal static string Stringify(object? value) => value switch
    {
        null => "null",
        string s => s,
        DataFrame frame => frame.ToString(),
        bool b => b ? "true" : "false",
        double d => d.ToString("0.############", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O"),
        _ => value.ToString() ?? string.Empty,
    };

    internal static void Sync(Task task) => task.GetAwaiter().GetResult();

    internal static T Sync<T>(Task<T> task) => task.GetAwaiter().GetResult();

    internal static Guid Id(string value, string what) =>
        Guid.TryParse(value, out var id)
            ? id
            : throw new ArgumentException($"'{value}' is not a valid {what} id.");
}

// ────────────────────────────────────── Drive ──────────────────────────────────────

public sealed class DriveApi(ScriptHost host, ScriptingOptions options, IDriveService drive)
{
    public IReadOnlyList<FileInfoView> List(string? folderId = null, int take = 200)
    {
        host.Require(ScriptScope.DriveRead, "drive.list()");

        return host.Track("drive.list", folderId, () =>
        {
            var page = WorkspaceApi.Sync(drive.QueryAsync(new DriveQuery
            {
                ParentId = string.IsNullOrWhiteSpace(folderId) ? null : WorkspaceApi.Id(folderId, "folder"),
                Take = Math.Clamp(take, 1, options.MaxRows),
            }));

            return page.Items.Select(Map).ToList();
        });
    }

    public IReadOnlyList<FileInfoView> Search(string keyword, string? type = null, int take = 100)
    {
        host.Require(ScriptScope.DriveRead, "drive.search()");

        return host.Track("drive.search", keyword, () =>
        {
            var page = WorkspaceApi.Sync(drive.QueryAsync(new DriveQuery
            {
                Keyword = keyword,
                Types = ParseType(type) is { } t ? [t] : null,
                Recursive = true,
                Take = Math.Clamp(take, 1, options.MaxRows),
            }));

            return page.Items.Select(Map).ToList();
        });
    }

    public FileInfoView? Get(string id)
    {
        host.Require(ScriptScope.DriveRead, "drive.get()");

        return host.Track("drive.get", id, () =>
        {
            var item = WorkspaceApi.Sync(drive.GetAsync(WorkspaceApi.Id(id, "item")));
            return item is null ? null : Map(item);
        });
    }

    public FileInfoView CreateFolder(string name, string? parentId = null)
    {
        host.Require(ScriptScope.DriveWrite, "drive.createFolder()");

        return host.Track("drive.createFolder", name, () => Map(WorkspaceApi.Sync(
            drive.CreateFolderAsync(name, Optional(parentId)))));
    }

    /// <summary><paramref name="type"/> is <c>document</c>, <c>spreadsheet</c> or <c>presentation</c>.</summary>
    public FileInfoView CreateDocument(string type, string name, string? parentId = null)
    {
        host.Require(ScriptScope.DriveWrite, "drive.createDocument()");

        var parsed = ParseType(type)
            ?? throw new ArgumentException($"'{type}' is not a document type.");

        if (parsed is DriveItemType.Folder or DriveItemType.File)
        {
            throw new ArgumentException("Use createFolder() for folders; files must be uploaded.");
        }

        return host.Track("drive.createDocument", name, () => Map(WorkspaceApi.Sync(
            drive.CreateDocumentAsync(parsed, name, Optional(parentId)))));
    }

    public FileInfoView Rename(string id, string newName)
    {
        host.Require(ScriptScope.DriveWrite, "drive.rename()");

        return host.Track("drive.rename", newName, () => Map(WorkspaceApi.Sync(
            drive.RenameAsync(WorkspaceApi.Id(id, "item"), newName))));
    }

    public FileInfoView Move(string id, string? parentId)
    {
        host.Require(ScriptScope.DriveWrite, "drive.move()");

        return host.Track("drive.move", id, () => Map(WorkspaceApi.Sync(
            drive.MoveAsync(WorkspaceApi.Id(id, "item"), Optional(parentId)))));
    }

    public FileInfoView Copy(string id, string? parentId = null, string? newName = null)
    {
        host.Require(ScriptScope.DriveWrite, "drive.copy()");

        return host.Track("drive.copy", id, () => Map(WorkspaceApi.Sync(
            drive.CopyAsync(WorkspaceApi.Id(id, "item"), Optional(parentId), newName))));
    }

    public void Star(string id, bool starred = true)
    {
        host.Require(ScriptScope.DriveWrite, "drive.star()");

        host.Track("drive.star", id, () =>
            WorkspaceApi.Sync(drive.SetStarredAsync(WorkspaceApi.Id(id, "item"), starred)));
    }

    /// <summary>Moves to trash. There is deliberately no permanent-delete on the script surface.</summary>
    public void Trash(string id)
    {
        host.Require(ScriptScope.DriveWrite, "drive.trash()");

        host.Track("drive.trash", id, () =>
            WorkspaceApi.Sync(drive.TrashAsync(WorkspaceApi.Id(id, "item"))));
    }

    public UsageView Usage()
    {
        host.Require(ScriptScope.DriveRead, "drive.usage()");

        return host.Track("drive.usage", null, () =>
        {
            var u = WorkspaceApi.Sync(drive.GetUsageAsync());

            return new UsageView
            {
                UsedBytes = u.UsedBytes,
                QuotaBytes = u.QuotaBytes,
                UsedPercent = u.UsedPercent,
                FileCount = u.FileCount,
                DocumentCount = u.DocumentCount,
                SpreadsheetCount = u.SpreadsheetCount,
                PresentationCount = u.PresentationCount,
                FolderCount = u.FolderCount,
            };
        });
    }

    internal static Guid? Optional(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : WorkspaceApi.Id(id, "folder");

    internal static DriveItemType? ParseType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "document" or "doc" or "docs" => DriveItemType.Document,
        "spreadsheet" or "sheet" or "sheets" => DriveItemType.Spreadsheet,
        "presentation" or "slide" or "slides" or "deck" => DriveItemType.Presentation,
        "folder" => DriveItemType.Folder,
        "file" => DriveItemType.File,
        _ => null,
    };

    internal static FileInfoView Map(DriveItemDto item) => new()
    {
        Id = item.Id.ToString(),
        Name = item.Name,
        Type = item.Type.ToString(),
        ParentId = item.ParentId?.ToString(),
        Size = item.SizeBytes,
        Owner = item.OwnerName,
        Starred = item.IsStarred,
        Shared = item.IsShared,
        Role = item.MyRole.ToString(),
        Updated = item.UpdatedAt,
    };
}

// ────────────────────────────────────── Docs ──────────────────────────────────────

public sealed partial class DocsApi(ScriptHost host, IDriveService drive, IDocumentContentService content)
{
    public DocumentView Read(string id)
    {
        host.Require(ScriptScope.DocsRead, "docs.read()");

        return host.Track("docs.read", id, () =>
        {
            var payload = WorkspaceApi.Sync(content.GetAsync(WorkspaceApi.Id(id, "document")))
                ?? throw new ArgumentException("That document could not be found.");

            var model = ContentJson.Deserialize<DocumentModel>(payload.Data);

            return new DocumentView
            {
                Id = payload.Id.ToString(),
                Name = payload.Name,
                Html = model.Html,
                Text = ToText(model.Html),
                WordCount = model.WordCount,
                Revision = payload.Revision,
            };
        });
    }

    /// <summary>Replaces the whole body.</summary>
    public long Write(string id, string html)
    {
        host.Require(ScriptScope.DocsWrite, "docs.write()");

        return host.Track("docs.write", id, () => Save(WorkspaceApi.Id(id, "document"), _ => html));
    }

    public long Append(string id, string html)
    {
        host.Require(ScriptScope.DocsWrite, "docs.append()");

        return host.Track("docs.append", id, () =>
            Save(WorkspaceApi.Id(id, "document"), existing => existing + html));
    }

    /// <summary>
    /// Replaces every <c>{{placeholder}}</c> with a value — the merge half of mail-merge, with the
    /// data usually coming from <c>api.sheets</c>.
    /// </summary>
    public long Merge(string id, Dictionary<string, object?> values)
    {
        host.Require(ScriptScope.DocsWrite, "docs.merge()");

        return host.Track("docs.merge", id, () => Save(WorkspaceApi.Id(id, "document"), existing =>
            PlaceholderPattern().Replace(existing, match =>
            {
                var key = match.Groups[1].Value.Trim();

                return values.TryGetValue(key, out var value)
                    ? System.Net.WebUtility.HtmlEncode(DataFrame.Text(value))
                    : match.Value;
            })));
    }

    /// <summary>Creates a new document from a template document, with placeholders filled in.</summary>
    public FileInfoView CreateFromTemplate(
        string templateId, string name, Dictionary<string, object?> values, string? parentId = null)
    {
        host.Require(ScriptScope.DocsWrite, "docs.createFromTemplate()");

        return host.Track("docs.createFromTemplate", name, () =>
        {
            var created = WorkspaceApi.Sync(drive.CopyAsync(
                WorkspaceApi.Id(templateId, "document"), DriveApi.Optional(parentId), name));

            Save(created.Id, existing => PlaceholderPattern().Replace(existing, match =>
            {
                var key = match.Groups[1].Value.Trim();

                return values.TryGetValue(key, out var value)
                    ? System.Net.WebUtility.HtmlEncode(DataFrame.Text(value))
                    : match.Value;
            }));

            return DriveApi.Map(created);
        });
    }

    private long Save(Guid id, Func<string, string> transform)
    {
        var payload = WorkspaceApi.Sync(content.GetAsync(id))
            ?? throw new ArgumentException("That document could not be found.");

        var model = ContentJson.Deserialize<DocumentModel>(payload.Data);
        model.Html = transform(model.Html);
        model.WordCount = CountWords(model.Html);

        // Forced write: a script edits from whatever it just read, and a revision conflict here would
        // surface as an opaque failure in an automation rather than something a user can rebase.
        return WorkspaceApi.Sync(content.SaveTypedAsync(id, model));
    }

    internal static string ToText(string html)
    {
        var stripped = TagPattern().Replace(
            html.Replace("</p>", "</p>\n").Replace("<br>", "\n").Replace("<br/>", "\n"),
            string.Empty);

        return string.Join('\n', System.Net.WebUtility.HtmlDecode(stripped)
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0));
    }

    private static int CountWords(string html) =>
        ToText(html).Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\{\{([^}]+)\}\}")]
    private static partial Regex PlaceholderPattern();
}

// ────────────────────────────────────── Sheets ──────────────────────────────────────

public sealed class SheetsApi(
    ScriptHost host,
    ScriptingOptions options,
    IDriveService drive,
    IDocumentContentService content)
{
    public IReadOnlyList<SheetView> Tabs(string id)
    {
        host.Require(ScriptScope.SheetsRead, "sheets.tabs()");

        return host.Track("sheets.tabs", id, () => Load(id).Model.Sheets
            .Select(s => new SheetView { Name = s.Name, RowCount = s.RowCount, ColCount = s.ColCount })
            .ToList());
    }

    /// <summary>
    /// Reads a sheet as a table, using the first non-empty row as headers and evaluating formulas so
    /// the caller gets values rather than <c>=B2*C2</c>.
    /// </summary>
    public DataFrame Read(string id, string? sheetName = null, bool hasHeaders = true)
    {
        host.Require(ScriptScope.SheetsRead, "sheets.read()");

        return host.Track("sheets.read", id, () =>
        {
            var (model, _) = Load(id);
            var tab = Tab(model, sheetName);
            var engine = new FormulaEngine(model);

            if (tab.Cells.Count == 0) return new DataFrame([], []);

            var addresses = tab.Cells.Keys
                .Select(k => CellAddress.TryParse(k, out var a)
                    ? (Ok: true, Row: a.Row, Col: a.Col)
                    : (Ok: false, Row: 0, Col: 0))
                .Where(x => x.Ok)
                .ToList();

            if (addresses.Count == 0) return new DataFrame([], []);

            var maxRow = Math.Min(addresses.Max(a => a.Row), options.MaxRows);
            var maxCol = addresses.Max(a => a.Col);

            var grid = new List<object?[]>();

            for (var r = 0; r <= maxRow; r++)
            {
                var row = new object?[maxCol + 1];
                var any = false;

                for (var c = 0; c <= maxCol; c++)
                {
                    var value = Value(engine, tab, r, c);
                    row[c] = value;
                    if (value is not null) any = true;
                }

                // Blank rows before the first real one are skipped; blank rows inside the data are
                // kept, because they usually carry meaning in a hand-built sheet.
                if (any || grid.Count > 0) grid.Add(row);
            }

            if (grid.Count == 0) return new DataFrame([], []);

            if (!hasHeaders)
            {
                return new DataFrame(
                    Enumerable.Range(0, maxCol + 1).Select(CellAddress.ColumnName), grid);
            }

            var headers = grid[0]
                .Select((h, i) => string.IsNullOrWhiteSpace(DataFrame.Text(h))
                    ? CellAddress.ColumnName(i)
                    : DataFrame.Text(h))
                .ToList();

            return new DataFrame(headers, grid.Skip(1));
        });
    }

    public object? GetCell(string id, string a1, string? sheetName = null)
    {
        host.Require(ScriptScope.SheetsRead, "sheets.getCell()");

        return host.Track("sheets.getCell", a1, () =>
        {
            var (model, _) = Load(id);
            var tab = Tab(model, sheetName);

            return CellAddress.TryParse(a1, out var address)
                ? Value(new FormulaEngine(model), tab, address.Row, address.Col)
                : null;
        });
    }

    public long SetCell(string id, string a1, object? value, string? sheetName = null)
    {
        host.Require(ScriptScope.SheetsWrite, "sheets.setCell()");

        return host.Track("sheets.setCell", a1, () =>
            Mutate(id, sheetName, tab => Write(tab, a1.ToUpperInvariant(), value)));
    }

    /// <summary>Writes a rectangular block with its top-left corner at <paramref name="a1"/>.</summary>
    public long SetRange(string id, string a1, IEnumerable<IEnumerable<object?>> rows, string? sheetName = null)
    {
        host.Require(ScriptScope.SheetsWrite, "sheets.setRange()");

        return host.Track("sheets.setRange", a1, () => Mutate(id, sheetName, tab =>
        {
            if (!CellAddress.TryParse(a1, out var start))
            {
                throw new ArgumentException($"'{a1}' is not a cell address.");
            }

            var r = start.Row;

            foreach (var row in rows)
            {
                var c = start.Col;

                foreach (var value in row)
                {
                    Write(tab, new CellAddress(r, c).ToA1(), value);
                    c++;
                }

                r++;
            }
        }));
    }

    public long AppendRow(string id, IEnumerable<object?> values, string? sheetName = null)
    {
        host.Require(ScriptScope.SheetsWrite, "sheets.appendRow()");

        return host.Track("sheets.appendRow", id, () => Mutate(id, sheetName, tab =>
        {
            var lastRow = tab.Cells.Keys
                .Select(k => CellAddress.TryParse(k, out var a) ? a.Row : -1)
                .DefaultIfEmpty(-1)
                .Max();

            var c = 0;

            foreach (var value in values)
            {
                Write(tab, new CellAddress(lastRow + 1, c).ToA1(), value);
                c++;
            }
        }));
    }

    /// <summary>Replaces a sheet's contents with a frame, headers included.</summary>
    public long WriteFrame(string id, DataFrame frame, string? sheetName = null)
    {
        host.Require(ScriptScope.SheetsWrite, "sheets.writeFrame()");

        return host.Track("sheets.writeFrame", id, () => Mutate(id, sheetName, tab =>
        {
            tab.Cells.Clear();

            for (var c = 0; c < frame.Columns.Count; c++)
            {
                Write(tab, new CellAddress(0, c).ToA1(), frame.Columns[c]);
            }

            var r = 1;

            foreach (var row in frame.Values)
            {
                for (var c = 0; c < row.Length; c++) Write(tab, new CellAddress(r, c).ToA1(), row[c]);
                r++;
            }
        }));
    }

    /// <summary>Evaluates a formula against the sheet without storing it anywhere.</summary>
    public object? Evaluate(string id, string formula, string? sheetName = null)
    {
        host.Require(ScriptScope.SheetsRead, "sheets.evaluate()");

        return host.Track<object?>("sheets.evaluate", formula, () =>
        {
            var (model, _) = Load(id);
            var result = new FormulaEngine(model).EvaluateFormula(formula, sheetName);

            // Numbers come back as numbers so a script can do arithmetic on them; errors and text
            // come back as their display form, exactly as a cell would show them.
            return result.Kind == FormulaValueKind.Number
                ? result.RawNumber
                : result.ToDisplayString();
        });
    }

    public FileInfoView Create(string name, DataFrame? data = null, string? parentId = null)
    {
        host.Require(ScriptScope.SheetsWrite, "sheets.create()");

        return host.Track("sheets.create", name, () =>
        {
            var created = WorkspaceApi.Sync(drive.CreateDocumentAsync(
                DriveItemType.Spreadsheet, name, DriveApi.Optional(parentId)));

            if (data is not null) WriteFrame(created.Id.ToString(), data);

            return DriveApi.Map(created);
        });
    }

    private (SpreadsheetModel Model, long Revision) Load(string id)
    {
        var payload = WorkspaceApi.Sync(content.GetAsync(WorkspaceApi.Id(id, "spreadsheet")))
            ?? throw new ArgumentException("That spreadsheet could not be found.");

        return (ContentJson.Deserialize<SpreadsheetModel>(payload.Data), payload.Revision);
    }

    private long Mutate(string id, string? sheetName, Action<SheetTab> mutate)
    {
        var guid = WorkspaceApi.Id(id, "spreadsheet");
        var (model, _) = Load(id);

        mutate(Tab(model, sheetName));

        return WorkspaceApi.Sync(content.SaveTypedAsync(guid, model));
    }

    private static SheetTab Tab(SpreadsheetModel model, string? name)
    {
        if (model.Sheets.Count == 0) model.Sheets.Add(new SheetTab());

        if (string.IsNullOrWhiteSpace(name)) return model.Sheets[0];

        return model.Sheets.FirstOrDefault(s =>
                   string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException($"This spreadsheet has no sheet named '{name}'.");
    }

    private static void Write(SheetTab tab, string a1, object? value)
    {
        var text = DataFrame.Text(value);

        if (string.IsNullOrEmpty(text))
        {
            tab.Cells.Remove(a1);
            return;
        }

        // A leading '=' means the script is writing a formula, not a literal — the same rule the
        // editor applies when a user types into a cell.
        tab.Cells[a1] = text.StartsWith('=')
            ? new Cell { F = text }
            : new Cell { V = text };
    }

    private static object? Value(FormulaEngine engine, SheetTab tab, int row, int col)
    {
        var a1 = new CellAddress(row, col).ToA1();

        if (!tab.Cells.TryGetValue(a1, out var cell)) return null;

        if (cell.F is { Length: > 0 } formula)
        {
            var evaluated = engine.EvaluateFormula(formula, tab.Name);

            return evaluated.Kind == FormulaValueKind.Number
                ? evaluated.RawNumber
                : evaluated.ToDisplayString();
        }

        if (cell.V is not { Length: > 0 } raw) return null;

        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
            ? number
            : raw;
    }
}
