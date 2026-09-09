// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using PowerPointNet.Diagrams;
using Xunit;

namespace PowerPointNet.Tests;

public class SmartArtTests
{
    private static readonly DiagramNode[] Steps =
    [
        new("Kumpulkan"),
        new("Olah"),
        new("Laporkan"),
    ];

    private static byte[] Save(Presentation deck)
    {
        using var stream = new MemoryStream();
        deck.Save(stream);
        return stream.ToArray();
    }

    private static Presentation WithDiagram(DiagramKind kind, out SmartArt diagram,
        IReadOnlyList<DiagramNode>? nodes = null)
    {
        var deck = Presentation.Create();
        diagram = deck.AddSlide(3).AddSmartArt(kind, nodes ?? Steps,
            Units.Cm(2), Units.Cm(3), Units.Cm(20), Units.Cm(8));

        return deck;
    }

    [Fact]
    public void ADiagramProducesAValidPresentation()
    {
        using var deck = WithDiagram(DiagramKind.Process, out var diagram);

        Assert.Equal(["Kumpulkan", "Olah", "Laporkan"], diagram.Nodes);

        PptxValidator.AssertValid(Save(deck));
    }

    [Fact]
    public void ADiagramIsFiveParts()
    {
        // Four standard ones plus the drawing extension. A library that writes only the data part
        // produces a package that validates and a diagram PowerPoint opens as an empty rectangle.
        using var deck = WithDiagram(DiagramKind.List, out _);

        using var package = OpcPackage.Open(new MemoryStream(Save(deck), writable: false));

        var names = package.Parts
            .Select(p => p.Name.ToString())
            .Where(n => n.StartsWith("/ppt/diagrams/", StringComparison.Ordinal))
            .Order()
            .ToList();

        Assert.Equal(
            ["/ppt/diagrams/colors1.xml", "/ppt/diagrams/data1.xml", "/ppt/diagrams/drawing1.xml",
             "/ppt/diagrams/layout1.xml", "/ppt/diagrams/quickStyle1.xml"],
            names);
    }

    [Fact]
    public void TheSlideNamesAllFourRelationshipsAtOnce()
    {
        // dgm:relIds carries dm, lo, qs and cs together. Naming three and omitting the fourth is the
        // usual way to get a diagram that opens as a blank frame with no error.
        using var deck = WithDiagram(DiagramKind.Cycle, out _);

        using var package = OpcPackage.Open(new MemoryStream(Save(deck), writable: false));
        var slide = package.Parts.Single(p => p.Name.ToString() == "/ppt/slides/slide1.xml");

        var relIds = slide.Xml.Descendants(Ns.Dgm + "relIds").Single();

        Assert.All(["dm", "lo", "qs", "cs"], name => Assert.NotNull(relIds.Attr(Ns.R + name)));

        // And each has to resolve to a part of the right type.
        Assert.Equal("/ppt/diagrams/data1.xml",
            slide.RelatedPart(relIds.Attr(Ns.R + "dm")!)!.Name.ToString());
        Assert.Equal("/ppt/diagrams/layout1.xml",
            slide.RelatedPart(relIds.Attr(Ns.R + "lo")!)!.Name.ToString());
        Assert.Equal("/ppt/diagrams/quickStyle1.xml",
            slide.RelatedPart(relIds.Attr(Ns.R + "qs")!)!.Name.ToString());
        Assert.Equal("/ppt/diagrams/colors1.xml",
            slide.RelatedPart(relIds.Attr(Ns.R + "cs")!)!.Name.ToString());
    }

    [Fact]
    public void TheDrawingHangsOffTheDataPartNotTheSlide()
    {
        // Attaching it to the slide produces a package that validates and a diagram PowerPoint draws
        // empty, because that is not where it looks.
        using var deck = WithDiagram(DiagramKind.Process, out _);

        using var package = OpcPackage.Open(new MemoryStream(Save(deck), writable: false));
        var slide = package.Parts.Single(p => p.Name.ToString() == "/ppt/slides/slide1.xml");
        var data = package.Parts.Single(p => p.Name.ToString() == "/ppt/diagrams/data1.xml");

        Assert.Null(slide.RelatedPartByType(RelationshipTypes.DiagramDrawing));
        Assert.Equal("/ppt/diagrams/drawing1.xml",
            data.RelatedPartByType(RelationshipTypes.DiagramDrawing)!.Name.ToString());
    }

