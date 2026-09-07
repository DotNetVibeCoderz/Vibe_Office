// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Objects;
using PdfNet.Text;

namespace PdfNet.Content;

/// <summary>The fourteen fonts every PDF consumer is required to have without embedding.</summary>
public enum StandardFont
{
    /// <summary>Helvetica.</summary>
    Helvetica,

    /// <summary>Helvetica Bold.</summary>
    HelveticaBold,

    /// <summary>Helvetica Oblique.</summary>
    HelveticaOblique,

    /// <summary>Helvetica Bold Oblique.</summary>
    HelveticaBoldOblique,

    /// <summary>Times Roman.</summary>
    TimesRoman,

    /// <summary>Times Bold.</summary>
    TimesBold,

    /// <summary>Times Italic.</summary>
    TimesItalic,

    /// <summary>Times Bold Italic.</summary>
    TimesBoldItalic,

    /// <summary>Courier.</summary>
    Courier,

    /// <summary>Courier Bold.</summary>
    CourierBold,

    /// <summary>Courier Oblique.</summary>
    CourierOblique,

    /// <summary>Courier Bold Oblique.</summary>
    CourierBoldOblique,

    /// <summary>Symbol.</summary>
    Symbol,

    /// <summary>Zapf Dingbats.</summary>
    ZapfDingbats,
}

/// <summary>
/// Metrics for the standard 14 fonts, so text can be measured and laid out without any font file.
/// </summary>
/// <remarks>
/// <para>
/// The widths are the AFM values Adobe published, in glyph-space units (1/1000 em). They are what
/// make a PDF written by this library lay out identically in every reader — a consumer substitutes
/// its own Helvetica, but it advances by the widths the file's font dictionary implies, and for a
/// standard font those are these.
/// </para>
/// <para>
/// Only codes 32-126 are tabulated. Above that, WinAnsi accented letters are close enough to their
/// unaccented base that using the base's width is invisible in a paragraph, and it is far better
/// than the alternative of a fixed 500 for every one of them.
/// </para>
/// </remarks>
public static class StandardFonts
{
    // Codes 32..126, in AFM units.
    private static ReadOnlySpan<short> HelveticaWidths =>
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static ReadOnlySpan<short> HelveticaBoldWidths =>
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    private static ReadOnlySpan<short> TimesRomanWidths =>
    [
        250, 333, 408, 500, 500, 833, 778, 180, 333, 333, 500, 564, 250, 333, 250, 278,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 278, 278, 564, 564, 564, 444,
        921, 722, 667, 667, 722, 611, 556, 722, 722, 333, 389, 722, 611, 889, 722, 722,
        556, 722, 667, 556, 611, 722, 722, 944, 722, 722, 611, 333, 278, 333, 469, 500,
        333, 444, 500, 444, 500, 444, 333, 500, 500, 278, 278, 500, 278, 778, 500, 500,
        500, 500, 333, 389, 278, 500, 500, 722, 500, 500, 444, 480, 200, 480, 541,
    ];

    private static ReadOnlySpan<short> TimesBoldWidths =>
    [
        250, 333, 555, 500, 500, 1000, 833, 278, 333, 333, 500, 570, 250, 333, 250, 278,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 333, 333, 570, 570, 570, 500,
        930, 722, 667, 722, 722, 667, 611, 778, 778, 389, 500, 778, 667, 944, 722, 778,
        611, 778, 722, 556, 667, 722, 722, 1000, 722, 722, 667, 333, 278, 333, 581, 500,
        333, 500, 556, 444, 556, 444, 333, 500, 556, 278, 333, 556, 278, 833, 556, 500,
        556, 556, 444, 389, 333, 556, 500, 722, 500, 500, 444, 394, 220, 394, 520,
    ];

    private static ReadOnlySpan<short> TimesItalicWidths =>
    [
        250, 333, 420, 500, 500, 833, 778, 214, 333, 333, 500, 675, 250, 333, 250, 278,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 333, 333, 675, 675, 675, 500,
        920, 611, 611, 667, 722, 611, 611, 722, 722, 333, 444, 667, 556, 833, 667, 722,
        611, 722, 611, 500, 556, 722, 611, 833, 611, 556, 556, 389, 278, 389, 422, 500,
        333, 500, 500, 444, 500, 444, 278, 500, 500, 278, 278, 444, 278, 722, 500, 500,
        500, 500, 389, 389, 278, 500, 444, 667, 444, 444, 389, 400, 275, 400, 541,
    ];

    private static ReadOnlySpan<short> TimesBoldItalicWidths =>
    [
        250, 389, 555, 500, 500, 833, 778, 278, 333, 333, 500, 570, 250, 333, 250, 278,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 333, 333, 570, 570, 570, 500,
        832, 667, 667, 667, 722, 667, 667, 722, 778, 389, 500, 667, 611, 889, 722, 722,
        611, 722, 667, 556, 611, 722, 611, 833, 611, 556, 556, 389, 278, 389, 570, 500,
        333, 500, 500, 444, 500, 444, 333, 500, 556, 278, 278, 500, 278, 778, 556, 500,
        500, 500, 389, 389, 278, 556, 444, 667, 500, 444, 389, 348, 220, 348, 570,
    ];

