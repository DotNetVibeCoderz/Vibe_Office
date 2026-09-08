// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ExcelNet.Styles;
using ExcelNet.Validation;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace ExcelNet;

/// <summary>
/// Reads and writes a worksheet part.
/// </summary>
/// <remarks>
/// <para>
/// Writing goes through <see cref="XmlWriter"/> rather than building an <see cref="XDocument"/>.
/// A sheet with a million cells would otherwise allocate a million elements and their attributes
/// before a byte is written; streaming keeps the cost proportional to the output and flat in
/// memory. Reading uses <see cref="XmlReader"/> for the same reason.
/// </para>
/// <para>
/// Element order inside <c>w:worksheet</c> is fixed by the schema, and Excel enforces it strictly:
/// <c>sheetPr</c>, <c>dimension</c>, <c>sheetViews</c>, <c>sheetFormatPr</c>, <c>cols</c>,
/// <c>sheetData</c>, <c>sheetProtection</c>, <c>autoFilter</c>, <c>mergeCells</c>,
/// <c>conditionalFormatting</c>, <c>dataValidations</c>, <c>hyperlinks</c>, <c>pageMargins</c>,
/// <c>drawing</c>. Writing <c>mergeCells</c> before <c>sheetData</c> produces a file Excel offers
/// to repair.
/// </para>
/// </remarks>
internal static class SheetXml
{
    private static readonly XNamespace S = Ns.S;
    private static readonly XNamespace R = Ns.R;

    // ---- Reading -------------------------------------------------------------------------------

    internal static void Read(Worksheet sheet, byte[] data, SharedStrings sharedStrings)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreWhitespace = false,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit,
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "sheetPr":
                    ReadSheetProperties(sheet, reader);
                    break;

                case "sheetView":
                    ReadSheetView(sheet, reader);
                    break;

                case "sheetFormatPr":
                    ReadSheetFormat(sheet, reader);
                    break;

                case "col":
                    ReadColumn(sheet, reader);
                    break;

                case "row":
                    ReadRow(sheet, reader, sharedStrings);
                    break;

                case "mergeCell":
                    if (CellRangeReference.TryParse(reader.GetAttribute("ref"), out var merge))
                    {
                        sheet.AddMergeUnchecked(merge);
                    }

                    break;

                case "autoFilter":
                    if (CellRangeReference.TryParse(reader.GetAttribute("ref"), out var filter))
                    {
                        sheet.AutoFilter = filter;
                    }

                    break;

                case "conditionalFormatting":
                    ReadConditionalFormatting(sheet, reader);
                    break;

                case "sheetProtection":
                    ReadSheetProtection(sheet, reader);
                    break;

                case "dataValidation":
                    ReadDataValidation(sheet, reader);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads <c>sheetProtection</c>, undoing the inversion on the way in.
    /// </summary>
    /// <remarks>
    /// Every flag has to be read against its own schema default rather than a shared one: most
    /// default to forbidden, but <c>selectLockedCells</c> and <c>selectUnlockedCells</c> default to
    /// allowed. Reading them all the same way silently flips those two on every round trip.
    /// </remarks>
    private static void ReadSheetProtection(Worksheet sheet, XmlReader reader)
    {
        sheet.Protection = new SheetProtection
        {
            PasswordHash = reader.GetAttribute("password"),
            FormatCells = Allowed("formatCells", defaultAllowed: false),
            FormatColumns = Allowed("formatColumns", defaultAllowed: false),
            FormatRows = Allowed("formatRows", defaultAllowed: false),
            InsertRows = Allowed("insertRows", defaultAllowed: false),
            InsertColumns = Allowed("insertColumns", defaultAllowed: false),
            DeleteRows = Allowed("deleteRows", defaultAllowed: false),
            DeleteColumns = Allowed("deleteColumns", defaultAllowed: false),
            Sort = Allowed("sort", defaultAllowed: false),
            AutoFilter = Allowed("autoFilter", defaultAllowed: false),
            SelectLockedCells = Allowed("selectLockedCells", defaultAllowed: true),
            SelectUnlockedCells = Allowed("selectUnlockedCells", defaultAllowed: true),
        };

        bool Allowed(string name, bool defaultAllowed) =>
            // The attribute says what is forbidden, so it is negated exactly once here too.
            XmlUtil.OoxmlBool(reader.GetAttribute(name)) is { } forbidden ? !forbidden : defaultAllowed;
    }

