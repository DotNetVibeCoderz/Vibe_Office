// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using OfficeNet.Core;
using PdfNet.Document;
using PdfNet.Text;

namespace ExcelNet.Io;

/// <summary>How a PDF's tables are turned into a workbook.</summary>
public sealed class PdfTableOptions
{
    /// <summary>How aggressively the page is broken into blocks.</summary>
    public StructureOptions Structure { get; set; } = new();

    /// <summary>Which pages to read, zero-based. <c>null</c> reads them all.</summary>
    public Range? Pages { get; set; }

    /// <summary>
    /// Whether a cell that looks like a number becomes one.
    /// </summary>
    /// <remarks>
    /// On by default, because a column of numbers stored as text is the single most annoying thing
    /// to receive in a spreadsheet — it will not sum, and Excel's green triangles are the only clue.
    /// A value that does not parse cleanly stays text rather than being coerced.
    /// </remarks>
    public bool ParseNumbers { get; set; } = true;

    /// <summary>
    /// The decimal separator, or <c>null</c> to try both.
    /// </summary>
    /// <remarks>
    /// A PDF carries no locale. "1.234" is one thousand two hundred and thirty-four in Indonesia and
    /// one-point-something-two-three-four elsewhere, and nothing in the file says which. Trying both
    /// is the honest default, and naming one is how a caller who knows removes the guess.
    /// </remarks>
    public char? DecimalSeparator { get; set; }

    /// <summary>Whether each table found becomes its own sheet.</summary>
    public bool SheetPerTable { get; set; } = true;
}

/// <summary>
/// Pulls a PDF's tables into a workbook.
/// </summary>
/// <remarks>
/// <para>
/// <b>An approximation, like every PDF table extractor.</b> A PDF has no tables; it has glyphs at
/// coordinates. A table is recognised here by its columns lining up over several consecutive lines
/// — see <see cref="PageStructure"/> for what that does and does not catch.
/// </para>
/// <para>
/// What it will miss: a table whose columns are ragged, one whose cells wrap onto several lines, one
/// separated only by ruling lines with generous spacing inside the cells, and any table on a scanned
/// page, which has no text to read at all. What it will not do is invent a table that is not there:
/// three consecutive lines have to agree on where the columns start before anything is emitted.
/// </para>
/// <para>
/// Check what comes back. That is not a disclaimer — it is the operating instruction for every tool
/// in this category, and a library that implied otherwise would be lying.
/// </para>
/// </remarks>
public static class PdfToExcel
{
    /// <summary>Reads the tables out of a PDF file.</summary>
    public static Workbook Convert(string path, PdfTableOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var pdf = PdfDocument.Open(path);
        return Convert(pdf, options);
    }

    /// <summary>Reads the tables out of an open PDF.</summary>
    public static Workbook Convert(PdfDocument pdf, PdfTableOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        options ??= new PdfTableOptions();

        var tables = FindTables(pdf, options);
        var workbook = Workbook.Create(tables.Count == 0 ? "Kosong" : "Tabel 1");

        if (tables.Count == 0)
        {
            return workbook;
        }

        if (!options.SheetPerTable)
        {
            var single = workbook.Worksheets[0];
            var row = 0;

            foreach (var table in tables)
            {
                Fill(single, table, options, row);

                // One blank row between tables, so a reader can tell where one ends. Running them
                // together makes the second table's header look like a row of the first.
                row += table.Count + 1;
            }

            return workbook;
        }

        Fill(workbook.Worksheets[0], tables[0], options, 0);

        for (var i = 1; i < tables.Count; i++)
        {
            Fill(workbook.AddSheet($"Tabel {i + 1}"), tables[i], options, 0);
        }

        return workbook;
    }

    /// <summary>Reads a PDF's tables and saves them as a workbook.</summary>
    public static void Convert(string pdfPath, string xlsxPath, PdfTableOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xlsxPath);

