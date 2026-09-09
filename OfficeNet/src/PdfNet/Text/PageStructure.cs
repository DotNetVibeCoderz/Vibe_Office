// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Document;

namespace PdfNet.Text;

/// <summary>One line of a page: the fragments that share a baseline, left to right.</summary>
public sealed class TextLine
{
    internal TextLine(List<TextFragment> fragments)
    {
        fragments.Sort((a, b) => a.X.CompareTo(b.X));
        Fragments = fragments;
    }

    /// <summary>The fragments, in reading order.</summary>
    public IReadOnlyList<TextFragment> Fragments { get; }

    /// <summary>The baseline, in points up from the page's bottom.</summary>
    public double Baseline => Fragments[0].Y;

    /// <summary>The left edge.</summary>
    public double Left => Fragments[0].X;

    /// <summary>The right edge.</summary>
    public double Right => Fragments.Max(f => f.Right);

    /// <summary>The largest font size on the line, which is what decides its height.</summary>
    public double FontSize => Fragments.Max(f => f.FontSize);

    /// <summary>Whether any fragment on the line is bold, judged by its font name.</summary>
    /// <remarks>
    /// A PDF does not record "bold" — it records a font, and the weight is part of that font's name.
    /// So this is a guess from a naming convention that almost every producer follows and none is
    /// obliged to.
    /// </remarks>
    public bool IsBold => Fragments.Any(f =>
        f.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true ||
        f.FontName?.Contains("Black", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>The line's text, with the spaces the producer never wrote put back.</summary>
    public string Text => TextExtractor.JoinLine(Fragments);

    public override string ToString() => Text;
}

/// <summary>What a block of a page turned out to be.</summary>
public enum BlockKind
{
    /// <summary>Running text.</summary>
    Paragraph,

    /// <summary>A line set larger than the body text around it.</summary>
    Heading,

    /// <summary>Lines whose fragments line up into columns.</summary>
    Table,
}

/// <summary>
/// A run of lines that belong together: a paragraph, a heading, or a table.
/// </summary>
public sealed class TextBlock
{
    internal TextBlock(BlockKind kind, IReadOnlyList<TextLine> lines,
        IReadOnlyList<IReadOnlyList<string>>? cells = null)
    {
        Kind = kind;
        Lines = lines;
        Cells = cells ?? [];
    }

    /// <summary>What the block is.</summary>
    public BlockKind Kind { get; }

    /// <summary>The lines it holds, top to bottom.</summary>
    public IReadOnlyList<TextLine> Lines { get; }

    /// <summary>
    /// The grid, for a <see cref="BlockKind.Table"/>. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// Rectangular: short rows are padded so every row has the same number of cells, because a
    /// consumer that has to check each row's length before reading it will forget to.
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<string>> Cells { get; }

    /// <summary>The block's text, lines joined by newlines.</summary>
    public string Text => string.Join('\n', Lines.Select(l => l.Text));

    /// <summary>The block's left edge.</summary>
    public double Left => Lines.Min(l => l.Left);

    /// <summary>The largest font size in the block.</summary>
    public double FontSize => Lines.Max(l => l.FontSize);

    public override string ToString() =>
        Kind == BlockKind.Table
            ? $"Table {Cells.Count}x{(Cells.Count == 0 ? 0 : Cells[0].Count)}"
            : $"{Kind}: {Text}";
}

/// <summary>How aggressively a page is broken into blocks.</summary>
public sealed class StructureOptions
{
    /// <summary>
    /// How much bigger than the body text a line must be to count as a heading.
    /// </summary>
    /// <remarks>
    /// Measured against the page's median font size rather than a fixed number of points, so a
    /// document set in 9 pt and one set in 12 pt both work.
    /// </remarks>
    public double HeadingRatio { get; set; } = 1.15;

    /// <summary>
    /// How many lines must line up before a run counts as a table.
    /// </summary>
    /// <remarks>
    /// Two is too eager — a heading over a figure caption lines up with it by accident. Three is
    /// the smallest number where the alignment is more likely to be a table than a coincidence.
    /// </remarks>
    public int MinimumTableRows { get; set; } = 3;

    /// <summary>Whether to look for tables at all.</summary>
    public bool DetectTables { get; set; } = true;
}

/// <summary>
/// Recovers a page's structure from where its glyphs landed.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is an approximation, and it always will be.</b> A PDF does not contain paragraphs or
/// tables; it contains instructions to put glyphs at coordinates. Everything below is inference from
/// those coordinates: lines from shared baselines, paragraphs from vertical gaps and left edges,
/// tables from columns that line up. A document laid out in a way the heuristics do not expect comes
/// back as paragraphs, which is the failure mode chosen deliberately — <b>no text is ever
/// dropped</b>, it is only structured less well.
/// </para>
/// <para>
/// What it does not attempt: reading order across multiple columns, headers and footers as distinct
/// from body text, lists as lists, or any table whose columns are ragged. A tagged PDF carries the
/// real structure and should be read from its tags instead; almost none are tagged.
/// </para>
/// </remarks>
public static class PageStructure
{
    /// <summary>Groups a page's fragments into lines, top to bottom.</summary>
    public static IReadOnlyList<TextLine> Lines(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return Lines(TextExtractor.ExtractFragments(page));
    }

    /// <summary>Groups fragments into lines, top to bottom.</summary>
    public static IReadOnlyList<TextLine> Lines(IReadOnlyList<TextFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        var grouped = new List<List<TextFragment>>();

        // Sorted top-down first, so a line is always compared against the one most recently opened
        // rather than against every line on the page.
        foreach (var fragment in fragments.OrderByDescending(f => f.Y).ThenBy(f => f.X))
        {
            // A tolerance proportional to the font size is what keeps a line that mixes 10 pt body
            // text with an 8 pt footnote marker in one piece.
            var tolerance = Math.Max(1.5, fragment.FontSize * 0.35);

            var line = grouped.FirstOrDefault(l => Math.Abs(l[0].Y - fragment.Y) <= tolerance);

            if (line is null)
            {
                grouped.Add([fragment]);
            }
            else
            {
                line.Add(fragment);
            }
        }

        return [.. grouped.Select(l => new TextLine(l))];
    }

    /// <summary>Breaks a page into paragraphs, headings and tables.</summary>
    public static IReadOnlyList<TextBlock> Analyse(PdfPage page, StructureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        return Analyse(Lines(page), options);
    }

    /// <summary>Breaks a page's lines into paragraphs, headings and tables.</summary>
    public static IReadOnlyList<TextBlock> Analyse(IReadOnlyList<TextLine> lines,
        StructureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        options ??= new StructureOptions();

        var body = lines.Where(l => l.Text.Trim().Length > 0).ToList();

        if (body.Count == 0)
        {
            return [];
        }

        var bodySize = MedianFontSize(body);
        var blocks = new List<TextBlock>();
        var index = 0;

        while (index < body.Count)
        {
            if (options.DetectTables &&
                TableAt(body, index, options.MinimumTableRows) is { } table)
            {
                blocks.Add(table.Block);
                index = table.Next;
                continue;
            }

            var paragraph = ParagraphAt(body, index, bodySize, options.HeadingRatio);
            blocks.Add(paragraph.Block);
            index = paragraph.Next;
        }

        return blocks;
    }

    // ---- Paragraphs ----------------------------------------------------------------------------

    private static (TextBlock Block, int Next) ParagraphAt(List<TextLine> lines, int start,
        double bodySize, double headingRatio)
    {
        var first = lines[start];

        // A single line set larger than the body is a heading. Two such lines in a row are a heading
        // that wrapped, so the size test runs before the paragraph is closed rather than after.
        var isHeading = first.FontSize > bodySize * headingRatio;

        var taken = new List<TextLine> { first };
        var index = start + 1;

        while (index < lines.Count)
        {
            var previous = lines[index - 1];
            var line = lines[index];

            var gap = previous.Baseline - line.Baseline;
            var lineHeight = Math.Max(previous.FontSize, line.FontSize);

            // A gap much larger than a line height ends the paragraph, and so does a change of size.
            // Left edge alone is not enough: a first-line indent shifts one line and not the rest.
            var sameSize = Math.Abs(line.FontSize - previous.FontSize) < previous.FontSize * 0.15;
            var close = gap <= lineHeight * 1.6 && gap > 0;

            if (!close || !sameSize || (line.FontSize > bodySize * headingRatio) != isHeading)
            {
                break;
            }

            // A line that starts noticeably further right than the paragraph's own left edge is a
            // new indented paragraph, not a continuation.
            var left = taken.Min(l => l.Left);

            if (line.Left > left + (lineHeight * 1.5))
            {
                break;
            }

            taken.Add(line);
            index++;
        }

        return (new TextBlock(isHeading ? BlockKind.Heading : BlockKind.Paragraph, taken), index);
    }

    private static double MedianFontSize(List<TextLine> lines)
    {
        // Weighted by how much text is set at each size, so a page with one huge title and forty
        // lines of body text has a body-sized median rather than a midpoint between the two.
        var sizes = lines
            .SelectMany(l => l.Fragments.Select(f => (f.FontSize, Weight: Math.Max(1, f.Text.Length))))
            .SelectMany(x => Enumerable.Repeat(x.FontSize, Math.Min(200, x.Weight)))
            .Order()
            .ToList();

        return sizes.Count == 0 ? 11 : sizes[sizes.Count / 2];
    }

    // ---- Tables --------------------------------------------------------------------------------

    /// <summary>
    /// Recognises a table starting at a line, if there is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The signal is columns: several consecutive lines whose fragments start at the same
    /// x-positions. Ruling lines would be a stronger signal, but they are painted paths rather than
    /// text and reading them means running the content stream a second time as a graphics
    /// interpreter — worth doing one day, and not needed for the tables people actually export.
    /// </para>
    /// <para>
    /// The failure this guards against is a run of ordinary paragraphs that happen to share a left
    /// margin. Requiring every candidate row to have the same number of columns, in the same places,
    /// is what tells a table from a coincidence.
    /// </para>
    /// </remarks>
    private static (TextBlock Block, int Next)? TableAt(List<TextLine> lines, int start,
        int minimumRows)
    {
        var first = lines[start];
        var columns = ColumnStarts(first);

        if (columns.Count < 2)
        {
            return null;
        }

        var rows = new List<TextLine> { first };
        var index = start + 1;

        while (index < lines.Count)
        {
            var line = lines[index];
            var gap = lines[index - 1].Baseline - line.Baseline;

            // A row far below the previous one is a different table, or not a table at all.
            if (gap <= 0 || gap > Math.Max(line.FontSize, lines[index - 1].FontSize) * 2.5)
            {
                break;
            }

            var candidate = ColumnStarts(line);

            if (candidate.Count != columns.Count || !AlignsWith(columns, candidate, line.FontSize))
            {
                break;
            }

            rows.Add(line);
            index++;
        }

        if (rows.Count < minimumRows)
        {
            return null;
        }

        var cells = rows.Select(row => (IReadOnlyList<string>)
            [.. Group(row, columns).Select(g => TextExtractor.JoinLine(g))]).ToList();

        return (new TextBlock(BlockKind.Table, rows, cells), index);
    }

    /// <summary>Where a line's columns begin, judged by the gaps between its fragments.</summary>
    private static List<double> ColumnStarts(TextLine line)
    {
        var starts = new List<double> { line.Left };

        for (var i = 1; i < line.Fragments.Count; i++)
        {
            var gap = line.Fragments[i].X - line.Fragments[i - 1].Right;

            // Wide enough to be a column boundary rather than a word space. Two spaces would be
            // about one em; a column gap in a real table is wider than that.
            if (gap > line.Fragments[i].FontSize * 1.2)
            {
                starts.Add(line.Fragments[i].X);
            }
        }

        return starts;
    }

    private static bool AlignsWith(List<double> expected, List<double> actual, double fontSize)
    {
        var tolerance = Math.Max(3, fontSize * 0.8);

        return !expected.Where((t, i) => Math.Abs(t - actual[i]) > tolerance).Any();
    }

    /// <summary>Splits a line's fragments into the given columns.</summary>
    private static IEnumerable<List<TextFragment>> Group(TextLine line, List<double> columns)
    {
        var cells = columns.Select(_ => new List<TextFragment>()).ToList();

        foreach (var fragment in line.Fragments)
        {
            // The last column whose start is at or before this fragment. Nearest-start would put a
            // fragment that overhangs its column into the next one.
            var column = 0;

            for (var i = 0; i < columns.Count; i++)
            {
                if (fragment.X + 1 >= columns[i])
                {
                    column = i;
                }
            }

            cells[column].Add(fragment);
        }

        return cells;
    }
}
