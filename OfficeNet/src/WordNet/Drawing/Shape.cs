// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace WordNet.Drawing;

/// <summary>The outline of a shape, as one of DrawingML's preset geometries.</summary>
/// <remarks>
/// These are a small, useful subset. <see cref="Shape.Geometry"/> accepts the raw preset name for
/// any of the other 180-odd, which are listed in ECMA-376 Part 1 §20.1.10.55.
/// </remarks>
public enum ShapeGeometry
{
    /// <summary>A rectangle. What a text box is.</summary>
    Rectangle,

    /// <summary>A rectangle with rounded corners.</summary>
    RoundedRectangle,

    /// <summary>An ellipse, or a circle when the box is square.</summary>
    Ellipse,

    /// <summary>An isosceles triangle.</summary>
    Triangle,

    /// <summary>A diamond.</summary>
    Diamond,

    /// <summary>A regular hexagon.</summary>
    Hexagon,

    /// <summary>A five-pointed star.</summary>
    Star,

    /// <summary>An arrow pointing right.</summary>
    RightArrow,

    /// <summary>An arrow pointing down.</summary>
    DownArrow,

    /// <summary>A rounded rectangle speech balloon.</summary>
    Callout,

    /// <summary>A straight line, drawn corner to corner of the box.</summary>
    Line,
}

/// <summary>How body text flows around a floating shape.</summary>
public enum TextWrap
{
    /// <summary>Text flows down both sides of the shape's bounding box.</summary>
    Square,

    /// <summary>Text flows around the shape's outline rather than its box.</summary>
    Tight,

    /// <summary>Text stops above the shape and resumes below it.</summary>
    TopAndBottom,

    /// <summary>Text ignores the shape and runs underneath it.</summary>
    InFrontOfText,

    /// <summary>Text ignores the shape and the shape sits behind it. A watermark.</summary>
    BehindText,
}

/// <summary>What a floating shape's horizontal position is measured from.</summary>
public enum HorizontalAnchor
{
    /// <summary>The left edge of the text column.</summary>
    Column,

    /// <summary>The left edge of the page.</summary>
    Page,

    /// <summary>The left margin.</summary>
    Margin,

    /// <summary>The character the anchor sits at.</summary>
    Character,
}

/// <summary>What a floating shape's vertical position is measured from.</summary>
public enum VerticalAnchor
{
    /// <summary>The top of the paragraph the anchor sits in.</summary>
    Paragraph,

    /// <summary>The top of the page.</summary>
    Page,

    /// <summary>The top margin.</summary>
    Margin,

    /// <summary>The current line.</summary>
    Line,
}

/// <summary>
/// A text box or shape.
/// </summary>
/// <remarks>
/// <para>
/// A shape is a <c>wps:wsp</c> inside a <c>w:drawing</c>. Two things about that are worth knowing
/// before reading the rest.
/// </para>
/// <para>
/// First, the <c>wps</c> namespace is a Microsoft extension, not an ECMA one. The standard's own
/// answer for shapes in a Word document is VML, which was deprecated in the same release that
/// shipped it and which no current reader treats as the primary form. Word 2010 and later, and
/// LibreOffice, read <c>wps</c> directly; a Word 2007 fallback would mean writing every shape twice
/// inside an <c>mc:AlternateContent</c>, and is deliberately not done here.
/// </para>
/// <para>
/// Second, a shape is either <em>inline</em> — laid out as if it were a very large character — or
/// <em>anchored</em>, floating at a position with text flowing around it. Those are two different
/// elements, <c>wp:inline</c> and <c>wp:anchor</c>, and the anchored one carries five mandatory
/// attributes that have no schema defaults. Leaving any of them out is one of the reliable ways to
/// make Word call a document unreadable.
/// </para>
/// </remarks>
public sealed class Shape
{
    private readonly WordDocument _document;

    internal Shape(WordDocument document, XElement drawing)
    {
        _document = document;
        Drawing = drawing;
    }

