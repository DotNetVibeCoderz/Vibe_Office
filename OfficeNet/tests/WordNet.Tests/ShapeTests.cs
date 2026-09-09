// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using WordNet;
using WordNet.Drawing;
using Xunit;

namespace WordNet.Tests;

public class ShapeTests
{
    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static XElement DocumentRoot(byte[] bytes)
    {
        using var package = OpcPackage.Open(new MemoryStream(bytes, writable: false));
        return package.Parts.Single(p => p.Name.ToString() == "/word/document.xml").Xml.Root!;
    }

    private static WordDocument WithTextBox(out Shape shape)
    {
        var document = WordDocument.Create();

        shape = document.AddParagraph("Body text.").AddTextBox(
            "A pull quote.", Length.FromCentimeters(6), Length.FromCentimeters(3));

        return document;
    }

    [Fact]
    public void ATextBoxProducesAValidDocument()
    {
        using var document = WithTextBox(out var shape);

        Assert.Equal("A pull quote.", shape.Text);

        var report = OpcValidator.Validate(Save(document));
        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void ATextBoxIsMarkedAsOne()
    {
        // Without txBox="1" Word treats the shape as a drawing that happens to contain words: the
        // text is there, but clicking it selects the shape rather than putting a caret in it.
        using var document = WithTextBox(out var shape);

        Assert.Equal("1", shape.Wsp.Element(Ns.Wps + "cNvSpPr")!.Attr("txBox"));
    }

    [Fact]
    public void AnEmptyShapeCarriesNoTextBody()
    {
        // Word reports an empty w:txbxContent as unreadable content, so a plain shape must not have
        // one at all.
        using var document = WordDocument.Create();

        var shape = document.AddParagraph().AddShape(
            ShapeGeometry.Ellipse, Length.FromCentimeters(3), Length.FromCentimeters(3));

        Assert.Null(shape.Wsp.Element(Ns.Wps + "txbx"));
        Assert.Empty(shape.Paragraphs);

        var report = OpcValidator.Validate(Save(document));
        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void TheTextBodyGoesBeforeBodyPr()
    {
        // wps:wsp is a schema sequence and bodyPr is last. Appending the text body would put it
        // after bodyPr, which Word refuses to open.
        using var document = WithTextBox(out var shape);

        var names = shape.Wsp.Elements().Select(e => e.Name.LocalName).ToList();

        Assert.Equal(["cNvSpPr", "spPr", "txbx", "bodyPr"], names);
    }

    [Fact]
    public void AnAnchorCarriesEveryMandatoryAttribute()
    {
        // None of these has a schema default. A missing one is not ignored — Word reports the whole
        // document as unreadable, with no hint which attribute it was.
        using var document = WithTextBox(out _);

        var anchor = DocumentRoot(Save(document)).Descendants(Ns.Wp + "anchor").Single();

        string[] required =
        [
            "distT", "distB", "distL", "distR",
            "simplePos", "relativeHeight", "behindDoc", "locked", "layoutInCell", "allowOverlap",
        ];

        Assert.All(required, name => Assert.NotNull(anchor.Attr(name)));
    }

    [Fact]
    public void AnAnchorsChildrenFollowTheSchemaSequence()
    {
        using var document = WithTextBox(out var shape);

        shape.MoveTo(Length.FromCentimeters(2), Length.FromCentimeters(1));

        var anchor = DocumentRoot(Save(document)).Descendants(Ns.Wp + "anchor").Single();

        Assert.Null(OpcValidator.CheckChildOrder(anchor,
            "simplePos", "positionH", "positionV", "extent", "effectExtent",
            "wrapNone", "wrapSquare", "wrapTight", "wrapThrough", "wrapTopAndBottom",
            "docPr", "cNvGraphicFramePr", "graphic"));
    }

    [Fact]
    public void MovingAShapeTwiceLeavesOnePositionEach()
    {
        // The setter removes before it inserts. Without that, a second MoveTo appends a duplicate
        // positionH and Word takes the first one — so the shape ignores the move.
        using var document = WithTextBox(out var shape);

        shape.MoveTo(Length.FromCentimeters(2), Length.FromCentimeters(1));
        shape.MoveTo(Length.FromCentimeters(5), Length.FromCentimeters(4), fromVertical: VerticalAnchor.Page);

        var anchor = DocumentRoot(Save(document)).Descendants(Ns.Wp + "anchor").Single();

        Assert.Single(anchor.Elements(Ns.Wp + "positionH"));
        Assert.Single(anchor.Elements(Ns.Wp + "positionV"));
        Assert.Equal("page", anchor.Element(Ns.Wp + "positionV")!.Attr("relativeFrom"));
        Assert.Equal(Length.FromCentimeters(5).Emu, shape.Offset.X.Emu);
        Assert.Equal(Length.FromCentimeters(4).Emu, shape.Offset.Y.Emu);
    }

    [Fact]
    public void AnInlineShapeHasNoPositionAndSaysSo()
    {
        using var document = WordDocument.Create();

        var shape = document.AddParagraph("Before ").AddShape(
            ShapeGeometry.Star, Length.FromPoints(20), Length.FromPoints(20), wrap: null);

        Assert.False(shape.IsFloating);
        Assert.Null(shape.Wrap);

        var exception = Assert.Throws<OfficeNetException>(() =>
            shape.MoveTo(Length.FromPoints(10), Length.FromPoints(10)));

        Assert.Contains("inline", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResizingMovesBothCopiesOfTheSize()
    {
        // The size is stored on wp:extent, which is the space reserved, and on a:ext, which is the
        // box drawn into. Moving one and not the other clips the shape or leaves a gap.
        using var document = WithTextBox(out var shape);

        shape.Resize(Length.FromCentimeters(10), Length.FromCentimeters(4));

        var anchor = DocumentRoot(Save(document)).Descendants(Ns.Wp + "anchor").Single();
        var extent = anchor.Element(Ns.Wp + "extent")!;
        var ext = anchor.Descendants(Ns.A + "ext").Single();

        Assert.Equal(Length.FromCentimeters(10).Emu.ToString(), extent.Attr("cx"));
        Assert.Equal(extent.Attr("cx"), ext.Attr("cx"));
        Assert.Equal(extent.Attr("cy"), ext.Attr("cy"));
    }

    [Fact]
    public void FillAndOutlineGoInTheOrderSpPrRequires()
    {
        // a:spPr is a sequence: geometry, then fill, then outline. Setting the fill after the
        // outline would append it, and Word rejects the part.
        using var document = WithTextBox(out var shape);

        shape.LineColor = OfficeColor.FromRgb(0x1F, 0x3A, 0x5F);
        shape.LineWidth = Length.FromPoints(1.5);
        shape.FillColor = OfficeColor.FromRgb(0xF2, 0xEC, 0xE3);

        var spPr = shape.Wsp.Element(Ns.Wps + "spPr")!;

        Assert.Equal(["xfrm", "prstGeom", "solidFill", "ln"],
            spPr.Elements().Select(e => e.Name.LocalName));

        Assert.Equal("F2ECE3", shape.FillColor?.ToHex());
        Assert.Equal("1F3A5F", shape.LineColor?.ToHex());
        Assert.Equal(Length.FromPoints(1.5).Emu, shape.LineWidth.Emu);

        var report = OpcValidator.Validate(Save(document));
        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void SettingTheFillTwiceReplacesItRatherThanStacking()
    {
        using var document = WithTextBox(out var shape);

        shape.FillColor = OfficeColor.Red;
        shape.FillColor = OfficeColor.FromRgb(0x00, 0x66, 0xCC);

        Assert.Single(shape.Wsp.Element(Ns.Wps + "spPr")!.Elements(Ns.A + "solidFill"));
        Assert.Equal("0066CC", shape.FillColor?.ToHex());

        shape.FillColor = null;

        Assert.Empty(shape.Wsp.Element(Ns.Wps + "spPr")!.Elements(Ns.A + "solidFill"));
        Assert.Single(shape.Wsp.Element(Ns.Wps + "spPr")!.Elements(Ns.A + "noFill"));
        Assert.Null(shape.FillColor);
    }

    [Fact]
    public void RotationIsStoredInSixtyThousandthsOfADegree()
    {
        using var document = WithTextBox(out var shape);

        shape.Rotation = 45;

        Assert.Equal("2700000",
            shape.Wsp.Descendants(Ns.A + "xfrm").Single().Attr("rot"));
        Assert.Equal(45, shape.Rotation, 6);

        // Zero means no rotation, and Word writes no attribute for it.
        shape.Rotation = 0;
        Assert.Null(shape.Wsp.Descendants(Ns.A + "xfrm").Single().Attr("rot"));
    }

    [Theory]
    [InlineData(TextWrap.Square, "wrapSquare")]
    [InlineData(TextWrap.Tight, "wrapTight")]
    [InlineData(TextWrap.TopAndBottom, "wrapTopAndBottom")]
    [InlineData(TextWrap.InFrontOfText, "wrapNone")]
    [InlineData(TextWrap.BehindText, "wrapNone")]
    public void EachWrapWritesItsOwnElement(TextWrap wrap, string expected)
    {
        using var document = WordDocument.Create();

        document.AddParagraph("Body.").AddShape(
            ShapeGeometry.Rectangle, Length.FromCentimeters(4), Length.FromCentimeters(2), wrap);

        var anchor = DocumentRoot(Save(document)).Descendants(Ns.Wp + "anchor").Single();

        Assert.NotNull(anchor.Element(Ns.Wp + expected));
    }

    [Fact]
    public void BehindTextAndInFrontOfTextDifferOnlyInBehindDoc()
    {
        // Both write wrapNone, so behindDoc is the only thing that tells a watermark from a stamp.
        // Reading the wrap back has to look at it, or every no-wrap shape reads as InFrontOfText.
        using var document = WordDocument.Create();

        var behind = document.AddParagraph().AddShape(
            ShapeGeometry.Rectangle, Length.FromCentimeters(4), Length.FromCentimeters(2),
            TextWrap.BehindText);

        var front = document.AddParagraph().AddShape(
            ShapeGeometry.Rectangle, Length.FromCentimeters(4), Length.FromCentimeters(2),
            TextWrap.InFrontOfText);

        Assert.Equal("1", behind.Container.Attr("behindDoc"));
        Assert.Equal("0", front.Container.Attr("behindDoc"));
        Assert.Equal(TextWrap.BehindText, behind.Wrap);
        Assert.Equal(TextWrap.InFrontOfText, front.Wrap);
    }

    [Fact]
    public void EachShapeGetsItsOwnDocPrId()
    {
        // Word tolerates duplicate drawing ids in some versions and calls the file corrupt in
        // others, which makes it exactly the kind of bug that only appears on someone else's
        // machine.
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Body.");

        for (var i = 0; i < 5; i++)
        {
            paragraph.AddShape(ShapeGeometry.Rectangle,
                Length.FromCentimeters(2), Length.FromCentimeters(1));
        }

        var ids = DocumentRoot(Save(document)).Descendants(Ns.Wp + "docPr")
            .Select(e => e.Attr("id"))
            .ToList();

        Assert.Equal(5, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ShapesAreFoundAndPicturesAreNot()
    {
        // A w:drawing holds either a shape or a picture, so the two have to be told apart by what
        // is inside the graphicData rather than by the drawing itself.
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Body.");

        paragraph.AddRun().AddPicture(TinyPng(), Length.FromCentimeters(2), Length.FromCentimeters(2));
        paragraph.AddTextBox("Quote", Length.FromCentimeters(5), Length.FromCentimeters(2));

        Assert.Single(document.Shapes);
        Assert.Equal("Quote", document.Shapes[0].Text);
        Assert.Single(paragraph.Shapes);
    }

    [Fact]
    public void RemovingAShapeTakesItsRunWithIt()
    {
        // The run exists only to hold the drawing. Leaving it behind puts an empty run in the
        // paragraph, which changes nothing visually and everything for anyone reading the runs.
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Body.");

        var shape = paragraph.AddTextBox("Quote", Length.FromCentimeters(5), Length.FromCentimeters(2));
        var runsBefore = paragraph.Runs.Count;

        shape.Remove();

        Assert.Empty(document.Shapes);
        Assert.Equal(runsBefore - 1, paragraph.Runs.Count);
        Assert.Equal("Body.", paragraph.Text);
    }

    [Fact]
    public void AShapeSurvivesASaveAndReopen()
    {
        byte[] bytes;

        using (var document = WithTextBox(out var shape))
        {
            shape.FillColor = OfficeColor.FromRgb(0xF2, 0xEC, 0xE3);
            shape.MoveTo(Length.FromCentimeters(1), Length.FromCentimeters(2), fromVertical: VerticalAnchor.Page);
            shape.AddParagraph("Second line.");
            bytes = Save(document);
        }

        using var reopened = WordDocument.Open(new MemoryStream(bytes, writable: false));
        var reloaded = Assert.Single(reopened.Shapes);

        Assert.Equal("A pull quote.\nSecond line.", reloaded.Text);
        Assert.Equal("F2ECE3", reloaded.FillColor?.ToHex());
        Assert.Equal(TextWrap.Square, reloaded.Wrap);
        Assert.Equal(Length.FromCentimeters(1).Emu, reloaded.Offset.X.Emu);
        Assert.Equal(Length.FromCentimeters(6).Emu, reloaded.Width.Emu);
    }

    [Fact]
    public void ShapeTextIsNotCountedAsBodyText()
    {
        // A text box floats over the page; its words are not part of the paragraph it is anchored
        // to. Letting them leak into Text would corrupt every extraction and every search.
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Body text.");

        paragraph.AddTextBox("A pull quote.", Length.FromCentimeters(6), Length.FromCentimeters(3));

        Assert.Equal("Body text.", paragraph.Text);
        Assert.DoesNotContain("pull quote", document.ExtractText(), StringComparison.Ordinal);
    }

    /// <summary>The smallest valid PNG: one transparent pixel.</summary>
    private static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