    /// <summary>The PDF <c>/BaseFont</c> name for a standard font.</summary>
    public static string BaseFontName(this StandardFont font) => font switch
    {
        StandardFont.Helvetica => "Helvetica",
        StandardFont.HelveticaBold => "Helvetica-Bold",
        StandardFont.HelveticaOblique => "Helvetica-Oblique",
        StandardFont.HelveticaBoldOblique => "Helvetica-BoldOblique",
        StandardFont.TimesRoman => "Times-Roman",
        StandardFont.TimesBold => "Times-Bold",
        StandardFont.TimesItalic => "Times-Italic",
        StandardFont.TimesBoldItalic => "Times-BoldItalic",
        StandardFont.Courier => "Courier",
        StandardFont.CourierBold => "Courier-Bold",
        StandardFont.CourierOblique => "Courier-Oblique",
        StandardFont.CourierBoldOblique => "Courier-BoldOblique",
        StandardFont.Symbol => "Symbol",
        StandardFont.ZapfDingbats => "ZapfDingbats",
        _ => "Helvetica",
    };

    /// <summary>Picks the standard font matching a family name and a bold/italic combination.</summary>
    /// <remarks>
    /// Family matching is deliberately loose. A .docx that asks for Calibri, Segoe UI or Verdana
    /// wants a humanist sans and gets Helvetica; one that asks for Cambria or Georgia wants a serif
    /// and gets Times. That substitution is what every PDF viewer does anyway when a font is not
    /// embedded, so doing it explicitly at least gets the metrics right.
    /// </remarks>
    public static StandardFont Match(string? familyName, bool bold, bool italic)
    {
        var family = (familyName ?? string.Empty).ToLowerInvariant();

        var isMono = family.Contains("courier") || family.Contains("mono") ||
                     family.Contains("consolas") || family.Contains("menlo");

        var isSerif = family.Contains("times") || family.Contains("serif") &&
                      !family.Contains("sans") || family.Contains("georgia") ||
                      family.Contains("cambria") || family.Contains("garamond") ||
                      family.Contains("book antiqua") || family.Contains("palatino") ||
                      family.Contains("minion") || family.Contains("constantia");

        if (isMono)
        {
            return (bold, italic) switch
            {
                (true, true) => StandardFont.CourierBoldOblique,
                (true, false) => StandardFont.CourierBold,
                (false, true) => StandardFont.CourierOblique,
                _ => StandardFont.Courier,
            };
        }

        if (isSerif)
        {
            return (bold, italic) switch
            {
                (true, true) => StandardFont.TimesBoldItalic,
                (true, false) => StandardFont.TimesBold,
                (false, true) => StandardFont.TimesItalic,
                _ => StandardFont.TimesRoman,
            };
        }

        return (bold, italic) switch
        {
            (true, true) => StandardFont.HelveticaBoldOblique,
            (true, false) => StandardFont.HelveticaBold,
            (false, true) => StandardFont.HelveticaOblique,
            _ => StandardFont.Helvetica,
        };
    }

    /// <summary>The width of a character in glyph-space units (1/1000 em).</summary>
    public static double WidthOf(StandardFont font, char c)
    {
        if (font is StandardFont.Courier or StandardFont.CourierBold or
            StandardFont.CourierOblique or StandardFont.CourierBoldOblique)
        {
            return 600;
        }

        var table = TableFor(font);

        if (c is >= ' ' and <= '~')
        {
            return table[c - ' '];
        }

        // Above ASCII, fall back to the width of the unaccented base letter. A stripped 'é' is 'e',
        // and 'e' is the right width for it in every Latin font.
        var stripped = StripAccent(c);
        if (stripped is >= ' ' and <= '~')
        {
            return table[stripped - ' '];
        }

        return c switch
        {
            ' ' => table[0],            // Non-breaking space is a space.
            '–' or '—' => 1000,    // En and em dash.
            '‘' or '’' => table['\'' - ' '],
            '“' or '”' => table['"' - ' '],
            '…' => 1000,                // Ellipsis.
            '•' => 350,                 // Bullet.
            _ => table['n' - ' '],
        };
    }

    /// <summary>The width of a string in glyph-space units.</summary>
    public static double MeasureString(StandardFont font, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        double total = 0;
        foreach (var c in text)
        {
            total += WidthOf(font, c);
        }

        return total;
    }

    /// <summary>The width of a string in points at a given size.</summary>
    public static double MeasurePoints(StandardFont font, string text, double fontSize) =>
        MeasureString(font, text) / 1000.0 * fontSize;

