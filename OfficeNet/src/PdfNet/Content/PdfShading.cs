// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Drawing;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Content;

/// <summary>What geometry a shading paints its colours along.</summary>
public enum ShadingKind
{
    /// <summary>Type 2: colours run along a line, perpendicular bands.</summary>
    Axial,

    /// <summary>Type 3: colours run between two circles.</summary>
    Radial,
}

/// <summary>
/// An axial or radial gradient, reduced to geometry plus a colour ramp.
/// </summary>
/// <remarks>
/// <para>
/// Types 2 and 3 are what producers actually emit for a gradient, and they are the two a general
/// drawing library can express directly. Types 1, 4, 5, 6 and 7 describe meshes and arbitrary
/// functions of two variables; those are read as <c>null</c> rather than approximated, because a
/// mesh drawn as a linear ramp is a plausible-looking wrong answer.
/// </para>
/// <para>
/// The colours are handed over as a sampled ramp rather than as the function itself. Every consumer
/// wants stops, sampling is the only way to get them from a stitched or sampled function, and doing
/// it once here keeps the interpolation identical between consumers.
/// </para>
/// </remarks>
public sealed class PdfShading
{
    private PdfShading(ShadingKind kind, double[] coords, OfficeColor[] ramp, bool extendStart,
        bool extendEnd)
    {
        Kind = kind;
        Coords = coords;
        Ramp = ramp;
        ExtendStart = extendStart;
        ExtendEnd = extendEnd;
    }

    /// <summary>Whether the colours run along a line or between two circles.</summary>
    public ShadingKind Kind { get; }

    /// <summary>
    /// <c>[x0 y0 x1 y1]</c> for an axial shading, <c>[x0 y0 r0 x1 y1 r1]</c> for a radial one.
    /// </summary>
    public double[] Coords { get; }

    /// <summary>The colour ramp, evenly spaced from the start of the shading to its end.</summary>
    public OfficeColor[] Ramp { get; }

    /// <summary>Whether the first colour continues before the start of the geometry.</summary>
    public bool ExtendStart { get; }

    /// <summary>Whether the last colour continues past the end of the geometry.</summary>
    public bool ExtendEnd { get; }

    /// <summary>How many points the ramp is sampled at.</summary>
    /// <remarks>
    /// Enough that a stitched function's corners land within half a percent of the axis, and few
    /// enough that building one costs nothing worth measuring. A smooth exponential ramp would be
    /// exact with two.
    /// </remarks>
    private const int RampSize = 64;

    /// <summary>
    /// Reads a shading dictionary, or returns <c>null</c> when it is one this cannot express.
    /// </summary>
    public static PdfShading? Read(PdfObject? entry, PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Follow(entry) is not PdfDictionary shading)
        {
            return null;
        }

        var type = shading.GetInt(PdfName.Get("ShadingType"), -1);

        var kind = type switch
        {
            2 => ShadingKind.Axial,
            3 => ShadingKind.Radial,
            _ => (ShadingKind?)null,
        };

        if (kind is null)
        {
            return null;
        }

        var coords = ReadNumbers(shading.Get(PdfName.Get("Coords")), document);
        var expected = kind == ShadingKind.Axial ? 4 : 6;

        if (coords.Length < expected)
        {
            return null;
        }

        if (PdfFunction.Read(shading.Get(PdfName.Get("Function")), document) is not { } function)
        {
            return null;
        }

        var components = ColorComponents(shading, document);
        var ramp = new OfficeColor[RampSize];

        for (var i = 0; i < RampSize; i++)
        {
            var t = function.Domain0 +
                    ((function.Domain1 - function.Domain0) * i / (RampSize - 1.0));

            ramp[i] = ToColor(function.Evaluate(t), components);
        }

        var extend = document.Follow(shading.Get(PdfName.Get("Extend"))) as PdfArray;

        return new PdfShading(
            kind.Value,
            coords,
            ramp,
            extend is { Count: > 0 } && extend[0] is PdfBoolean s && s.Value,
            extend is { Count: > 1 } && extend[1] is PdfBoolean e && e.Value);
    }

    /// <summary>
    /// How many components the shading's colour space has.
    /// </summary>
    /// <remarks>
    /// Taken from the colour space rather than from the function's output, because a function may
    /// legitimately return more values than the space uses and the extra ones are not colour.
    /// Anything unrecognised is treated by output count, which is right for the three common spaces
    /// and no worse than refusing to draw.
    /// </remarks>
    private static int ColorComponents(PdfDictionary shading, PdfDocument document)
    {
        var space = document.Follow(shading.Get(PdfName.Get("ColorSpace")));

        return space switch
        {
            PdfName { Value: "DeviceGray" or "CalGray" or "G" } => 1,
            PdfName { Value: "DeviceRGB" or "CalRGB" or "Lab" or "RGB" } => 3,
            PdfName { Value: "DeviceCMYK" or "CMYK" } => 4,
            PdfArray { Count: > 0 } array when array[0] is PdfName { Value: "ICCBased" } =>
                IccComponents(array, document),
            _ => 0,
        };
    }

    private static int IccComponents(PdfArray array, PdfDocument document) =>
        array.Count > 1 && document.Follow(array[1]) is PdfStream stream
            ? stream.GetInt(PdfName.Get("N"), 3)
            : 3;

    /// <summary>Turns a function's output into a colour.</summary>
    private static OfficeColor ToColor(double[] values, int components)
    {
        var count = components > 0 ? Math.Min(components, values.Length) : values.Length;

        return count switch
        {
            1 => Gray(values[0]),
            3 => OfficeColor.FromRgb(Channel(values[0]), Channel(values[1]), Channel(values[2])),
            4 => Cmyk(values[0], values[1], values[2], values[3]),
            >= 3 => OfficeColor.FromRgb(Channel(values[0]), Channel(values[1]), Channel(values[2])),
            _ => OfficeColor.Black,
        };

        static OfficeColor Gray(double g) => OfficeColor.FromRgb(Channel(g), Channel(g), Channel(g));

        static OfficeColor Cmyk(double c, double m, double y, double k) =>
            OfficeColor.FromRgb(
                Channel((1 - c) * (1 - k)),
                Channel((1 - m) * (1 - k)),
                Channel((1 - y) * (1 - k)));

        static byte Channel(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
    }

    private static double[] ReadNumbers(PdfObject? entry, PdfDocument document)
    {
        if (document.Follow(entry) is not PdfArray array)
        {
            return [];
        }

        var values = new double[array.Count];

        for (var i = 0; i < array.Count; i++)
        {
            values[i] = document.Follow(array[i]) is PdfNumber number ? number.DoubleValue : 0;
        }

        return values;
    }
}
