// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using PdfNet.Text;
using WordNet.Drawing;
using WordNet.Export;
using Xunit;

namespace WordNet.Tests;

public class FloatingExportTests
{
    private const string Body =
        "Pendapatan naik tiga puluh dua persen dibanding kuartal sebelumnya, dengan " +
        "pertumbuhan terbesar datang dari wilayah Jakarta dan Bandung. Biaya operasional " +
        "turun tipis setelah konsolidasi gudang di Surabaya selesai pada bulan kedua, dan " +
        "marjin kotor tetap di kisaran empat puluh persen sejalan dengan rencana tahunan.";

    private static IReadOnlyList<TextFragment> Fragments(WordDocument document)
    {
        using var pdf = WordToPdf.Convert(document);
        return TextExtractor.ExtractFragments(pdf.Pages[0]);
    }

    [Fact]
    public void AFloatingShapeDoesNotPushTheParagraphDown()
    {
        // The bug this replaces: every drawing went down the inline path, so a text box anchored to
        // a paragraph moved the paragraph down by the box's height and printed it in the wrong
        // place. The text has to start where it would have started with no box at all.
        using var plain = WordDocument.Create();
        plain.AddParagraph(Body);

        using var floated = WordDocument.Create();
        floated.AddParagraph(Body).AddTextBox(
            "Kutipan.", Length.FromCentimeters(5), Length.FromCentimeters(3))
            .MoveTo(Length.FromCentimeters(10), Length.FromCentimeters(0.5));

        var plainFirst = Fragments(plain)[0];
        var floatedFirst = Fragments(floated).First(f => f.Text.StartsWith("Pendapatan", StringComparison.Ordinal));

        Assert.Equal(plainFirst.Y, floatedFirst.Y, 1);
        Assert.Equal(plainFirst.X, floatedFirst.X, 1);
    }

