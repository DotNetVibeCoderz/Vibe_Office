// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using PdfNet.Objects;
using PowerPointNet.Shapes;
using Xunit;

namespace PowerPointNet.Tests;

public class FreeformTests
{
    private static Length Cm(double value) => Length.FromCentimeters(value);

    private static string PageContent(Presentation deck)
    {
        using var pdf = deck.ToPdf();
        return string.Concat(pdf.Pages[0].Dictionary.GetArray(PdfName.Contents)
            .Select(pdf.Follow).OfType<PdfStream>().Select(s => s.DecodedText));
    }

    [Fact]
    public void AFreeformIsWrittenInSchemaOrderAndReadsBack()
    {
        using var deck = Presentation.Create();
        var slide = deck.AddSlide(3);

        var outline = new FreeformPath(100, 50) { Fill = false }
            .MoveTo(0, 25).QuadTo(25, 0, 50, 25).CubicTo(60, 40, 90, 40, 100, 25).ArcTo(10, 5, 0, 90);
        var triangle = FreeformPath.Polygon(100, 50, (50, 0), (100, 50), (0, 50));

        slide.AddFreeform([outline, triangle], Cm(2), Cm(2), Cm(10), Cm(5), OfficeColor.FromRgb(0xE0, 0x20, 0x40),
            OfficeColor.Black);

        var bytes = deck.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var shape = reopened.Slides[0].Shapes.Single();
        var geometry = shape.Element.Descendants(Ns.A + "custGeom").Single();

        Assert.Equal(["avLst", "gdLst", "ahLst", "cxnLst", "rect", "pathLst"], geometry.Elements().Select(e => e.Name.LocalName));

        var paths = shape.FreeformPaths;
        Assert.Equal(2, paths.Count);
        Assert.Equal((100.0, 50.0, false, true), (paths[0].Width, paths[0].Height, paths[0].Fill, paths[0].Stroke));
        Assert.Equal(["moveTo", "quadBezTo", "cubicBezTo", "arcTo"], paths[0].Commands.Select(c => c.Kind));
        Assert.Equal([10.0, 5, 0, 90], paths[0].Commands[3].Values);
        Assert.Equal(["moveTo", "lnTo", "lnTo", "close"], paths[1].Commands.Select(c => c.Kind));
        Assert.Equal([100.0, 50], paths[1].Commands[1].Values);
    }