    /// <summary>Reads one <c>dataValidation</c> rule.</summary>
    private static void ReadDataValidation(Worksheet sheet, XmlReader reader)
    {
        var sqref = reader.GetAttribute("sqref");

        // A rule can cover several disjoint areas, separated by spaces. The model holds one range
        // per rule, so the areas become separate rules — which behaves identically in Excel.
        if (sqref is not { Length: > 0 })
        {
            return;
        }

        var type = reader.GetAttribute("type") switch
        {
            "list" => Validation.ValidationType.List,
            "whole" => Validation.ValidationType.WholeNumber,
            "decimal" => Validation.ValidationType.Decimal,
            "date" => Validation.ValidationType.Date,
            "time" => Validation.ValidationType.Time,
            "textLength" => Validation.ValidationType.TextLength,
            _ => Validation.ValidationType.Custom,
        };

        var op = reader.GetAttribute("operator") switch
        {
            "notBetween" => ValidationOperator.NotBetween,
            "equal" => ValidationOperator.Equal,
            "notEqual" => ValidationOperator.NotEqual,
            "greaterThan" => ValidationOperator.GreaterThan,
            "lessThan" => ValidationOperator.LessThan,
            "greaterThanOrEqual" => ValidationOperator.GreaterThanOrEqual,
            "lessThanOrEqual" => ValidationOperator.LessThanOrEqual,
            _ => ValidationOperator.Between,
        };

        var style = reader.GetAttribute("errorStyle") switch
        {
            "warning" => ValidationErrorStyle.Warning,
            "information" => ValidationErrorStyle.Information,
            _ => ValidationErrorStyle.Stop,
        };

        var allowBlank = XmlUtil.OoxmlBool(reader.GetAttribute("allowBlank")) ?? false;

        // Inverted, like the protection flags: showDropDown="1" hides the arrow.
        var showDropDown = XmlUtil.OoxmlBool(reader.GetAttribute("showDropDown")) is not true;

        var errorTitle = reader.GetAttribute("errorTitle");
        var errorMessage = reader.GetAttribute("error");
        var promptTitle = reader.GetAttribute("promptTitle");
        var promptMessage = reader.GetAttribute("prompt");

        string? formula1 = null;
        string? formula2 = null;

        if (!reader.IsEmptyElement)
        {
            using var subtree = reader.ReadSubtree();

            while (subtree.Read())
            {
                if (subtree.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                switch (subtree.LocalName)
                {
                    case "formula1":
                        formula1 = ReadElementText(subtree);
                        break;

                    case "formula2":
                        formula2 = ReadElementText(subtree);
                        break;
                }
            }
        }

        foreach (var area in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!CellRangeReference.TryParse(area, out var range))
            {
                continue;
            }

            sheet.AddValidation(new DataValidation(range, type)
            {
                Operator = op,
                Formula1 = formula1,
                Formula2 = formula2,
                AllowBlank = allowBlank,
                ShowDropDown = showDropDown,
                ErrorStyle = style,
                ErrorTitle = errorTitle,
                ErrorMessage = errorMessage,
                PromptTitle = promptTitle,
                PromptMessage = promptMessage,
            });
        }
    }

    private static void ReadSheetProperties(Worksheet sheet, XmlReader reader)
    {
        using var subtree = reader.ReadSubtree();

        while (subtree.Read())
        {
            if (subtree is { NodeType: XmlNodeType.Element, LocalName: "tabColor" })
            {
                var rgb = subtree.GetAttribute("rgb");

                if (rgb is { Length: 8 } && OfficeColor.TryParse(rgb[2..], out var color))
                {
                    sheet.TabColor = color;
                }
            }
        }
    }

    private static void ReadSheetView(Worksheet sheet, XmlReader reader)
    {
        sheet.ShowGridLines = reader.GetAttribute("showGridLines") is not "0";
        sheet.IsSelected = reader.GetAttribute("tabSelected") is "1";

        if (reader.IsEmptyElement)
        {
            return;
        }

        using var subtree = reader.ReadSubtree();

        while (subtree.Read())
        {
            if (subtree is not { NodeType: XmlNodeType.Element, LocalName: "pane" })
            {
                continue;
            }

            // xSplit and ySplit are the frozen column and row counts when the pane state is
            // "frozen"; in a split (unfrozen) pane they are twips and mean something else entirely.
            if (subtree.GetAttribute("state") is not ("frozen" or "frozenSplit"))
            {
                continue;
            }

            var columns = ParseInt(subtree.GetAttribute("xSplit"));
            var rows = ParseInt(subtree.GetAttribute("ySplit"));
            sheet.Frozen = new FreezePanes(rows, columns);
        }
    }