    [Fact]
    public void TextFlowsAroundAFloatRatherThanUnderIt()
    {
        using var document = WordDocument.Create();

        document.AddParagraph(Body).AddTextBox(
            "Kutipan.", Length.FromCentimeters(6), Length.FromCentimeters(4))
            .MoveTo(Length.FromCentimeters(9), Length.FromCentimeters(0));

        // The box starts 9 cm into the column, so no body word may begin past that.
        var boxLeft = 72 + Length.FromCentimeters(9).Points;

        var body = Fragments(document)
            .Where(f => !f.Text.Contains("Kutipan", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(body);
        Assert.All(body, f => Assert.True(f.X < boxLeft,
            $"\"{f.Text}\" starts at {f.X:0.#}, inside the float at {boxLeft:0.#}."));
    }

    [Fact]
    public void ShapeTextIsDrawnInsideTheShape()
    {
        using var document = WordDocument.Create();

        document.AddParagraph("Body.").AddTextBox(
            "Kutipan.", Length.FromCentimeters(6), Length.FromCentimeters(3))
            .MoveTo(Length.FromCentimeters(8), Length.FromCentimeters(1));

        var quote = Fragments(document).Single(f => f.Text.Contains("Kutipan", StringComparison.Ordinal));

        // The box's left edge, plus Word's default 0.1 inch inset.
        var expected = 72 + Length.FromCentimeters(8).Points + Length.FromEmu(91440).Points;

        Assert.Equal(expected, quote.X, 1);
    }

    [Fact]
    public void ATopAndBottomWrapStopsTheTextEntirely()
    {
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph(Body);

        paragraph.AddShape(ShapeGeometry.Rectangle,
            Length.FromCentimeters(15), Length.FromCentimeters(2), TextWrap.TopAndBottom)
            .MoveTo(Length.FromCentimeters(0), Length.FromCentimeters(1.5));

        // The banner spans the column, so no line may sit level with it — the text stops above and
        // resumes below rather than squeezing into a margin.
        var top = 72 + Length.FromCentimeters(1.5).Points;
        var bottom = top + Length.FromCentimeters(2).Points;

        foreach (var fragment in Fragments(document))
        {
            // Fragment Y is a baseline measured up from the page bottom; the band is measured down
            // from the top.
            var fromTop = 842 - fragment.Y;

            Assert.False(fromTop > top && fromTop < bottom,
                $"\"{fragment.Text}\" sits at {fromTop:0.#}, inside the banner's {top:0.#}-{bottom:0.#}.");
        }
    }

    [Fact]
    public void AnInlineShapeStillTakesItsOwnSpace()
    {
        // Inline is the other half of the split: it is laid out where it sits, so it does push the
        // text after it down.
        using var withShape = WordDocument.Create();
        withShape.AddParagraph("Before.").AddShape(
            ShapeGeometry.Ellipse, Length.FromCentimeters(3), Length.FromCentimeters(3), wrap: null);
        withShape.AddParagraph("After.");

        using var plain = WordDocument.Create();
        plain.AddParagraph("Before.");
        plain.AddParagraph("After.");

        var shifted = Fragments(withShape).Single(f => f.Text.StartsWith("After", StringComparison.Ordinal));
        var unshifted = Fragments(plain).Single(f => f.Text.StartsWith("After", StringComparison.Ordinal));

        Assert.True(shifted.Y < unshifted.Y - 50,
            "An inline shape should push the following paragraph down by roughly its height.");
    }

    [Fact]
    public void ARotatedShapeRotatesItsText()
    {
        using var document = WordDocument.Create();

        var stamp = document.AddParagraph("Body.").AddTextBox(
            "DISETUJUI", Length.FromCentimeters(4), Length.FromCentimeters(1.4));

        stamp.Rotation = -12;
        stamp.MoveTo(Length.FromCentimeters(8), Length.FromCentimeters(0.5));

        var text = Fragments(document).Single(f => f.Text.Contains("DISETUJUI", StringComparison.Ordinal));

        // DrawingML rotation is clockwise; PDF reports the baseline anticlockwise, so -12 degrees of
        // shape rotation is +12 degrees of baseline.
        Assert.Equal(12, text.Rotation, 1);
    }

    [Fact]
    public void UprightTextReportsNoRotation()
    {
        // Floating-point drift makes an untransformed baseline come back as something like 1e-15,
        // and every consumer downstream then treats ordinary text as rotated.
        using var document = WordDocument.Create();
        document.AddParagraph(Body);

        Assert.All(Fragments(document), f => Assert.Equal(0, f.Rotation));
    }

    [Fact]
    public void ARotatedFragmentReportsItsFullWidth()
    {
        // Width measured as the horizontal projection shrinks with the angle, and reads as zero for
        // text turned on its side.
        using var upright = WordDocument.Create();
        upright.AddParagraph("Body.").AddTextBox(
            "DISETUJUI", Length.FromCentimeters(4), Length.FromCentimeters(1.4));

        using var turned = WordDocument.Create();
        var stamp = turned.AddParagraph("Body.").AddTextBox(
            "DISETUJUI", Length.FromCentimeters(4), Length.FromCentimeters(1.4));
        stamp.Rotation = 30;

        var flat = Fragments(upright).Single(f => f.Text.Contains("DISETUJUI", StringComparison.Ordinal));
        var tilted = Fragments(turned).Single(f => f.Text.Contains("DISETUJUI", StringComparison.Ordinal));

        Assert.Equal(flat.Width, tilted.Width, 1);
    }

    [Fact]
    public void AWatermarkShapeSitsBehindTheTextAndDoesNotMoveIt()
    {
        using var plain = WordDocument.Create();
        plain.AddParagraph(Body);

        using var stamped = WordDocument.Create();
        stamped.AddParagraph(Body).AddShape(
            ShapeGeometry.Ellipse, Length.FromCentimeters(8), Length.FromCentimeters(8),
            TextWrap.BehindText)
            .MoveTo(Length.FromCentimeters(2), Length.FromCentimeters(0));

        // Behind-text excludes nothing: the words fall exactly where they would without it.
        var expected = Fragments(plain).Select(f => (f.Text, X: Math.Round(f.X, 1), Y: Math.Round(f.Y, 1)));
        var actual = Fragments(stamped).Select(f => (f.Text, X: Math.Round(f.X, 1), Y: Math.Round(f.Y, 1)));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AShapeWithNoFillAndNoOutlineStillShowsItsText()
    {
        // A plain text box is the common case and has neither. Painting nothing at all when there is
        // no geometry to paint must not take the words with it.
        using var document = WordDocument.Create();

        var box = document.AddParagraph("Body.").AddTextBox(
            "Kutipan.", Length.FromCentimeters(6), Length.FromCentimeters(3));

        box.FillColor = null;
        box.LineColor = null;

        Assert.Contains(Fragments(document), f => f.Text.Contains("Kutipan", StringComparison.Ordinal));
    }

    [Fact]
    public void ShapeTextIsClippedToItsBox()
    {
        // Word stops drawing at the bottom of the box. Letting the overflow through would spill a
        // paragraph of words across whatever is underneath.
        using var document = WordDocument.Create();

        var box = document.AddParagraph("Body.").AddTextBox(
            "Baris pertama.", Length.FromCentimeters(6), Length.FromCentimeters(1.2));

        for (var i = 0; i < 20; i++)
        {
            box.AddParagraph($"Baris tambahan nomor {i}.");
        }

        var inside = Fragments(document)
            .Count(f => f.Text.Contains("Baris", StringComparison.Ordinal));

        Assert.InRange(inside, 1, 4);
    }

    [Fact]
    public void AFloatPositionedOffThePageIsClampedRatherThanLost()
    {
        using var document = WordDocument.Create();

        document.AddParagraph("Body.").AddTextBox(
            "Kutipan.", Length.FromCentimeters(5), Length.FromCentimeters(2))
            .MoveTo(Length.FromCentimeters(40), Length.FromCentimeters(1));

        var quote = Fragments(document).Single(f => f.Text.Contains("Kutipan", StringComparison.Ordinal));

        Assert.InRange(quote.X, 0, 595);
    }

    [Fact]
    public void APictureWithASquareWrapFloatsToo()
    {
        // Pictures and shapes share the anchor: the split is by container, not by content. A picture
        // given a wrap has to stop pushing the paragraph down and start excluding it.
        using var plain = WordDocument.Create();
        plain.AddParagraph(Body);

        using var illustrated = WordDocument.Create();
        var paragraph = illustrated.AddParagraph(Body);

        paragraph.AddRun().AddPicture(TinyPng(),
            Length.FromCentimeters(5), Length.FromCentimeters(4),
            wrap: TextWrap.Square);

        var picture = Assert.Single(paragraph.Runs[^1].Drawings);
        picture.MoveTo(Length.FromCentimeters(10), Length.FromCentimeters(0));

        Assert.True(picture.IsFloating);

        var plainFirst = Fragments(plain)[0];
        var floatedFirst = Fragments(illustrated)[0];

        Assert.Equal(plainFirst.Y, floatedFirst.Y, 1);

        var edge = 72 + Length.FromCentimeters(10).Points;
        Assert.All(Fragments(illustrated), f => Assert.True(f.X < edge,
            $"\"{f.Text}\" starts at {f.X:0.#}, inside the picture at {edge:0.#}."));
    }

    [Fact]
    public void APictureIsStillInlineByDefault()
    {
        // The default has to stay what it was: adding the wrap parameter must not turn every
        // existing caller's picture into a floating one.
        using var document = WordDocument.Create();

        var run = document.AddParagraph("Body.").AddRun();
        run.AddPicture(TinyPng(), Length.FromCentimeters(3), Length.FromCentimeters(3));

        Assert.False(Assert.Single(run.Drawings).IsFloating);
    }

    private static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
