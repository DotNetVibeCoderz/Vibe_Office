// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.Styles;
using OfficeNet.Core.Drawing;
using PdfNet.Content;
using PdfNet.Document;

namespace ExcelNet.Io;

/// <summary>Options for rendering a workbook to PDF.</summary>
public sealed class ExcelPdfOptions
{
    /// <summary>The page size. Landscape A4 suits most sheets.</summary>
    public PdfRectangle PageSize { get; set; } = PdfNet.Document.PageSize.A4.Landscape();

    /// <summary>The page margin in points.</summary>
    public double Margin { get; set; } = 36;

    /// <summary>Which sheets to render; <c>null</c> renders all of them.</summary>
    public IReadOnlyList<string>? SheetNames { get; set; }

    /// <summary>Draws the sheet's gridlines.</summary>
    public bool ShowGridLines { get; set; } = true;

    /// <summary>Repeats the first row of each sheet at the top of every page.</summary>
    public bool RepeatHeaderRow { get; set; } = true;

    /// <summary>Prints the sheet name and page number in a footer.</summary>
    public bool ShowFooter { get; set; } = true;

    /// <summary>The base font size in points.</summary>
    public double FontSizePoints { get; set; } = 8.5;

    /// <summary>Evaluates formulas before rendering, so computed cells are not blank.</summary>
    public bool Recalculate { get; set; } = true;
}

/// <summary>
/// Renders a workbook's used ranges to PDF pages.
/// </summary>
/// <remarks>
/// <para>
/// A spreadsheet has no pages, so pagination has to be invented. This paginates in both directions:
/// columns that do not fit the page width become a second horizontal band of pages, and rows that
/// do not fit become further pages down. That is what Excel's own printing does, and it is the
/// difference between a usable export and one that silently drops every column past H.
/// </para>
/// <para>
/// A column's width is stored in "characters of the digit zero", which is not a length. The
/// conversion here uses the documented approximation — seven pixels per character plus five pixels
/// of padding, at 96 DPI — which lands within a point or two of what Excel prints.
/// </para>
/// </remarks>
public static class ExcelToPdf
{
    /// <summary>Renders a workbook to a new PDF.</summary>
    public static PdfDocument Convert(Workbook workbook, ExcelPdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        options ??= new ExcelPdfOptions();

        if (options.Recalculate)
        {
            workbook.Recalculate();
        }

        var pdf = PdfDocument.Create();
        pdf.Info.Title = workbook.Properties.Title;
        pdf.Info.Author = workbook.Properties.Creator;
        pdf.Info.Creator = "OfficeNet ExcelNet by Gravicode Studios";

        var sheets = options.SheetNames is null
            ? workbook.Worksheets
            : [.. options.SheetNames.Select(workbook.Find).Where(s => s is not null).Cast<Worksheet>()];

        foreach (var sheet in sheets)
        {
            RenderSheet(pdf, sheet, options);
        }

        if (pdf.Pages.Count == 0)
        {
            pdf.Pages.Add(options.PageSize);
        }

        return pdf;
    }

    private static void RenderSheet(PdfDocument pdf, Worksheet sheet, ExcelPdfOptions options)
    {
        if (sheet.UsedRange is not { } used)
        {
            return;
        }

        var widths = new double[used.ColumnCount];

        for (var i = 0; i < used.ColumnCount; i++)
        {
            widths[i] = ColumnPoints(sheet.GetColumnWidth(used.Start.Column + i));
        }

        var available = options.PageSize.Width - options.Margin * 2;

        // Split the columns into bands that each fit one page width.
        var bands = new List<(int First, int Count)>();
        var bandStart = 0;
        double bandWidth = 0;

        for (var i = 0; i < widths.Length; i++)
        {
            if (bandWidth + widths[i] > available && i > bandStart)
            {
                bands.Add((bandStart, i - bandStart));
                bandStart = i;
                bandWidth = 0;
            }

            bandWidth += widths[i];
        }

        bands.Add((bandStart, widths.Length - bandStart));

        var pageNumber = 1;

        foreach (var (firstColumn, columnCount) in bands)
        {
            var row = used.Start.Row;

            while (row <= used.End.Row)
            {
                var page = pdf.Pages.Add(options.PageSize);
                using var canvas = page.OpenCanvas();
                canvas.TopDown = true;

                var y = options.Margin;

                canvas.SetFont(StandardFont.HelveticaBold, options.FontSizePoints + 2.5);
                canvas.SetFillColor(OfficeColor.FromRgb(0x1F, 0x38, 0x64));
                canvas.DrawText(sheet.Name, options.Margin, y + options.FontSizePoints + 2.5);
                y += options.FontSizePoints + 12;

                var bottom = options.PageSize.Height - options.Margin - (options.ShowFooter ? 18 : 0);

                // The header row repeats on continuation pages so a long sheet stays readable.
                if (options.RepeatHeaderRow && row > used.Start.Row)
                {
                    y = DrawRow(canvas, sheet, used, used.Start.Row, firstColumn, columnCount,
                        widths, options, y);
                }

                while (row <= used.End.Row)
                {
                    var height = RowPoints(sheet.GetRowHeight(row));

                    if (y + height > bottom)
                    {
                        break;
                    }

                    y = DrawRow(canvas, sheet, used, row, firstColumn, columnCount, widths, options, y);
                    row++;
                }

                if (options.ShowFooter)
                {
                    canvas.SetFont(StandardFont.Helvetica, 7.5);
                    canvas.SetFillColor(OfficeColor.Gray);
                    canvas.DrawText($"{sheet.Name} — page {pageNumber}",
                        options.Margin, options.PageSize.Height - options.Margin + 4);
                }

                pageNumber++;

                // A band with no rows left would loop forever; the guard is the page that drew
                // nothing but the header.
                if (row <= used.End.Row && y <= options.Margin + options.FontSizePoints + 12)
                {
                    row++;
                }
            }
        }
    }

