// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using ExcelNet.Styles;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Html;

namespace ExcelNet.Io;

/// <summary>How a workbook is written out as HTML.</summary>
public sealed class ExcelHtmlOptions
{
    /// <summary>A complete page, or only the tables, for a page that has its own head.</summary>
    public bool FullDocument { get; set; } = true;

    /// <summary>The page title. Defaults to the workbook's title, then its first sheet's name.</summary>
    public string? Title { get; set; }

    /// <summary>Light borders on every cell, as Excel shows its gridlines. A sheet that hides them keeps them hidden.</summary>
    public bool ShowGridLines { get; set; } = true;
}

/// <summary>How HTML tables are read into a workbook.</summary>
public sealed class ExcelHtmlImportOptions
{
    /// <summary>
    /// Converts text that looks numeric, boolean, a percentage or a date into typed cells — the
    /// same rules as CSV, so an identifier with a leading zero stays text.
    /// </summary>
    public bool InferTypes { get; set; } = true;

    /// <summary>The culture numbers and dates are read in.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Carries bold, italic, underline, strikethrough, colours and alignment from the markup —
    /// <c>th</c>, <c>b</c> and its relatives, and inline CSS.
    /// </summary>
    public bool KeepFormatting { get; set; } = true;

    /// <summary>Sizes each column to its content, as the table had no widths to give.</summary>
    public bool AutoFitColumns { get; set; } = true;
}

/// <summary>
/// Writes worksheets as HTML tables and reads HTML tables into worksheets.
/// </summary>
/// <remarks>
/// <para>
/// Out, every sheet is one <c>table</c> with the sheet's name as its <c>caption</c>, and each value
/// as Excel would display it — through its number format, so <c>0.125</c> formatted as a percentage
/// reads <c>12.5%</c>. Merged cells become <c>colspan</c>/<c>rowspan</c>; font, fill, alignment,
/// borders and column widths become CSS; links survive when their scheme is safe to serve.
/// Formulas are written as their results: a page has no way to recalculate.
/// </para>
/// <para>
/// In, every <c>table</c> becomes a sheet named after its caption. Cells are placed by the HTML
/// table algorithm — a <c>rowspan</c> pushes the cells of the rows below it to the right — and a
/// span becomes a merge. Text is typed as it is for CSV. The import reads its own export back, so
/// a workbook taken through HTML keeps its sheet names, merges, values and the visible formatting.
/// </para>
/// </remarks>
public static class HtmlIo
{
    private const string Stylesheet =
        "body{font-family:Calibri,Arial,sans-serif;margin:1em}" +
        "table{border-collapse:collapse;margin:0 0 2em;font-size:11pt}" +
        "caption{text-align:left;font-weight:bold;font-size:13pt;padding:4px 0}" +
        "td{padding:2px 4px;vertical-align:bottom;white-space:nowrap}";

    private const string GridLines = "table.grid td{border:1px solid #d9d9d9}";

    /// <summary>Writes every sheet of a workbook as HTML.</summary>
    public static string ExportText(Workbook workbook, ExcelHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        return Page([.. workbook], options ?? new ExcelHtmlOptions(),
            workbook.Properties.Title ?? workbook.FirstOrDefault()?.Name);
    }

    /// <summary>Writes one sheet as HTML.</summary>
    public static string ExportText(Worksheet sheet, ExcelHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        return Page([sheet], options ?? new ExcelHtmlOptions(), sheet.Name);
    }

    /// <summary>Writes every sheet of a workbook to an HTML file.</summary>
    public static void Export(Workbook workbook, string path, ExcelHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        File.WriteAllText(path, ExportText(workbook, options), new UTF8Encoding(false));
    }

    /// <summary>Reads every table in an HTML file into sheets of the workbook.</summary>
    /// <returns>The sheets created, one per table.</returns>
    public static IReadOnlyList<Worksheet> Import(Workbook workbook, string path, ExcelHtmlImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        return ImportText(workbook, File.ReadAllText(path), options);
    }

