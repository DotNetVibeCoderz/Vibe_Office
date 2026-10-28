// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using PdfNet.Objects;
using PowerPointNet.Shapes;
using Xunit;

namespace PowerPointNet.Tests;

public class ConnectorTests
{
    private static Length Cm(double value) => Length.FromCentimeters(value);

    [Fact]
    public void ALineDrawnRightToLeftIsABoxWithAFlipAndReadsBackItsEnds()
    {
        using var deck = Presentation.Create();
        var slide = deck.AddSlide(3);
        var line = slide.AddLine(Cm(9), Cm(9), Cm(4), Cm(7), OfficeColor.FromRgb(200, 0, 0), Length.FromPoints(3));
        line.EndArrow = ArrowHead.Diamond;
        line.Dash = LineDash.DashDot;

        var bytes = deck.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var read = Assert.Single(reopened.Slides[0].Connectors);

        Assert.Equal((Cm(9).Emu, Cm(9).Emu), (read.Start.X.Emu, read.Start.Y.Emu));
        Assert.Equal((Cm(4).Emu, Cm(7).Emu), (read.End.X.Emu, read.End.Y.Emu));
        Assert.Equal(ArrowHead.Diamond, read.EndArrow);
        Assert.Equal(ArrowHead.None, read.StartArrow);
        Assert.Equal(LineDash.DashDot, read.Dash);
        Assert.Equal(OfficeColor.FromRgb(200, 0, 0), read.LineColor);
        Assert.Equal(ShapeKind.Connector, read.Kind);

        var transform = read.Element.Descendants(Ns.A + "xfrm").Single();
        Assert.Equal("1", transform.Attribute("flipH")?.Value);
        Assert.Equal("1", transform.Attribute("flipV")?.Value);
    }

    [Fact]
    public void AConnectorIsGluedToTheSidesThatFaceEachOther()
    {
        using var deck = Presentation.Create();
        var slide = deck.AddSlide(3);
        var left = slide.AddShape(ShapeGeometry.Rectangle, Cm(2), Cm(3), Cm(5), Cm(2), "A");
        var right = slide.AddShape(ShapeGeometry.Rectangle, Cm(14), Cm(3), Cm(5), Cm(2), "B");
        var below = slide.AddShape(ShapeGeometry.Rectangle, Cm(14), Cm(11), Cm(5), Cm(2), "C");

        var across = slide.AddConnector(left, right);
        var down = slide.AddConnector(right, below, ConnectorType.Elbow);

        // Across: from the right of A (site 3) to the left of B (site 1), level.
        Assert.Equal((left.Id, right.Id), (across.StartShapeId, across.EndShapeId));
        Assert.Equal((Cm(7).Emu, Cm(4).Emu), (across.Start.X.Emu, across.Start.Y.Emu));
        Assert.Equal((Cm(14).Emu, Cm(4).Emu), (across.End.X.Emu, across.End.Y.Emu));
        Assert.Equal(["3", "1"], Sites(across));

        // Back to the left: from the left of B (site 1) to the right of A (site 3).
        Assert.Equal(["1", "3"], Sites(slide.AddConnector(right, left)));

        // Down: from the bottom of B (site 2) to the top of C (site 0).
        Assert.Equal(["2", "0"], Sites(down));
        Assert.Equal(ConnectorType.Elbow, down.Type);
        Assert.Equal("bentConnector3", down.Element.Descendants(Ns.A + "prstGeom").Single().Attribute("prst")!.Value);

        PptxValidator.AssertValid(deck.ToArray());

        static string[] Sites(Connector c) =>
            [.. c.Element.Descendants().Where(e => e.Name.LocalName is "stCxn" or "endCxn").Select(e => e.Attribute("idx")!.Value)];
    }

    [Fact]
    public void AShapeWithUnknownSitesIsJoinedButNotGlued()
    {
        // An ellipse numbers its eight sites differently; gluing by the rectangle's numbers would
        // attach to the wrong point, so the end is placed and left free.
        using var deck = Presentation.Create();
        var slide = deck.AddSlide(3);
        var box = slide.AddShape(ShapeGeometry.Rectangle, Cm(2), Cm(3), Cm(5), Cm(2));
        var oval = slide.AddShape(ShapeGeometry.Ellipse, Cm(14), Cm(3), Cm(5), Cm(2));

        var connector = slide.AddConnector(box, oval, ConnectorType.Curved);

        Assert.Equal(box.Id, connector.StartShapeId);
        Assert.Null(connector.EndShapeId);
        Assert.Equal(Cm(14).Emu, connector.End.X.Emu);
        Assert.Throws<OfficeNetException>(() => slide.AddConnector(box, box));
    }

    [Fact]
    public void LineElementsAreInSchemaOrder()
    {
        using var deck = Presentation.Create();
        var line = deck.AddSlide(3).AddLine(Cm(1), Cm(1), Cm(5), Cm(1));

        // Set in the wrong order on purpose: tail, then dash, then head.
        line.EndArrow = ArrowHead.Triangle;
        line.Dash = LineDash.Dash;
        line.StartArrow = ArrowHead.Oval;

        var ln = line.Element.Descendants(Ns.A + "ln").Single();
        Assert.Equal(["solidFill", "prstDash", "headEnd", "tailEnd"], ln.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void ThePdfDrawsTheLinesArrowheadsAndRealGeometry()
    {
        using var deck = Presentation.Create();
        var slide = deck.AddSlide(3);
        slide.AddShape(ShapeGeometry.Ellipse, Cm(2), Cm(3), Cm(5), Cm(3), fill: OfficeColor.FromRgb(0x70, 0xAD, 0x47));
        var line = slide.AddLine(Cm(2), Cm(12), Cm(20), Cm(12), OfficeColor.FromRgb(200, 0, 0));
        line.EndArrow = ArrowHead.Triangle;
        line.Dash = LineDash.Dash;

        using var pdf = deck.ToPdf();
        var content = string.Concat(pdf.Pages[0].Dictionary.GetArray(PdfName.Contents)
            .Select(pdf.Follow).OfType<PdfStream>().Select(s => s.DecodedText));

        // An ellipse is curves, not a rectangle; the line is dashed and red; its head is a filled
        // triangle.
        Assert.Contains(" c\n", content, StringComparison.Ordinal);
        Assert.Matches(@"\[[\d.]+ [\d.]+\] 0 d", content);
        Assert.Matches(@"0\.78\d* 0 0 RG", content);
        Assert.Matches(@"h\s+f", content);

        // The head starts at the line's end — 20 cm across — rather than only being implied by it.
        Assert.Matches(@"566\.929\d* [\d.]+ m", content);
    }
}
