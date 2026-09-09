// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace PowerPointNet.Diagrams;

/// <summary>The shape of a SmartArt diagram.</summary>
/// <remarks>
/// A small set, chosen because between them they cover what decks actually contain. Each maps to one
/// of PowerPoint's own layout ids, so a diagram written here opens in PowerPoint as the diagram it
/// says it is and can be edited there.
/// </remarks>
public enum DiagramKind
{
    /// <summary>Boxes stacked top to bottom. A list of points.</summary>
    List,

    /// <summary>Chevrons left to right. Steps in a sequence.</summary>
    Process,

    /// <summary>Boxes round a ring. A repeating sequence with no end.</summary>
    Cycle,

    /// <summary>A tree. An organisation chart, or anything that reports upwards.</summary>
    Hierarchy,

    /// <summary>Stacked bands, widest at the bottom. A foundation and what sits on it.</summary>
    Pyramid,
}

/// <summary>One node of a diagram, and the nodes beneath it.</summary>
/// <param name="Text">What the node says.</param>
/// <param name="Children">Nodes reporting to this one. Only <see cref="DiagramKind.Hierarchy"/> draws them.</param>
public sealed record DiagramNode(string Text, IReadOnlyList<DiagramNode>? Children = null)
{
    /// <summary>The node's children, empty rather than null.</summary>
    public IReadOnlyList<DiagramNode> Nodes => Children ?? [];

    /// <summary>Builds a node with children, without naming the list type.</summary>
    public static DiagramNode With(string text, params DiagramNode[] children) => new(text, children);

    public override string ToString() =>
        Nodes.Count == 0 ? Text : $"{Text} ({Nodes.Count})";
}

/// <summary>
/// Builds the five parts a SmartArt diagram is made of.
/// </summary>
/// <remarks>
/// <para>
/// A diagram is not one part but four standard ones — data, layout, colours, style — plus a
/// Microsoft extension part holding the rendered shapes. The slide points at the first four through
/// a <c>dgm:relIds</c> element; the data part points at the fifth.
/// </para>
/// <para>
/// The division of labour matters for what this class can honestly do. <c>data</c> is the content:
/// the nodes, their text, and how they connect. <c>layout</c> is an <em>algorithm</em> — a
/// constraint system PowerPoint solves at draw time to decide where each node goes. Reimplementing
/// that algorithm is not a thing a library does in an afternoon, and writing a hollow one produces a
/// diagram that opens as a blank rectangle.
/// </para>
/// <para>
/// So the geometry is computed here and written into the <c>dsp:drawing</c> extension part, which is
/// exactly what PowerPoint itself caches there. That part is what every consumer that does not run
/// the layout algorithm draws — LibreOffice, Google Slides, this library's own PDF export — and
/// PowerPoint draws it too, until someone edits the diagram, at which point it re-runs its own
/// engine and the shapes move to wherever it decides. That is the honest boundary: the content and
/// the appearance are ours, and the moment a person edits the diagram the appearance becomes
/// PowerPoint's.
/// </para>
/// </remarks>
internal static class DiagramXml
{
    /// <summary>
    /// PowerPoint's own layout ids, which is how it knows which diagram this is.
    /// </summary>
    /// <remarks>
    /// These strings are not arbitrary. PowerPoint matches the diagram to an entry in its gallery by
    /// this id, and an unrecognised one leaves the user with a diagram they cannot re-style.
    /// </remarks>
    internal static string LayoutId(DiagramKind kind) => kind switch
    {
        DiagramKind.Process => "urn:microsoft.com/office/officeart/2005/8/layout/process1",
        DiagramKind.Cycle => "urn:microsoft.com/office/officeart/2005/8/layout/cycle2",
        DiagramKind.Hierarchy => "urn:microsoft.com/office/officeart/2005/8/layout/orgChart1",
        DiagramKind.Pyramid => "urn:microsoft.com/office/officeart/2005/8/layout/pyramid1",
        _ => "urn:microsoft.com/office/officeart/2005/8/layout/vList2",
    };

