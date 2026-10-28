// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core.Xml;

namespace PowerPointNet.Shapes;

/// <summary>One step of a freeform path.</summary>
/// <param name="Kind">moveTo, lnTo, cubicBezTo, quadBezTo, arcTo or close.</param>
/// <param name="Values">
/// The points as x, y pairs — one for a move or line, two for a quadratic, three for a cubic — or,
/// for an arc, its width radius, height radius, start angle and swing angle in degrees.
/// </param>
public readonly record struct PathCommand(string Kind, IReadOnlyList<double> Values);

/// <summary>
/// A freeform outline in its own coordinate grid, scaled to the shape it draws.
/// </summary>
/// <remarks>
/// <para>
/// DrawingML's own model: a path declares a width and height, and its points are in those units, so
/// a star drawn once in a 100 × 100 grid fits whatever box it is placed in. A path may hold several
/// figures — each <see cref="MoveTo"/> starts one — and need not be filled or stroked, which is how a
/// single shape mixes an outline with a filled part.
/// </para>
/// <para>
/// Arcs take DrawingML's parameters: the ellipse's radii, then the angle the arc starts at on that
/// ellipse and how far it turns, in degrees, positive clockwise. The arc starts at the current point.
/// </para>
/// </remarks>
public sealed class FreeformPath
{
    private readonly List<PathCommand> _commands = [];

    /// <summary>A path whose points are given in a <paramref name="width"/> × <paramref name="height"/> grid.</summary>
    public FreeformPath(double width, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }

    /// <summary>Whether the path is filled with the shape's fill.</summary>
    public bool Fill { get; set; } = true;

    /// <summary>Whether the path is drawn with the shape's outline.</summary>
    public bool Stroke { get; set; } = true;

    public IReadOnlyList<PathCommand> Commands => _commands;

    public FreeformPath MoveTo(double x, double y) => Add("moveTo", x, y);

    public FreeformPath LineTo(double x, double y) => Add("lnTo", x, y);

    public FreeformPath CubicTo(double x1, double y1, double x2, double y2, double x, double y) =>
        Add("cubicBezTo", x1, y1, x2, y2, x, y);

    public FreeformPath QuadTo(double x1, double y1, double x, double y) => Add("quadBezTo", x1, y1, x, y);

    /// <summary>An elliptical arc from the current point; angles in degrees, clockwise.</summary>
    public FreeformPath ArcTo(double widthRadius, double heightRadius, double startAngle, double swingAngle) =>
        Add("arcTo", widthRadius, heightRadius, startAngle, swingAngle);

    public FreeformPath Close() => Add("close");

    /// <summary>A closed polygon through the points given, in order.</summary>
    public static FreeformPath Polygon(double width, double height, params (double X, double Y)[] points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Length < 3)
        {
            throw new ArgumentException("A polygon needs at least three points.", nameof(points));
        }

        var path = new FreeformPath(width, height).MoveTo(points[0].X, points[0].Y);

        foreach (var (x, y) in points.Skip(1))
        {
            path.LineTo(x, y);
        }

        return path.Close();
    }

    private FreeformPath Add(string kind, params double[] values)
    {
        if (_commands.Count == 0 && kind != "moveTo")
        {
            throw new InvalidOperationException("A path starts with MoveTo.");
        }

        _commands.Add(new PathCommand(kind, values));
        return this;
    }

    // ---- XML -----------------------------------------------------------------------------------

    /// <summary>The path as DrawingML coordinates are written: whole numbers.</summary>
    internal XElement ToXml()
    {
        var path = new XElement(Ns.A + "path",
            new XAttribute("w", Whole(Width)), new XAttribute("h", Whole(Height)),
            Fill ? null : new XAttribute("fill", "none"),
            Stroke ? null : new XAttribute("stroke", "0"));

        foreach (var command in _commands)
        {
            var element = new XElement(Ns.A + command.Kind);

            if (command.Kind == "arcTo")
            {
                // Radii in path units; angles in 60000ths of a degree.
                element.Add(new XAttribute("wR", Whole(command.Values[0])), new XAttribute("hR", Whole(command.Values[1])),
                    new XAttribute("stAng", Whole(command.Values[2] * 60000)), new XAttribute("swAng", Whole(command.Values[3] * 60000)));
            }
            else
            {
                for (var i = 0; i + 1 < command.Values.Count; i += 2)
                {
                    element.Add(new XElement(Ns.A + "pt",
                        new XAttribute("x", Whole(command.Values[i])), new XAttribute("y", Whole(command.Values[i + 1]))));
                }
            }

            path.Add(element);
        }

        return path;
    }

    private static string Whole(double value) => Math.Round(value).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the <c>a:custGeom</c> element: the guide and handle lists the schema requires, empty,
    /// then the text rectangle (the whole shape) and the paths.
    /// </summary>
    internal static XElement Geometry(IEnumerable<FreeformPath> paths) =>
        new(Ns.A + "custGeom",
            new XElement(Ns.A + "avLst"),
            new XElement(Ns.A + "gdLst"),
            new XElement(Ns.A + "ahLst"),
            new XElement(Ns.A + "cxnLst"),
            new XElement(Ns.A + "rect", new XAttribute("l", "l"), new XAttribute("t", "t"),
                new XAttribute("r", "r"), new XAttribute("b", "b")),
            new XElement(Ns.A + "pathLst", paths.Select(p => p.ToXml())));

    /// <summary>
    /// Reads the paths of a custom geometry, or nothing when a coordinate is a guide formula rather
    /// than a number — something only PowerPoint's guide engine could evaluate.
    /// </summary>
    internal static IReadOnlyList<FreeformPath> Read(XElement? custGeom)
    {
        var result = new List<FreeformPath>();

        if (custGeom?.Element(Ns.A + "pathLst") is not { } list)
        {
            return result;
        }

        foreach (var element in list.Elements(Ns.A + "path"))
        {
            var width = Number(element.Attr("w")) ?? 0;
            var height = Number(element.Attr("h")) ?? 0;

            // A path with no size is in the shape's own EMU; one that says nothing takes the shape's.
            var path = new FreeformPath(width > 0 ? width : 1, height > 0 ? height : 1)
            {
                Fill = element.Attr("fill") is not "none",
                Stroke = element.Attr("stroke") is not ("0" or "false"),
            };

            foreach (var command in element.Elements())
            {
                var kind = command.Name.LocalName;
                double[]? values;

                if (kind == "arcTo")
                {
                    values = [Number(command.Attr("wR")) ?? double.NaN, Number(command.Attr("hR")) ?? double.NaN,
                        (Number(command.Attr("stAng")) ?? double.NaN) / 60000, (Number(command.Attr("swAng")) ?? double.NaN) / 60000];
                }
                else if (kind == "close")
                {
                    values = [];
                }
                else
                {
                    values = [.. command.Elements(Ns.A + "pt").SelectMany(pt => new[] { Number(pt.Attr("x")) ?? double.NaN, Number(pt.Attr("y")) ?? double.NaN })];
                }

                if (values.Any(double.IsNaN))
                {
                    return [];
                }

                path._commands.Add(new PathCommand(kind, values));
            }

            if (path._commands.Count > 0)
            {
                result.Add(path);
            }
        }

        return result;
    }

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
