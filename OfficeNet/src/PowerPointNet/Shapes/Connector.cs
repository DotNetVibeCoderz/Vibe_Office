// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace PowerPointNet.Shapes;

/// <summary>The path a connector takes between its ends.</summary>
public enum ConnectorType
{
    /// <summary>A straight line.</summary>
    Straight,

    /// <summary>Horizontal, vertical, horizontal — the elbow PowerPoint draws for flowcharts.</summary>
    Elbow,

    /// <summary>A smooth S-curve.</summary>
    Curved,
}

/// <summary>What sits at an end of a line.</summary>
public enum ArrowHead
{
    None,
    Triangle,
    Arrow,
    Stealth,
    Diamond,
    Oval,
}

/// <summary>
/// A line or connector — <c>p:cxnSp</c> — with arrowheads, a dash pattern, and optionally the
/// shapes it joins.
/// </summary>
/// <remarks>
/// <para>
/// A connector is stored as a box and two flips: the box is the bounding rectangle of its ends,
/// and <c>flipH</c>/<c>flipV</c> say which corners they are in. A line drawn right to left is a box
/// with <c>flipH</c>, not a negative width. <see cref="Start"/> and <see cref="End"/> undo that.
/// </para>
/// <para>
/// A connector glued to shapes names them in <c>stCxn</c>/<c>endCxn</c>, with the index of the
/// connection site on each: for a rectangle, 0 is the top, 1 the left, 2 the bottom and 3 the right.
/// PowerPoint then moves the connector when the shapes move.
/// </para>
/// </remarks>
public sealed class Connector : Shape
{
    internal Connector(Presentation presentation, XElement element)
        : base(presentation, element)
    {
    }

    private XElement Transform => ShapeProperties.Element(Ns.A + "xfrm") ?? new XElement(Ns.A + "xfrm");

    private XElement Line
    {
        get
        {
            var line = ShapeProperties.Element(Ns.A + "ln");

            if (line is null)
            {
                line = new XElement(Ns.A + "ln");
                XmlUtil.SetOrdered(ShapeProperties, Ns.A + "ln", line, SpPrOrder);
            }

            return line;
        }
    }

    // CT_ShapeProperties: xfrm, geometry, fill, ln, effects, scene3d, sp3d, extLst.
    private static readonly XName[] SpPrOrder =
    [
        Ns.A + "xfrm", Ns.A + "custGeom", Ns.A + "prstGeom", Ns.A + "noFill", Ns.A + "solidFill", Ns.A + "gradFill",
        Ns.A + "blipFill", Ns.A + "pattFill", Ns.A + "grpFill", Ns.A + "ln", Ns.A + "effectLst", Ns.A + "effectDag",
        Ns.A + "scene3d", Ns.A + "sp3d", Ns.A + "extLst",
    ];

    // CT_LineProperties: fill, dash, join, headEnd, tailEnd, extLst.
    private static readonly XName[] LineOrder =
    [
        Ns.A + "noFill", Ns.A + "solidFill", Ns.A + "gradFill", Ns.A + "pattFill", Ns.A + "prstDash", Ns.A + "custDash",
        Ns.A + "round", Ns.A + "bevel", Ns.A + "miter", Ns.A + "headEnd", Ns.A + "tailEnd", Ns.A + "extLst",
    ];

    /// <summary>Where the line starts, in slide coordinates.</summary>
    public (Length X, Length Y) Start => End_(start: true);

    /// <summary>Where the line ends, in slide coordinates.</summary>
    public (Length X, Length Y) End => End_(start: false);

    private (Length X, Length Y) End_(bool start)
    {
        var flipH = Transform.Attr("flipH") is "1" or "true";
        var flipV = Transform.Attr("flipV") is "1" or "true";

        var right = start == flipH;
        var bottom = start == flipV;

        return (right ? Left + Width : Left, bottom ? Top + Height : Top);
    }