    private static void ReadSheetFormat(Worksheet sheet, XmlReader reader)
    {
        if (ParseDouble(reader.GetAttribute("defaultColWidth")) is { } columnWidth and > 0)
        {
            sheet.DefaultColumnWidth = columnWidth;
        }

        if (ParseDouble(reader.GetAttribute("defaultRowHeight")) is { } rowHeight and > 0)
        {
            sheet.DefaultRowHeight = rowHeight;
        }
    }

    private static void ReadColumn(Worksheet sheet, XmlReader reader)
    {
        // A <col> covers a span from min to max, not a single column.
        var min = ParseInt(reader.GetAttribute("min"));
        var max = ParseInt(reader.GetAttribute("max"));
        var width = ParseDouble(reader.GetAttribute("width"));
        var hidden = reader.GetAttribute("hidden") is "1";

        if (min < 1)
        {
            return;
        }

        // A trailing <col> often spans to the last possible column; materialising 16384 entries
        // for it would be pure waste.
        max = Math.Min(max <= 0 ? min : max, min + 1024);

        for (var column = min; column <= max; column++)
        {
            if (width is { } value and > 0)
            {
                sheet.SetColumnWidth(column - 1, value);
            }

            if (hidden)
            {
                sheet.HideColumn(column - 1);
            }
        }
    }

    private static void ReadRow(Worksheet sheet, XmlReader reader, SharedStrings sharedStrings)
    {
        var rowNumber = ParseInt(reader.GetAttribute("r"));
        var height = ParseDouble(reader.GetAttribute("ht"));
        var hidden = reader.GetAttribute("hidden") is "1";

        if (rowNumber >= 1)
        {
            if (height is { } value and > 0 && reader.GetAttribute("customHeight") is "1")
            {
                sheet.SetRowHeight(rowNumber - 1, value);
            }

            if (hidden)
            {
                sheet.HideRow(rowNumber - 1);
            }
        }

        if (reader.IsEmptyElement)
        {
            return;
        }

        using var subtree = reader.ReadSubtree();
        subtree.Read();

        var column = -1;

        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element || subtree.LocalName != "c")
            {
                continue;
            }

            var address = subtree.GetAttribute("r");
            var type = subtree.GetAttribute("t") ?? "n";
            var styleIndex = ParseInt(subtree.GetAttribute("s"));

            CellReference reference;

            if (address is not null && CellReference.TryParse(address, out var parsed))
            {
                reference = parsed;
                column = parsed.Column;
            }
            else
            {
                // A cell may omit its address, in which case it is the next column of the current
                // row. Files written by some generators rely on this.
                column++;
                if (rowNumber < 1)
                {
                    continue;
                }

                reference = new CellReference(rowNumber - 1, Math.Max(0, column));
            }

            string? formula = null;
            string? rawValue = null;
            string? inlineText = null;

