// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Text;
using WordNet.Export;
using Xunit;

namespace WordNet.Tests;

/// <summary>
/// Covers laying text out as ranges of the paragraph's own text rather than as cut-out strings.
/// </summary>
/// <remarks>
/// <para>
/// A piece of a line names a segment and a span within it. That is what makes merging neighbouring
/// words free — they are already one unbroken range — and it rests on an invariant worth pinning
/// down: words are split so a space is only ever the <em>last</em> character of a piece. A piece
/// that begins with a space therefore holds nothing else, which is why a line that would start with
/// one drops that piece whole rather than trimming part of it and remeasuring what is left.
/// </para>
/// <para>
/// So these cover the places a range can go wrong: a run of spaces at a wrap point, a segment
/// boundary where merging must stop even though the words look continuous, and a justified line,
/// where a piece is walked for its own spaces and must stop at its end rather than the segment's.
/// </para>
/// </remarks>
public class LinePieceRangeTests
{
    private static IReadOnlyList<TextFragment> Fragments(WordDocument document)
    {
        using var pdf = WordToPdf.Convert(document);
        return TextExtractor.ExtractFragments(pdf.Pages[0]);
    }

    private static List<IGrouping<double, TextFragment>> Lines(WordDocument document) =>
        Fragments(document)
            .GroupBy(f => Math.Round(f.Y, 3))
            .OrderByDescending(g => g.Key)
            .ToList();

    [Fact]
    public void NoLineStartsWithASpaceEvenWhereTheTextHasRunsOfThem()
    {
        // Double spaces put a lone space piece at the start of a line whenever a wrap falls between
        // them. Drawing it would indent that line by a space nobody asked for, and by a different
        // amount on every line.
        using var document = WordDocument.Create();

        // A long run of spaces is what makes this bite. A single doubled space almost always fits
        // at the end of the line before it, so the wrap falls after it and nothing lands at a line
        // start; sixty of them cannot fit on one line, so the next line has to begin with one.
        document.AddParagraph(
            "Pendapatan wilayah pertumbuhan laporan tahunan kuartal operasional" +
            new string(' ', 60) +
            "pendapatan wilayah pertumbuhan laporan tahunan kuartal operasional");

        var lines = Lines(document);

        Assert.True(lines.Count > 1, "The text has to wrap for this to test anything.");

        foreach (var line in lines)
        {
            var first = line.OrderBy(f => f.X).First();

            Assert.False(first.Text.StartsWith(' '),
                $"A line begins with '{first.Text[..Math.Min(12, first.Text.Length)]}'.");
        }
    }

    [Fact]
    public void ALineIsAsWideAsTheCharactersItActuallyHolds()
    {
        // Every piece carries a width measured when the line was filled, and the drawing trusts it.
        // A width that outlived a change to its range would leave the rest of the line shifted —
        // invisible in the extracted text and obvious on the page. So this compares where a line
        // really ends against what its own characters say, which holds however the pieces were
        // formed.
        using var document = WordDocument.Create();

        document.AddParagraph(
            "Pendapatan wilayah pertumbuhan laporan tahunan kuartal operasional" +
            new string(' ', 60) +
            "pendapatan wilayah pertumbuhan laporan tahunan kuartal operasional");

        foreach (var line in Lines(document))
        {
            var ordered = line.OrderBy(f => f.X).ToList();
            var last = ordered[^1];
            var measured = 0.0;

            foreach (var fragment in ordered)
            {
                measured += fragment.Width;
            }

            Assert.Equal(measured, last.X + last.Width - ordered[0].X, 2);
        }
    }

    [Fact]
    public void MergingStopsAtASegmentBoundaryEvenWhenTheWordsLookContinuous()
    {
        // Two runs with identical formatting are two segments, so their words are two ranges that
        // cannot be merged into one — the ranges are not contiguous, they are in different strings.
        // A merge that compared only the formatting would splice them and draw the second run's
        // text at the first run's offset.
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph();
        paragraph.AddRun("Pendapatan bersih ");
        paragraph.AddRun("naik tajam tahun ini.");

        var fragments = Fragments(document);

        Assert.Equal(2, fragments.Count);
        Assert.Equal("Pendapatan bersih ", fragments[0].Text);
        Assert.Equal("naik tajam tahun ini.", fragments[1].Text);

        // And they still sit flush against each other, which is what says the widths are right.
        Assert.Equal(fragments[0].X + fragments[0].Width, fragments[1].X, 2);
    }

    [Fact]
    public void APieceCarryingSeveralWordsIsStillSpacedOutWhenJustified()
    {
        // Under justification a piece is drawn word by word, walking its own range for the spaces.
        // The walk has to find the gap inside a piece and stop at the piece's end rather than at
        // the end of the segment it points into.
        using var document = WordDocument.Create();

        document.AddParagraph(string.Join(' ', Enumerable.Repeat(
            "pendapatan wilayah pertumbuhan laporan", 14))).Alignment = ParagraphAlignment.Justify;

        var lines = Lines(document);
        var first = lines[0].OrderBy(f => f.X).ToList();

        // Every line but the last is stretched to the measure, and the words are separate.
        Assert.True(first.Count > 1, "A justified line was merged into one piece.");

        var right = first[^1].X + first[^1].Width;
        var natural = first.Sum(f => f.Width);

        Assert.True(right - first[0].X > natural,
            $"A justified line spans {right - first[0].X:0.0} points for {natural:0.0} points of " +
            "text, so its spaces were not stretched.");

        // No word came out with a space glued to it, which is what a walk that ran past the piece's
        // end would produce.
        Assert.All(first, f => Assert.Equal(f.Text.Trim(), f.Text));
    }
}
