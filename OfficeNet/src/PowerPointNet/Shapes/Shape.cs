// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace PowerPointNet.Shapes;

/// <summary>What kind of thing a shape on a slide is.</summary>
public enum ShapeKind
{
    /// <summary>An autoshape or text box.</summary>
    Shape,

    /// <summary>A picture.</summary>
    Picture,

    /// <summary>A table, chart or other graphic frame.</summary>
    GraphicFrame,

    /// <summary>A group of shapes.</summary>
    Group,

    /// <summary>A connector line.</summary>
    Connector,
}

/// <summary>The preset geometries a shape can use.</summary>
public enum ShapeGeometry
{
    /// <summary>A rectangle.</summary>
    Rectangle,

    /// <summary>A rectangle with rounded corners.</summary>
    RoundedRectangle,

    /// <summary>An ellipse.</summary>
    Ellipse,

    /// <summary>A triangle.</summary>
    Triangle,

    /// <summary>A right-pointing arrow.</summary>
    RightArrow,

    /// <summary>A five-pointed star.</summary>
    Star,

    /// <summary>A diamond.</summary>
    Diamond,

    /// <summary>A chevron.</summary>
    Chevron,

    /// <summary>A rounded speech bubble.</summary>
    Callout,

    /// <summary>A straight line.</summary>
    Line,
}

/// <summary>One shape on a slide, layout or master.</summary>
public class Shape
{
    /// <summary>
    /// The presentation the shape belongs to.
    /// </summary>
    /// <remarks>
    /// Public rather than protected because the effect helpers in
    /// <see cref="ShapeEffects"/> are extension methods, and because a caller holding a shape
    /// reasonably wants to reach the deck it came from.
    /// </remarks>
    public Presentation Presentation { get; }

    internal Shape(Presentation presentation, XElement element)
    {
        Presentation = presentation;
        Element = element;
    }

    /// <summary>The underlying <c>p:sp</c>, <c>p:pic</c> or <c>p:graphicFrame</c> element.</summary>
    public XElement Element { get; }

    /// <summary>What kind of shape this is.</summary>
    public ShapeKind Kind => Element.Name.LocalName switch
    {
        "pic" => ShapeKind.Picture,
        "graphicFrame" => ShapeKind.GraphicFrame,
        "grpSp" => ShapeKind.Group,
        "cxnSp" => ShapeKind.Connector,
        _ => ShapeKind.Shape,
    };

    private XElement? NonVisualProperties =>
        Element.Element(Ns.P + "nvSpPr") ?? Element.Element(Ns.P + "nvPicPr")
        ?? Element.Element(Ns.P + "nvGraphicFramePr") ?? Element.Element(Ns.P + "nvGrpSpPr")
        ?? Element.Element(Ns.P + "nvCxnSpPr");

    private XElement? Identity => NonVisualProperties?.Element(Ns.P + "cNvPr");

    /// <summary>The shape's id, unique within its slide.</summary>
    public uint Id => (uint)(Identity?.LongAttr("id") ?? 0);

    /// <summary>The shape's name, as shown in the selection pane.</summary>
    public string Name
    {
        get => Identity?.Attr("name") ?? string.Empty;
        set
        {
            Identity?.SetAttributeValue("name", value);
            Presentation.Touch();
        }
    }

    /// <summary>Alternative text for accessibility.</summary>
    public string? AltText
    {
        get => Identity?.Attr("descr");
        set
        {
            Identity?.SetAttributeValue("descr", value);
            Presentation.Touch();
        }
    }

    /// <summary>
    /// The placeholder type this shape fills, or <c>null</c> when it is a free shape.
    /// </summary>
    public string? PlaceholderType =>
        NonVisualProperties?.Element(Ns.P + "nvPr")?.Element(Ns.P + "ph")?.Attr("type");

    /// <summary>The placeholder index this shape binds to on the layout.</summary>
    public int? PlaceholderIndex
    {
        get
        {
            var raw = NonVisualProperties?.Element(Ns.P + "nvPr")?.Element(Ns.P + "ph")?.Attr("idx");
            return raw is not null && int.TryParse(raw, out var index) ? index : null;
        }
    }

    /// <summary>True when the shape fills a layout placeholder.</summary>
    public bool IsPlaceholder =>
        NonVisualProperties?.Element(Ns.P + "nvPr")?.Element(Ns.P + "ph") is not null;

    private protected XElement ShapeProperties =>
        Element.Element(Ns.P + "spPr") ?? Element.Element(Ns.P + "grpSpPr")
        ?? CreateShapeProperties();

    private XElement CreateShapeProperties()
    {
        var created = new XElement(Ns.P + "spPr");

        // p:spPr follows the non-visual properties and precedes p:txBody.
        var body = Element.Element(Ns.P + "txBody");

        if (body is not null)
        {
            body.AddBeforeSelf(created);
        }
        else
        {
            Element.Add(created);
        }

        Presentation.Touch();
        return created;
    }