    /// <summary>Widths keyed by character code, for a font dictionary that omits <c>/Widths</c>.</summary>
    internal static IEnumerable<(int Code, double Width)> WidthsFor(string baseFont, char[]? encoding)
    {
        var font = FromBaseFontName(baseFont);
        if (font is null)
        {
            yield break;
        }

        for (var code = 0; code < 256; code++)
        {
            var c = encoding is not null ? encoding[code] : (char)code;
            if (c != '\0')
            {
                yield return (code, WidthOf(font.Value, c));
            }
        }
    }

    private static StandardFont? FromBaseFontName(string baseFont)
    {
        // A subset font's name is prefixed with six letters and a plus sign, which must be stripped
        // before any name comparison — "ABCDEF+Helvetica" is Helvetica.
        var name = baseFont.Length > 7 && baseFont[6] == '+' ? baseFont[7..] : baseFont;

        return name switch
        {
            "Helvetica" or "Arial" or "ArialMT" => StandardFont.Helvetica,
            "Helvetica-Bold" or "Arial-BoldMT" or "Arial,Bold" => StandardFont.HelveticaBold,
            "Helvetica-Oblique" or "Arial-ItalicMT" => StandardFont.HelveticaOblique,
            "Helvetica-BoldOblique" or "Arial-BoldItalicMT" => StandardFont.HelveticaBoldOblique,
            "Times-Roman" or "TimesNewRomanPSMT" => StandardFont.TimesRoman,
            "Times-Bold" or "TimesNewRomanPS-BoldMT" => StandardFont.TimesBold,
            "Times-Italic" or "TimesNewRomanPS-ItalicMT" => StandardFont.TimesItalic,
            "Times-BoldItalic" or "TimesNewRomanPS-BoldItalicMT" => StandardFont.TimesBoldItalic,
            "Courier" or "CourierNew" or "CourierNewPSMT" => StandardFont.Courier,
            "Courier-Bold" or "CourierNewPS-BoldMT" => StandardFont.CourierBold,
            "Courier-Oblique" => StandardFont.CourierOblique,
            "Courier-BoldOblique" => StandardFont.CourierBoldOblique,
            _ => null,
        };
    }

    private static ReadOnlySpan<short> TableFor(StandardFont font) => font switch
    {
        StandardFont.HelveticaBold or StandardFont.HelveticaBoldOblique => HelveticaBoldWidths,
        StandardFont.TimesRoman => TimesRomanWidths,
        StandardFont.TimesBold => TimesBoldWidths,
        StandardFont.TimesItalic => TimesItalicWidths,
        StandardFont.TimesBoldItalic => TimesBoldItalicWidths,
        _ => HelveticaWidths,
    };

    private static char StripAccent(char c) => c switch
    {
        >= 'À' and <= 'Å' => 'A',
        'Æ' => 'A',
        'Ç' => 'C',
        >= 'È' and <= 'Ë' => 'E',
        >= 'Ì' and <= 'Ï' => 'I',
        'Ð' => 'D',
        'Ñ' => 'N',
        >= 'Ò' and <= 'Ö' or 'Ø' => 'O',
        >= 'Ù' and <= 'Ü' => 'U',
        'Ý' => 'Y',
        'ß' => 'B',
        >= 'à' and <= 'å' or 'æ' => 'a',
        'ç' => 'c',
        >= 'è' and <= 'ë' => 'e',
        >= 'ì' and <= 'ï' => 'i',
        'ñ' => 'n',
        >= 'ò' and <= 'ö' or 'ø' => 'o',
        >= 'ù' and <= 'ü' => 'u',
        'ý' or 'ÿ' => 'y',
        _ => c,
    };

    /// <summary>Builds the font dictionary for a standard font.</summary>
    public static PdfDictionary CreateFontDictionary(StandardFont font)
    {
        var dictionary = new PdfDictionary();
        dictionary[PdfName.Type] = PdfName.Font;
        dictionary.SetName(PdfName.Subtype, "Type1");
        dictionary.SetName(PdfName.Get("BaseFont"), font.BaseFontName());

        // Symbol and ZapfDingbats have their own built-in encodings and must not be given
        // WinAnsi — doing so replaces every dingbat with a Latin letter.
        if (font is not (StandardFont.Symbol or StandardFont.ZapfDingbats))
        {
            dictionary.SetName(PdfName.Get("Encoding"), "WinAnsiEncoding");
        }

        return dictionary;
    }

    /// <summary>
    /// Encodes text for a standard font's WinAnsi encoding, replacing what it cannot represent.
    /// </summary>
    public static byte[] EncodeWinAnsi(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new byte[text.Length];

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c < 0x80)
            {
                result[i] = (byte)c;
                continue;
            }

            var index = Array.IndexOf(SimpleEncodings.WinAnsi, c, 0x80);
            result[i] = index >= 0 ? (byte)index : (byte)StripAccent(c);
        }

        return result;
    }
}
