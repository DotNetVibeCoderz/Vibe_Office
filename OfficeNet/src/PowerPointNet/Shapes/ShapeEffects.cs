// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace PowerPointNet.Shapes;

/// <summary>Which way a linear gradient runs.</summary>
public enum GradientDirection
{
    /// <summary>Left to right.</summary>
    Horizontal,

    /// <summary>Top to bottom.</summary>
    Vertical,

    /// <summary>Top-left to bottom-right.</summary>
    DiagonalDown,

    /// <summary>Bottom-left to top-right.</summary>
    DiagonalUp,
}

/// <summary>How a line is dashed.</summary>
public enum LineDash
{
    /// <summary>A continuous line.</summary>
    Solid,

    /// <summary>Evenly spaced dots.</summary>
    Dot,

    /// <summary>Evenly spaced dashes.</summary>
    Dash,

    /// <summary>Long dashes.</summary>
    LongDash,

    /// <summary>A dash then a dot.</summary>
    DashDot,

    /// <summary>Long dash, dot, dot.</summary>
    LongDashDotDot,
}

/// <summary>
/// The visual effects PptxGenJS exposes on a shape: gradients, transparency, shadows and dashes.
/// </summary>
/// <remarks>
/// <para>
/// These are extension methods rather than properties on <see cref="Shape"/> because every one of
/// them writes into <c>p:spPr</c>, and DrawingML fixes the order of what goes in there: transform,
/// geometry, fill, line, effect list. Keeping the writers together makes that order checkable in
/// one place instead of spread across a dozen setters.
/// </para>
/// </remarks>
public static class ShapeEffects
{
    /// <summary>The order <c>p:spPr</c>'s children must appear in.</summary>
    private static readonly XName[] ShapePropertyOrder =
    [
        Ns.A + "xfrm", Ns.A + "custGeom", Ns.A + "prstGeom",
        Ns.A + "noFill", Ns.A + "solidFill", Ns.A + "gradFill", Ns.A + "blipFill",
        Ns.A + "pattFill", Ns.A + "grpFill",
        Ns.A + "ln", Ns.A + "effectLst", Ns.A + "effectDag", Ns.A + "scene3d", Ns.A + "sp3d",
    ];

    private static XElement Properties(Shape shape) =>
        shape.Element.Element(Ns.P + "spPr")
        ?? throw new OfficeNetException($"Shape '{shape.Name}' has no p:spPr to style.");

    private static void SetFill(Shape shape, XElement? fill)
    {
        var properties = Properties(shape);

        // Exactly one fill element may be present; leaving an old one alongside a new one makes
        // PowerPoint pick whichever comes first, which is rarely the one that was just set.
        foreach (var name in (XName[])
                 [
                     Ns.A + "noFill", Ns.A + "solidFill", Ns.A + "gradFill", Ns.A + "blipFill",
                     Ns.A + "pattFill", Ns.A + "grpFill",
                 ])
        {
            properties.Elements(name).Remove();
        }

        if (fill is not null)
        {
            XmlUtil.SetOrdered(properties, fill.Name, fill, ShapePropertyOrder);
        }

        shape.Presentation.Touch();
    }