            if (!subtree.IsEmptyElement)
            {
                var cellDepth = subtree.Depth;

                while (subtree.Read())
                {
                    if (subtree.NodeType == XmlNodeType.EndElement && subtree.Depth == cellDepth)
                    {
                        break;
                    }

                    if (subtree.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    switch (subtree.LocalName)
                    {
                        case "f":
                            formula = ReadElementText(subtree);
                            break;

                        case "v":
                            rawValue = ReadElementText(subtree);
                            break;

                        case "is":
                            inlineText = ReadInlineString(subtree);
                            break;
                    }
                }
            }

            var value = InterpretValue(type, rawValue, inlineText, styleIndex, sharedStrings,
                sheet.Workbook.Styles);

            sheet.PutCellData(reference, new CellData
            {
                Value = value,
                Formula = string.IsNullOrWhiteSpace(formula) ? null : formula,
                StyleIndex = styleIndex,
            });
        }
    }

    private static string ReadInlineString(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        var depth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader is { NodeType: XmlNodeType.Element, LocalName: "t" })
            {
                builder.Append(ReadElementText(reader));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads an element's text and leaves the reader on that element's end tag.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlReader.ReadElementContentAsString()"/> would be shorter and is the trap: it
    /// advances <em>past</em> the end tag onto the next sibling, so the caller's next
    /// <see cref="XmlReader.Read"/> skips that sibling entirely. In a cell that sibling is the
    /// <c>&lt;v&gt;</c> after an <c>&lt;f&gt;</c> — and every formula's cached result silently
    /// reads back as empty.
    /// </remarks>
    private static string ReadElementText(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        var depth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or
                XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
            {
                builder.Append(reader.Value);
            }
        }

        return builder.ToString();
    }

    private static CellValue InterpretValue(string type, string? raw, string? inline, int styleIndex,
        SharedStrings sharedStrings, Stylesheet styles)
    {
        switch (type)
        {
            case "s":
                // A shared-string cell holds an index into the string table, not the text.
                return int.TryParse(raw, out var index)
                    ? CellValue.FromText(sharedStrings.Get(index))
                    : CellValue.Empty;

            case "inlineStr":
                return inline is null ? CellValue.Empty : CellValue.FromText(inline);

            case "str":
                // A formula's cached string result.
                return raw is null ? CellValue.Empty : CellValue.FromText(raw);

            case "b":
                return CellValue.FromBoolean(raw is "1");

            case "e":
                return raw is null ? CellValue.Empty : CellValue.FromError(raw);

            case "d":
                // ISO 8601 dates, which the transitional schema allows but Excel rarely writes.
                return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var isoDate)
                    ? CellValue.FromDateTime(isoDate)
                    : CellValue.Empty;

            default:
            {
                if (raw is null || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var number))
                {
                    return CellValue.Empty;
                }

                // The file has no date type: a date is a number whose format says so. Consulting
                // the style is the only way to tell 45292 from the day it stands for.
                return styles.IsDateStyle(styleIndex)
                    ? CellValue.FromDateTime(CellValue.FromSerial(number))
                    : CellValue.FromNumber(number);
            }
        }
    }

    private static void ReadConditionalFormatting(Worksheet sheet, XmlReader reader)
    {
        if (!CellRangeReference.TryParse(reader.GetAttribute("sqref"), out var range) ||
            reader.IsEmptyElement)
        {
            return;
        }

        using var subtree = reader.ReadSubtree();
        subtree.Read();

        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element || subtree.LocalName != "cfRule")
            {
                continue;
            }

            var type = subtree.GetAttribute("type") ?? "cellIs";
            var comparison = subtree.GetAttribute("operator");
            var priority = ParseInt(subtree.GetAttribute("priority"));

            var formulas = new List<string>();

            if (!subtree.IsEmptyElement)
            {
                var ruleDepth = subtree.Depth;

                while (subtree.Read())
                {
                    if (subtree.NodeType == XmlNodeType.EndElement && subtree.Depth == ruleDepth)
                    {
                        break;
                    }

                    if (subtree is { NodeType: XmlNodeType.Element, LocalName: "formula" })
                    {
                        formulas.Add(ReadElementText(subtree));
                    }
                }
            }

            // The differential format a rule applies lives in the stylesheet's dxfs table, which
            // this library does not round-trip; the rule is preserved with no style so the
            // condition survives even though the formatting has to be re-specified.
            sheet.AddConditionalRuleUnchecked(
                new ConditionalRule(range, type, comparison, formulas, null, priority));
        }
    }

    private static int ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static double? ParseDouble(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // ---- Writing -------------------------------------------------------------------------------

    internal static byte[] Write(Worksheet sheet, SharedStrings sharedStrings)
    {
        using var buffer = new MemoryStream(16 * 1024);

        var settings = new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            Indent = false,
            OmitXmlDeclaration = false,
            CloseOutput = false,
        };

        using (var writer = XmlWriter.Create(buffer, settings))
        {
            writer.WriteStartDocument(standalone: true);
            writer.WriteStartElement("worksheet", S.NamespaceName);
            writer.WriteAttributeString("xmlns", "r", null, R.NamespaceName);

            WriteSheetProperties(writer, sheet);
            WriteDimension(writer, sheet);
            WriteSheetViews(writer, sheet);
            WriteSheetFormat(writer, sheet);
            WriteColumns(writer, sheet);
            WriteSheetData(writer, sheet, sharedStrings);
            WriteSheetProtection(writer, sheet);
            WriteAutoFilter(writer, sheet);
            WriteMerges(writer, sheet);
            WriteConditionalFormatting(writer, sheet);
            WriteDataValidations(writer, sheet);
            WriteHyperlinks(writer, sheet);
            WritePageMargins(writer);
            WriteDrawing(writer, sheet);

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return buffer.ToArray();
    }

    private static void WriteSheetProperties(XmlWriter writer, Worksheet sheet)
    {
        if (sheet.TabColor is not { } color)
        {
            return;
        }

        writer.WriteStartElement("sheetPr", S.NamespaceName);
        writer.WriteStartElement("tabColor", S.NamespaceName);
        writer.WriteAttributeString("rgb", "FF" + color.ToHex());
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteDimension(XmlWriter writer, Worksheet sheet)
    {
        // The dimension is a hint Excel uses to size its scrollbars. It is allowed to be wrong,
        // but a wrong one makes the sheet look empty until the user scrolls.
        var used = sheet.UsedRange;

        writer.WriteStartElement("dimension", S.NamespaceName);
        writer.WriteAttributeString("ref", used?.A1 ?? "A1");
        writer.WriteEndElement();
    }

    private static void WriteSheetViews(XmlWriter writer, Worksheet sheet)
    {
        writer.WriteStartElement("sheetViews", S.NamespaceName);
        writer.WriteStartElement("sheetView", S.NamespaceName);

        if (!sheet.ShowGridLines)
        {
            writer.WriteAttributeString("showGridLines", "0");
        }

        if (sheet.IsSelected)
        {
            writer.WriteAttributeString("tabSelected", "1");
        }

        writer.WriteAttributeString("workbookViewId", "0");

        if (!sheet.Frozen.IsNone)
        {
            var topLeft = new CellReference(sheet.Frozen.Rows, sheet.Frozen.Columns);

            writer.WriteStartElement("pane", S.NamespaceName);

            if (sheet.Frozen.Columns > 0)
            {
                writer.WriteAttributeString("xSplit",
                    sheet.Frozen.Columns.ToString(CultureInfo.InvariantCulture));
            }

            if (sheet.Frozen.Rows > 0)
            {
                writer.WriteAttributeString("ySplit",
                    sheet.Frozen.Rows.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteAttributeString("topLeftCell", topLeft.A1);

            // activePane names which pane has the cursor. "bottomRight" is right whenever both
            // splits are set; with only a row split it must be "bottomLeft" or Excel repairs it.
            writer.WriteAttributeString("activePane",
                sheet.Frozen.Columns > 0 && sheet.Frozen.Rows > 0 ? "bottomRight"
                : sheet.Frozen.Columns > 0 ? "topRight"
                : "bottomLeft");

            writer.WriteAttributeString("state", "frozen");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteSheetFormat(XmlWriter writer, Worksheet sheet)
    {
        writer.WriteStartElement("sheetFormatPr", S.NamespaceName);
        writer.WriteAttributeString("defaultRowHeight",
            sheet.DefaultRowHeight.ToString("0.##", CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private static void WriteColumns(XmlWriter writer, Worksheet sheet)
    {
        if (sheet.ColumnWidths.Count == 0 && sheet.HiddenColumns.Count == 0)
        {
            return;
        }

        var columns = sheet.ColumnWidths.Keys
            .Concat(sheet.HiddenColumns)
            .Distinct()
            .Order()
            .ToList();

        writer.WriteStartElement("cols", S.NamespaceName);

        foreach (var column in columns)
        {
            writer.WriteStartElement("col", S.NamespaceName);
            writer.WriteAttributeString("min", (column + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("max", (column + 1).ToString(CultureInfo.InvariantCulture));

            if (sheet.ColumnWidths.TryGetValue(column, out var width))
            {
                writer.WriteAttributeString("width", width.ToString("0.##", CultureInfo.InvariantCulture));
                // customWidth is what makes Excel honour the width rather than autofitting.
                writer.WriteAttributeString("customWidth", "1");
            }

            if (sheet.HiddenColumns.Contains(column))
            {
                writer.WriteAttributeString("hidden", "1");
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteSheetData(XmlWriter writer, Worksheet sheet, SharedStrings sharedStrings)
    {
        writer.WriteStartElement("sheetData", S.NamespaceName);

        // Cells must be written in row-major order and rows in ascending order; Excel does not
        // sort them and a sheet with rows out of order shows blanks.
        var byRow = sheet.RawCells
            .GroupBy(pair => pair.Key.Row)
            .OrderBy(group => group.Key);

        var rowsWithFormatOnly = sheet.RowHeights.Keys
            .Concat(sheet.HiddenRows)
            .Distinct()
            .ToHashSet();

        foreach (var group in byRow)
        {
            rowsWithFormatOnly.Remove(group.Key);
            WriteRow(writer, sheet, sharedStrings, group.Key,
                group.OrderBy(pair => pair.Key.Column));
        }

        // A row that carries only a height or a hidden flag still needs an element.
        foreach (var row in rowsWithFormatOnly.Order())
        {
            WriteRow(writer, sheet, sharedStrings, row, []);
        }

        writer.WriteEndElement();
    }

    private static void WriteRow(XmlWriter writer, Worksheet sheet, SharedStrings sharedStrings,
        int row, IEnumerable<KeyValuePair<CellReference, CellData>> cells)
    {
        writer.WriteStartElement("row", S.NamespaceName);
        writer.WriteAttributeString("r", (row + 1).ToString(CultureInfo.InvariantCulture));

        if (sheet.RowHeights.TryGetValue(row, out var height))
        {
            writer.WriteAttributeString("ht", height.ToString("0.##", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customHeight", "1");
        }

        if (sheet.HiddenRows.Contains(row))
        {
            writer.WriteAttributeString("hidden", "1");
        }

        foreach (var (reference, data) in cells)
        {
            WriteCell(writer, reference, data, sharedStrings);
        }

        writer.WriteEndElement();
    }

    private static void WriteCell(XmlWriter writer, CellReference reference, CellData data,
        SharedStrings sharedStrings)
    {
        // A cell with no value, no formula and the default style contributes nothing.
        if (data.Value.IsEmpty && data.Formula is null && data.StyleIndex == 0)
        {
            return;
        }

        writer.WriteStartElement("c", S.NamespaceName);
        writer.WriteAttributeString("r", reference.A1);

        if (data.StyleIndex != 0)
        {
            writer.WriteAttributeString("s", data.StyleIndex.ToString(CultureInfo.InvariantCulture));
        }

        var value = data.Value;

        switch (value.ValueType)
        {
            case CellValueType.Text when data.Formula is null:
            {
                writer.WriteAttributeString("t", "s");
                var index = sharedStrings.Add(value.AsText());
                writer.WriteElementString("v", S.NamespaceName,
                    index.ToString(CultureInfo.InvariantCulture));
                break;
            }

            case CellValueType.Text:
                // A formula whose cached result is text uses type "str", not the shared table.
                writer.WriteAttributeString("t", "str");
                writer.WriteElementString("f", S.NamespaceName, data.Formula!);
                writer.WriteElementString("v", S.NamespaceName, value.AsText());
                break;

            case CellValueType.Boolean:
                writer.WriteAttributeString("t", "b");
                WriteFormula(writer, data.Formula);
                writer.WriteElementString("v", S.NamespaceName, value.AsBoolean() ? "1" : "0");
                break;

            case CellValueType.Error:
                writer.WriteAttributeString("t", "e");
                WriteFormula(writer, data.Formula);
                writer.WriteElementString("v", S.NamespaceName, value.AsText());
                break;

            case CellValueType.Number:
            case CellValueType.DateTime:
                WriteFormula(writer, data.Formula);
                writer.WriteElementString("v", S.NamespaceName,
                    value.SerialNumber.ToString("R", CultureInfo.InvariantCulture));
                break;

            default:
                WriteFormula(writer, data.Formula);
                break;
        }

        writer.WriteEndElement();
    }

    private static void WriteFormula(XmlWriter writer, string? formula)
    {
        if (formula is not null)
        {
            writer.WriteElementString("f", S.NamespaceName, formula);
        }
    }

    private static void WriteAutoFilter(XmlWriter writer, Worksheet sheet)
    {
        if (sheet.AutoFilter is not { } filter)
        {
            return;
        }

        writer.WriteStartElement("autoFilter", S.NamespaceName);
        writer.WriteAttributeString("ref", filter.A1);
        writer.WriteEndElement();
    }

    private static void WriteMerges(XmlWriter writer, Worksheet sheet)
    {
        if (sheet.MergedRanges.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("mergeCells", S.NamespaceName);
        writer.WriteAttributeString("count",
            sheet.MergedRanges.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var range in sheet.MergedRanges)
        {
            writer.WriteStartElement("mergeCell", S.NamespaceName);
            writer.WriteAttributeString("ref", range.A1);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteConditionalFormatting(XmlWriter writer, Worksheet sheet)
    {
        foreach (var rule in sheet.ConditionalRules)
        {
            writer.WriteStartElement("conditionalFormatting", S.NamespaceName);
            writer.WriteAttributeString("sqref", rule.Range.A1);

            writer.WriteStartElement("cfRule", S.NamespaceName);
            writer.WriteAttributeString("type", rule.Type);
            writer.WriteAttributeString("priority", rule.Priority.ToString(CultureInfo.InvariantCulture));

            if (rule.Operator is not null)
            {
                writer.WriteAttributeString("operator", rule.Operator);
            }

            foreach (var formula in rule.Formulas)
            {
                writer.WriteElementString("formula", S.NamespaceName, formula);
            }

            if (rule.ScaleColors is { Count: >= 2 } colors)
            {
                writer.WriteStartElement("colorScale", S.NamespaceName);

                // The cfvo entries and the colours must be the same count and in the same order:
                // min, (mid), max.
                writer.WriteStartElement("cfvo", S.NamespaceName);
                writer.WriteAttributeString("type", "min");
                writer.WriteEndElement();

                if (colors.Count == 3)
                {
                    writer.WriteStartElement("cfvo", S.NamespaceName);
                    writer.WriteAttributeString("type", "percentile");
                    writer.WriteAttributeString("val", "50");
                    writer.WriteEndElement();
                }

                writer.WriteStartElement("cfvo", S.NamespaceName);
                writer.WriteAttributeString("type", "max");
                writer.WriteEndElement();

                foreach (var color in colors)
                {
                    writer.WriteStartElement("color", S.NamespaceName);
                    writer.WriteAttributeString("rgb", "FF" + color.ToHex());
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            if (rule.BarColor is { } barColor)
            {
                writer.WriteStartElement("dataBar", S.NamespaceName);

                writer.WriteStartElement("cfvo", S.NamespaceName);
                writer.WriteAttributeString("type", "min");
                writer.WriteEndElement();

                writer.WriteStartElement("cfvo", S.NamespaceName);
                writer.WriteAttributeString("type", "max");
                writer.WriteEndElement();

                writer.WriteStartElement("color", S.NamespaceName);
                writer.WriteAttributeString("rgb", "FF" + barColor.ToHex());
                writer.WriteEndElement();

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }
    }

    private static void WriteHyperlinks(XmlWriter writer, Worksheet sheet)
    {
        var links = sheet.RawCells
            .Where(pair => pair.Value.HyperlinkTarget is not null)
            .OrderBy(pair => pair.Key)
            .ToList();

        if (links.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("hyperlinks", S.NamespaceName);

        foreach (var (reference, data) in links)
        {
            var relationshipId = sheet.Workbook.EnsureHyperlink(sheet, data.HyperlinkTarget!);

            writer.WriteStartElement("hyperlink", S.NamespaceName);
            writer.WriteAttributeString("ref", reference.A1);
            writer.WriteAttributeString("id", R.NamespaceName, relationshipId);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    /// <summary>
    /// Writes the <c>drawing</c> reference, which must be the worksheet's last child.
    /// </summary>
    /// <remarks>
    /// The element carries only a relationship id — the anchors and the chart itself live in the
    /// drawing part. Placing it before <c>pageMargins</c> puts it out of schema order and Excel
    /// offers to repair the file.
    /// </remarks>
    private static void WriteDrawing(XmlWriter writer, Worksheet sheet)
    {
        var relationship = sheet.Part
            .RelationshipsByType(RelationshipTypes.Drawing)
            .FirstOrDefault();

        if (relationship is null)
        {
            return;
        }

        writer.WriteStartElement("drawing", S.NamespaceName);
        writer.WriteAttributeString("id", Ns.R.NamespaceName, relationship.Id);
        writer.WriteEndElement();
    }

    /// <summary>
    /// Writes <c>sheetProtection</c>, which sits immediately after <c>sheetData</c>.
    /// </summary>
    /// <remarks>
    /// The attributes are inverted from how the API reads: <c>formatCells="0"</c> means formatting
    /// is <em>allowed</em>. Writing them the intuitive way round produces a sheet that permits
    /// exactly what the caller asked to forbid.
    /// </remarks>
    private static void WriteSheetProtection(XmlWriter writer, Worksheet sheet)
    {
        if (sheet.Protection is not { Enabled: true } protection)
        {
            return;
        }

        writer.WriteStartElement("sheetProtection", S.NamespaceName);

        if (protection.PasswordHash is { Length: > 0 } hash)
        {
            writer.WriteAttributeString("password", hash);
        }

        writer.WriteAttributeString("sheet", "1");
        writer.WriteAttributeString("objects", "1");
        writer.WriteAttributeString("scenarios", "1");

        // "1" forbids. The API says what is allowed, so each flag is negated here exactly once.
        Flag("formatCells", protection.FormatCells);
        Flag("formatColumns", protection.FormatColumns);
        Flag("formatRows", protection.FormatRows);
        Flag("insertRows", protection.InsertRows);
        Flag("insertColumns", protection.InsertColumns);
        Flag("deleteRows", protection.DeleteRows);
        Flag("deleteColumns", protection.DeleteColumns);
        Flag("sort", protection.Sort);
        Flag("autoFilter", protection.AutoFilter);
        Flag("selectLockedCells", protection.SelectLockedCells);
        Flag("selectUnlockedCells", protection.SelectUnlockedCells);

        writer.WriteEndElement();

        void Flag(string name, bool allowed)
        {
            // Every flag is written out rather than relying on the schema default, because the
            // defaults are not uniform: most of them default to "forbidden", but selectLockedCells
            // and selectUnlockedCells default to "allowed". Omitting an attribute therefore means
            // the opposite thing depending on which attribute it is, and a sheet that was asked to
            // stop people even clicking a locked cell would happily let them. A few dozen bytes buy
            // the whole class of mistake away.
            writer.WriteAttributeString(name, allowed ? "0" : "1");
        }
    }

    /// <summary>Writes <c>dataValidations</c>, which follows <c>conditionalFormatting</c>.</summary>
    private static void WriteDataValidations(XmlWriter writer, Worksheet sheet)
    {
        if (sheet.Validations.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("dataValidations", S.NamespaceName);
        writer.WriteAttributeString("count",
            sheet.Validations.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var validation in sheet.Validations)
        {
            writer.WriteStartElement("dataValidation", S.NamespaceName);
            writer.WriteAttributeString("type", validation.TypeAttribute);

            // The operator is meaningless for a list and Excel rejects the pair.
            if (validation.Type != Validation.ValidationType.List &&
                validation.Type != Validation.ValidationType.Custom)
            {
                writer.WriteAttributeString("operator", validation.OperatorAttribute);
            }

            writer.WriteAttributeString("allowBlank", validation.AllowBlank ? "1" : "0");

            // Inverted, like the protection flags: showDropDown="1" *hides* the arrow.
            if (validation.Type == Validation.ValidationType.List && !validation.ShowDropDown)
            {
                writer.WriteAttributeString("showDropDown", "1");
            }

            if (validation.ErrorStyle != ValidationErrorStyle.Stop)
            {
                writer.WriteAttributeString("errorStyle", validation.ErrorStyleAttribute);
            }

            if (validation.ErrorTitle is { Length: > 0 } errorTitle)
            {
                writer.WriteAttributeString("errorTitle", errorTitle);
            }

            if (validation.ErrorMessage is { Length: > 0 } error)
            {
                writer.WriteAttributeString("showErrorMessage", "1");
                writer.WriteAttributeString("error", error);
            }

            if (validation.PromptTitle is { Length: > 0 } promptTitle)
            {
                writer.WriteAttributeString("promptTitle", promptTitle);
            }

            if (validation.PromptMessage is { Length: > 0 } prompt)
            {
                writer.WriteAttributeString("showInputMessage", "1");
                writer.WriteAttributeString("prompt", prompt);
            }

            writer.WriteAttributeString("sqref", validation.Range.A1);

            if (validation.Formula1 is { Length: > 0 } first)
            {
                writer.WriteElementString("formula1", S.NamespaceName, first);
            }

            if (validation.Formula2 is { Length: > 0 } second)
            {
                writer.WriteElementString("formula2", S.NamespaceName, second);
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WritePageMargins(XmlWriter writer)
    {
        writer.WriteStartElement("pageMargins", S.NamespaceName);
        writer.WriteAttributeString("left", "0.7");
        writer.WriteAttributeString("right", "0.7");
        writer.WriteAttributeString("top", "0.75");
        writer.WriteAttributeString("bottom", "0.75");
        writer.WriteAttributeString("header", "0.3");
        writer.WriteAttributeString("footer", "0.3");
        writer.WriteEndElement();
    }
}