    /// <summary>Straight, elbow or curved.</summary>
    public ConnectorType Type
    {
        get => ShapeProperties.Element(Ns.A + "prstGeom")?.Attr("prst") switch
        {
            { } name when name.StartsWith("bentConnector", StringComparison.Ordinal) => ConnectorType.Elbow,
            { } name when name.StartsWith("curvedConnector", StringComparison.Ordinal) => ConnectorType.Curved,
            _ => ConnectorType.Straight,
        };
        set
        {
            var geometry = ShapeProperties.Element(Ns.A + "prstGeom");
            geometry?.SetAttributeValue("prst", GeometryFor(value));
            Presentation.Touch();
        }
    }

    internal static string GeometryFor(ConnectorType type) => type switch
    {
        ConnectorType.Elbow => "bentConnector3",
        ConnectorType.Curved => "curvedConnector3",
        _ => "straightConnector1",
    };

    /// <summary>The arrowhead at the start.</summary>
    public ArrowHead StartArrow
    {
        get => ArrowOf(Ns.A + "headEnd");
        set => SetArrow(Ns.A + "headEnd", value);
    }

    /// <summary>The arrowhead at the end.</summary>
    public ArrowHead EndArrow
    {
        get => ArrowOf(Ns.A + "tailEnd");
        set => SetArrow(Ns.A + "tailEnd", value);
    }

    private ArrowHead ArrowOf(XName end) => ShapeProperties.Element(Ns.A + "ln")?.Element(end)?.Attr("type") switch
    {
        "triangle" => ArrowHead.Triangle,
        "arrow" => ArrowHead.Arrow,
        "stealth" => ArrowHead.Stealth,
        "diamond" => ArrowHead.Diamond,
        "oval" => ArrowHead.Oval,
        _ => ArrowHead.None,
    };

    private void SetArrow(XName end, ArrowHead arrow)
    {
        var element = new XElement(end, new XAttribute("type", arrow switch
        {
            ArrowHead.Triangle => "triangle",
            ArrowHead.Arrow => "arrow",
            ArrowHead.Stealth => "stealth",
            ArrowHead.Diamond => "diamond",
            ArrowHead.Oval => "oval",
            _ => "none",
        }));

        XmlUtil.SetOrdered(Line, end, element, LineOrder);
        Presentation.Touch();
    }

    /// <summary>The dash pattern.</summary>
    public LineDash Dash
    {
        get => ShapeProperties.Element(Ns.A + "ln")?.Element(Ns.A + "prstDash")?.Attr("val") switch
        {
            "dash" => LineDash.Dash,
            "sysDot" or "dot" => LineDash.Dot,
            "dashDot" or "sysDashDot" => LineDash.DashDot,
            "lgDash" => LineDash.LongDash,
            "lgDashDotDot" or "sysDashDotDot" => LineDash.LongDashDotDot,
            "sysDash" => LineDash.Dash,
            _ => LineDash.Solid,
        };
        set
        {
            XmlUtil.SetOrdered(Line, Ns.A + "prstDash", new XElement(Ns.A + "prstDash", new XAttribute("val", value switch
            {
                LineDash.Dash => "dash",
                LineDash.Dot => "sysDot",
                LineDash.DashDot => "dashDot",
                LineDash.LongDash => "lgDash",
                LineDash.LongDashDotDot => "lgDashDotDot",
                _ => "solid",
            })), LineOrder);
            Presentation.Touch();
        }
    }

    /// <summary>The id of the shape the start is glued to, or <c>null</c>.</summary>
    public uint? StartShapeId => GluedTo(Ns.A + "stCxn");

    /// <summary>The id of the shape the end is glued to, or <c>null</c>.</summary>
    public uint? EndShapeId => GluedTo(Ns.A + "endCxn");