        using var workbook = Convert(pdfPath, options);
        workbook.Save(xlsxPath);
    }

    /// <summary>Every table the analysis found, as rows of strings.</summary>
    /// <remarks>
    /// Offered on its own because a caller often wants the grid rather than a workbook — to load into
    /// a database, or to check what was found before deciding to keep it.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> FindTables(PdfDocument pdf,
        PdfTableOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        options ??= new PdfTableOptions();

        var found = new List<IReadOnlyList<IReadOnlyList<string>>>();
        var (offset, count) = (options.Pages ?? Range.All).GetOffsetAndLength(pdf.Pages.Count);

        for (var i = offset; i < offset + count; i++)
        {
            foreach (var block in PageStructure.Analyse(pdf.Pages[i], options.Structure))
            {
                if (block.Kind == BlockKind.Table && block.Cells.Count > 0)
                {
                    found.Add(block.Cells);
                }
            }
        }

        return found;
    }

    // ---- Filling -------------------------------------------------------------------------------

    private static void Fill(Worksheet sheet, IReadOnlyList<IReadOnlyList<string>> table,
        PdfTableOptions options, int firstRow)
    {
        for (var row = 0; row < table.Count; row++)
        {
            for (var column = 0; column < table[row].Count; column++)
            {
                var text = table[row][column].Trim();

                if (text.Length == 0)
                {
                    continue;
                }

                var cell = sheet[firstRow + row, column];

                if (options.ParseNumbers && TryParseNumber(text, options.DecimalSeparator, out var number))
                {
                    cell.Set(number);
                }
                else
                {
                    cell.Set(text);
                }
            }
        }
    }

    /// <summary>
    /// Reads a string as a number, if it is unambiguously one.
    /// </summary>
    /// <param name="text">The cell's text.</param>
    /// <param name="separator">
    /// The decimal separator, or <c>null</c> to read only what one convention can mean.
    /// </param>
    /// <param name="value">The number, when there is one.</param>
    /// <returns>Whether the text is a number.</returns>
    /// <remarks>
    /// <para>
    /// A PDF carries no locale, so <c>1.234</c> is one thousand two hundred and thirty-four in
    /// Indonesia and one-point-two-three-four elsewhere, and nothing in the file says which. Getting
    /// it wrong changes a value by a factor of a thousand while looking perfectly reasonable, so the
    /// rule here is to answer only when the string can mean one thing:
    /// </para>
    /// <list type="bullet">
    /// <item>Both separators present — the rightmost is the decimal point. <c>1.234,56</c> and
    /// <c>1,234.56</c> are both unambiguous.</item>
    /// <item>One separator, more than once — it groups. <c>1.234.567</c> is a million.</item>
    /// <item>One separator, once, with exactly three digits after it — ambiguous, and left as
    /// text unless the caller named a separator.</item>
    /// <item>One separator, once, with any other number of digits after it — a decimal point.
    /// <c>15.5</c> and <c>1.2345</c> can only be read one way.</item>
    /// </list>
    /// <para>
    /// Currency symbols and a trailing percent sign are stripped first, because they are formatting
    /// rather than value; parentheses are an accountant's minus sign; and a percentage becomes its
    /// fraction, which is what Excel stores.
    /// </para>
    /// </remarks>
    public static bool TryParseNumber(string text, char? separator, out double value)
    {
        value = 0;

        var cleaned = text.Trim();

        if (cleaned.Length == 0)
        {
            return false;
        }

        var percent = cleaned.EndsWith('%');

        if (percent)
        {
            cleaned = cleaned[..^1].TrimEnd();
        }

        // Parentheses are an accountant's minus sign.
        var negative = cleaned.StartsWith('(') && cleaned.EndsWith(')');

        if (negative)
        {
            cleaned = cleaned[1..^1].Trim();
        }

        // Anything before the first digit or sign is a currency symbol or code — "Rp", "$", "USD".
        var firstDigit = cleaned.IndexOfAny(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']);

        if (firstDigit < 0)
        {
            return false;
        }

        var sign = cleaned.LastIndexOf('-', Math.Max(0, firstDigit - 1));
        cleaned = sign >= 0 ? "-" + cleaned[firstDigit..] : cleaned[firstDigit..];

        // And anything after the last digit that is not a separator.
        var lastDigit = cleaned.LastIndexOfAny(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']);
        cleaned = cleaned[..(lastDigit + 1)];

        if (cleaned.Any(c => !char.IsDigit(c) && c is not ('.' or ',' or '-')))
        {
            return false;
        }

        if (!TryReadDecimal(cleaned, separator, out value))
        {
            return false;
        }

        if (negative)
        {
            value = -value;
        }

        if (percent)
        {
            value /= 100;
        }

        return true;
    }

    /// <summary>Decides which separator is the decimal point, then parses.</summary>
    private static bool TryReadDecimal(string text, char? separator, out double value)
    {
        value = 0;

        var dots = text.Count(c => c == '.');
        var commas = text.Count(c => c == ',');

        char? decimalPoint;

        if (separator is { } named)
        {
            decimalPoint = named;
        }
        else if (dots > 0 && commas > 0)
        {
            // Both present: the rightmost one separates the fraction, whichever it is.
            decimalPoint = text.LastIndexOf('.') > text.LastIndexOf(',') ? '.' : ',';
        }
        else if (dots + commas == 0)
        {
            decimalPoint = null;
        }
        else if (dots + commas > 1)
        {
            // Repeated, so it groups — a decimal point appears at most once.
            decimalPoint = dots > 0 ? ',' : '.';
        }
        else
        {
            var only = dots > 0 ? '.' : ',';
            var after = text.Length - text.IndexOf(only) - 1;

            // Exactly three digits after a single separator is the ambiguous case, and the only one.
            if (after == 3)
            {
                return false;
            }

            decimalPoint = only;
        }

        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        culture.NumberFormat.NumberDecimalSeparator = (decimalPoint ?? '.').ToString();
        culture.NumberFormat.NumberGroupSeparator = decimalPoint == ',' ? "." : ",";

        return double.TryParse(text, NumberStyles.Number, culture, out value);
    }
}
