// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using PowerPointNet.Shapes;

namespace PowerPointNet.Diagrams;

/// <summary>
/// A SmartArt diagram on a slide.
/// </summary>
/// <remarks>
/// <para>
/// Like a chart, a diagram is a <c>p:graphicFrame</c> pointing at parts outside the slide. Unlike a
/// chart, it points at <em>four</em> of them — data, layout, colours, style — through a single
/// <c>dgm:relIds</c> element that names all four relationship ids at once. A fifth part, holding the
/// rendered shapes, hangs off the data part rather than off the slide.
/// </para>
/// <para>
/// <b>What is ours and what is PowerPoint's.</b> The content — the nodes and their text — is written
/// here and is authoritative. The appearance is computed here too, and written into the drawing
/// part, which is what every consumer draws: PowerPoint, LibreOffice, Google Slides, and this
/// library's own PDF export. The moment someone edits the diagram in PowerPoint it re-runs its own
/// layout engine and the shapes move to wherever it decides. That is a real boundary and worth
/// knowing about, but it is the same boundary PowerPoint puts on its own cached drawing.
/// </para>
/// <example>
/// <code>
/// slide.AddSmartArt(DiagramKind.Process,
///     [new DiagramNode("Kumpulkan"), new DiagramNode("Olah"), new DiagramNode("Laporkan")],
///     Units.Cm(2), Units.Cm(5), Units.Cm(20), Units.Cm(6));
/// </code>
/// </example>
/// </remarks>
public sealed class SmartArt : Shape
{
    private const string DiagramGraphicUri =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    internal SmartArt(Presentation presentation, XElement element, OpcPart dataPart,
        OpcPart drawingPart)
        : base(presentation, element)
    {
        DataPart = dataPart;
        DrawingPart = drawingPart;
    }

    /// <summary>The part holding the nodes and how they connect.</summary>
    public OpcPart DataPart { get; }

    /// <summary>The part holding the rendered shapes.</summary>
    public OpcPart DrawingPart { get; }

    /// <summary>The diagram's node text, in document order.</summary>
    /// <remarks>
    /// Read from the data model rather than from the drawing, because the data model is the content
    /// and the drawing is one rendering of it.
    /// </remarks>
    public IReadOnlyList<string> Nodes =>
    [
        .. DataPart.Xml.Root?
            .Element(Ns.Dgm + "ptLst")?
            .Elements(Ns.Dgm + "pt")
            .Where(p => p.Attr("type") is null)
            .Select(p => string.Concat(p.Descendants(Ns.A + "t").Select(t => t.Value)))
            .Where(t => t.Length > 0) ?? [],
    ];

    internal static SmartArt Create(Presentation presentation, Slide slide, XElement shapeTree,
        uint id, DiagramKind kind, IReadOnlyList<DiagramNode> nodes,
        Length left, Length top, Length width, Length height,
        OfficeColor fill, OfficeColor text)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0)
        {
            throw new OfficeNetException("A diagram needs at least one node.");
        }

        var package = presentation.Package;

        var data = package.AddXmlPart(package.NextPartName("/ppt/diagrams/data{0}.xml"),
            ContentTypes.DiagramData, DiagramXml.BuildData(kind, nodes));

        var layout = package.AddXmlPart(package.NextPartName("/ppt/diagrams/layout{0}.xml"),
            ContentTypes.DiagramLayout, DiagramXml.BuildLayout(kind));

        var colors = package.AddXmlPart(package.NextPartName("/ppt/diagrams/colors{0}.xml"),
            ContentTypes.DiagramColors, DiagramXml.BuildColors());

        var style = package.AddXmlPart(package.NextPartName("/ppt/diagrams/quickStyle{0}.xml"),
            ContentTypes.DiagramStyle, DiagramXml.BuildQuickStyle());

        var drawing = package.AddXmlPart(package.NextPartName("/ppt/diagrams/drawing{0}.xml"),
            ContentTypes.DiagramDrawing,
            DiagramXml.BuildDrawing(kind, nodes, width, height, fill, text));

        // The drawing hangs off the data part, not off the slide. Attaching it to the slide instead
        // produces a package that validates and a diagram PowerPoint draws empty.
        data.AddRelationship(drawing, RelationshipTypes.DiagramDrawing);

        var dataId = slide.Part.AddRelationship(data, RelationshipTypes.DiagramData).Id;
        var layoutId = slide.Part.AddRelationship(layout, RelationshipTypes.DiagramLayout).Id;
        var styleId = slide.Part.AddRelationship(style, RelationshipTypes.DiagramQuickStyle).Id;
        var colorsId = slide.Part.AddRelationship(colors, RelationshipTypes.DiagramColors).Id;

        var frame = new XElement(Ns.P + "graphicFrame",
            new XElement(Ns.P + "nvGraphicFramePr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", $"Diagram {id}")),
                new XElement(Ns.P + "cNvGraphicFramePr"),
                new XElement(Ns.P + "nvPr")),
            new XElement(Ns.P + "xfrm",
                new XElement(Ns.A + "off",
                    new XAttribute("x", left.Emu), new XAttribute("y", top.Emu)),
                new XElement(Ns.A + "ext",
                    new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu))),
            new XElement(Ns.A + "graphic",
                new XElement(Ns.A + "graphicData",
                    new XAttribute("uri", DiagramGraphicUri),
                    // All four ids in one element. Naming three of them and omitting the fourth is
                    // the usual way to get a diagram that opens as a blank frame.
                    new XElement(Ns.Dgm + "relIds",
                        new XAttribute(XNamespace.Xmlns + "dgm", Ns.Dgm.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
                        new XAttribute(Ns.R + "dm", dataId),
                        new XAttribute(Ns.R + "lo", layoutId),
                        new XAttribute(Ns.R + "qs", styleId),
                        new XAttribute(Ns.R + "cs", colorsId)))));

        shapeTree.Add(frame);
        presentation.Touch();

        return new SmartArt(presentation, frame, data, drawing);
    }

    /// <summary>True when a graphic frame holds a diagram rather than a chart or a table.</summary>
    internal static bool IsDiagram(XElement graphicFrame) =>
        graphicFrame.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData")
            ?.Attr("uri") == DiagramGraphicUri;

    public override string ToString() => $"SmartArt \"{Name}\" ({Nodes.Count} nodes)";
}