    private static double DrawRow(PdfCanvas canvas, Worksheet sheet, CellRangeReference used, int row,
        int firstColumn, int columnCount, double[] widths, ExcelPdfOptions options, double y)
    {
        var height = RowPoints(sheet.GetRowHeight(row));
        var x = options.Margin;

        for (var i = 0; i < columnCount; i++)
        {
            var columnIndex = firstColumn + i;
            var width = widths[columnIndex];
            var reference = new CellReference(row, used.Start.Column + columnIndex);
            var style = sheet.GetStyle(reference);
            var value = sheet[reference].Value;

            if (style.BackgroundColor is { } background)
            {
                canvas.SetFillColor(background);
                canvas.Rectangle(x, y, width, height).Fill();
            }

            if (options.ShowGridLines && sheet.ShowGridLines)
            {
                canvas.SetStrokeColor(OfficeColor.FromRgb(0xD9, 0xD9, 0xD9));
                canvas.SetLineWidth(0.4);
                canvas.Rectangle(x, y, width, height).Stroke();
            }

            if (!value.IsEmpty)
            {
                var text = Format(value, style.NumberFormat);

                var font = StandardFonts.Match(style.Font.Name, style.Font.Bold, style.Font.Italic);
                var size = Math.Min(options.FontSizePoints, style.Font.SizePoints);

                canvas.SetFont(font, size);
                canvas.SetFillColor(style.Font.Color ?? OfficeColor.Black);

                var textWidth = StandardFonts.MeasurePoints(font, text, size);

                // Numbers are right-aligned unless something says otherwise, which is what a
                // spreadsheet's General alignment means.
                var alignment = style.Horizontal == HorizontalAlignment.General
                    ? value.ValueType is CellValueType.Number or CellValueType.DateTime
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Left
                    : style.Horizontal;

                var offset = alignment switch
                {
                    HorizontalAlignment.Center => (width - textWidth) / 2,
                    HorizontalAlignment.Right => width - textWidth - 3,
                    _ => 3.0,
                };

                var baseline = y + (height + size) / 2 - size * 0.22;

                // Content wider than its column is clipped rather than allowed to run into the
                // next one — which is what Excel does when the neighbour is not empty.
                canvas.Save();
                canvas.Rectangle(x, y, width, height).Clip();
                canvas.DrawText(text, x + Math.Max(1, offset), baseline);
                canvas.Restore();
            }

            x += width;
        }

        return y + height;
    }

    /// <summary>Converts a stored column width to points.</summary>
    private static double ColumnPoints(double characters) =>
        // Characters -> pixels at 96 DPI (7 px per character plus 5 px padding), then -> points.
        Math.Max(12, (characters * 7 + 5) * 72 / 96);

    private static double RowPoints(double points) => Math.Max(9, points);