    private XElement? Transform =>
        // A graphic frame keeps its transform in p:xfrm, everything else in a:xfrm inside spPr.
        Element.Element(Ns.P + "xfrm") ?? ShapeProperties.Element(Ns.A + "xfrm");

    private XElement EnsureTransform()
    {
        if (Transform is { } existing)
        {
            return existing;
        }

        var created = new XElement(Ns.A + "xfrm",
            new XElement(Ns.A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
            new XElement(Ns.A + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")));

        // a:xfrm must be the first child of p:spPr, before the geometry.
        ShapeProperties.AddFirst(created);
        Presentation.Touch();
        return created;
    }

    private Length Offset(string attribute)
    {
        var raw = Transform?.Element(Ns.A + "off")?.Attr(attribute);
        return raw is not null && long.TryParse(raw, out var emu) ? Length.FromEmu(emu) : Length.Zero;
    }

    private Length Extent(string attribute)
    {
        var raw = Transform?.Element(Ns.A + "ext")?.Attr(attribute);
        return raw is not null && long.TryParse(raw, out var emu) ? Length.FromEmu(emu) : Length.Zero;
    }

    private void SetOffset(string attribute, Length value)
    {
        var transform = EnsureTransform();
        var offset = transform.Element(Ns.A + "off");

        if (offset is null)
        {
            offset = new XElement(Ns.A + "off", new XAttribute("x", "0"), new XAttribute("y", "0"));
            transform.AddFirst(offset);
        }

        offset.SetAttributeValue(attribute, value.Emu);
        Presentation.Touch();
    }

    private void SetExtent(string attribute, Length value)
    {
        var transform = EnsureTransform();
        var extent = transform.Element(Ns.A + "ext");

        if (extent is null)
        {
            extent = new XElement(Ns.A + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0"));
            transform.Add(extent);
        }

        extent.SetAttributeValue(attribute, value.Emu);
        Presentation.Touch();
    }

    /// <summary>The distance from the slide's left edge.</summary>
    public Length Left
    {
        get => Offset("x");
        set => SetOffset("x", value);
    }

    /// <summary>The distance from the slide's top edge.</summary>
    public Length Top
    {
        get => Offset("y");
        set => SetOffset("y", value);
    }

    /// <summary>The shape's width.</summary>
    public Length Width
    {
        get => Extent("cx");
        set => SetExtent("cx", value);
    }

    /// <summary>The shape's height.</summary>
    public Length Height
    {
        get => Extent("cy");
        set => SetExtent("cy", value);
    }

    /// <summary>Rotation in degrees, clockwise.</summary>
    public double Rotation
    {
        get
        {
            // a:xfrm/@rot is in 60000ths of a degree.
            var raw = Transform?.Attr("rot");
            return raw is not null && double.TryParse(raw, out var value) ? value / 60000 : 0;
        }
        set
        {
            EnsureTransform().SetAttributeValue("rot",
                Math.Abs(value) < 1e-9 ? null : (long)Math.Round(value * 60000));
            Presentation.Touch();
        }
    }

    /// <summary>Sets the shape's position and size in one call.</summary>
    public Shape SetBounds(Length left, Length top, Length width, Length height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        return this;
    }

    /// <summary>The shape's text, or <c>null</c> when it holds none.</summary>
    public TextFrame? TextFrame =>
        Element.Element(Ns.P + "txBody") is { } body
            ? new TextFrame(Presentation, body)
            : null;

    /// <summary>The shape's text, or the empty string.</summary>
    public string Text
    {
        get => TextFrame?.Text ?? string.Empty;
        set
        {
            var frame = TextFrame ?? throw new OfficeNetException(
                $"Shape '{Name}' has no text frame. Only shapes and placeholders hold text.");

            frame.Text = value;
        }
    }

    /// <summary>The shape's solid fill colour, or <c>null</c>.</summary>
    public OfficeColor? FillColor
    {
        get
        {
            var raw = ShapeProperties.Element(Ns.A + "solidFill")?.Element(Ns.A + "srgbClr")
                ?.Attr("val");
            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set
        {
            var properties = ShapeProperties;
            properties.Elements(Ns.A + "solidFill").Remove();
            properties.Elements(Ns.A + "noFill").Remove();

            var fill = value is null
                ? new XElement(Ns.A + "noFill")
                : new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "srgbClr", new XAttribute("val", value.Value.ToHex())));

            InsertAfterGeometry(properties, fill);
            Presentation.Touch();
        }
    }

    /// <summary>The shape's outline colour, or <c>null</c> for no outline.</summary>
    public OfficeColor? LineColor
    {
        get
        {
            var raw = ShapeProperties.Element(Ns.A + "ln")?.Element(Ns.A + "solidFill")
                ?.Element(Ns.A + "srgbClr")?.Attr("val");
            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set => SetLine(value, LineWidth);
    }

    /// <summary>The shape's outline width.</summary>
    public Length LineWidth
    {
        get
        {
            var raw = ShapeProperties.Element(Ns.A + "ln")?.Attr("w");
            return raw is not null && long.TryParse(raw, out var emu) ? Length.FromEmu(emu) : Length.Zero;
        }
        set => SetLine(LineColor, value);
    }

    private void SetLine(OfficeColor? color, Length width)
    {
        var properties = ShapeProperties;
        properties.Elements(Ns.A + "ln").Remove();

        var line = new XElement(Ns.A + "ln");

        if (width.Emu > 0)
        {
            line.SetAttributeValue("w", width.Emu);
        }

        line.Add(color is null
            ? new XElement(Ns.A + "noFill")
            : new XElement(Ns.A + "solidFill",
                new XElement(Ns.A + "srgbClr", new XAttribute("val", color.Value.ToHex()))));

        // a:ln comes after the fill in p:spPr.
        properties.Add(line);
        Presentation.Touch();
    }

    private static void InsertAfterGeometry(XElement properties, XElement fill)
    {
        var geometry = properties.Element(Ns.A + "prstGeom") ?? properties.Element(Ns.A + "custGeom");

        if (geometry is not null)
        {
            geometry.AddAfterSelf(fill);
            return;
        }

        var transform = properties.Element(Ns.A + "xfrm");

        if (transform is not null)
        {
            transform.AddAfterSelf(fill);
            return;
        }

        properties.AddFirst(fill);
    }

    /// <summary>Removes the shape from its slide.</summary>
    public void Remove()
    {
        Element.Remove();
        Presentation.Touch();
    }

    internal static string GeometryName(ShapeGeometry geometry) => geometry switch
    {
        ShapeGeometry.RoundedRectangle => "roundRect",
        ShapeGeometry.Ellipse => "ellipse",
        ShapeGeometry.Triangle => "triangle",
        ShapeGeometry.RightArrow => "rightArrow",
        ShapeGeometry.Star => "star5",
        ShapeGeometry.Diamond => "diamond",
        ShapeGeometry.Chevron => "chevron",
        ShapeGeometry.Callout => "wedgeRoundRectCallout",
        ShapeGeometry.Line => "line",
        _ => "rect",
    };

    public override string ToString() =>
        $"{Kind} \"{Name}\"{(Text.Length > 0 ? $": {Text.Split('\n')[0]}" : "")}";
}

/// <summary>A picture on a slide.</summary>
public sealed class Picture : Shape
{
    internal Picture(Presentation presentation, XElement element) : base(presentation, element)
    {
    }

    /// <summary>The relationship id of the image part this picture draws.</summary>
    public string? ImageRelationshipId =>
        Element.Element(Ns.P + "blipFill")?.Element(Ns.A + "blip")?.Attr(Ns.R + "embed");

    /// <summary>The image's bytes, or <c>null</c> when the relationship is broken.</summary>
    public byte[]? GetImageBytes()
    {
        var id = ImageRelationshipId;
        return id is null ? null : Presentation.ResolveImage(Element, id);
    }

    /// <summary>
    /// Crops the picture by a fraction of each edge, 0 to 1.
    /// </summary>
    /// <remarks>
    /// Crop is stored as the fraction removed, in thousandths of a percent, and it is applied to
    /// the source image rather than to the frame — so cropping does not change the shape's size,
    /// it changes which part of the picture fills it.
    /// </remarks>
    public Picture SetCrop(double left = 0, double top = 0, double right = 0, double bottom = 0)
    {
        var fill = Element.Element(Ns.P + "blipFill")
            ?? throw new OfficeNetException("The picture has no blipFill.");

        fill.Elements(Ns.A + "srcRect").Remove();

        static int Rate(double fraction) => (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100000);

        var rect = new XElement(Ns.A + "srcRect");

        if (left > 0)
        {
            rect.SetAttributeValue("l", Rate(left));
        }

        if (top > 0)
        {
            rect.SetAttributeValue("t", Rate(top));
        }

        if (right > 0)
        {
            rect.SetAttributeValue("r", Rate(right));
        }

        if (bottom > 0)
        {
            rect.SetAttributeValue("b", Rate(bottom));
        }

        // a:srcRect goes after a:blip and before a:stretch.
        var blip = fill.Element(Ns.A + "blip");

        if (blip is not null)
        {
            blip.AddAfterSelf(rect);
        }
        else
        {
            fill.AddFirst(rect);
        }

        Presentation.Touch();
        return this;
    }
}