    internal static string LayoutName(DiagramKind kind) => kind switch
    {
        DiagramKind.Process => "process1",
        DiagramKind.Cycle => "cycle2",
        DiagramKind.Hierarchy => "orgChart1",
        DiagramKind.Pyramid => "pyramid1",
        _ => "vList2",
    };

    // ---- data1.xml -----------------------------------------------------------------------------

    /// <summary>
    /// Builds the data model: the nodes, and the connections between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every diagram has a single root point of type <c>doc</c>, and every other point hangs off it
    /// through a <c>parTrans</c>/<c>sibTrans</c> pair. Those two transition points per node are not
    /// decoration: PowerPoint uses them to hold the formatting of the connector between nodes, and a
    /// diagram missing them opens with its connectors gone.
    /// </para>
    /// <para>
    /// Ids are GUIDs because PowerPoint writes GUIDs, and because a diagram pasted into another one
    /// must not collide. They are generated per build rather than derived from the text, so two
    /// diagrams with the same words are still two diagrams.
    /// </para>
    /// </remarks>
    internal static XDocument BuildData(DiagramKind kind, IReadOnlyList<DiagramNode> nodes)
    {
        var points = new XElement(Ns.Dgm + "ptLst");
        var connections = new XElement(Ns.Dgm + "cxnLst");

        var rootId = NewId();

        points.Add(new XElement(Ns.Dgm + "pt",
            new XAttribute("modelId", rootId),
            new XAttribute("type", "doc"),
            new XElement(Ns.Dgm + "prSet",
                new XAttribute("loTypeId", LayoutId(kind)),
                new XAttribute("loCatId", "list"),
                new XAttribute("qsTypeId", "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1"),
                new XAttribute("qsCatId", "simple"),
                new XAttribute("csTypeId", "urn:microsoft.com/office/officeart/2005/8/colors/accent1_2"),
                new XAttribute("csCatId", "accent1"),
                new XAttribute("phldr", "1")),
            new XElement(Ns.Dgm + "spPr"),
            TextBody(string.Empty)));

        var order = 0;

        // Hierarchy is the only kind that draws children; the rest read the tree as a flat list, so
        // the whole tree is flattened for them rather than silently dropping the deeper levels.
        if (kind == DiagramKind.Hierarchy)
        {
            foreach (var node in nodes)
            {
                AddNode(node, rootId, ref order);
            }
        }
        else
        {
            foreach (var node in Flatten(nodes))
            {
                AddNode(node with { Children = null }, rootId, ref order);
            }
        }

        return XmlUtil.NewDocument(new XElement(Ns.Dgm + "dataModel",
            new XAttribute(XNamespace.Xmlns + "dgm", Ns.Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            points,
            connections,
            new XElement(Ns.Dgm + "bg"),
            new XElement(Ns.Dgm + "whole")));

        void AddNode(DiagramNode node, string parentId, ref int index)
        {
            var id = NewId();
            var parTransId = NewId();
            var sibTransId = NewId();

            points.Add(new XElement(Ns.Dgm + "pt",
                new XAttribute("modelId", id),
                new XElement(Ns.Dgm + "prSet",
                    new XAttribute("phldrT", node.Text)),
                new XElement(Ns.Dgm + "spPr"),
                TextBody(node.Text)));

            // The two transition points. They carry no text and exist to hold the connector's
            // formatting; without them PowerPoint draws the nodes and none of the lines between.
            points.Add(TransitionPoint(parTransId, "parTrans"));
            points.Add(TransitionPoint(sibTransId, "sibTrans"));

            connections.Add(new XElement(Ns.Dgm + "cxn",
                new XAttribute("modelId", NewId()),
                new XAttribute("srcId", parentId),
                new XAttribute("destId", id),
                new XAttribute("srcOrd", index.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("destOrd", "0"),
                new XAttribute("parTransId", parTransId),
                new XAttribute("sibTransId", sibTransId)));

            index++;

            var childOrder = 0;

            foreach (var child in node.Nodes)
            {
                AddNode(child, id, ref childOrder);
            }
        }
    }

    private static XElement TransitionPoint(string id, string type) =>
        new(Ns.Dgm + "pt",
            new XAttribute("modelId", id),
            new XAttribute("type", type),
            new XAttribute("cxnId", NewId()),
            new XElement(Ns.Dgm + "prSet"),
            new XElement(Ns.Dgm + "spPr"),
            TextBody(string.Empty));

    private static XElement TextBody(string text) =>
        new(Ns.Dgm + "t",
            new XElement(Ns.A + "bodyPr"),
            new XElement(Ns.A + "lstStyle"),
            new XElement(Ns.A + "p",
                text.Length == 0
                    ? null
                    : new XElement(Ns.A + "r",
                        new XElement(Ns.A + "rPr", new XAttribute("lang", "en-US")),
                        XmlUtil.TextElement(Ns.A + "t", text))));

    /// <summary>Every node in the tree, depth first.</summary>
    internal static IEnumerable<DiagramNode> Flatten(IEnumerable<DiagramNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Nodes))
            {
                yield return child;
            }
        }
    }

    // ---- layout1.xml, colors1.xml, quickStyle1.xml ----------------------------------------------

    /// <summary>
    /// Builds a layout definition naming the diagram, with an empty algorithm.
    /// </summary>
    /// <remarks>
    /// The part has to exist and has to carry the right <c>uniqueId</c>, which is how PowerPoint
    /// identifies the diagram and offers the right gallery entry. What it does <em>not</em> carry is
    /// a working algorithm: those run to hundreds of lines of constraints per layout, and a
    /// half-implemented one lays the diagram out wrongly rather than not at all. PowerPoint replaces
    /// this wholesale the first time someone edits the diagram; until then the shapes in the drawing
    /// part are what everyone sees.
    /// </remarks>
    internal static XDocument BuildLayout(DiagramKind kind) =>
        XmlUtil.NewDocument(new XElement(Ns.Dgm + "layoutDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Ns.Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute("uniqueId", LayoutId(kind)),
            new XElement(Ns.Dgm + "title", new XAttribute("val", LayoutName(kind))),
            new XElement(Ns.Dgm + "desc", new XAttribute("val", string.Empty)),
            new XElement(Ns.Dgm + "catLst",
                new XElement(Ns.Dgm + "cat",
                    new XAttribute("type", "list"),
                    new XAttribute("pri", "1000"))),
            new XElement(Ns.Dgm + "sampData",
                new XElement(Ns.Dgm + "dataModel",
                    new XElement(Ns.Dgm + "ptLst"),
                    new XElement(Ns.Dgm + "cxnLst"),
                    new XElement(Ns.Dgm + "bg"),
                    new XElement(Ns.Dgm + "whole"))),
            new XElement(Ns.Dgm + "styleData",
                new XElement(Ns.Dgm + "dataModel",
                    new XElement(Ns.Dgm + "ptLst"),
                    new XElement(Ns.Dgm + "cxnLst"),
                    new XElement(Ns.Dgm + "bg"),
                    new XElement(Ns.Dgm + "whole"))),
            new XElement(Ns.Dgm + "clrData",
                new XElement(Ns.Dgm + "dataModel",
                    new XElement(Ns.Dgm + "ptLst"),
                    new XElement(Ns.Dgm + "cxnLst"),
                    new XElement(Ns.Dgm + "bg"),
                    new XElement(Ns.Dgm + "whole"))),
            new XElement(Ns.Dgm + "layoutNode",
                new XAttribute("name", "root"))));

    /// <summary>Builds a colour definition: accent 1, which is the theme's own.</summary>
    internal static XDocument BuildColors() =>
        XmlUtil.NewDocument(new XElement(Ns.Dgm + "colorsDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Ns.Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute("uniqueId", "urn:microsoft.com/office/officeart/2005/8/colors/accent1_2"),
            new XAttribute("minVer", "12.0"),
            new XElement(Ns.Dgm + "title", new XAttribute("val", string.Empty)),
            new XElement(Ns.Dgm + "desc", new XAttribute("val", string.Empty)),
            new XElement(Ns.Dgm + "catLst",
                new XElement(Ns.Dgm + "cat",
                    new XAttribute("type", "accent1"),
                    new XAttribute("pri", "11200"))),
            ColorStyleLabel("node0", "accent1"),
            ColorStyleLabel("node1", "accent1"),
            ColorStyleLabel("alignNode1", "accent1"),
            ColorStyleLabel("lnNode1", "accent1")));

    private static XElement ColorStyleLabel(string name, string accent) =>
        new(Ns.Dgm + "styleLbl",
            new XAttribute("name", name),
            new XElement(Ns.Dgm + "fillClrLst",
                new XAttribute("meth", "repeat"),
                new XElement(Ns.A + "schemeClr", new XAttribute("val", accent))),
            new XElement(Ns.Dgm + "linClrLst",
                new XAttribute("meth", "repeat"),
                new XElement(Ns.A + "schemeClr",
                    new XAttribute("val", "lt1"))),
            new XElement(Ns.Dgm + "effectClrLst"),
            new XElement(Ns.Dgm + "txLinClrLst"),
            new XElement(Ns.Dgm + "txFillClrLst",
                new XAttribute("meth", "repeat"),
                new XElement(Ns.A + "schemeClr", new XAttribute("val", "lt1"))),
            new XElement(Ns.Dgm + "txEffectClrLst"));

    /// <summary>Builds a style definition: the plain one, which is what most decks want.</summary>
    internal static XDocument BuildQuickStyle() =>
        XmlUtil.NewDocument(new XElement(Ns.Dgm + "styleDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Ns.Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute("uniqueId", "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1"),
            new XAttribute("minVer", "12.0"),
            new XElement(Ns.Dgm + "title", new XAttribute("val", string.Empty)),
            new XElement(Ns.Dgm + "desc", new XAttribute("val", string.Empty)),
            new XElement(Ns.Dgm + "catLst",
                new XElement(Ns.Dgm + "cat",
                    new XAttribute("type", "simple"),
                    new XAttribute("pri", "10100"))),
            new XElement(Ns.Dgm + "scene3d",
                new XElement(Ns.A + "camera", new XAttribute("prst", "orthographicFront")),
                new XElement(Ns.A + "lightRig",
                    new XAttribute("rig", "threePt"),
                    new XAttribute("dir", "t"))),
            new XElement(Ns.Dgm + "style"),
            StyleLabel("node0"),
            StyleLabel("node1"),
            StyleLabel("alignNode1"),
            StyleLabel("lnNode1")));

    private static XElement StyleLabel(string name) =>
        new(Ns.Dgm + "styleLbl",
            new XAttribute("name", name),
            new XElement(Ns.Dgm + "scene3d",
                new XElement(Ns.A + "camera", new XAttribute("prst", "orthographicFront")),
                new XElement(Ns.A + "lightRig",
                    new XAttribute("rig", "threePt"),
                    new XAttribute("dir", "t"))),
            new XElement(Ns.Dgm + "sp3d"),
            new XElement(Ns.Dgm + "txPr"),
            new XElement(Ns.Dgm + "style"));

    // ---- drawing1.xml --------------------------------------------------------------------------

    /// <summary>
    /// Builds the rendered shapes, at positions this library computes.
    /// </summary>
    /// <remarks>
    /// Coordinates are relative to the diagram's frame, not to the slide: the frame's own
    /// <c>xfrm</c> places the whole thing.
    /// </remarks>
    internal static XDocument BuildDrawing(DiagramKind kind, IReadOnlyList<DiagramNode> nodes,
        Length width, Length height, OfficeColor fill, OfficeColor text)
    {
        var tree = new XElement(Ns.Dsp + "spTree",
            new XElement(Ns.Dsp + "nvGrpSpPr",
                new XElement(Ns.Dsp + "cNvPr",
                    new XAttribute("id", "0"),
                    new XAttribute("name", string.Empty)),
                new XElement(Ns.Dsp + "cNvGrpSpPr")),
            new XElement(Ns.Dsp + "grpSpPr"));

        var id = 1u;

        foreach (var placement in DiagramLayout.Place(kind, nodes, width, height))
        {
            tree.Add(BuildShape(id++, placement, fill, text));
        }

        return XmlUtil.NewDocument(new XElement(Ns.Dsp + "drawing",
            new XAttribute(XNamespace.Xmlns + "dsp", Ns.Dsp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            tree));
    }

    private static XElement BuildShape(uint id, PlacedNode placement, OfficeColor fill, OfficeColor text)
    {
        var body = new XElement(Ns.Dsp + "txBody",
            new XElement(Ns.A + "bodyPr",
                new XAttribute("anchor", "ctr"),
                new XAttribute("lIns", "45720"), new XAttribute("rIns", "45720"),
                new XAttribute("tIns", "45720"), new XAttribute("bIns", "45720")),
            new XElement(Ns.A + "lstStyle"),
            new XElement(Ns.A + "p",
                new XElement(Ns.A + "pPr", new XAttribute("algn", "ctr")),
                placement.Text.Length == 0
                    ? null
                    : new XElement(Ns.A + "r",
                        new XElement(Ns.A + "rPr",
                            new XAttribute("lang", "en-US"),
                            new XAttribute("sz", placement.FontSize.Centipoints),
                            new XElement(Ns.A + "solidFill",
                                new XElement(Ns.A + "srgbClr", new XAttribute("val", text.ToHex())))),
                        XmlUtil.TextElement(Ns.A + "t", placement.Text))));

        return new XElement(Ns.Dsp + "sp",
            new XElement(Ns.Dsp + "nvSpPr",
                new XElement(Ns.Dsp + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", placement.Text.Length > 0 ? placement.Text : $"Shape {id}")),
                new XElement(Ns.Dsp + "cNvSpPr")),
            new XElement(Ns.Dsp + "spPr",
                new XElement(Ns.A + "xfrm",
                    // DrawingML angles are in sixty-thousandths of a degree, and the attribute is
                    // omitted rather than written as zero, which is what PowerPoint does.
                    placement.Rotation == 0
                        ? null
                        : new XAttribute("rot",
                            (long)Math.Round(((placement.Rotation % 360) + 360) % 360 * 60000)),
                    new XElement(Ns.A + "off",
                        new XAttribute("x", placement.Left.Emu),
                        new XAttribute("y", placement.Top.Emu)),
                    new XElement(Ns.A + "ext",
                        new XAttribute("cx", placement.Width.Emu),
                        new XAttribute("cy", placement.Height.Emu))),
                new XElement(Ns.A + "prstGeom",
                    new XAttribute("prst", placement.Geometry),
                    new XElement(Ns.A + "avLst",
                        // A preset's adjustment is a guide value in hundred-thousandths. Leaving it
                        // out takes the preset's own default, which is right for a rounded rectangle
                        // and wrong for a trapezoid whose slope has to match the band above it.
                        placement.Adjust is not { } adjust
                            ? null
                            : new XElement(Ns.A + "gd",
                                new XAttribute("name", "adj"),
                                new XAttribute("fmla",
                                    FormattableString.Invariant(
                                        $"val {(long)Math.Round(adjust * 100000)}"))))),
                // A connector is a filled shape, not an outlined one: an org chart's lines are thin
                // rectangles and its arrows are solid. Stroking them instead draws each as a hollow
                // outline, which is a hairline box where a line was wanted.
                new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "srgbClr", new XAttribute("val", fill.ToHex()))),
                new XElement(Ns.A + "ln", new XElement(Ns.A + "noFill"))),
            body);
    }

    private static string NewId() => Guid.NewGuid().ToString("B").ToUpperInvariant();
}