    /// <summary>Reads every table in HTML into sheets of the workbook.</summary>
    /// <returns>The sheets created, one per table.</returns>
    public static IReadOnlyList<Worksheet> ImportText(Workbook workbook, string html, ExcelHtmlImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(html);

        options ??= new ExcelHtmlImportOptions();

        var root = HtmlParser.Parse(html);
        var sheets = new List<Worksheet>();

        // Only outermost tables: a table inside a cell is that cell's content.
        foreach (var table in root.Descendants("table").Where(t => !Ancestors(t).Any(a => a.Name == "table")))
        {
            sheets.Add(ReadTable(workbook, table, sheets.Count + 1, options));
        }

        return sheets;
    }

    // ---- Writing -------------------------------------------------------------------------------

    private static string Page(IReadOnlyList<Worksheet> sheets, ExcelHtmlOptions options, string? defaultTitle)
    {
        var body = new StringBuilder();

        foreach (var sheet in sheets)
        {
            WriteSheet(body, sheet, options);
        }

        if (!options.FullDocument)
        {
            return body.ToString();
        }

        var title = options.Title ?? defaultTitle ?? "Workbook";

        return new StringBuilder()
            .Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<title>").Append(Escape(title)).Append("</title>\n")
            .Append("<style>").Append(Stylesheet).Append(options.ShowGridLines ? GridLines : string.Empty).Append("</style>\n")
            .Append("</head>\n<body>\n").Append(body).Append("</body>\n</html>\n")
            .ToString();
    }

    private static void WriteSheet(StringBuilder html, Worksheet sheet, ExcelHtmlOptions options)
    {
        var grid = options.ShowGridLines && sheet.ShowGridLines;

        html.Append(grid ? "<table class=\"grid\">\n" : "<table>\n");
        html.Append("<caption>").Append(Escape(sheet.Name)).Append("</caption>\n");

        if (Bounds(sheet) is not { } bounds)
        {
            html.Append("</table>\n");
            return;
        }

        // The anchor of each merge carries its span; the cells it covers are not written at all.
        var anchors = new Dictionary<CellReference, CellRangeReference>();
        var covered = new HashSet<CellReference>();

        foreach (var merge in sheet.MergedRanges)
        {
            anchors[merge.Start] = merge;

            for (var row = merge.Start.Row; row <= merge.End.Row; row++)
            {
                for (var column = merge.Start.Column; column <= merge.End.Column; column++)
                {
                    if (row != merge.Start.Row || column != merge.Start.Column)
                    {
                        covered.Add(new CellReference(row, column));
                    }
                }
            }
        }

        html.Append("<colgroup>");

        for (var column = bounds.Start.Column; column <= bounds.End.Column; column++)
        {
            // Excel's width is in characters of the digit zero: seven pixels each, plus padding.
            var pixels = Math.Round(sheet.GetColumnWidth(column) * 7 + 5);
            html.Append("<col style=\"width:").Append(pixels.ToString(CultureInfo.InvariantCulture)).Append("px\">");
        }

        html.Append("</colgroup>\n");

        for (var row = bounds.Start.Row; row <= bounds.End.Row; row++)
        {
            html.Append("<tr>");

            for (var column = bounds.Start.Column; column <= bounds.End.Column; column++)
            {
                var reference = new CellReference(row, column);

                if (covered.Contains(reference))
                {
                    continue;
                }

                html.Append("<td");

                if (anchors.TryGetValue(reference, out var merge))
                {
                    if (merge.ColumnCount > 1)
                    {
                        html.Append(" colspan=\"").Append(merge.ColumnCount).Append('"');
                    }

                    if (merge.RowCount > 1)
                    {
                        html.Append(" rowspan=\"").Append(merge.RowCount).Append('"');
                    }
                }

                var cell = sheet[reference];
                var value = cell.Value;
                var style = sheet.GetStyle(reference);
                var css = Css(style, value);

                if (css.Length > 0)
                {
                    html.Append(" style=\"").Append(css).Append('"');
                }

                html.Append('>');

                if (!value.IsEmpty)
                {
                    var text = Escape(ExcelToPdf.Format(value, style.NumberFormat))
                        .Replace("\n", "<br>", StringComparison.Ordinal);

                    html.Append(cell.Hyperlink is { } link && IsSafeLink(link)
                        ? $"<a href=\"{Escape(link)}\">{text}</a>"
                        : text);
                }

                html.Append("</td>");
            }

            html.Append("</tr>\n");
        }

        html.Append("</table>\n");
    }

