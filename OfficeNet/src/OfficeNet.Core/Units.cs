// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace OfficeNet.Core;

/// <summary>
/// Length in the one unit every OOXML part can be converted to without loss: the English Metric
/// Unit (914400 per inch, 360000 per centimetre).
/// </summary>
/// <remarks>
/// <para>
/// EMU is the storage unit on purpose. The three formats disagree about what they write —
/// WordprocessingML measures pages in twips (1/1440 inch), DrawingML measures shapes in EMU,
/// SpreadsheetML measures column widths in character units and row heights in points, and
/// PresentationML measures slides in EMU — and every one of those is an exact integer fraction of
/// an inch. Converting through <see cref="double"/> inches instead loses the round trip: a 2.54 cm
/// margin written as twips and read back as centimetres comes out 2.5400000000000005.
/// </para>
/// <para>
/// EMU was chosen by the format authors precisely because 914400 = 2^7 x 3^2 x 5^2 x 127 is
/// divisible by 72 (points), 96 (pixels), 1440 (twips), 100 (hundredths of a point) and 360000
/// (centimetres), so all of those are exact.
/// </para>
/// </remarks>
public readonly struct Length : IEquatable<Length>, IComparable<Length>
{
    /// <summary>English Metric Units per inch.</summary>
    public const long EmuPerInch = 914_400;

    /// <summary>English Metric Units per centimetre.</summary>
    public const long EmuPerCentimeter = 360_000;

    /// <summary>English Metric Units per millimetre.</summary>
    public const long EmuPerMillimeter = 36_000;

    /// <summary>English Metric Units per point (1/72 inch).</summary>
    public const long EmuPerPoint = 12_700;

    /// <summary>English Metric Units per twip (1/20 point, 1/1440 inch).</summary>
    public const long EmuPerTwip = 635;

    /// <summary>English Metric Units per pixel at the OOXML nominal 96 DPI.</summary>
    public const long EmuPerPixel = 9_525;

    /// <summary>The underlying value in English Metric Units.</summary>
    public long Emu { get; }

    private Length(long emu) => Emu = emu;

    /// <summary>A zero length.</summary>
    public static Length Zero => new(0);

    /// <summary>Creates a length from raw English Metric Units.</summary>
    public static Length FromEmu(long emu) => new(emu);

    /// <summary>Creates a length from inches.</summary>
    public static Length FromInches(double inches) => new((long)Math.Round(inches * EmuPerInch));

    /// <summary>Creates a length from centimetres.</summary>
    public static Length FromCentimeters(double cm) => new((long)Math.Round(cm * EmuPerCentimeter));

    /// <summary>Creates a length from millimetres.</summary>
    public static Length FromMillimeters(double mm) => new((long)Math.Round(mm * EmuPerMillimeter));

    /// <summary>Creates a length from points (1/72 inch).</summary>
    public static Length FromPoints(double points) => new((long)Math.Round(points * EmuPerPoint));

    /// <summary>Creates a length from twips (1/1440 inch), the unit WordprocessingML writes.</summary>
    public static Length FromTwips(double twips) => new((long)Math.Round(twips * EmuPerTwip));

    /// <summary>Creates a length from pixels at 96 DPI.</summary>
    public static Length FromPixels(double pixels) => new((long)Math.Round(pixels * EmuPerPixel));

    /// <summary>Creates a length from pixels at an explicit resolution.</summary>
    public static Length FromPixels(double pixels, double dpi) =>
        new((long)Math.Round(pixels / dpi * EmuPerInch));

    /// <summary>The length in inches.</summary>
    public double Inches => (double)Emu / EmuPerInch;

    /// <summary>The length in centimetres.</summary>
    public double Centimeters => (double)Emu / EmuPerCentimeter;

    /// <summary>The length in millimetres.</summary>
    public double Millimeters => (double)Emu / EmuPerMillimeter;

    /// <summary>The length in points.</summary>
    public double Points => (double)Emu / EmuPerPoint;

    /// <summary>The length in pixels at 96 DPI.</summary>
    public double Pixels => (double)Emu / EmuPerPixel;

    /// <summary>
    /// The length in twips, rounded to the nearest whole twip. WordprocessingML attributes are
    /// integers, so this is what gets written.
    /// </summary>
    public long Twips => (long)Math.Round((double)Emu / EmuPerTwip);

    /// <summary>
    /// The length in half-points, the unit <c>w:sz</c> uses for font size. A 12 pt font is
    /// <c>w:sz w:val="24"</c>.
    /// </summary>
    public long HalfPoints => (long)Math.Round((double)Emu / EmuPerPoint * 2);

    /// <summary>The length in hundredths of a point, the unit DrawingML line widths use.</summary>
    public long Centipoints => (long)Math.Round((double)Emu / EmuPerPoint * 100);

    public static Length operator +(Length a, Length b) => new(a.Emu + b.Emu);
    public static Length operator -(Length a, Length b) => new(a.Emu - b.Emu);
    public static Length operator -(Length a) => new(-a.Emu);
    public static Length operator *(Length a, double factor) => new((long)Math.Round(a.Emu * factor));
    public static Length operator *(double factor, Length a) => a * factor;
    public static Length operator /(Length a, double divisor) => new((long)Math.Round(a.Emu / divisor));
    public static double operator /(Length a, Length b) => (double)a.Emu / b.Emu;

    public static bool operator <(Length a, Length b) => a.Emu < b.Emu;
    public static bool operator >(Length a, Length b) => a.Emu > b.Emu;
    public static bool operator <=(Length a, Length b) => a.Emu <= b.Emu;
    public static bool operator >=(Length a, Length b) => a.Emu >= b.Emu;
    public static bool operator ==(Length a, Length b) => a.Emu == b.Emu;
    public static bool operator !=(Length a, Length b) => a.Emu != b.Emu;

    public bool Equals(Length other) => Emu == other.Emu;
    public override bool Equals(object? obj) => obj is Length other && Equals(other);
    public override int GetHashCode() => Emu.GetHashCode();
    public int CompareTo(Length other) => Emu.CompareTo(other.Emu);

    public override string ToString() =>
        Emu % EmuPerCentimeter == 0 ? $"{Centimeters:0.##} cm" : $"{Points:0.##} pt";
}

/// <summary>
/// Convenience constructors for <see cref="Length"/>, so call sites read as
/// <c>Units.Cm(2.54)</c> rather than <c>Length.FromCentimeters(2.54)</c>.
/// </summary>
public static class Units
{
    /// <summary>A length in inches.</summary>
    public static Length Inches(double value) => Length.FromInches(value);

    /// <summary>A length in centimetres.</summary>
    public static Length Cm(double value) => Length.FromCentimeters(value);

    /// <summary>A length in millimetres.</summary>
    public static Length Mm(double value) => Length.FromMillimeters(value);

    /// <summary>A length in points.</summary>
    public static Length Pt(double value) => Length.FromPoints(value);

    /// <summary>A length in twips.</summary>
    public static Length Twips(double value) => Length.FromTwips(value);

    /// <summary>A length in pixels at 96 DPI.</summary>
    public static Length Px(double value) => Length.FromPixels(value);

    /// <summary>A length in raw English Metric Units.</summary>
    public static Length Emu(long value) => Length.FromEmu(value);
}