    /// <summary>
    /// Formats a value through its number format, enough to be recognisable in a printed sheet.
    /// </summary>
    /// <remarks>
    /// Excel's format language is a small programming language of its own — up to four
    /// semicolon-separated sections, conditions in brackets, literal text in quotes, colour names.
    /// This handles the shapes that appear in practice: dates, percentages, thousands separators,
    /// fixed decimals and a currency prefix. A format it does not understand falls back to the
    /// value's own text, which is always readable even when it is not what Excel would print.
    /// </remarks>
    internal static string Format(CellValue value, string? format)
    {
        if (value.ValueType == CellValueType.DateTime || NumberFormats.IsDateFormat(format))
        {
            var date = value.AsDateTime();
            return format is null
                ? date.ToString("yyyy-MM-dd")
                : date.ToString(TranslateDateFormat(format), System.Globalization.CultureInfo.CurrentCulture);
        }

        if (value.ValueType != CellValueType.Number || string.IsNullOrEmpty(format) ||
            format == NumberFormats.General)
        {
            return value.AsText();
        }

        // Only the positive section applies to the values a report shows; the negative and zero
        // sections would need the full grammar to honour.
        var section = format.Split(';')[0];

        var isPercent = section.Contains('%');
        var number = isPercent ? value.AsNumber() * 100 : value.AsNumber();

        var prefix = ExtractLiteral(section, leading: true);
        var suffix = ExtractLiteral(section, leading: false);

        var digits = CountDecimals(section);
        var grouped = section.Contains("#,#") || section.Contains("0,0");

        var formatted = number.ToString(
            (grouped ? "#,##0" : "0") + (digits > 0 ? "." + new string('0', digits) : string.Empty),
            System.Globalization.CultureInfo.CurrentCulture);

        return prefix + formatted + suffix + (isPercent ? "%" : string.Empty);
    }

    private static string TranslateDateFormat(string format)
    {
        // Excel's codes are case-insensitive and use lower-case "mm" for both month and minute;
        // .NET distinguishes "MM" (month) from "mm" (minute) by case, so the month positions have
        // to be identified by what surrounds them.
        var result = new System.Text.StringBuilder(format.Length);
        var sawHour = false;

        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];

            switch (char.ToLowerInvariant(c))
            {
                case 'y':
                    result.Append('y');
                    break;

                case 'm':
                {
                    // An "m" directly after an hour code, or directly before a seconds code, is
                    // minutes; anywhere else it is months.
                    var isMinute = sawHour;

                    for (var j = i + 1; j < format.Length; j++)
                    {
                        if (char.ToLowerInvariant(format[j]) == 'm')
                        {
                            continue;
                        }

                        if (char.ToLowerInvariant(format[j]) == 's')
                        {
                            isMinute = true;
                        }

                        break;
                    }

                    result.Append(isMinute ? 'm' : 'M');
                    break;
                }

                case 'd':
                    result.Append('d');
                    sawHour = false;
                    break;

                case 'h':
                    result.Append('H');
                    sawHour = true;
                    break;

                case 's':
                    result.Append('s');
                    break;

                case '\\':
                    if (i + 1 < format.Length)
                    {
                        result.Append('\\').Append(format[++i]);
                    }

                    break;

                default:
                    result.Append(c);
                    if (!char.IsWhiteSpace(c) && c != ':')
                    {
                        sawHour = false;
                    }

                    break;
            }
        }

        var translated = result.ToString();
        return translated.Length == 0 ? "yyyy-MM-dd" : translated;
    }

    private static string ExtractLiteral(string format, bool leading)
    {
        var builder = new System.Text.StringBuilder();

        if (leading)
        {
            for (var i = 0; i < format.Length; i++)
            {
                if (format[i] == '"')
                {
                    var end = format.IndexOf('"', i + 1);

                    if (end < 0)
                    {
                        break;
                    }

                    builder.Append(format[(i + 1)..end]);
                    i = end;
                    continue;
                }

                if (format[i] is '#' or '0' or '?')
                {
                    break;
                }
            }

            return builder.ToString();
        }

        var lastDigit = format.LastIndexOfAny(['#', '0', '?']);

        if (lastDigit < 0 || lastDigit == format.Length - 1)
        {
            return string.Empty;
        }

        for (var i = lastDigit + 1; i < format.Length; i++)
        {
            if (format[i] == '"')
            {
                var end = format.IndexOf('"', i + 1);

                if (end < 0)
                {
                    break;
                }

                builder.Append(format[(i + 1)..end]);
                i = end;
            }
        }

        return builder.ToString();
    }

    private static int CountDecimals(string format)
    {
        var dot = format.IndexOf('.');

        if (dot < 0)
        {
            return 0;
        }

        var count = 0;

        for (var i = dot + 1; i < format.Length && format[i] is '0' or '#' or '?'; i++)
        {
            count++;
        }

        return count;
    }
}