    /// <summary>
    /// Fills the shape with a two-colour linear gradient.
    /// </summary>
    /// <param name="shape">The shape to fill.</param>
    /// <param name="from">The colour at the start of the gradient.</param>
    /// <param name="to">The colour at the end.</param>
    /// <param name="direction">Which way the gradient runs.</param>
    public static T WithGradientFill<T>(this T shape, OfficeColor from, OfficeColor to,
        GradientDirection direction = GradientDirection.Vertical)
        where T : Shape
    {
        ArgumentNullException.ThrowIfNull(shape);

        // a:lin/@ang is in 60000ths of a degree, measured clockwise from the positive x axis.
        var angle = direction switch
        {
            GradientDirection.Horizontal => 0,
            GradientDirection.Vertical => 5_400_000,
            GradientDirection.DiagonalDown => 2_700_000,
            _ => 18_900_000,
        };

        var fill = new XElement(Ns.A + "gradFill",
            new XAttribute("flip", "none"),
            new XAttribute("rotWithShape", "1"),
            new XElement(Ns.A + "gsLst",
                // Stop positions are in thousandths of a percent, so a full sweep is 0 to 100000.
                Stop(from, 0),
                Stop(to, 100_000)),
            new XElement(Ns.A + "lin",
                new XAttribute("ang", angle.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("scaled", "0")));

        SetFill(shape, fill);
        return shape;
    }

    /// <summary>Fills the shape with a gradient through several colours.</summary>
    /// <param name="shape">The shape to fill.</param>
    /// <param name="direction">Which way the gradient runs.</param>
    /// <param name="colors">Two or more colours, spread evenly along the gradient.</param>
    public static T WithGradientFill<T>(this T shape, GradientDirection direction,
        params OfficeColor[] colors)
        where T : Shape
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(colors);

        if (colors.Length < 2)
        {
            throw new ArgumentException("A gradient needs at least two colours.", nameof(colors));
        }

        var stops = new XElement(Ns.A + "gsLst");

        for (var i = 0; i < colors.Length; i++)
        {
            stops.Add(Stop(colors[i], (int)Math.Round(100_000.0 * i / (colors.Length - 1))));
        }

        var angle = direction switch
        {
            GradientDirection.Horizontal => 0,
            GradientDirection.Vertical => 5_400_000,
            GradientDirection.DiagonalDown => 2_700_000,
            _ => 18_900_000,
        };

        SetFill(shape, new XElement(Ns.A + "gradFill",
            new XAttribute("flip", "none"),
            new XAttribute("rotWithShape", "1"),
            stops,
            new XElement(Ns.A + "lin",
                new XAttribute("ang", angle.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("scaled", "0"))));

        return shape;
    }

    private static XElement Stop(OfficeColor color, int position) =>
        new(Ns.A + "gs",
            new XAttribute("pos", position.ToString(CultureInfo.InvariantCulture)),
            new XElement(Ns.A + "srgbClr", new XAttribute("val", color.ToHex())));

    /// <summary>
    /// Fills the shape with a solid colour at a given opacity.
    /// </summary>
    /// <param name="shape">The shape to fill.</param>
    /// <param name="color">The colour.</param>
    /// <param name="opacity">0 is invisible, 1 is opaque.</param>
    /// <remarks>
    /// Transparency in DrawingML is a child of the colour, not a property of the fill: an
    /// <c>a:alpha</c> inside <c>a:srgbClr</c>. Setting it anywhere else is silently ignored.
    /// </remarks>
    public static T WithFill<T>(this T shape, OfficeColor color, double opacity = 1)
        where T : Shape
    {
        ArgumentNullException.ThrowIfNull(shape);

        var value = new XElement(Ns.A + "srgbClr", new XAttribute("val", color.ToHex()));

        if (opacity < 1)
        {
            value.Add(new XElement(Ns.A + "alpha",
                new XAttribute("val",
                    ((int)Math.Round(Math.Clamp(opacity, 0, 1) * 100_000))
                    .ToString(CultureInfo.InvariantCulture))));
        }

        SetFill(shape, new XElement(Ns.A + "solidFill", value));
        return shape;
    }

    /// <summary>Removes the shape's fill.</summary>
    public static T WithNoFill<T>(this T shape) where T : Shape
    {
        ArgumentNullException.ThrowIfNull(shape);

        SetFill(shape, new XElement(Ns.A + "noFill"));
        return shape;
    }

    /// <summary>Sets the shape's outline.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="color">The line colour; <c>null</c> removes the outline.</param>
    /// <param name="width">The line width.</param>
    /// <param name="dash">The dash pattern.</param>
    public static T WithOutline<T>(this T shape, OfficeColor? color, Length? width = null,
        LineDash dash = LineDash.Solid)
        where T : Shape
    {
        ArgumentNullException.ThrowIfNull(shape);

        var properties = Properties(shape);
        properties.Elements(Ns.A + "ln").Remove();

        var line = new XElement(Ns.A + "ln");

        if (width is { } thickness && thickness.Emu > 0)
        {
            line.SetAttributeValue("w", thickness.Emu);
        }

        line.Add(color is null
            ? new XElement(Ns.A + "noFill")
            : new XElement(Ns.A + "solidFill",
                new XElement(Ns.A + "srgbClr", new XAttribute("val", color.Value.ToHex()))));

        if (dash != LineDash.Solid)
        {
            line.Add(new XElement(Ns.A + "prstDash",
                new XAttribute("val", dash switch
                {
                    LineDash.Dot => "sysDot",
                    LineDash.Dash => "dash",
                    LineDash.LongDash => "lgDash",
                    LineDash.DashDot => "dashDot",
                    LineDash.LongDashDotDot => "lgDashDotDot",
                    _ => "solid",
                })));
        }

        XmlUtil.SetOrdered(properties, Ns.A + "ln", line, ShapePropertyOrder);
        shape.Presentation.Touch();
        return shape;
    }

    /// <summary>
    /// Adds a drop shadow.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="color">The shadow colour.</param>
    /// <param name="blur">How soft the shadow is.</param>
    /// <param name="distance">How far the shadow is offset.</param>
    /// <param name="angleDegrees">The offset direction, clockwise from the right.</param>
    /// <param name="opacity">0 is invisible, 1 is opaque.</param>
    public static T WithShadow<T>(this T shape, OfficeColor? color = null, Length? blur = null,
        Length? distance = null, double angleDegrees = 45, double opacity = 0.4)
        where T : Shape
    {
        ArgumentNullException.ThrowIfNull(shape);

        var properties = Properties(shape);

        var shadowColor = new XElement(Ns.A + "srgbClr",
            new XAttribute("val", (color ?? OfficeColor.Black).ToHex()),
            new XElement(Ns.A + "alpha",
                new XAttribute("val",
                    ((int)Math.Round(Math.Clamp(opacity, 0, 1) * 100_000))
                    .ToString(CultureInfo.InvariantCulture))));

        var effects = new XElement(Ns.A + "effectLst",
            new XElement(Ns.A + "outerShdw",
                new XAttribute("blurRad", (blur ?? Units.Pt(4)).Emu),
                new XAttribute("dist", (distance ?? Units.Pt(3)).Emu),
                // dir is in 60000ths of a degree, like every other DrawingML angle.
                new XAttribute("dir",
                    ((long)Math.Round(angleDegrees * 60_000)).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("rotWithShape", "0"),
                shadowColor));

        XmlUtil.SetOrdered(properties, Ns.A + "effectLst", effects, ShapePropertyOrder);
        shape.Presentation.Touch();
        return shape;
    }

    /// <summary>Adds a soft glow around the shape.</summary>
    public static T WithGlow<T>(this T shape, OfficeColor color, Length? radius = null,
        double opacity = 0.6)
        where T : Shape
    {
        ArgumentNullException.ThrowIfNull(shape);

        var properties = Properties(shape);

        var effects = new XElement(Ns.A + "effectLst",
            new XElement(Ns.A + "glow",
                new XAttribute("rad", (radius ?? Units.Pt(6)).Emu),
                new XElement(Ns.A + "srgbClr",
                    new XAttribute("val", color.ToHex()),
                    new XElement(Ns.A + "alpha",
                        new XAttribute("val",
                            ((int)Math.Round(Math.Clamp(opacity, 0, 1) * 100_000))
                            .ToString(CultureInfo.InvariantCulture))))));

        XmlUtil.SetOrdered(properties, Ns.A + "effectLst", effects, ShapePropertyOrder);
        shape.Presentation.Touch();
        return shape;
    }

    /// <summary>Removes every effect from the shape.</summary>
    public static T WithNoEffects<T>(this T shape) where T : Shape
    {
        ArgumentNullException.ThrowIfNull(shape);

        Properties(shape).Elements(Ns.A + "effectLst").Remove();
        shape.Presentation.Touch();
        return shape;
    }

    /// <summary>
    /// Makes the shape a hyperlink.
    /// </summary>
    /// <remarks>
    /// The link lives in the shape's non-visual properties, not in its text: clicking anywhere on
    /// the shape follows it, which is what makes a rounded rectangle work as a button.
    /// </remarks>
    public static T WithHyperlink<T>(this T shape, string url) where T : Shape
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var identity = shape.Element.Elements()
            .FirstOrDefault(e => e.Name.LocalName.StartsWith("nv", StringComparison.Ordinal))
            ?.Element(Ns.P + "cNvPr")
            ?? throw new OfficeNetException($"Shape '{shape.Name}' has no cNvPr to link from.");

        var relationshipId = shape.Presentation.AddHyperlink(shape.Element, url);

        identity.Elements(Ns.A + "hlinkClick").Remove();

        // a:hlinkClick is the last child of a:cNvPr, after any extension list.
        identity.Add(new XElement(Ns.A + "hlinkClick",
            new XAttribute(Ns.R + "id", relationshipId)));

        shape.Presentation.Touch();
        return shape;
    }

    /// <summary>Rounds a picture's corners into a circle or an ellipse.</summary>
    /// <remarks>
    /// A picture's shape comes from its geometry, not from a crop: swapping the preset from
    /// <c>rect</c> to <c>ellipse</c> is what makes a round avatar.
    /// </remarks>
    public static Picture AsEllipse(this Picture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        SetGeometry(picture, "ellipse");
        return picture;
    }

    /// <summary>Rounds a picture's corners.</summary>
    public static Picture AsRoundedRectangle(this Picture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        SetGeometry(picture, "roundRect");
        return picture;
    }

    private static void SetGeometry(Shape shape, string preset)
    {
        var properties = Properties(shape);
        properties.Elements(Ns.A + "prstGeom").Remove();
        properties.Elements(Ns.A + "custGeom").Remove();

        XmlUtil.SetOrdered(properties, Ns.A + "prstGeom",
            new XElement(Ns.A + "prstGeom",
                new XAttribute("prst", preset),
                new XElement(Ns.A + "avLst")), ShapePropertyOrder);

        shape.Presentation.Touch();
    }

    /// <summary>
    /// Crops a picture so it fills its frame without distortion — the CSS <c>cover</c> behaviour.
    /// </summary>
    /// <remarks>
    /// Stretching a photograph to a frame of a different aspect ratio is the most visible way a
    /// generated deck looks wrong. This trims the overflowing dimension instead, which is what a
    /// designer means by "fill this box".
    /// </remarks>
    public static Picture Cover(this Picture picture, byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(imageBytes);

        var info = ImageInfo.Read(imageBytes);

        var frameRatio = picture.Height.Emu == 0
            ? info.AspectRatio
            : (double)picture.Width.Emu / picture.Height.Emu;

        if (Math.Abs(frameRatio - info.AspectRatio) < 1e-6)
        {
            return picture;
        }

        if (info.AspectRatio > frameRatio)
        {
            // The image is wider than the frame: trim the sides.
            var keep = frameRatio / info.AspectRatio;
            var trim = (1 - keep) / 2;
            picture.SetCrop(left: trim, right: trim);
        }
        else
        {
            var keep = info.AspectRatio / frameRatio;
            var trim = (1 - keep) / 2;
            picture.SetCrop(top: trim, bottom: trim);
        }

        return picture;
    }
}