    [Fact]
    public void EveryNodeHasItsTwoTransitionPoints()
    {
        // parTrans and sibTrans hold the formatting of the connector between nodes. A diagram
        // missing them opens with its nodes drawn and none of the lines between.
        using var deck = WithDiagram(DiagramKind.Hierarchy, out var diagram);

        var points = diagram.DataPart.Xml.Root!.Element(Ns.Dgm + "ptLst")!
            .Elements(Ns.Dgm + "pt")
            .ToList();

        Assert.Equal(3, points.Count(p => p.Attr("type") == "parTrans"));
        Assert.Equal(3, points.Count(p => p.Attr("type") == "sibTrans"));
        Assert.Single(points, p => p.Attr("type") == "doc");
    }

    [Fact]
    public void EveryConnectionNamesItsTransitionPoints()
    {
        using var deck = WithDiagram(DiagramKind.Process, out var diagram);

        var root = diagram.DataPart.Xml.Root!;
        var ids = root.Element(Ns.Dgm + "ptLst")!.Elements(Ns.Dgm + "pt")
            .Select(p => p.Attr("modelId")!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var connection in root.Element(Ns.Dgm + "cxnLst")!.Elements(Ns.Dgm + "cxn"))
        {
            Assert.Contains(connection.Attr("srcId")!, ids);
            Assert.Contains(connection.Attr("destId")!, ids);
            Assert.Contains(connection.Attr("parTransId")!, ids);
            Assert.Contains(connection.Attr("sibTransId")!, ids);
        }
    }

    [Fact]
    public void TheLayoutPartNamesTheDiagramPowerPointThinksItIs()
    {
        // PowerPoint matches the diagram to its gallery by this id. An unrecognised one leaves the
        // user with a diagram they cannot re-style.
        using var deck = WithDiagram(DiagramKind.Hierarchy, out _);

        using var package = OpcPackage.Open(new MemoryStream(Save(deck), writable: false));
        var layout = package.Parts.Single(p => p.Name.ToString() == "/ppt/diagrams/layout1.xml");

        Assert.Equal("urn:microsoft.com/office/officeart/2005/8/layout/orgChart1",
            layout.Xml.Root!.Attr("uniqueId"));

        // And the data model has to agree with it, or PowerPoint re-lays the diagram out as
        // something else the moment it is opened.
        var root = package.Parts.Single(p => p.Name.ToString() == "/ppt/diagrams/data1.xml")
            .Xml.Root!.Element(Ns.Dgm + "ptLst")!
            .Elements(Ns.Dgm + "pt")
            .Single(p => p.Attr("type") == "doc");

        Assert.Equal("urn:microsoft.com/office/officeart/2005/8/layout/orgChart1",
            root.Element(Ns.Dgm + "prSet")!.Attr("loTypeId"));
    }

    [Fact]
    public void TheDrawingCarriesOneShapePerNode()
    {
        using var deck = WithDiagram(DiagramKind.List, out var diagram);

        var shapes = diagram.DrawingPart.Xml.Root!
            .Element(Ns.Dsp + "spTree")!
            .Elements(Ns.Dsp + "sp")
            .ToList();

        Assert.Equal(3, shapes.Count);

        Assert.Equal(["Kumpulkan", "Olah", "Laporkan"],
            shapes.Select(s => string.Concat(s.Descendants(Ns.A + "t").Select(t => t.Value))));
    }

