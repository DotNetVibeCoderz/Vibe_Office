using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Spreadsheets;
// Both sides genuinely call this a Cell, so each gets a name that says which one is meant.
using VibeCell = VibeDesk.Application.Documents.Cell;
using XlCell = DocumentFormat.OpenXml.Spreadsheet.Cell;

namespace VibeDesk.Office;

/// <summary>
/// .xlsx to the sparse cell map and back.
/// </summary>
/// <remarks>
/// This is the highest-fidelity of the three converters, because the models are the closest: both
/// sides are sheets of addressed cells holding a value or a formula. Carried: every sheet, values,
/// types, formulas and number formats. Dropped: fonts, fills, borders, charts, pivot tables,
/// conditional formatting, data validation, merged ranges and images.
/// </remarks>
internal static class ExcelConverter
{
    public static SpreadsheetModel Read(Stream source)
    {
        using var package = SpreadsheetDocument.Open(source, isEditable: false);

        var workbookPart = package.WorkbookPart;
        var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().ToList();

        if (workbookPart is null || sheets is null || sheets.Count == 0) return new SpreadsheetModel();

        var shared = SharedStrings(workbookPart);
        var formats = NumberFormats(workbookPart);
        var model = new SpreadsheetModel { Sheets = [] };
        var index = 0;

        foreach (var sheet in sheets)
        {
            if (sheet.Id?.Value is not { } relationshipId) continue;
            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart part) continue;

            index++;

            var tab = new SheetTab
            {
                Id = $"s{index}",
                Name = sheet.Name?.Value ?? $"Sheet{index}",
            };

            var maxRow = 0;
            var maxCol = 0;

            // A worksheet part can exist without its root element when the package is malformed.
            if (part.Worksheet is not { } worksheet) continue;

            foreach (var cell in worksheet.Descendants<XlCell>())
            {
                if (cell.CellReference?.Value is not { } reference) continue;

                var converted = Convert(cell, shared, formats);
                if (converted is null) continue;

                tab.Cells[reference] = converted;

                if (CellAddress.TryParse(reference, out var address))
                {
                    maxRow = Math.Max(maxRow, address.Row + 1);
                    maxCol = Math.Max(maxCol, address.Col + 1);
                }
            }

            // Give the grid room to breathe past the last filled cell, matching a new sheet's feel.
            tab.RowCount = Math.Max(200, maxRow + 20);
            tab.ColCount = Math.Max(26, maxCol + 5);

            model.Sheets.Add(tab);
        }

        if (model.Sheets.Count == 0) model.Sheets.Add(new SheetTab());

