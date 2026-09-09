// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using PdfNet.Document;
using PdfNet.Text;
using WordNet.Styles;

namespace WordNet.Import;

/// <summary>How a PDF is turned into a Word document.</summary>
public sealed class PdfImportOptions
{
    /// <summary>How aggressively the page is broken into blocks.</summary>
    public StructureOptions Structure { get; set; } = new();

    /// <summary>Whether each page of the PDF starts a new page in the document.</summary>
    /// <remarks>
    /// On by default, because a PDF's pagination is usually the only structure it has that everyone
    /// agrees on. Turning it off gives one continuous flow, which reflows better and loses that.
    /// </remarks>
    public bool KeepPageBreaks { get; set; } = true;

    /// <summary>Whether a table found on the page becomes a Word table rather than paragraphs.</summary>
    public bool ConvertTables { get; set; } = true;

    /// <summary>Which pages to take, zero-based. <c>null</c> takes them all.</summary>
    public Range? Pages { get; set; }
}

/// <summary>
/// Rebuilds a Word document from a PDF.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a reconstruction, not a conversion, and the result is an approximation.</b> A PDF has
/// no paragraphs and no tables — it has instructions to put glyphs at coordinates. What comes back is
/// what <see cref="PageStructure"/> could infer from where those glyphs landed: lines from shared
/// baselines, paragraphs from vertical gaps, tables from columns that line up, headings from type
/// larger than the body.
/// </para>
/// <para>
/// It is built so that the failure is predictable rather than invisible. Structure the heuristics
/// miss comes back as paragraphs; <b>no text is ever dropped</b>. What is genuinely lost: colours,
/// fonts beyond bold, images, exact positions, multi-column reading order, and anything drawn as a
/// path rather than written as text. A scanned page with no text layer produces an empty document,
/// because there is nothing in it to read — that needs OCR, which is a different tool.
/// </para>
/// <para>
/// The right use for this is getting text back into an editable shape. It is not a round trip, and a
/// document that came from Word originally will not come back looking like the original.
/// </para>
/// </remarks>
public static class PdfToWord
{
    /// <summary>Converts a PDF file to a Word document.</summary>
    public static WordDocument Convert(string path, PdfImportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var pdf = PdfDocument.Open(path);
        return Convert(pdf, options);
    }

    /// <summary>Converts an open PDF to a Word document.</summary>
    public static WordDocument Convert(PdfDocument pdf, PdfImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        options ??= new PdfImportOptions();

        var (offset, count) = (options.Pages ?? Range.All).GetOffsetAndLength(pdf.Pages.Count);

        // Analysed in full before anything is written, because a heading's *level* is relative: the
        // biggest type in the document is a Heading 1 and the next size down a Heading 2, and
        // neither can be known from one page in isolation.
        var pages = new List<IReadOnlyList<TextBlock>>();

        for (var i = offset; i < offset + count; i++)
        {
            pages.Add(PageStructure.Analyse(pdf.Pages[i], options.Structure));
        }

        var levels = HeadingLevels(pages);

        var document = WordDocument.Create();
        EnsureStyles(document, levels.Count);

        var wrote = false;

        foreach (var blocks in pages)
        {
            if (blocks.Count == 0)
            {
                continue;
            }

            if (wrote && options.KeepPageBreaks)
            {
                document.AddParagraph().AddPageBreak();
            }

            foreach (var block in blocks)
            {
                Write(document, block, options, levels);
            }

            wrote = true;
        }

        // A Word document with no body content at all is legal but opens oddly, and an empty result
        // is a real outcome here: a scanned page has no text layer to read.
        if (!wrote)
        {
            document.AddParagraph();
        }

        return document;
    }

    /// <summary>
    /// Maps each heading size in the document to a heading level.
    /// </summary>
    /// <remarks>
    /// A PDF records type sizes, not outline levels. Largest becomes Heading 1, next largest
    /// Heading 2, and so on down to Heading 6 — after which everything else shares the last level,
    /// because a document with seven distinct heading sizes is one where the sizes were not an
    /// outline in the first place.
    /// </remarks>
    private static Dictionary<double, int> HeadingLevels(List<IReadOnlyList<TextBlock>> pages)
    {
        var sizes = pages
            .SelectMany(page => page)
            .Where(b => b.Kind == BlockKind.Heading)
            // Rounded, or two headings set at 18 pt and 18.000001 pt become different levels.
            .Select(b => Math.Round(b.FontSize, 1))
            .Distinct()
            .OrderByDescending(size => size)
            .ToList();

        return sizes
            .Select((size, index) => (size, level: Math.Min(6, index + 1)))
            .ToDictionary(x => x.size, x => x.level);
    }

    /// <summary>Converts a PDF file and saves the result.</summary>
    public static void Convert(string pdfPath, string docxPath, PdfImportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        using var document = Convert(pdfPath, options);
        document.Save(docxPath);
    }

    // ---- Blocks --------------------------------------------------------------------------------

    private static void Write(WordDocument document, TextBlock block, PdfImportOptions options,
        Dictionary<double, int> levels)
    {
        if (block.Kind == BlockKind.Table && options.ConvertTables && block.Cells.Count > 0)
        {
            WriteTable(document, block);
            return;
        }

        if (block.Kind == BlockKind.Heading)
        {
            var level = levels.GetValueOrDefault(Math.Round(block.FontSize, 1), 2);
            document.AddParagraph(block.Text.Replace('\n', ' '), $"Heading{level}");
            return;
        }

        // Lines within a paragraph are joined with spaces rather than kept as separate lines: a PDF
        // line break is where the text ran out of column, not where the author put one.
        var paragraph = document.AddParagraph();
        var text = string.Join(' ', block.Lines.Select(l => l.Text.Trim()));

        var run = paragraph.AddRun(text);

        if (block.Lines.All(l => l.IsBold))
        {
            run.WithBold();
        }
    }

    private static void WriteTable(WordDocument document, TextBlock block)
    {
        var columns = block.Cells.Max(row => row.Count);
        var table = document.AddTable(block.Cells.Count, columns);

        for (var row = 0; row < block.Cells.Count; row++)
        {
            for (var column = 0; column < block.Cells[row].Count; column++)
            {
                table[row, column].Text = block.Cells[row][column].Trim();
            }
        }

        // The first row is treated as a header when its type is bold, which is the only signal a PDF
        // carries. A table whose header is distinguished by shading alone comes back plain.
        if (block.Lines.Count > 0 && block.Lines[0].IsBold)
        {
            foreach (var cell in table.Rows[0].Cells)
            {
                foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
                {
                    run.WithBold();
                }
            }
        }
    }

    /// <summary>Makes sure the heading styles the import will use exist before it writes them.</summary>
    private static void EnsureStyles(WordDocument document, int levels)
    {
        for (var level = 1; level <= Math.Max(2, Math.Min(6, levels)); level++)
        {
            document.Styles.GetOrAdd($"Heading{level}", $"heading {level}", StyleType.Paragraph);
        }
    }
}