    [Fact]
    public void ShapesAreLaidOutInsideTheFrame()
    {
        // Coordinates are relative to the frame, not to the slide. Writing slide coordinates here
        // puts the diagram off the page by however far the frame is from the origin.
        var width = Units.Cm(20);
        var height = Units.Cm(8);

        using var deck = WithDiagram(DiagramKind.Process, out var diagram);

        foreach (var shape in diagram.DrawingPart.Xml.Root!
                     .Element(Ns.Dsp + "spTree")!.Elements(Ns.Dsp + "sp"))
        {
            var offset = shape.Descendants(Ns.A + "off").Single();
            var extent = shape.Descendants(Ns.A + "ext").Single();

            Assert.InRange(offset.LongAttr("x"), 0, width.Emu);
            Assert.InRange(offset.LongAttr("y"), 0, height.Emu);
            Assert.InRange(offset.LongAttr("x") + extent.LongAttr("cx"), 0, width.Emu + 1);
            Assert.InRange(offset.LongAttr("y") + extent.LongAttr("cy"), 0, height.Emu + 1);
        }
    }

    [Fact]
    public void APyramidsBandsShareOneSlopeAtAnyBandCount()
    {
        // A fixed trapezoid inset lines the bands up at exactly one band count and at no other, and
        // the failure looks like a stack of unrelated boxes rather than a pyramid.
        for (var count = 2; count <= 6; count++)
        {
            using var deck = WithDiagram(DiagramKind.Pyramid, out var diagram,
                [.. Enumerable.Range(1, count).Select(i => new DiagramNode($"Lapis {i}"))]);

            var bands = diagram.DrawingPart.Xml.Root!
                .Element(Ns.Dsp + "spTree")!
                .Elements(Ns.Dsp + "sp")
                .Select(s => (
                    Left: s.Descendants(Ns.A + "off").Single().LongAttr("x"),
                    Width: s.Descendants(Ns.A + "ext").Single().LongAttr("cx"),
                    Adjust: Adjustment(s)))
                .ToList();

            for (var i = 1; i < bands.Count; i++)
            {
                // This band's top edge has to be as wide as the one above it is at the bottom.
                var topWidth = bands[i].Width * (1 - (2 * bands[i].Adjust));
                var above = (double)bands[i - 1].Width;

                Assert.True(Math.Abs(topWidth - above) < above * 0.02,
                    $"With {count} bands, band {i} has a top edge of {topWidth:0} against " +
                    $"{above:0} above it.");
            }
        }

        static double Adjustment(XElement shape) =>
            shape.Descendants(Ns.A + "gd").FirstOrDefault(g => g.Attr("name") == "adj")
                ?.Attr("fmla") is { } formula
                ? double.Parse(formula[4..], System.Globalization.CultureInfo.InvariantCulture) / 100000
                : 0;
    }

    [Fact]
    public void ACyclesArrowsFollowTheRing()
    {
        // An arrow that always points right makes the bottom half of a cycle read backwards.
        using var deck = WithDiagram(DiagramKind.Cycle, out var diagram,
            [new DiagramNode("A"), new DiagramNode("B"), new DiagramNode("C"), new DiagramNode("D")]);

        var rotations = diagram.DrawingPart.Xml.Root!
            .Element(Ns.Dsp + "spTree")!
            .Elements(Ns.Dsp + "sp")
            .Select(s => s.Descendants(Ns.A + "xfrm").Single().Attr("rot"))
            .Where(r => r is not null)
            .Select(r => long.Parse(r!, System.Globalization.CultureInfo.InvariantCulture) / 60000.0)
            .ToList();

        // Four nodes, four arrows, each a quarter turn on from the last.
        Assert.Equal(4, rotations.Count);
        Assert.Equal(4, rotations.Distinct().Count());

        for (var i = 1; i < rotations.Count; i++)
        {
            var step = (rotations[i] - rotations[i - 1] + 360) % 360;
            Assert.Equal(90, step, 1);
        }
    }

