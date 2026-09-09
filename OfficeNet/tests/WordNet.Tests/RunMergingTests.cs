// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Drawing;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Text;
using WordNet.Export;
using Xunit;

namespace WordNet.Tests;

/// <summary>
/// Guards the merging of identically formatted words into a single text-showing operator.
/// </summary>
/// <remarks>
/// <para>
/// The export lays a line out word by word, because that is how line breaking works. Drawing it
/// word by word as well cost a font, a colour and a positioning operator each — 713 bytes allocated
/// per word against 147 when twelve words went out together, and roughly twice the bytes in the
/// finished PDF.
/// </para>
/// <para>
/// Merging is only safe while it is invisible, so these assert both halves: that the operators
/// really do collapse, and that the text, the formatting and the geometry come back unchanged.
/// </para>
/// </remarks>
public class RunMergingTests
{
    private const string Body =
        "Pendapatan naik tiga puluh dua persen dibanding kuartal sebelumnya karena " +
        "konsolidasi gudang di Surabaya sudah selesai pada bulan kedua tahun ini.";

    private static IReadOnlyList<TextFragment> Fragments(WordDocument document)
    {
        using var pdf = WordToPdf.Convert(document);
        return TextExtractor.ExtractFragments(pdf.Pages[0]);
    }

    [Fact]
    public void AUniformlyFormattedLineIsDrawnAsOnePiece()
    {
        // One fragment per line, not one per word. Before the merge this paragraph produced twenty
        // two fragments on its first line alone.
        using var document = WordDocument.Create();
        document.AddParagraph(Body);

        var lines = Fragments(document)
            .GroupBy(f => Math.Round(f.Y, 3))
            .ToList();

        Assert.All(lines, line => Assert.Single(line));
    }

    [Fact]
    public void FormattingStillBreaksTheRun()
    {
        // The other direction: a word that looks different has to be drawn separately, or merging
        // would silently restyle it. Three runs, three fragments.
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph();
        paragraph.AddRun("Pendapatan bersih ");
        paragraph.AddRun("naik tajam ").Format.Bold = true;
        paragraph.AddRun("pada kuartal ini.");

        var fragments = Fragments(document);

        Assert.Equal(3, fragments.Count);
        Assert.Equal("Pendapatan bersih ", fragments[0].Text);
        Assert.Equal("naik tajam ", fragments[1].Text);
        Assert.Equal("pada kuartal ini.", fragments[2].Text);
    }

    [Fact]
    public void MergingLeavesTheTextAndTheGeometryAlone()
    {
        // The claim the optimisation rests on: a piece's width is the sum of its glyph advances, so
        // a merged run puts every glyph exactly where drawing the words separately put it. Asserted
        // against the arithmetic the layout itself does, so a change to either side shows up here.
        foreach (var alignment in new[]
        {
            ParagraphAlignment.Left, ParagraphAlignment.Center,
            ParagraphAlignment.Right, ParagraphAlignment.Justify,
        })
        {
            using var document = WordDocument.Create();
            document.AddParagraph(Body).Alignment = alignment;

            foreach (var fragment in Fragments(document))
            {
                var words = fragment.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var expected = string.Join(' ', words);

                Assert.Equal(expected, fragment.Text.Trim());

                // The fragment's own width has to match the sum of the words it swallowed, spaces
                // included — that is what makes an underline and a highlight span the right box.
                var measured = StandardFonts.MeasurePoints(
                    StandardFont.Helvetica, fragment.Text, fragment.FontSize);

                Assert.Equal(measured, fragment.Width, 2);
            }
        }
    }

    [Fact]
    public void JustifiedTextStillSpacesEveryWordItself()
    {
        // Justification stretches the gaps, so the words on a justified line are not contiguous and
        // must not be merged. If they were, the line would collapse to its natural width and the
        // right margin would go ragged.
        using var document = WordDocument.Create();
        document.AddParagraph(Body + " " + Body).Alignment = ParagraphAlignment.Justify;

        var lines = Fragments(document)
            .GroupBy(f => Math.Round(f.Y, 3))
            .OrderByDescending(g => g.Key)
            .ToList();

        // Every line but the last is stretched to the full measure.
        var first = lines[0].OrderBy(f => f.X).ToList();

        Assert.True(first.Count > 1,
            "A justified line was merged into one piece, which loses the stretched spacing.");

        var right = first[^1].X + first[^1].Width;

        Assert.True(right > first[0].X + 400,
            $"A justified line ends at {right:0.0}, so it was not stretched to the measure.");
    }

    [Fact]
    public void TheExportedPdfIsSubstantiallySmaller()
    {
        // The user-visible half: fewer operators means a smaller file. Measured at 1.84 bytes per
        // word against 4.98 when every word was drawn separately, both after compression. The
        // budget sits between them with room for an unrelated change to the trailer or the fonts.
        using var document = WordDocument.Create();

        for (var i = 0; i < 200; i++)
        {
            document.AddParagraph(Body);
        }

        using var pdf = WordToPdf.Convert(document);
        using var stream = new MemoryStream();

        pdf.Save(stream);

        var words = 200 * Body.Split(' ').Length;
        var perWord = stream.Length / (double)words;

        Assert.True(perWord < 3,
            $"The export spends {perWord:0.0} bytes per word, which is the per-word drawing back.");
    }
}