    /// <summary>The used range, grown to take in any merge that reaches past it.</summary>
    private static CellRangeReference? Bounds(Worksheet sheet)
    {
        var used = sheet.UsedRange;

        foreach (var merge in sheet.MergedRanges)
        {
            used = used is not { } range
                ? merge
                : new CellRangeReference(
                    new CellReference(Math.Min(range.Start.Row, merge.Start.Row), Math.Min(range.Start.Column, merge.Start.Column)),
                    new CellReference(Math.Max(range.End.Row, merge.End.Row), Math.Max(range.End.Column, merge.End.Column)));
        }

        return used;
    }

    private static string Css(CellStyle style, CellValue value)
    {
        var css = new StringBuilder();
        var font = style.Font;

        if (font.Bold)
        {
            css.Append("font-weight:bold;");
        }

        if (font.Italic)
        {
            css.Append("font-style:italic;");
        }

        if (font.Underline || font.Strike)
        {
            css.Append("text-decoration:").Append(font.Underline && font.Strike ? "underline line-through"
                : font.Underline ? "underline" : "line-through").Append(';');
        }

        if (Hex(font.Color) is { } color)
        {
            css.Append("color:").Append(color).Append(';');
        }

        if (style.Pattern != FillPattern.None && Hex(style.BackgroundColor) is { } fill)
        {
            css.Append("background-color:").Append(fill).Append(';');
        }

        if (Math.Abs(font.SizePoints - 11) > 0.01)
        {
            css.Append("font-size:").Append(font.SizePoints.ToString(CultureInfo.InvariantCulture)).Append("pt;");
        }

        if (!string.Equals(font.Name, "Calibri", StringComparison.OrdinalIgnoreCase))
        {
            css.Append("font-family:'").Append(font.Name.Replace("'", string.Empty, StringComparison.Ordinal)).Append("';");
        }

        // General alignment is Excel's own rule: numbers and dates right, booleans centred, text left.
        var align = style.Horizontal switch
        {
            HorizontalAlignment.Left => "left",
            HorizontalAlignment.Center or HorizontalAlignment.CenterContinuous => "center",
            HorizontalAlignment.Right => "right",
            HorizontalAlignment.Justify or HorizontalAlignment.Distributed => "justify",
            _ => value.ValueType switch
            {
                CellValueType.Number or CellValueType.DateTime => "right",
                CellValueType.Boolean => "center",
                _ => null,
            },
        };

        if (align is not null)
        {
            css.Append("text-align:").Append(align).Append(';');
        }

        if (style.Vertical != VerticalAlignment.Bottom)
        {
            css.Append("vertical-align:").Append(style.Vertical switch
            {
                VerticalAlignment.Top => "top",
                VerticalAlignment.Center => "middle",
                _ => "bottom",
            }).Append(';');
        }

        if (style.WrapText)
        {
            css.Append("white-space:pre-wrap;");
        }

        var border = style.Border;
        Side("left", border.Left);
        Side("right", border.Right);
        Side("top", border.Top);
        Side("bottom", border.Bottom);

        return css.ToString();

        void Side(string side, BorderLineStyle line)
        {
            if (line == BorderLineStyle.None)
            {
                return;
            }

            var (width, kind) = line switch
            {
                BorderLineStyle.Medium => (2, "solid"),
                BorderLineStyle.Thick => (3, "solid"),
                BorderLineStyle.Double => (3, "double"),
                BorderLineStyle.Hair or BorderLineStyle.Dotted => (1, "dotted"),
                BorderLineStyle.Dashed or BorderLineStyle.DashDot or BorderLineStyle.DashDotDot => (1, "dashed"),
                _ => (1, "solid"),
            };

            css.Append("border-").Append(side).Append(':').Append(width).Append("px ").Append(kind).Append(' ')
                .Append(Hex(border.Color) ?? "#000000").Append(';');
        }
    }