    [Fact]
    public void AnArcIsPlacedByItsVisualAngles()
    {
        // A half ellipse from its left end, over the top, to its right: in a 200 × 100 grid scaled
        // to 8 × 4 cm, it ends at the right edge, halfway down.
        using var deck = Presentation.Create();
        var moon = new FreeformPath(200, 100).MoveTo(0, 50).ArcTo(100, 50, 180, 180).Close();
        deck.AddSlide(3).AddFreeform(moon, Cm(2), Cm(2), Cm(8), Cm(4), OfficeColor.FromRgb(0x20, 0x60, 0xC0));

        var content = PageContent(deck);
        var curves = Regex.Matches(content, @"(\S+) (\S+) c\n").ToList();

        Assert.Equal(2, curves.Count);   // two quarter turns

        var endX = double.Parse(curves[^1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Cm(10).Points, endX, 2);

        // Over the top: the first quarter ends at the topmost point, centre x, top edge.
        var top = double.Parse(curves[0].Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var pageHeight = deck.SlideHeight.Points;
        Assert.Equal(Cm(2).Points, pageHeight - top, 2);
    }

    /// <summary>The point of an ellipse in a given direction from its centre — a visual angle.</summary>
    private static (double X, double Y) Visual(double cx, double cy, double rx, double ry, double degrees)
    {
        var a = degrees * Math.PI / 180;
        var r = rx * ry / Math.Sqrt(Math.Pow(ry * Math.Cos(a), 2) + Math.Pow(rx * Math.Sin(a), 2));
        return (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    }

    // A quarter turn of visual angle is not a quarter of the ellipse's parameter on a flat ellipse:
    // 45 to 135 degrees is about 53 degrees of parameter (one segment), 45 to -45 about 127 (two).
    // Going the long way round would end at the same point but take three or more.
    [Theory]
    [InlineData(45, 90, 135, 1)]
    [InlineData(45, -90, -45, 2)]
    public void AnArcOnAFlatEllipseEndsInTheDirectionItsAnglesSay(double start, double swing, double end, int segments)
    {
        // On a 2:1 ellipse a visual angle and the ellipse's parameter differ everywhere but on the
        // axes, so this is where reading one as the other would show.
        var (sx, sy) = Visual(100, 50, 100, 50, start);
        var (ex, ey) = Visual(100, 50, 100, 50, end);

        using var deck = Presentation.Create();
        var arc = new FreeformPath(200, 100) { Fill = false }.MoveTo(sx, sy).ArcTo(100, 50, start, swing);
        deck.AddSlide(3).AddFreeform(arc, Cm(0), Cm(0), Length.FromPoints(200), Length.FromPoints(100), null, OfficeColor.Black);

        var curves = Regex.Matches(PageContent(deck), @"(\S+) (\S+) c\n");

        Assert.Equal(segments, curves.Count);
        var last = curves[^1];
        var x = double.Parse(last.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var y = deck.SlideHeight.Points - double.Parse(last.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

        // The path's points are whole numbers in the file, so allow that rounding.
        Assert.Equal(ex, x, 0.6);
        Assert.Equal(ey, y, 0.6);
    }

    [Fact]
    public void AQuadraticIsDrawnAsACurve()
    {
        using var deck = Presentation.Create();
        var hump = new FreeformPath(100, 100) { Fill = false }.MoveTo(0, 100).QuadTo(50, 0, 100, 100);
        deck.AddSlide(3).AddFreeform(hump, Cm(2), Cm(2), Cm(4), Cm(4), null, OfficeColor.Black);

        Assert.Single(Regex.Matches(PageContent(deck), @" c\n"));
    }

    [Fact]
    public void AnUnfilledPathIsOnlyStrokedAndAFilledOneFilled()
    {
        using var deck = Presentation.Create();
        var line = new FreeformPath(100, 10) { Fill = false }.MoveTo(0, 5).LineTo(100, 5);
        deck.AddSlide(3).AddFreeform(line, Cm(2), Cm(2), Cm(10), Cm(1), OfficeColor.FromRgb(200, 0, 0), OfficeColor.Black);

        var content = PageContent(deck);

        // Stroked, never filled — or the unfilled stroke would paint red.
        Assert.Matches(@"l\nS\n", content);
        Assert.DoesNotMatch(@"l\n(f|B)\n", content);
    }

    [Fact]
    public void AFreeformInGuideFormulasIsLeftToPowerPointAndDrawsAsItsBox()
    {
        // A path whose coordinates are guide names only PowerPoint's engine evaluates.
        using var deck = Presentation.Create();
        var shape = deck.AddSlide(3).AddFreeform(FreeformPath.Polygon(10, 10, (0, 0), (10, 0), (5, 10)),
            Cm(2), Cm(2), Cm(4), Cm(4), OfficeColor.FromRgb(0, 0x80, 0));
        shape.Element.Descendants(Ns.A + "pt").First().SetAttributeValue("x", "wd2");

        Assert.Empty(shape.FreeformPaths);
        Assert.Contains(" re\n", PageContent(deck), StringComparison.Ordinal);
    }

    [Fact]
    public void APathMustStartWithAMoveAndAPolygonNeedsThreePoints()
    {
        Assert.Throws<InvalidOperationException>(() => new FreeformPath(10, 10).LineTo(1, 1));
        Assert.Throws<ArgumentException>(() => FreeformPath.Polygon(10, 10, (0, 0), (1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FreeformPath(0, 10));
    }
}