    private uint? GluedTo(XName name) =>
        Element.Element(Ns.P + "nvCxnSpPr")?.Element(Ns.P + "cNvCxnSpPr")?.Element(name)?.Attr("id") is { } id &&
        uint.TryParse(id, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Builds the element; the slide places it.</summary>
    internal static XElement Build(uint id, (Length X, Length Y) start, (Length X, Length Y) end, ConnectorType type,
        OfficeColor color, Length width, (uint Id, int Site)? from, (uint Id, int Site)? to)
    {
        var left = Math.Min(start.X.Emu, end.X.Emu);
        var top = Math.Min(start.Y.Emu, end.Y.Emu);

        var transform = new XElement(Ns.A + "xfrm",
            end.X.Emu < start.X.Emu ? new XAttribute("flipH", "1") : null,
            end.Y.Emu < start.Y.Emu ? new XAttribute("flipV", "1") : null,
            new XElement(Ns.A + "off", new XAttribute("x", left), new XAttribute("y", top)),
            new XElement(Ns.A + "ext",
                new XAttribute("cx", Math.Abs(end.X.Emu - start.X.Emu)),
                new XAttribute("cy", Math.Abs(end.Y.Emu - start.Y.Emu))));

        var nonVisual = new XElement(Ns.P + "cNvCxnSpPr",
            from is { } f ? new XElement(Ns.A + "stCxn", new XAttribute("id", f.Id), new XAttribute("idx", f.Site)) : null,
            to is { } t ? new XElement(Ns.A + "endCxn", new XAttribute("id", t.Id), new XAttribute("idx", t.Site)) : null);

        return new XElement(Ns.P + "cxnSp",
            new XElement(Ns.P + "nvCxnSpPr",
                new XElement(Ns.P + "cNvPr", new XAttribute("id", id), new XAttribute("name", $"Connector {id}")),
                nonVisual,
                new XElement(Ns.P + "nvPr")),
            new XElement(Ns.P + "spPr",
                transform,
                new XElement(Ns.A + "prstGeom", new XAttribute("prst", GeometryFor(type)), new XElement(Ns.A + "avLst")),
                new XElement(Ns.A + "ln",
                    new XAttribute("w", Math.Max(0, width.Emu)),
                    new XElement(Ns.A + "solidFill", new XElement(Ns.A + "srgbClr", new XAttribute("val", color.ToHex()))))));
    }

    /// <summary>
    /// The connection sites to glue at when joining two boxes: the sides that face each other, and
    /// where each site is. <c>null</c> sites for a shape whose geometry has no known site list.
    /// </summary>
    internal static ((Length X, Length Y) Start, int? StartSite, (Length X, Length Y) End, int? EndSite) Facing(Shape from, Shape to)
    {
        var fromCentre = (X: from.Left.Points + from.Width.Points / 2, Y: from.Top.Points + from.Height.Points / 2);
        var toCentre = (X: to.Left.Points + to.Width.Points / 2, Y: to.Top.Points + to.Height.Points / 2);

        // Sites 0 top, 1 left, 2 bottom, 3 right — the rectangle's own list.
        int fromSite, toSite;

        if (Math.Abs(toCentre.X - fromCentre.X) >= Math.Abs(toCentre.Y - fromCentre.Y))
        {
            (fromSite, toSite) = toCentre.X >= fromCentre.X ? (3, 1) : (1, 3);
        }
        else
        {
            (fromSite, toSite) = toCentre.Y >= fromCentre.Y ? (2, 0) : (0, 2);
        }

        return (Site(from, fromSite), Glues(from) ? fromSite : null, Site(to, toSite), Glues(to) ? toSite : null);

        static (Length X, Length Y) Site(Shape shape, int site) => site switch
        {
            0 => (shape.Left + Length.FromEmu(shape.Width.Emu / 2), shape.Top),
            1 => (shape.Left, shape.Top + Length.FromEmu(shape.Height.Emu / 2)),
            2 => (shape.Left + Length.FromEmu(shape.Width.Emu / 2), shape.Top + shape.Height),
            _ => (shape.Left + shape.Width, shape.Top + Length.FromEmu(shape.Height.Emu / 2)),
        };

        // Only geometries whose connection sites are the rectangle's four, in that order. Gluing
        // to another shape by these numbers would attach the line to the wrong point.
        static bool Glues(Shape shape) =>
            shape.Element.Element(Ns.P + "spPr")?.Element(Ns.A + "prstGeom")?.Attr("prst") is "rect" or "roundRect" ||
            shape.Element.Name.LocalName is "pic" or "graphicFrame";
    }

    internal static void ThrowIfSame(Shape from, Shape to)
    {
        if (from.Element == to.Element)
        {
            throw new OfficeNetException("A connector joins two shapes; both ends name the same one.");
        }
    }
}