    /// <summary>The <c>w:drawing</c> element that holds the shape.</summary>
    public XElement Drawing { get; }

    /// <summary>
    /// The <c>wp:inline</c> or <c>wp:anchor</c> element — where the shape sits on the page.
    /// </summary>
    public XElement Container =>
        Drawing.Element(Ns.Wp + "anchor") ?? Drawing.Element(Ns.Wp + "inline")!;

    /// <summary>
    /// The <c>wps:wsp</c> element — the shape itself, as distinct from where it sits.
    /// </summary>
    public XElement Wsp =>
        Container.Element(Ns.A + "graphic")!
            .Element(Ns.A + "graphicData")!
            .Element(Ns.Wps + "wsp")!;

    private XElement ShapeProperties => Wsp.Element(Ns.Wps + "spPr")!;

    /// <summary>Whether the shape floats rather than sitting in the line of text.</summary>
    public bool IsFloating => Drawing.Element(Ns.Wp + "anchor") is not null;

    /// <summary>The shape's name, as the selection pane shows it.</summary>
    public string Name
    {
        get => Container.Element(Ns.Wp + "docPr")?.Attr("name") ?? string.Empty;
        set
        {
            Container.Element(Ns.Wp + "docPr")?.SetAttributeValue("name", value);
            _document.Touch();
        }
    }

    /// <summary>Alternative text, for screen readers.</summary>
    public string AltText
    {
        get => Container.Element(Ns.Wp + "docPr")?.Attr("descr") ?? string.Empty;
        set
        {
            Container.Element(Ns.Wp + "docPr")?.SetAttributeValue("descr", value);
            _document.Touch();
        }
    }

    /// <summary>The drawn width.</summary>
    public Length Width
    {
        get => Emu(Container.Element(Ns.Wp + "extent")?.Attr("cx"));
        set => Resize(value, Height);
    }

    /// <summary>The drawn height.</summary>
    public Length Height
    {
        get => Emu(Container.Element(Ns.Wp + "extent")?.Attr("cy"));
        set => Resize(Width, value);
    }

    /// <summary>Sets both dimensions at once.</summary>
    /// <remarks>
    /// The size is stored twice — once on <c>wp:extent</c>, which is the space the layout engine
    /// reserves, and once on <c>a:ext</c>, which is the box the shape is drawn into. Setting one and
    /// not the other gives a shape that is clipped or that leaves a gap, so they move together.
    /// </remarks>
    public Shape Resize(Length width, Length height)
    {
        Container.Element(Ns.Wp + "extent")?.SetAttributeValue("cx", width.Emu);
        Container.Element(Ns.Wp + "extent")?.SetAttributeValue("cy", height.Emu);

        var ext = ShapeProperties.Element(Ns.A + "xfrm")?.Element(Ns.A + "ext");
        ext?.SetAttributeValue("cx", width.Emu);
        ext?.SetAttributeValue("cy", height.Emu);

        _document.Touch();
        return this;
    }

