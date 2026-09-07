// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;

namespace OfficeNet.Core.Drawing;

/// <summary>The theme colour slots a document can reference instead of a literal RGB value.</summary>
/// <remarks>
/// A theme colour is a late binding: the slot is stored, and the actual RGB comes from the theme
/// part when the document is rendered. That is why changing a theme restyles a whole deck, and why
/// resolving these to RGB at authoring time is a one-way loss.
/// </remarks>
public enum ThemeColor
{
    /// <summary>Not a theme colour.</summary>
    None = 0,

    /// <summary>Dark 1 — usually the body text colour.</summary>
    Dark1,

    /// <summary>Light 1 — usually the page background.</summary>
    Light1,

    /// <summary>Dark 2.</summary>
    Dark2,

    /// <summary>Light 2.</summary>
    Light2,

    /// <summary>Accent 1, the primary accent.</summary>
    Accent1,

    /// <summary>Accent 2.</summary>
    Accent2,

    /// <summary>Accent 3.</summary>
    Accent3,

    /// <summary>Accent 4.</summary>
    Accent4,

    /// <summary>Accent 5.</summary>
    Accent5,

    /// <summary>Accent 6.</summary>
    Accent6,

    /// <summary>Hyperlink colour.</summary>
    Hyperlink,

    /// <summary>Followed hyperlink colour.</summary>
    FollowedHyperlink,
}

