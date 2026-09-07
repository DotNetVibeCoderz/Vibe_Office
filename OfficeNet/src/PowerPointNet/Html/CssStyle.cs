// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using PowerPointNet.Shapes;

namespace PowerPointNet.Html;

/// <summary>
/// The subset of CSS that maps onto DrawingML text and shape formatting.
/// </summary>
/// <remarks>
/// <para>
/// Only inline <c>style</c> attributes and the presentational tags are read. A stylesheet — even
/// one inside a <c>&lt;style&gt;</c> block in the same fragment — needs a selector engine and a
/// cascade, and the cascade is where CSS stops being a small problem: specificity, inheritance and
/// the box model together are most of a browser.
/// </para>
/// <para>
/// The properties handled are the ones that survive the trip to a slide at all: colour, font,
/// size, weight, style, decoration, alignment and background. Anything positional (float,
/// position, flex) has no meaning inside a text frame and is ignored rather than approximated.
/// </para>
/// </remarks>
public readonly record struct CssStyle
{
    /// <summary>The text colour.</summary>
    public OfficeColor? Color { get; init; }

    /// <summary>The background colour.</summary>
    public OfficeColor? BackgroundColor { get; init; }

    /// <summary>The font size.</summary>
    public Length? FontSize { get; init; }

    /// <summary>The font family's first name.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Bold.</summary>
    public bool? Bold { get; init; }

    /// <summary>Italic.</summary>
    public bool? Italic { get; init; }

    /// <summary>Underline.</summary>
    public bool? Underline { get; init; }

    /// <summary>Strikethrough.</summary>
    public bool? Strike { get; init; }

    /// <summary>Horizontal alignment.</summary>
    public TextAlignment? Alignment { get; init; }

    /// <summary>True when nothing is set.</summary>
    public bool IsEmpty =>
        Color is null && BackgroundColor is null && FontSize is null && FontFamily is null &&
        Bold is null && Italic is null && Underline is null && Strike is null && Alignment is null;

    /// <summary>
    /// Layers another style over this one; the other style's set properties win.
    /// </summary>
    /// <remarks>
    /// This is inheritance, not the cascade: a nested <c>&lt;span&gt;</c> starts from its parent's
    /// resolved style and overrides what it declares. It gets nesting right without needing
    /// specificity, because inline styles all have the same specificity anyway.
    /// </remarks>
    public CssStyle Merge(CssStyle other) => new()
    {
        Color = other.Color ?? Color,
        BackgroundColor = other.BackgroundColor ?? BackgroundColor,
        FontSize = other.FontSize ?? FontSize,
        FontFamily = other.FontFamily ?? FontFamily,
        Bold = other.Bold ?? Bold,
        Italic = other.Italic ?? Italic,
        Underline = other.Underline ?? Underline,
        Strike = other.Strike ?? Strike,
        Alignment = other.Alignment ?? Alignment,
    };

    /// <summary>Parses an inline <c>style</c> attribute.</summary>
    public static CssStyle Parse(string? declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration))
        {
            return default;
        }

        var style = default(CssStyle);

        foreach (var part in declaration.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':');

            if (colon <= 0)
            {
                continue;
            }

            var name = part[..colon].Trim().ToLowerInvariant();
            var value = part[(colon + 1)..].Trim();

            if (value.Length == 0)
            {
                continue;
            }

            style = Apply(style, name, value);
        }

        return style;
    }

    private static CssStyle Apply(CssStyle style, string name, string value) => name switch
    {
        "color" => ParseColor(value) is { } c ? style with { Color = c } : style,

        "background-color" or "background" =>
            ParseColor(value) is { } bg ? style with { BackgroundColor = bg } : style,

        "font-size" => ParseLength(value) is { } size ? style with { FontSize = size } : style,

        "font-family" => style with { FontFamily = FirstFamily(value) },

        // Numeric weights: 600 and above is bold, which is what browsers render for a font with
        // only two weights.
        "font-weight" => style with
        {
            Bold = value switch
            {
                "bold" or "bolder" => true,
                "normal" or "lighter" => false,
                _ => int.TryParse(value, out var weight) ? weight >= 600 : null,
            },
        },

        "font-style" => style with
        {
            Italic = value switch
            {
                "italic" or "oblique" => true,
                "normal" => false,
                _ => null,
            },
        },

        "text-decoration" or "text-decoration-line" => style with
        {
            Underline = value.Contains("underline", StringComparison.OrdinalIgnoreCase)
                ? true
                : value.Contains("none", StringComparison.OrdinalIgnoreCase) ? false : null,
            Strike = value.Contains("line-through", StringComparison.OrdinalIgnoreCase)
                ? true
                : value.Contains("none", StringComparison.OrdinalIgnoreCase) ? false : null,
        },

        "text-align" => style with
        {
            Alignment = value switch
            {
                "left" or "start" => TextAlignment.Left,
                "center" or "centre" => TextAlignment.Center,
                "right" or "end" => TextAlignment.Right,
                "justify" => TextAlignment.Justify,
                _ => null,
            },
        },

        _ => style,
    };

    private static string FirstFamily(string value)
    {
        // A font stack names fallbacks the slide cannot use; the first entry is the intent.
        var first = value.Split(',')[0].Trim().Trim('"', '\'');

        return first.Length == 0 ? "Calibri" : first;
    }

    /// <summary>
    /// Parses a CSS length into a slide length.
    /// </summary>
    /// <remarks>
    /// Relative units are resolved against a 16 px root, which is the browser default. A percentage
    /// or an <c>em</c> depends on the element's inherited size and cannot be resolved without a
    /// layout, so those are approximated against that same root rather than dropped — a heading at
    /// <c>1.5em</c> comes out at 24 px, which is what a browser shows for default body text.
    /// </remarks>
    public static Length? ParseLength(string value)
    {
        var span = value.AsSpan().Trim();
        var end = 0;

        while (end < span.Length && (char.IsAsciiDigit(span[end]) || span[end] is '.' or '-' or '+'))
        {
            end++;
        }

        if (end == 0 || !double.TryParse(span[..end], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var unit = span[end..].Trim().ToString().ToLowerInvariant();

        const double RootPixels = 16;

        return unit switch
        {
            "px" or "" => Length.FromPixels(number),
            "pt" => Length.FromPoints(number),
            "pc" => Length.FromPoints(number * 12),
            "in" => Length.FromInches(number),
            "cm" => Length.FromCentimeters(number),
            "mm" => Length.FromMillimeters(number),
            "em" or "rem" => Length.FromPixels(number * RootPixels),
            "%" => Length.FromPixels(number / 100 * RootPixels),
            _ => null,
        };
    }

    /// <summary>Parses a CSS colour: hex, <c>rgb()</c>, or one of the named colours.</summary>
    public static OfficeColor? ParseColor(string value)
    {
        var text = value.Trim();

        if (text.Length == 0 || text.Equals("transparent", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("inherit", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');

            if (open < 0 || close <= open)
            {
                return null;
            }

            var parts = text[(open + 1)..close]
                .Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
            {
                return null;
            }

            // A component may be a percentage; a slide has no alpha on text, so a fourth is ignored.
            static byte Component(string part)
            {
                part = part.Trim();

                if (part.EndsWith('%') &&
                    double.TryParse(part[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var percent))
                {
                    return (byte)Math.Clamp(percent / 100 * 255, 0, 255);
                }

                return double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var number)
                    ? (byte)Math.Clamp(number, 0, 255)
                    : (byte)0;
            }

            return OfficeColor.FromRgb(Component(parts[0]), Component(parts[1]), Component(parts[2]));
        }

        return OfficeColor.TryParse(text, out var color) ? color : null;
    }

    /// <summary>
    /// The style a presentational tag implies, before its own <c>style</c> attribute is applied.
    /// </summary>
    /// <remarks>
    /// <c>&lt;b&gt;</c> and <c>&lt;strong&gt;</c> differ semantically and render identically, and a
    /// converter has no use for the difference. Heading sizes follow the browser default scale so a
    /// converted fragment keeps the proportions the author saw.
    /// </remarks>
    public static CssStyle ForTag(string tag) => tag switch
    {
        "b" or "strong" => new CssStyle { Bold = true },
        "i" or "em" or "cite" or "var" or "dfn" => new CssStyle { Italic = true },
        "u" or "ins" => new CssStyle { Underline = true },
        "s" or "strike" or "del" => new CssStyle { Strike = true },
        "mark" => new CssStyle { BackgroundColor = OfficeColor.FromRgb(0xFF, 0xF0, 0x00) },
        "code" or "kbd" or "samp" or "tt" or "pre" => new CssStyle { FontFamily = "Consolas" },
        "small" => new CssStyle { FontSize = Length.FromPoints(10) },
        "big" => new CssStyle { FontSize = Length.FromPoints(16) },
        "a" => new CssStyle
        {
            Color = OfficeColor.FromRgb(0x05, 0x63, 0xC1),
            Underline = true,
        },
        "h1" => new CssStyle { Bold = true, FontSize = Length.FromPoints(32) },
        "h2" => new CssStyle { Bold = true, FontSize = Length.FromPoints(26) },
        "h3" => new CssStyle { Bold = true, FontSize = Length.FromPoints(22) },
        "h4" => new CssStyle { Bold = true, FontSize = Length.FromPoints(18) },
        "h5" => new CssStyle { Bold = true, FontSize = Length.FromPoints(16) },
        "h6" => new CssStyle { Bold = true, FontSize = Length.FromPoints(14) },
        "th" => new CssStyle { Bold = true },
        "blockquote" => new CssStyle
        {
            Italic = true,
            Color = OfficeColor.FromRgb(0x59, 0x59, 0x59),
        },
        _ => default,
    };

    /// <summary>Reads a node's own style: its tag's implied formatting plus its inline attribute.</summary>
    public static CssStyle Of(HtmlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var style = ForTag(node.Name);

        // The presentational attributes predate CSS and still appear in exported HTML and email.
        if (node.Attribute("color") is { } color && ParseColor(color) is { } parsed)
        {
            style = style with { Color = parsed };
        }

        if (node.Attribute("align") is { } align)
        {
            style = align.ToLowerInvariant() switch
            {
                "left" => style with { Alignment = TextAlignment.Left },
                "center" => style with { Alignment = TextAlignment.Center },
                "right" => style with { Alignment = TextAlignment.Right },
                "justify" => style with { Alignment = TextAlignment.Justify },
                _ => style,
            };
        }

        return style.Merge(Parse(node.Attribute("style")));
    }
}
