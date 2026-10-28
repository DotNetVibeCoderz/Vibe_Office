// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.ComponentModel;
using System.Globalization;
using ExcelNet;
using ModelContextProtocol.Server;

namespace OfficeNet.Mcp.Tools;

/// <summary>Excel (.xlsx) tools.</summary>
[McpServerToolType]
public sealed class ExcelTools(Workspace workspace)
{
    /// <summary>A model reading a sheet wants the gist, not a megabyte of JSON.</summary>
    private const int DefaultMaxRows = 200;

    [McpServerTool(Name = "excel_create", Title = "Create an Excel workbook", Destructive = true)]
    [Description(
        "Creates an .xlsx with one or more sheets. Cell values may be numbers, text, booleans, ISO " +
        "dates, or formulas (strings starting with =). Formulas are evaluated so the file opens " +
        "with results. Each sheet can carry number formats per column and a chart.")]
    public string Create(
        [Description("Workspace path of the .xlsx to write.")] string path,
        [Description("The sheets, in tab order.")] SheetSpec[] sheets) =>
        ToolGuard.Run(() =>
        {
            if (sheets.Length == 0)
            {
                throw new ArgumentException("A workbook needs at least one sheet.");
            }

            var full = workspace.ForWriting(path);

            using var workbook = Workbook.Create(sheets[0].Name);

            foreach (var (spec, index) in sheets.Select((s, i) => (s, i)))
            {
                var sheet = index == 0 ? workbook[spec.Name] : workbook.AddSheet(spec.Name);
                Fill(sheet, spec);
            }

            var formulas = workbook.Recalculate();
            workbook.Save(full);

            return $"Created {workspace.Relative(full)}: {sheets.Length} sheet(s), {formulas} formula(s) evaluated.";
        });

    [McpServerTool(Name = "excel_write_cells", Title = "Write Excel cells", Destructive = true)]
    [Description(
        "Writes cells in an existing workbook by address, e.g. {\"B2\": 1500, \"B3\": \"=B2*1.1\"}. " +
        "Creates the sheet if it does not exist, and recalculates.")]
    public string WriteCells(
        [Description("Workspace path of the .xlsx.")] string path,
        [Description("Sheet name.")] string sheet,
        [Description("Cell address to value; a string starting with = is a formula.")]
        Dictionary<string, object?> cells) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.Existing(path);
            workspace.ForWriting(path);

            using var workbook = Workbook.Open(full);
            var target = workbook.Find(sheet) ?? workbook.AddSheet(sheet);

            foreach (var (address, value) in cells)
            {
                Set(target[address], Values.Cell(value));
            }

            workbook.Recalculate();
            workbook.Save(full);
            return $"Wrote {cells.Count} cell(s) to {sheet}.";
        });

    [McpServerTool(Name = "excel_read", Title = "Read Excel cells", ReadOnly = true)]
    [Description(
        "Reads a sheet (or a range of it) as rows of values. Formulas come back as their " +
        "calculated result. Defaults to the first sheet and its used range.")]
    public string Read(
        [Description("Workspace path of the .xlsx.")] string path,
        [Description("Sheet name; defaults to the first sheet.")] string? sheet = null,
        [Description("A1 range such as A1:D20; defaults to the used range.")] string? range = null,
        [Description("At most this many rows are returned. Defaults to 200.")] int maxRows = DefaultMaxRows) =>
        ToolGuard.Run(() =>
        {
            using var workbook = Workbook.Open(workspace.Existing(path));
            var target = sheet is null ? workbook[0] : workbook.Find(sheet)
                ?? throw new ArgumentException(
                    $"No sheet named '{sheet}'. The sheets are: {string.Join(", ", workbook.Select(s => s.Name))}.");

            var area = range is null ? target.UsedRange : CellRangeReference.Parse(range);

            if (area is not { } used)
            {
                return Values.ToJson(new { sheet = target.Name, sheets = workbook.Select(s => s.Name), rows = Array.Empty<object>() });
            }

            var rows = new List<object?[]>();

            for (var row = used.Start.Row; row <= used.End.Row && rows.Count < maxRows; row++)
            {
                var values = new object?[used.End.Column - used.Start.Column + 1];

                for (var column = used.Start.Column; column <= used.End.Column; column++)
                {
                    values[column - used.Start.Column] = Read(target[row, column].Value);
                }

                rows.Add(values);
            }

            return Values.ToJson(new
            {
                sheet = target.Name,
                sheets = workbook.Select(s => s.Name),
                range = used.A1,
                truncated = used.RowCount > rows.Count,
                rows,
            });
        });

    [McpServerTool(Name = "excel_add_chart", Title = "Add a chart to a sheet", Destructive = true)]
    [Description("Adds a native Excel chart to a sheet, from the categories and series given.")]
    public string AddChart(
        [Description("Workspace path of the .xlsx.")] string path,
        [Description("Sheet name.")] string sheet,
        [Description("The chart.")] ChartSpec chart,
        [Description("Where it goes, as a cell range, e.g. H2:P20. Defaults to beside the data.")]
        string? anchor = null) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.Existing(path);
            workspace.ForWriting(path);

            using var workbook = Workbook.Open(full);
            var target = workbook.Find(sheet) ?? throw new ArgumentException($"No sheet named '{sheet}'.");

            target.AddChart(Values.Chart(chart), anchor ?? BesideData(target));
            workbook.Save(full);
            return $"Added a {chart.Type} chart to {sheet}.";
        });

    // ---- Writing -------------------------------------------------------------------------------

    private static void Fill(Worksheet sheet, SheetSpec spec)
    {
        for (var row = 0; row < spec.Rows.Length; row++)
        {
            for (var column = 0; column < spec.Rows[row].Length; column++)
            {
                var cell = sheet[row, column];
                Set(cell, Values.Cell(spec.Rows[row][column]));

                if (row == 0 && spec.Header != false)
                {
                    cell.Bold();
                }
            }
        }

        if (spec.NumberFormats is { } formats)
        {
            foreach (var (letters, format) in formats)
            {
                var column = CellReference.ColumnNameToIndex(letters.Trim().ToUpperInvariant());

                for (var row = spec.Header == false ? 0 : 1; row < spec.Rows.Length; row++)
                {
                    sheet[row, column].WithNumberFormat(format);
                }
            }
        }

        if (spec.Header != false && spec.Rows.Length > 1)
        {
            sheet.Frozen = new FreezePanes(1, 0);
        }

        sheet.AutoFitColumns();

        if (spec.Chart is { } chart)
        {
            sheet.AddChart(Values.Chart(chart), BesideData(sheet));
        }
    }

    private static void Set(Cell cell, object? value)
    {
        switch (value)
        {
            case string text when text.StartsWith('=') && text.Length > 1:
                cell.SetFormula(text);
                break;

            default:
                cell.Set(value);
                break;
        }
    }

    private static object? Read(CellValue value) => value.ValueType switch
    {
        CellValueType.Empty => null,
        CellValueType.Number => value.AsNumber(),
        CellValueType.Boolean => value.AsNumber() != 0,
        CellValueType.DateTime => value.AsDateTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        _ => value.AsText(),
    };

    /// <summary>Two columns right of the used range, fifteen rows tall.</summary>
    private static string BesideData(Worksheet sheet)
    {
        var start = (sheet.UsedRange?.End.Column ?? 0) + 2;
        var top = new CellReference(1, start);
        var bottom = new CellReference(16, start + 7);
        return $"{top.A1}:{bottom.A1}";
    }
}