    [Fact]
    public void OnlyAHierarchyKeepsItsChildren()
    {
        // The other kinds read the tree as a flat list. Dropping the deeper levels instead would
        // lose content silently, which is worse than laying it out as one long list.
        DiagramNode[] tree = [DiagramNode.With("Atas", new DiagramNode("Bawah"))];

        using var flat = WithDiagram(DiagramKind.List, out var list, tree);
        Assert.Equal(["Atas", "Bawah"], list.Nodes);

        using var deck = WithDiagram(DiagramKind.Hierarchy, out var hierarchy, tree);
        Assert.Equal(["Atas", "Bawah"], hierarchy.Nodes);

        // Same content, different shape: the hierarchy puts the child on a second row.
        var rows = hierarchy.DrawingPart.Xml.Root!
            .Element(Ns.Dsp + "spTree")!
            .Elements(Ns.Dsp + "sp")
            .Select(s => s.Descendants(Ns.A + "off").Single().LongAttr("y"))
            .Distinct()
            .Count();

        Assert.True(rows > 1, "A hierarchy should put a child on its own row.");
    }

    [Fact]
    public void ADiagramIsFoundAgainAfterASaveAndReopen()
    {
        byte[] bytes;

        using (var deck = WithDiagram(DiagramKind.Process, out _))
        {
            bytes = Save(deck);
        }

        using var reopened = Presentation.Open(new MemoryStream(bytes, writable: false));
        var diagram = Assert.Single(reopened.Slides[0].Diagrams);

        Assert.Equal(["Kumpulkan", "Olah", "Laporkan"], diagram.Nodes);
        Assert.Equal(Units.Cm(20).Emu, diagram.Width.Emu);
    }

    [Fact]
    public void ADiagramWithNoNodesIsRefused()
    {
        using var deck = Presentation.Create();
        var slide = deck.AddSlide(3);

        Assert.Throws<OfficeNetException>(() =>
            slide.AddSmartArt(DiagramKind.List, [], Units.Cm(1), Units.Cm(1),
                Units.Cm(10), Units.Cm(5)));
    }

    [Fact]
    public void TheColoursAreTheCallersToChoose()
    {
        using var deck = Presentation.Create();

        var diagram = deck.AddSlide(3).AddSmartArt(DiagramKind.List, Steps,
            Units.Cm(1), Units.Cm(1), Units.Cm(10), Units.Cm(5),
            fill: OfficeColor.FromRgb(0xB0, 0x30, 0x50),
            text: OfficeColor.FromRgb(0xFF, 0xF8, 0xF0));

        var shape = diagram.DrawingPart.Xml.Root!
            .Element(Ns.Dsp + "spTree")!
            .Elements(Ns.Dsp + "sp")
            .First();

        Assert.Equal("B03050",
            shape.Element(Ns.Dsp + "spPr")!.Element(Ns.A + "solidFill")!
                .Element(Ns.A + "srgbClr")!.Attr("val"));

        Assert.Equal("FFF8F0",
            shape.Descendants(Ns.A + "rPr").First().Element(Ns.A + "solidFill")!
                .Element(Ns.A + "srgbClr")!.Attr("val"));
    }

    [Fact]
    public void TwoDiagramsGetSeparatePartsAndSeparateIds()
    {
        // Ids are GUIDs so that a diagram pasted into another does not collide, and they are
        // generated per build rather than derived from the text: two diagrams saying the same thing
        // are still two diagrams.
        using var deck = Presentation.Create();
        var slide = deck.AddSlide(3);

        var first = slide.AddSmartArt(DiagramKind.List, Steps,
            Units.Cm(1), Units.Cm(1), Units.Cm(10), Units.Cm(5));

        var second = slide.AddSmartArt(DiagramKind.List, Steps,
            Units.Cm(12), Units.Cm(1), Units.Cm(10), Units.Cm(5));

        Assert.NotEqual(first.DataPart.Name.ToString(), second.DataPart.Name.ToString());

        var firstIds = first.DataPart.Xml.Descendants(Ns.Dgm + "pt")
            .Select(p => p.Attr("modelId")).ToHashSet(StringComparer.Ordinal);

        var secondIds = second.DataPart.Xml.Descendants(Ns.Dgm + "pt")
            .Select(p => p.Attr("modelId")).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(firstIds.Intersect(secondIds));

        PptxValidator.AssertValid(Save(deck));
    }
}