    /// <summary>The preset geometry name, for example <c>roundRect</c> or <c>cloud</c>.</summary>
    public string Geometry
    {
        get => ShapeProperties.Element(Ns.A + "prstGeom")?.Attr("prst") ?? "rect";
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            ShapeProperties.Element(Ns.A + "prstGeom")?.SetAttributeValue("prst", value);
            _document.Touch();
        }
    }

    /// <summary>Rotation clockwise, in degrees.</summary>
    public double Rotation
    {
        get => ShapeProperties.Element(Ns.A + "xfrm")?.Attr("rot") is { } rot &&
               long.TryParse(rot, CultureInfo.InvariantCulture, out var value)
            ? value / 60000.0
            : 0;
        set
        {
            // DrawingML angles are in sixty-thousandths of a degree.
            var units = (long)Math.Round((((value % 360) + 360) % 360) * 60000);
            var xfrm = ShapeProperties.Element(Ns.A + "xfrm");

            if (units == 0)
            {
                xfrm?.Attribute("rot")?.Remove();
            }
            else
            {
                xfrm?.SetAttributeValue("rot", units);
            }

            _document.Touch();
        }
    }

    /// <summary>The fill colour, or <c>null</c> for no fill.</summary>
    public OfficeColor? FillColor
    {
        get => ShapeProperties.Element(Ns.A + "solidFill")?.Element(Ns.A + "srgbClr")?.Attr("val")
            is { } hex && OfficeColor.TryParse(hex, out var color) ? color : null;
        set
        {
            ShapeProperties.Element(Ns.A + "solidFill")?.Remove();
            ShapeProperties.Element(Ns.A + "noFill")?.Remove();

            // a:spPr is a sequence: the fill goes after the geometry and before the outline.
            ShapeProperties.Element(Ns.A + "prstGeom")!.AddAfterSelf(value is { } color
                ? new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "srgbClr", new XAttribute("val", color.ToHex())))
                : new XElement(Ns.A + "noFill"));

            _document.Touch();
        }
    }

    /// <summary>The outline colour, or <c>null</c> for no outline.</summary>
    public OfficeColor? LineColor
    {
        get => ShapeProperties.Element(Ns.A + "ln")?.Element(Ns.A + "solidFill")
            ?.Element(Ns.A + "srgbClr")?.Attr("val") is { } hex &&
            OfficeColor.TryParse(hex, out var color) ? color : null;
        set
        {
            var line = EnsureLine();
            line.Element(Ns.A + "solidFill")?.Remove();
            line.Element(Ns.A + "noFill")?.Remove();

            line.AddFirst(value is { } color
                ? new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "srgbClr", new XAttribute("val", color.ToHex())))
                : new XElement(Ns.A + "noFill"));

            _document.Touch();
        }
    }

    /// <summary>The outline width.</summary>
    public Length LineWidth
    {
        get => Emu(ShapeProperties.Element(Ns.A + "ln")?.Attr("w"));
        set
        {
            EnsureLine().SetAttributeValue("w", value.Emu);
            _document.Touch();
        }
    }

    /// <summary>The paragraphs inside the shape, empty when it holds no text.</summary>
    public IReadOnlyList<Paragraph> Paragraphs =>
        Wsp.Element(Ns.Wps + "txbx")?.Element(Ns.W + "txbxContent") is { } content
            ? [.. content.Elements(Ns.W + "p").Select(p => new Paragraph(_document, p))]
            : [];

    /// <summary>The shape's text, paragraphs joined by newlines.</summary>
    public string Text => string.Join('\n', Paragraphs.Select(p => p.Text));

    /// <summary>Appends a paragraph inside the shape.</summary>
    /// <remarks>
    /// A shape only grows a text body once something is written into it: Word treats an empty
    /// <c>w:txbxContent</c> as unreadable content, so a plain shape must not carry one.
    /// </remarks>
    public Paragraph AddParagraph(string text = "")
    {
        var content = EnsureTextBody();
        var element = new XElement(Ns.W + "p");
        content.Add(element);

        var paragraph = new Paragraph(_document, element);

        if (text.Length > 0)
        {
            paragraph.AddRun(text);
        }

        _document.Touch();
        return paragraph;
    }

    /// <summary>Moves a floating shape.</summary>
    /// <exception cref="OfficeNetException">The shape is inline and has no position.</exception>
    public Shape MoveTo(Length x, Length y,
        HorizontalAnchor from = HorizontalAnchor.Column,
        VerticalAnchor fromVertical = VerticalAnchor.Paragraph)
    {
        if (Drawing.Element(Ns.Wp + "anchor") is not { } anchor)
        {
            throw new OfficeNetException(
                "An inline shape has no position — it sits in the line of text like a character. " +
                "Give it a wrap when adding it to make it float.");
        }

        anchor.Element(Ns.Wp + "positionH")?.Remove();
        anchor.Element(Ns.Wp + "positionV")?.Remove();

        // CT_Anchor is a sequence: simplePos, positionH, positionV, extent, ...
        anchor.Element(Ns.Wp + "simplePos")!.AddAfterSelf(
            new XElement(Ns.Wp + "positionH",
                new XAttribute("relativeFrom", HorizontalName(from)),
                new XElement(Ns.Wp + "posOffset", x.Emu.ToString(CultureInfo.InvariantCulture))),
            new XElement(Ns.Wp + "positionV",
                new XAttribute("relativeFrom", VerticalName(fromVertical)),
                new XElement(Ns.Wp + "posOffset", y.Emu.ToString(CultureInfo.InvariantCulture))));

        _document.Touch();
        return this;
    }

    /// <summary>The shape's offset from its anchor, or zero when it is inline.</summary>
    public (Length X, Length Y) Offset =>
        Drawing.Element(Ns.Wp + "anchor") is { } anchor
            ? (Emu(anchor.Element(Ns.Wp + "positionH")?.Element(Ns.Wp + "posOffset")?.Value),
               Emu(anchor.Element(Ns.Wp + "positionV")?.Element(Ns.Wp + "posOffset")?.Value))
            : (Length.Zero, Length.Zero);

    /// <summary>How text flows around the shape, or <c>null</c> when it is inline.</summary>
    public TextWrap? Wrap
    {
        get
        {
            if (Drawing.Element(Ns.Wp + "anchor") is not { } anchor)
            {
                return null;
            }

            if (anchor.Element(Ns.Wp + "wrapSquare") is not null) return TextWrap.Square;
            if (anchor.Element(Ns.Wp + "wrapTight") is not null) return TextWrap.Tight;
            if (anchor.Element(Ns.Wp + "wrapTopAndBottom") is not null) return TextWrap.TopAndBottom;

            // wrapNone covers both of the no-wrap cases; behindDoc is what tells them apart.
            return anchor.Attr("behindDoc") == "1" ? TextWrap.BehindText : TextWrap.InFrontOfText;
        }
    }

    /// <summary>Removes the shape from the document.</summary>
    public void Remove()
    {
        // The drawing normally lives in a run that exists only to hold it.
        var run = Drawing.Parent;

        if (run is { } parent && parent.Name == Ns.W + "r" && parent.Elements().Count() == 1)
        {
            parent.Remove();
        }
        else
        {
            Drawing.Remove();
        }

        _document.Touch();
    }

    public override string ToString() =>
        Text.Length > 0
            ? $"{Geometry} \"{Text}\""
            : $"{Geometry} {Width.Points:0.#}x{Height.Points:0.#}pt";

    // ---- Construction --------------------------------------------------------------------------

    internal static Shape Create(WordDocument document, XElement run, string geometry,
        Length width, Length height, TextWrap? wrap, string? altText)
    {
        var id = document.NextDrawingId();
        var name = $"Shape {id}";

        var properties = new XElement(Ns.Wps + "spPr",
            new XElement(Ns.A + "xfrm",
                new XElement(Ns.A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                new XElement(Ns.A + "ext",
                    new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu))),
            new XElement(Ns.A + "prstGeom",
                new XAttribute("prst", geometry),
                new XElement(Ns.A + "avLst")));

        var wsp = new XElement(Ns.Wps + "wsp",
            new XAttribute(XNamespace.Xmlns + "wps", Ns.Wps.NamespaceName),
            new XElement(Ns.Wps + "cNvSpPr"),
            properties,
            // bodyPr is required and must come last, even on a shape holding no text. The insets are
            // Word's own defaults; without them text sits flush against the outline.
            new XElement(Ns.Wps + "bodyPr",
                new XAttribute("rot", "0"),
                new XAttribute("vert", "horz"),
                new XAttribute("wrap", "square"),
                new XAttribute("lIns", "91440"),
                new XAttribute("tIns", "45720"),
                new XAttribute("rIns", "91440"),
                new XAttribute("bIns", "45720"),
                new XAttribute("anchor", "t"),
                new XAttribute("anchorCtr", "0"),
                new XElement(Ns.A + "noAutofit")));

        var graphic = new XElement(Ns.A + "graphic",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XElement(Ns.A + "graphicData",
                new XAttribute("uri", Ns.Wps.NamespaceName),
                wsp));

        var drawing = new XElement(Ns.W + "drawing",
            BuildContainer(id, name, width, height, altText, wrap, graphic));

        run.Add(drawing);

        return new Shape(document, drawing);
    }

    /// <summary>
    /// Builds the container a drawing sits in: <c>wp:anchor</c> when it floats, <c>wp:inline</c>
    /// when it is laid out in the line of text.
    /// </summary>
    internal static XElement BuildContainer(int id, string name, Length width, Length height,
        string? altText, TextWrap? wrap, XElement graphic) =>
        wrap is null
            ? Inline(id, name, width, height, altText, graphic)
            : Anchor(id, name, width, height, altText, wrap.Value, graphic);

    /// <summary>Builds the <c>wp:inline</c> container a drawing sits in when it is not floating.</summary>
    internal static XElement Inline(int id, string name, Length width, Length height,
        string? altText, XElement graphic) =>
        new(Ns.Wp + "inline",
            new XAttribute("distT", "0"), new XAttribute("distB", "0"),
            new XAttribute("distL", "0"), new XAttribute("distR", "0"),
            new XElement(Ns.Wp + "extent",
                new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu)),
            new XElement(Ns.Wp + "effectExtent",
                new XAttribute("l", "0"), new XAttribute("t", "0"),
                new XAttribute("r", "0"), new XAttribute("b", "0")),
            new XElement(Ns.Wp + "docPr",
                new XAttribute("id", id), new XAttribute("name", name),
                new XAttribute("descr", altText ?? string.Empty)),
            new XElement(Ns.Wp + "cNvGraphicFramePr"),
            graphic);

    /// <summary>Builds the <c>wp:anchor</c> container a floating drawing sits in.</summary>
    internal static XElement Anchor(int id, string name, Length width, Length height,
        string? altText, TextWrap wrap, XElement graphic) =>
        new(Ns.Wp + "anchor",
            // Every one of these is mandatory on CT_Anchor and none has a schema default. A missing
            // one is not ignored — Word reports the document as unreadable.
            new XAttribute("distT", "0"), new XAttribute("distB", "0"),
            new XAttribute("distL", "114300"), new XAttribute("distR", "114300"),
            new XAttribute("simplePos", "0"),
            new XAttribute("relativeHeight", id),
            new XAttribute("behindDoc", wrap == TextWrap.BehindText ? "1" : "0"),
            new XAttribute("locked", "0"),
            new XAttribute("layoutInCell", "1"),
            new XAttribute("allowOverlap", "1"),
            // simplePos="0" says to use positionH/positionV instead, but the element is still required.
            new XElement(Ns.Wp + "simplePos",
                new XAttribute("x", "0"), new XAttribute("y", "0")),
            new XElement(Ns.Wp + "positionH",
                new XAttribute("relativeFrom", "column"),
                new XElement(Ns.Wp + "posOffset", "0")),
            new XElement(Ns.Wp + "positionV",
                new XAttribute("relativeFrom", "paragraph"),
                new XElement(Ns.Wp + "posOffset", "0")),
            new XElement(Ns.Wp + "extent",
                new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu)),
            new XElement(Ns.Wp + "effectExtent",
                new XAttribute("l", "0"), new XAttribute("t", "0"),
                new XAttribute("r", "0"), new XAttribute("b", "0")),
            WrapElement(wrap),
            new XElement(Ns.Wp + "docPr",
                new XAttribute("id", id), new XAttribute("name", name),
                new XAttribute("descr", altText ?? string.Empty)),
            new XElement(Ns.Wp + "cNvGraphicFramePr"),
            graphic);

    private static XElement WrapElement(TextWrap wrap) => wrap switch
    {
        TextWrap.Square => new XElement(Ns.Wp + "wrapSquare",
            new XAttribute("wrapText", "bothSides")),

        // A tight wrap needs an outline to flow around. Word recomputes it from the geometry when
        // the shape is edited; until then the bounding box is the honest answer.
        TextWrap.Tight => new XElement(Ns.Wp + "wrapTight",
            new XAttribute("wrapText", "bothSides"),
            new XElement(Ns.Wp + "wrapPolygon",
                new XAttribute("edited", "0"),
                new XElement(Ns.Wp + "start", new XAttribute("x", "0"), new XAttribute("y", "0")),
                new XElement(Ns.Wp + "lineTo", new XAttribute("x", "0"), new XAttribute("y", "21600")),
                new XElement(Ns.Wp + "lineTo", new XAttribute("x", "21600"), new XAttribute("y", "21600")),
                new XElement(Ns.Wp + "lineTo", new XAttribute("x", "21600"), new XAttribute("y", "0")),
                new XElement(Ns.Wp + "lineTo", new XAttribute("x", "0"), new XAttribute("y", "0")))),

        TextWrap.TopAndBottom => new XElement(Ns.Wp + "wrapTopAndBottom"),
        _ => new XElement(Ns.Wp + "wrapNone"),
    };

    // ---- Helpers -------------------------------------------------------------------------------

    private XElement EnsureTextBody()
    {
        var wsp = Wsp;

        if (wsp.Element(Ns.Wps + "txbx")?.Element(Ns.W + "txbxContent") is { } existing)
        {
            return existing;
        }

        var content = new XElement(Ns.W + "txbxContent");

        // wps:wsp is a sequence and bodyPr is last, so the text body goes immediately before it.
        wsp.Element(Ns.Wps + "bodyPr")!.AddBeforeSelf(new XElement(Ns.Wps + "txbx", content));

        return content;
    }

    private XElement EnsureLine()
    {
        if (ShapeProperties.Element(Ns.A + "ln") is { } existing)
        {
            return existing;
        }

        // Word's own default weight, so a shape given only a colour looks like one Word drew.
        var line = new XElement(Ns.A + "ln", new XAttribute("w", Length.FromPoints(0.75).Emu));
        ShapeProperties.Add(line);

        return line;
    }

    private static Length Emu(string? value) =>
        long.TryParse(value, CultureInfo.InvariantCulture, out var emu)
            ? Length.FromEmu(emu)
            : Length.Zero;

    private static string HorizontalName(HorizontalAnchor anchor) => anchor switch
    {
        HorizontalAnchor.Page => "page",
        HorizontalAnchor.Margin => "margin",
        HorizontalAnchor.Character => "character",
        _ => "column",
    };

    private static string VerticalName(VerticalAnchor anchor) => anchor switch
    {
        VerticalAnchor.Page => "page",
        VerticalAnchor.Margin => "margin",
        VerticalAnchor.Line => "line",
        _ => "paragraph",
    };

    internal static string PresetName(ShapeGeometry geometry) => geometry switch
    {
        ShapeGeometry.RoundedRectangle => "roundRect",
        ShapeGeometry.Ellipse => "ellipse",
        ShapeGeometry.Triangle => "triangle",
        ShapeGeometry.Diamond => "diamond",
        ShapeGeometry.Hexagon => "hexagon",
        ShapeGeometry.Star => "star5",
        ShapeGeometry.RightArrow => "rightArrow",
        ShapeGeometry.DownArrow => "downArrow",
        ShapeGeometry.Callout => "wedgeRoundRectCallout",
        ShapeGeometry.Line => "line",
        _ => "rect",
    };
}