        return model;
    }

    private static VibeCell? Convert(
        XlCell cell, List<string> shared, Dictionary<uint, string> formats)
    {
        var formula = cell.CellFormula?.Text;
        var raw = cell.CellValue?.Text;

        // if/else rather than a switch: in the OpenXML SDK CellValues is a value wrapper, not a C#
        // enum, so its members are not the compile-time constants a switch pattern requires.
        var type = cell.DataType?.Value;
        string? value;

        if (type == CellValues.SharedString && int.TryParse(raw, out var index))
        {
            value = index >= 0 && index < shared.Count ? shared[index] : null;
        }
        else if (type == CellValues.InlineString)
        {
            // An inline string lives in the cell rather than the shared table.
            value = cell.InlineString?.Text?.Text ?? cell.InlineString?.InnerText;
        }
        else if (type == CellValues.Boolean)
        {
            value = raw == "1" ? "TRUE" : "FALSE";
        }
        else
        {
            value = raw;
        }

        if (value is null && formula is null) return null;

        var format = cell.StyleIndex?.Value is { } style && formats.TryGetValue(style, out var f) ? f : null;

        return new VibeCell
        {
            V = value,
            // Excel stores a formula without its leading '='; VibeDesk stores it with one.
            F = string.IsNullOrEmpty(formula) ? null : "=" + formula,
            Fmt = format,
        };
    }

    private static List<string> SharedStrings(WorkbookPart workbook) =>
        workbook.SharedStringTablePart?.SharedStringTable is { } table
            ? [.. table.Elements<SharedStringItem>().Select(i => i.InnerText)]
            : [];

    /// <summary>Maps a style index to its number format string, for the custom formats only.</summary>
    private static Dictionary<uint, string> NumberFormats(WorkbookPart workbook)
    {
        var result = new Dictionary<uint, string>();

        var stylesheet = workbook.WorkbookStylesPart?.Stylesheet;
        var formats = stylesheet?.CellFormats?.Elements<CellFormat>().ToList();
        if (formats is null) return result;

        var custom = stylesheet?.NumberingFormats?
            .Elements<NumberingFormat>()
            .Where(n => n.NumberFormatId?.Value is not null && n.FormatCode?.Value is not null)
            .ToDictionary(n => n.NumberFormatId!.Value!, n => n.FormatCode!.Value!) ?? [];

        for (var i = 0; i < formats.Count; i++)
        {
            if (formats[i].NumberFormatId?.Value is not { } id) continue;

            // Built-in ids are rendered by the app's own defaults; only custom codes carry meaning
            // that would otherwise be lost.
            if (custom.TryGetValue(id, out var code)) result[(uint)i] = code;
        }

        return result;
    }

    // ─────────────────────────────────── write ───────────────────────────────────

    public static void Write(Stream destination, SpreadsheetModel model)
    {
        using var package = SpreadsheetDocument.Create(destination, SpreadsheetDocumentType.Workbook);

        var workbookPart = package.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());

        uint id = 1;

        foreach (var tab in model.Sheets)
        {
            var part = workbookPart.AddNewPart<WorksheetPart>();
            part.Worksheet = new Worksheet(BuildSheetData(tab));

            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(part),
                SheetId = id,
                Name = tab.Name,
            });

            id++;
        }

        if (id == 1)
        {
            // A workbook with no sheets will not open; an empty one will.
            var part = workbookPart.AddNewPart<WorksheetPart>();
            part.Worksheet = new Worksheet(new SheetData());

            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(part),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        workbookPart.Workbook.Save();
    }

    private static SheetData BuildSheetData(SheetTab tab)
    {
        var data = new SheetData();

        // Rows must be written in order, and cells in column order within a row, or Excel reports the
        // file as corrupt rather than sorting it out.
        var byRow = tab.Cells
            .Select(kv => (Ok: CellAddress.TryParse(kv.Key, out var a), Address: a, kv.Key, kv.Value))
            .Where(x => x.Ok && x.Value is not null)
            .GroupBy(x => x.Address.Row)
            .OrderBy(g => g.Key);

        foreach (var group in byRow)
        {
            var row = new Row { RowIndex = (uint)(group.Key + 1) };

            foreach (var entry in group.OrderBy(x => x.Address.Col))
            {
                row.AppendChild(BuildCell(entry.Key, entry.Value!));
            }

            data.AppendChild(row);
        }

        return data;
    }

    private static XlCell BuildCell(string reference, VibeCell source)
    {
        var cell = new XlCell { CellReference = reference };

        if (!string.IsNullOrEmpty(source.F))
        {
            cell.CellFormula = new CellFormula(source.F.TrimStart('='));

            // The cached result travels with the formula so the file shows values before Excel
            // recalculates — and so a viewer that never recalculates still shows something.
            if (!string.IsNullOrEmpty(source.V)) cell.CellValue = new CellValue(source.V);

            if (!IsNumeric(source.V)) cell.DataType = CellValues.String;

            return cell;
        }

        var value = source.V ?? string.Empty;

        if (IsNumeric(value))
        {
            cell.CellValue = new CellValue(value);
            cell.DataType = CellValues.Number;
        }
        else if (bool.TryParse(value, out var flag))
        {
            cell.CellValue = new CellValue(flag ? "1" : "0");
            cell.DataType = CellValues.Boolean;
        }
        else
        {
            // Inline rather than shared: one pass, no string table to keep consistent, and the size
            // difference does not matter at the scale a browser upload implies.
            cell.InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve });
            cell.DataType = CellValues.InlineString;
        }

        return cell;
    }

    private static bool IsNumeric(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
}