    /// <summary>A colour as CSS, or nothing for a theme or automatic colour this cannot resolve.</summary>
    private static string? Hex(OfficeColor? color) =>
        color is { IsAutomatic: false, IsThemeColor: false } c ? c.ToCssHex() : null;

    /// <summary>
    /// Links whose scheme is safe to serve. A <c>javascript:</c> link is inert in a workbook and an
    /// injection in a page.
    /// </summary>
    private static bool IsSafeLink(string link)
    {
        var colon = link.IndexOf(':', StringComparison.Ordinal);
        var slash = link.IndexOfAny(['/', '?', '#']);

        // No scheme at all — a relative link or an anchor — is safe.
        if (colon < 0 || (slash >= 0 && slash < colon))
        {
            return true;
        }

        return link[..colon].ToLowerInvariant() is "http" or "https" or "mailto" or "tel" or "ftp";
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 16);

        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    // ---- Reading -------------------------------------------------------------------------------

    private static IEnumerable<HtmlNode> Ancestors(HtmlNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            yield return parent;
        }
    }

    private static Worksheet ReadTable(Workbook workbook, HtmlNode table, int number, ExcelHtmlImportOptions options)
    {
        var caption = table.Elements("caption").FirstOrDefault() is { } c ? Collapse(c.InnerText).Trim() : string.Empty;
        var name = SheetName(caption, number);

        // An empty sheet of the same name is filled rather than shadowed by "Sheet1 (2)" — which is
        // what reading a workbook's own export into a new workbook would otherwise do. A sheet
        // that has content is never overwritten.
        var sheet = workbook.Find(name) is { UsedRange: null, MergedRanges.Count: 0 } empty
            ? empty
            : workbook.AddSheetUnique(name);

        // The rows in document order, whether or not they sit in thead/tbody/tfoot.
        var rows = table.Elements().SelectMany(e => e.Name == "tr" ? [e] :
            e.Name is "thead" or "tbody" or "tfoot" ? e.Elements("tr") : []).ToList();

        // Which cells a rowspan from above already occupies.
        var taken = new HashSet<(int Row, int Column)>();
        var widths = new Dictionary<int, double>();

        for (var row = 0; row < rows.Count; row++)
        {
            var column = 0;
            var rowStyle = CssStyle.OfAttributes(rows[row]);

            foreach (var td in rows[row].Elements().Where(e => e.Name is "td" or "th"))
            {
                while (taken.Contains((row, column)))
                {
                    column++;
                }

                var colspan = Span(td.Attribute("colspan"));
                var rowspan = Span(td.Attribute("rowspan"));

                for (var r = row; r < row + rowspan; r++)
                {
                    for (var k = column; k < column + colspan; k++)
                    {
                        taken.Add((r, k));
                    }
                }

                WriteCell(sheet, new CellReference(row, column), td, rowStyle, options);

                if (colspan > 1 || rowspan > 1)
                {
                    sheet.MergeCells(new CellRangeReference(new CellReference(row, column),
                        new CellReference(row + rowspan - 1, column + colspan - 1)));
                }

                column += colspan;
            }
        }

        // Widths the table states win; otherwise columns are sized to what is in them.
        var cols = table.Descendants("col").ToList();

        for (var i = 0; i < cols.Count; i++)
        {
            if (CssStyle.ParseLength(Width(cols[i]) ?? string.Empty) is { } length)
            {
                widths[i] = Math.Max(1, (length.Points * 96 / 72 - 5) / 7);
            }
        }

        if (options.AutoFitColumns && sheet.UsedRange is not null)
        {
            sheet.AutoFitColumns();
        }

        foreach (var (column, width) in widths)
        {
            sheet.SetColumnWidth(column, Math.Round(width, 2));
        }

        return sheet;

        static string? Width(HtmlNode col) =>
            col.Attribute("width") is { } attribute
                ? attribute.EndsWith("px", StringComparison.OrdinalIgnoreCase) || !attribute.Any(char.IsLetter) && !attribute.EndsWith('%')
                    ? attribute.TrimEnd('p', 'x', 'P', 'X') + "px"
                    : null
                : (col.Attribute("style") ?? string.Empty).Split(';')
                    .Select(d => d.Split(':', 2))
                    .Where(p => p.Length == 2 && p[0].Trim().Equals("width", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p[1].Trim())
                    .FirstOrDefault(w => !w.EndsWith('%'));
    }

    private static int Span(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var span) ? Math.Clamp(span, 1, 1000) : 1;

    /// <summary>A caption made fit to be a sheet name, or <c>Sheet</c> and the table's number.</summary>
    private static string SheetName(string caption, int number)
    {
        // Characters Excel forbids become spaces, so "Q3/2026" stays two words.
        var name = Collapse(new string([.. caption.Select(ch => @":\/?*[]".Contains(ch, StringComparison.Ordinal) ? ' ' : ch)]))
            .Trim().Trim('\'').Trim();

        if (name.Length > 31)
        {
            name = name[..31].TrimEnd();
        }

        return name.Length > 0 ? name : $"Sheet{number}";
    }

    private static void WriteCell(Worksheet sheet, CellReference reference, HtmlNode td, CssStyle rowStyle,
        ExcelHtmlImportOptions options)
    {
        var text = CellText(td);

        if (text.Length > 0)
        {
            var value = options.InferTypes && td.Name != "th" ? Infer(text, options.Culture, out var format) : Text(text, out format);
            sheet.SetValue(reference, value);

            if (format is not null)
            {
                sheet.SetStyle(reference, sheet.GetStyle(reference).WithNumberFormat(format));
            }
        }

        if (td.Descendants("a").FirstOrDefault()?.Attribute("href") is { Length: > 0 } href && IsSafeLink(href))
        {
            sheet[reference].WithHyperlink(href);
        }

        if (!options.KeepFormatting)
        {
            return;
        }

        var css = rowStyle.Merge(CssStyle.Of(td)).Merge(Uniform(td));
        var style = sheet.GetStyle(reference);
        var font = style.Font with
        {
            Bold = css.Bold ?? style.Font.Bold,
            Italic = css.Italic ?? style.Font.Italic,
            Underline = css.Underline ?? style.Font.Underline,
            Strike = css.Strike ?? style.Font.Strike,
            Color = css.Color ?? style.Font.Color,
            SizePoints = css.FontSize is { } size ? size.Points : style.Font.SizePoints,
            Name = css.FontFamily is { Length: > 0 } family ? family.Split(',')[0].Trim().Trim('"', '\'') : style.Font.Name,
        };

        var updated = style with
        {
            Font = font,
            BackgroundColor = css.BackgroundColor ?? style.BackgroundColor,
            Horizontal = css.Alignment switch
            {
                TextAlign.Left => HorizontalAlignment.Left,
                TextAlign.Center => HorizontalAlignment.Center,
                TextAlign.Right => HorizontalAlignment.Right,
                TextAlign.Justify => HorizontalAlignment.Justify,
                _ => style.Horizontal,
            },
            WrapText = style.WrapText || text.Contains('\n', StringComparison.Ordinal),
        };

        if (updated != style)
        {
            sheet.SetStyle(reference, updated);
        }

        static CellValue Text(string text, out string? format)
        {
            format = null;
            return CellValue.FromText(text);
        }
    }

    /// <summary>
    /// Formatting that covers all of a cell's text — <c>&lt;td&gt;&lt;b&gt;Total&lt;/b&gt;&lt;/td&gt;</c>
    /// is a bold cell. A cell only partly bold is not; a cell has one format.
    /// </summary>
    private static CssStyle Uniform(HtmlNode td)
    {
        var spans = new List<CssStyle>();
        Collect(td, default, spans);

        if (spans.Count == 0)
        {
            return default;
        }

        return new CssStyle
        {
            Bold = spans.All(s => s.Bold == true) ? true : null,
            Italic = spans.All(s => s.Italic == true) ? true : null,
            Underline = spans.All(s => s.Underline == true) ? true : null,
            Strike = spans.All(s => s.Strike == true) ? true : null,
            Color = spans.Select(s => s.Color).Distinct().Count() == 1 ? spans[0].Color : null,
        };

        static void Collect(HtmlNode node, CssStyle style, List<CssStyle> spans)
        {
            foreach (var child in node.Children)
            {
                if (child.IsText)
                {
                    if (!string.IsNullOrWhiteSpace(child.Text))
                    {
                        spans.Add(style);
                    }
                }
                else if (child.Name != "table")
                {
                    // A link's own colour and underline are the browser's, not the author's.
                    Collect(child, child.Name == "a" ? style : style.Merge(CssStyle.Of(child)), spans);
                }
            }
        }
    }

    /// <summary>A cell's text as a browser shows it: whitespace collapsed, <c>br</c> and blocks as line breaks.</summary>
    private static string CellText(HtmlNode td)
    {
        var builder = new StringBuilder();
        Collect(td, builder);

        var lines = builder.ToString().Split('\n').Select(line => Collapse(line).Trim());
        return string.Join('\n', lines).Trim('\n');

        static void Collect(HtmlNode node, StringBuilder builder)
        {
            foreach (var child in node.Children)
            {
                if (child.IsText)
                {
                    builder.Append(child.Text);
                }
                else if (child.Name == "br")
                {
                    builder.Append('\n');
                }
                else if (child.Name is "p" or "div" or "li" or "tr")
                {
                    builder.Append('\n');
                    Collect(child, builder);
                    builder.Append('\n');
                }
                else if (child.Name is not ("script" or "style"))
                {
                    Collect(child, builder);
                }
            }
        }
    }

    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) && c != ' ')
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>CSV's typing, plus percentages, which a page writes and a CSV seldom does.</summary>
    private static CellValue Infer(string text, CultureInfo culture, out string? format)
    {
        format = null;

        if (text.EndsWith('%') && text.Length > 1 &&
            double.TryParse(text[..^1].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, culture, out var percent))
        {
            var separator = culture.NumberFormat.NumberDecimalSeparator;
            var decimals = text.Contains(separator, StringComparison.Ordinal)
                ? text[..^1].Trim().Length - text.IndexOf(separator, StringComparison.Ordinal) - separator.Length
                : 0;

            format = decimals > 0 ? "0." + new string('0', decimals) + "%" : "0%";
            return CellValue.FromNumber(percent / 100);
        }

        var value = CsvIo.Infer(text, culture);

        // "1,480,000.50" is a number written with a format; keep the format, or the cell shows
        // 1480000.5. Grouping, or decimals ending in zero, are what say a format was there.
        if (value.ValueType == CellValueType.Number)
        {
            var numbers = culture.NumberFormat;
            var trimmed = text.Trim();
            var point = trimmed.LastIndexOf(numbers.NumberDecimalSeparator, StringComparison.Ordinal);
            var decimals = point < 0 ? 0 : trimmed.Length - point - numbers.NumberDecimalSeparator.Length;
            var grouped = trimmed.Contains(numbers.NumberGroupSeparator, StringComparison.Ordinal);
            var zeros = decimals > 0 ? "." + new string('0', decimals) : string.Empty;

            if (grouped)
            {
                format = "#,##0" + zeros;
            }
            else if (decimals > 0 && trimmed.EndsWith('0'))
            {
                format = "0" + zeros;
            }
        }

        return value;
    }
}