/// <summary>
/// A colour as OOXML stores one: either a literal sRGB triple, a theme slot with optional
/// tint/shade, or "automatic" (let the consumer decide).
/// </summary>
public readonly struct OfficeColor : IEquatable<OfficeColor>
{
    private readonly uint _packed;

    private OfficeColor(byte r, byte g, byte b, byte a, ThemeColor theme, double luminanceModulation, bool isAuto)
    {
        _packed = (uint)(a << 24 | r << 16 | g << 8 | b);
        Theme = theme;
        LuminanceModulation = luminanceModulation;
        IsAutomatic = isAuto;
    }

    /// <summary>The theme slot, or <see cref="ThemeColor.None"/> for a literal colour.</summary>
    public ThemeColor Theme { get; }

    /// <summary>
    /// Tint (positive) or shade (negative) applied to the theme colour, in the range -1 to 1;
    /// 0 means the theme colour unmodified.
    /// </summary>
    public double LuminanceModulation { get; }

    /// <summary>True for the OOXML <c>auto</c> colour, which is not black — it is "unspecified".</summary>
    public bool IsAutomatic { get; }

    /// <summary>The red channel.</summary>
    public byte R => (byte)(_packed >> 16);

    /// <summary>The green channel.</summary>
    public byte G => (byte)(_packed >> 8);

    /// <summary>The blue channel.</summary>
    public byte B => (byte)_packed;

    /// <summary>The alpha channel; 255 is opaque.</summary>
    public byte A => (byte)(_packed >> 24);

    /// <summary>True when this is a theme reference rather than a literal colour.</summary>
    public bool IsThemeColor => Theme != ThemeColor.None;

    /// <summary>The OOXML <c>auto</c> colour.</summary>
    public static OfficeColor Automatic => new(0, 0, 0, 255, ThemeColor.None, 0, true);

    /// <summary>Creates a colour from red, green and blue channels.</summary>
    public static OfficeColor FromRgb(byte r, byte g, byte b) => new(r, g, b, 255, ThemeColor.None, 0, false);

    /// <summary>Creates a colour from alpha, red, green and blue channels.</summary>
    public static OfficeColor FromArgb(byte a, byte r, byte g, byte b) =>
        new(r, g, b, a, ThemeColor.None, 0, false);

    /// <summary>Creates a colour from a packed 0xRRGGBB integer.</summary>
    public static OfficeColor FromRgb(int rgb) =>
        new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255, ThemeColor.None, 0, false);

    /// <summary>Creates a theme colour reference with an optional tint or shade.</summary>
    public static OfficeColor FromTheme(ThemeColor theme, double luminanceModulation = 0) =>
        new(0, 0, 0, 255, theme, Math.Clamp(luminanceModulation, -1, 1), false);

    /// <summary>
    /// Parses an OOXML colour string: six hex digits, optionally with a <c>#</c>, or <c>auto</c>.
    /// </summary>
    /// <returns>False when the string is not a colour.</returns>
    public static bool TryParse(string? value, out OfficeColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();

        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            color = Automatic;
            return true;
        }

        var hex = value.StartsWith('#') ? value[1..] : value;

        // Eight digits is AARRGGBB, which DrawingML never writes but CSS-shaped input often does.
        if (hex.Length == 8 &&
            uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            color = FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            return true;
        }

        if (hex.Length == 6 &&
            uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = FromRgb((int)rgb);
            return true;
        }

        if (hex.Length == 3 &&
            uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var shortRgb))
        {
            var r = (byte)((shortRgb >> 8 & 0xF) * 0x11);
            var g = (byte)((shortRgb >> 4 & 0xF) * 0x11);
            var b = (byte)((shortRgb & 0xF) * 0x11);
            color = FromRgb(r, g, b);
            return true;
        }

        if (NamedColors.TryGetValue(value, out var named))
        {
            color = named;
            return true;
        }

        return false;
    }

    /// <summary>Parses an OOXML colour string.</summary>
    /// <exception cref="FormatException">The value is not a colour.</exception>
    public static OfficeColor Parse(string value) =>
        TryParse(value, out var color) ? color : throw new FormatException($"'{value}' is not a colour.");

    /// <summary>The six-hex-digit form OOXML attributes take, or <c>auto</c>.</summary>
    public string ToHex() => IsAutomatic ? "auto" : $"{R:X2}{G:X2}{B:X2}";

    /// <summary>The <c>#RRGGBB</c> form, for HTML and for the sample applications.</summary>
    public string ToCssHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>
    /// Relative luminance per WCAG 2.1, used to pick a readable foreground over this colour.
    /// </summary>
    public double Luminance
    {
        get
        {
            static double Channel(byte c)
            {
                var v = c / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
        }
    }

    /// <summary>Black or white, whichever has more contrast against this colour.</summary>
    public OfficeColor ContrastingForeground => Luminance > 0.179 ? Black : White;

    /// <summary>Blends towards white by <paramref name="amount"/> (0 to 1).</summary>
    public OfficeColor Tint(double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return FromRgb(
            (byte)Math.Round(R + (255 - R) * amount),
            (byte)Math.Round(G + (255 - G) * amount),
            (byte)Math.Round(B + (255 - B) * amount));
    }

    /// <summary>Blends towards black by <paramref name="amount"/> (0 to 1).</summary>
    public OfficeColor Shade(double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return FromRgb(
            (byte)Math.Round(R * (1 - amount)),
            (byte)Math.Round(G * (1 - amount)),
            (byte)Math.Round(B * (1 - amount)));
    }

    /// <summary>Black.</summary>
    public static OfficeColor Black => FromRgb(0, 0, 0);

    /// <summary>White.</summary>
    public static OfficeColor White => FromRgb(255, 255, 255);

    /// <summary>Red.</summary>
    public static OfficeColor Red => FromRgb(255, 0, 0);

    /// <summary>Green (the CSS "lime", which is what most callers mean).</summary>
    public static OfficeColor Green => FromRgb(0, 176, 80);

    /// <summary>Blue.</summary>
    public static OfficeColor Blue => FromRgb(0, 0, 255);

    /// <summary>Yellow.</summary>
    public static OfficeColor Yellow => FromRgb(255, 255, 0);

    /// <summary>A neutral mid grey.</summary>
    public static OfficeColor Gray => FromRgb(128, 128, 128);

    /// <summary>Transparent (alpha zero).</summary>
    public static OfficeColor Transparent => FromArgb(0, 0, 0, 0);

    private static readonly Dictionary<string, OfficeColor> NamedColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = FromRgb(0x000000),
            ["white"] = FromRgb(0xFFFFFF),
            ["red"] = FromRgb(0xFF0000),
            ["green"] = FromRgb(0x008000),
            ["lime"] = FromRgb(0x00FF00),
            ["blue"] = FromRgb(0x0000FF),
            ["yellow"] = FromRgb(0xFFFF00),
            ["cyan"] = FromRgb(0x00FFFF),
            ["magenta"] = FromRgb(0xFF00FF),
            ["gray"] = FromRgb(0x808080),
            ["grey"] = FromRgb(0x808080),
            ["silver"] = FromRgb(0xC0C0C0),
            ["maroon"] = FromRgb(0x800000),
            ["olive"] = FromRgb(0x808000),
            ["navy"] = FromRgb(0x000080),
            ["purple"] = FromRgb(0x800080),
            ["teal"] = FromRgb(0x008080),
            ["orange"] = FromRgb(0xFFA500),
            ["darkblue"] = FromRgb(0x00008B),
            ["darkred"] = FromRgb(0x8B0000),
            ["darkgreen"] = FromRgb(0x006400),
        };

    public bool Equals(OfficeColor other) =>
        _packed == other._packed && Theme == other.Theme &&
        Math.Abs(LuminanceModulation - other.LuminanceModulation) < 1e-9 &&
        IsAutomatic == other.IsAutomatic;

    public override bool Equals(object? obj) => obj is OfficeColor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_packed, Theme, LuminanceModulation, IsAutomatic);

    public static bool operator ==(OfficeColor a, OfficeColor b) => a.Equals(b);
    public static bool operator !=(OfficeColor a, OfficeColor b) => !a.Equals(b);

    public override string ToString() =>
        IsAutomatic ? "auto" : IsThemeColor ? $"{Theme}{(LuminanceModulation == 0 ? "" : $" {LuminanceModulation:+0.##;-0.##}")}" : ToCssHex();
}
