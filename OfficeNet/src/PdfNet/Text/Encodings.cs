// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;

namespace PdfNet.Text;

/// <summary>
/// PDFDocEncoding — the single-byte encoding text strings outside content streams use.
/// </summary>
/// <remarks>
/// It is Latin-1 with a different 0x18-0x1F block and a different 0x80-0x9F block, where Latin-1
/// has control characters and PDFDocEncoding has typographic punctuation. Treating it as plain
/// Latin-1 turns an em dash in a document title into a control character — which most readers then
/// display as a box.
/// </remarks>
public static class PdfDocEncoding
{
    private static readonly char[] Map = BuildMap();

    private static char[] BuildMap()
    {
        var map = new char[256];

        for (var i = 0; i < 256; i++)
        {
            map[i] = (char)i;
        }

        // 0x18-0x1F
        ReadOnlySpan<char> low =
        [
            '˘', 'ˇ', 'ˆ', '˙', '˝', '˛', '˚', '˜',
        ];
        for (var i = 0; i < low.Length; i++)
        {
            map[0x18 + i] = low[i];
        }

        // 0x80-0x9F
        ReadOnlySpan<char> high =
        [
            '•', '†', '‡', '…', '—', '–', 'ƒ', '⁄',
            '‹', '›', '−', '‰', '„', '“', '”', '‘',
            '’', '‚', '™', 'ﬁ', 'ﬂ', 'Ł', 'Œ', 'Š',
            'Ÿ', 'Ž', 'ı', 'ł', 'œ', 'š', 'ž', '�',
        ];
        for (var i = 0; i < high.Length; i++)
        {
            map[0x80 + i] = high[i];
        }

        map[0xA0] = '€';
        map[0xAD] = '�';

        return map;
    }

    /// <summary>Decodes PDFDocEncoded bytes.</summary>
    public static string GetString(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = Map[bytes[i]];
        }

        return new string(chars);
    }
}

/// <summary>
/// The simple-font encodings a PDF can name, as byte-to-Unicode tables.
/// </summary>
/// <remarks>
/// A simple font maps a byte to a glyph <em>name</em>, and the glyph name to a character is the
/// Adobe glyph list. Rather than embed the whole 4000-entry list, these tables collapse both steps
/// for the encodings PDFs actually name, and <see cref="GlyphList"/> covers the differences an
/// <c>/Encoding</c> dictionary can introduce.
/// </remarks>
public static class SimpleEncodings
{
    /// <summary>WinAnsiEncoding — the encoding almost every Windows-produced PDF uses.</summary>
    public static readonly char[] WinAnsi = BuildWinAnsi();

    /// <summary>MacRomanEncoding.</summary>
    public static readonly char[] MacRoman = BuildMacRoman();

    /// <summary>StandardEncoding, Adobe's original.</summary>
    public static readonly char[] Standard = BuildStandard();

    /// <summary>Looks an encoding up by its PDF name; <c>null</c> when unknown.</summary>
    public static char[]? ByName(string? name) => name switch
    {
        "WinAnsiEncoding" => WinAnsi,
        "MacRomanEncoding" => MacRoman,
        "StandardEncoding" => Standard,
        "MacExpertEncoding" => Standard,
        "PDFDocEncoding" => Standard,
        _ => null,
    };

    private static char[] BuildWinAnsi()
    {
        var map = new char[256];

        // WinAnsi is CP1252: Latin-1 apart from 0x80-0x9F.
        for (var i = 0; i < 256; i++)
        {
            map[i] = (char)i;
        }

        ReadOnlySpan<char> cp1252 =
        [
            '€', '�', '‚', 'ƒ', '„', '…', '†', '‡',
            'ˆ', '‰', 'Š', '‹', 'Œ', '�', 'Ž', '�',
            '�', '‘', '’', '“', '”', '•', '–', '—',
            '˜', '™', 'š', '›', 'œ', '�', 'ž', 'Ÿ',
        ];
        for (var i = 0; i < cp1252.Length; i++)
        {
            map[0x80 + i] = cp1252[i];
        }

        // Below 0x20 WinAnsi has no printable glyphs; a producer that puts text there means
        // control characters, and rendering them as replacement characters is noise in extracted
        // text. They become spaces instead.
        for (var i = 0; i < 0x20; i++)
        {
            map[i] = ' ';
        }

        map[0x7F] = ' ';
        return map;
    }

    private static char[] BuildMacRoman()
    {
        var map = new char[256];
        for (var i = 0; i < 0x80; i++)
        {
            map[i] = (char)i;
        }

        for (var i = 0; i < 0x20; i++)
        {
            map[i] = ' ';
        }

        map[0x7F] = ' ';

        ReadOnlySpan<char> upper =
        [
            'Ä', 'Å', 'Ç', 'É', 'Ñ', 'Ö', 'Ü', 'á',
            'à', 'â', 'ä', 'ã', 'å', 'ç', 'é', 'è',
            'ê', 'ë', 'í', 'ì', 'î', 'ï', 'ñ', 'ó',
            'ò', 'ô', 'ö', 'õ', 'ú', 'ù', 'û', 'ü',
            '†', '°', '¢', '£', '§', '•', '¶', 'ß',
            '®', '©', '™', '´', '¨', '≠', 'Æ', 'Ø',
            '∞', '±', '≤', '≥', '¥', 'µ', '∂', '∑',
            '∏', 'π', '∫', 'ª', 'º', 'Ω', 'æ', 'ø',
            '¿', '¡', '¬', '√', 'ƒ', '≈', '∆', '«',
            '»', '…', ' ', 'À', 'Ã', 'Õ', 'Œ', 'œ',
            '–', '—', '“', '”', '‘', '’', '÷', '◊',
            'ÿ', 'Ÿ', '⁄', '€', '‹', '›', 'ﬁ', 'ﬂ',
            '‡', '·', '‚', '„', '‰', 'Â', 'Ê', 'Á',
            'Ë', 'È', 'Í', 'Î', 'Ï', 'Ì', 'Ó', 'Ô',
            '', 'Ò', 'Ú', 'Û', 'Ù', 'ı', 'ˆ', '˜',
            '¯', '˘', '˙', '˚', '¸', '˝', '˛', 'ˇ',
        ];

        for (var i = 0; i < upper.Length; i++)
        {
            map[0x80 + i] = upper[i];
        }

        return map;
    }

    private static char[] BuildStandard()
    {
        var map = new char[256];
        for (var i = 0; i < 256; i++)
        {
            map[i] = i is >= 0x20 and < 0x7F ? (char)i : ' ';
        }

        // Standard encoding's quotes differ from ASCII: 0x27 is a right single quote and 0x60 a
        // left one. Text extracted with the ASCII assumption gets apostrophes subtly wrong.
        map[0x27] = '’';
        map[0x60] = '‘';

        ReadOnlySpan<(byte Code, char Char)> upper =
        [
            (0xA1, '¡'), (0xA2, '¢'), (0xA3, '£'), (0xA4, '⁄'),
            (0xA5, '¥'), (0xA6, 'ƒ'), (0xA7, '§'), (0xA8, '¤'),
            (0xA9, '\''), (0xAA, '“'), (0xAB, '«'), (0xAC, '‹'),
            (0xAD, '›'), (0xAE, 'ﬁ'), (0xAF, 'ﬂ'), (0xB1, '–'),
            (0xB2, '†'), (0xB3, '‡'), (0xB4, '·'), (0xB6, '¶'),
            (0xB7, '•'), (0xB8, '‚'), (0xB9, '„'), (0xBA, '”'),
            (0xBB, '»'), (0xBC, '…'), (0xBD, '‰'), (0xBF, '¿'),
            (0xC1, '`'), (0xC2, '´'), (0xC3, 'ˆ'), (0xC4, '˜'),
            (0xC5, '¯'), (0xC6, '˘'), (0xC7, '˙'), (0xC8, '¨'),
            (0xCA, '˚'), (0xCB, '¸'), (0xCD, '˝'), (0xCE, '˛'),
            (0xCF, 'ˇ'), (0xD0, '—'), (0xE1, 'Æ'), (0xE3, 'ª'),
            (0xE8, 'Ł'), (0xE9, 'Ø'), (0xEA, 'Œ'), (0xEB, 'º'),
            (0xF1, 'æ'), (0xF5, 'ı'), (0xF8, 'ł'), (0xF9, 'ø'),
            (0xFA, 'œ'), (0xFB, 'ß'),
        ];

        foreach (var (code, ch) in upper)
        {
            map[code] = ch;
        }

        return map;
    }
}

/// <summary>
/// Maps Adobe glyph names to Unicode, for fonts whose <c>/Encoding</c> lists explicit
/// <c>/Differences</c>.
/// </summary>
/// <remarks>
/// Only the mechanical rules plus the names that appear in practice are implemented. The
/// mechanical rules (<c>uniXXXX</c>, <c>uXXXX</c>, <c>gNN</c>, and a name's part before the first
/// period) cover the overwhelming majority of what real fonts emit, including every subset font a
/// TeX or LaTeX pipeline produces.
/// </remarks>
public static class GlyphList
{
    private static readonly Dictionary<string, char> Names = BuildNames();

    /// <summary>Resolves a glyph name to a character, or <c>null</c> when it cannot be mapped.</summary>
    public static char? Resolve(string glyphName)
    {
        if (string.IsNullOrEmpty(glyphName))
        {
            return null;
        }

        // A suffix after a period is a variant marker ("a.sc", "one.oldstyle") and does not change
        // which character the glyph represents.
        var period = glyphName.IndexOf('.');
        var name = period > 0 ? glyphName[..period] : glyphName;

        if (Names.TryGetValue(name, out var known))
        {
            return known;
        }

        if (name.Length == 7 && name.StartsWith("uni", StringComparison.Ordinal) &&
            ushort.TryParse(name.AsSpan(3), System.Globalization.NumberStyles.HexNumber, null, out var uni))
        {
            return (char)uni;
        }

        if (name.Length is >= 5 and <= 7 && name[0] == 'u' &&
            uint.TryParse(name.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var u) &&
            u <= 0xFFFF)
        {
            return (char)u;
        }

        // A single-character name is itself, which is how "A" and "1" are encoded.
        if (name.Length == 1)
        {
            return name[0];
        }

        return null;
    }

    private static Dictionary<string, char> BuildNames()
    {
        var map = new Dictionary<string, char>(StringComparer.Ordinal);

        // Latin letters and digits follow a fixed pattern in the Adobe glyph list.
        for (var c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = c;
        }

        for (var c = 'a'; c <= 'z'; c++)
        {
            map[c.ToString()] = c;
        }

        ReadOnlySpan<string> digits =
            ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
        for (var i = 0; i < digits.Length; i++)
        {
            map[digits[i]] = (char)('0' + i);
        }

        ReadOnlySpan<(string Name, char Char)> punctuation =
        [
            ("space", ' '), ("exclam", '!'), ("quotedbl", '"'), ("numbersign", '#'),
            ("dollar", '$'), ("percent", '%'), ("ampersand", '&'), ("quotesingle", '\''),
            ("quoteright", '’'), ("quoteleft", '‘'), ("parenleft", '('),
            ("parenright", ')'), ("asterisk", '*'), ("plus", '+'), ("comma", ','),
            ("hyphen", '-'), ("period", '.'), ("slash", '/'), ("colon", ':'),
            ("semicolon", ';'), ("less", '<'), ("equal", '='), ("greater", '>'),
            ("question", '?'), ("at", '@'), ("bracketleft", '['), ("backslash", '\\'),
            ("bracketright", ']'), ("asciicircum", '^'), ("underscore", '_'), ("grave", '`'),
            ("braceleft", '{'), ("bar", '|'), ("braceright", '}'), ("asciitilde", '~'),
            ("bullet", '•'), ("endash", '–'), ("emdash", '—'),
            ("quotedblleft", '“'), ("quotedblright", '”'), ("quotesinglbase", '‚'),
            ("quotedblbase", '„'), ("ellipsis", '…'), ("dagger", '†'),
            ("daggerdbl", '‡'), ("perthousand", '‰'), ("guilsinglleft", '‹'),
            ("guilsinglright", '›'), ("guillemotleft", '«'), ("guillemotright", '»'), ("fraction", '⁄'),
            ("florin", 'ƒ'), ("section", '§'), ("paragraph", '¶'),
            ("copyright", '©'), ("registered", '®'), ("trademark", '™'),
            ("degree", '°'), ("plusminus", '±'), ("multiply", '×'),
            ("divide", '÷'), ("minus", '−'), ("euro", '€'),
            ("sterling", '£'), ("yen", '¥'), ("cent", '¢'),
            ("currency", '¤'), ("fi", 'ﬁ'), ("fl", 'ﬂ'),
            ("germandbls", 'ß'), ("adieresis", 'ä'), ("odieresis", 'ö'),
            ("udieresis", 'ü'), ("Adieresis", 'Ä'), ("Odieresis", 'Ö'),
            ("Udieresis", 'Ü'), ("eacute", 'é'), ("egrave", 'è'),
            ("ecircumflex", 'ê'), ("agrave", 'à'), ("aacute", 'á'),
            ("acircumflex", 'â'), ("atilde", 'ã'), ("aring", 'å'),
            ("ccedilla", 'ç'), ("ntilde", 'ñ'), ("oacute", 'ó'),
            ("ograve", 'ò'), ("ocircumflex", 'ô'), ("otilde", 'õ'),
            ("uacute", 'ú'), ("ugrave", 'ù'), ("ucircumflex", 'û'),
            ("iacute", 'í'), ("igrave", 'ì'), ("icircumflex", 'î'),
            ("idieresis", 'ï'), ("ae", 'æ'), ("oslash", 'ø'),
            ("oe", 'œ'), ("OE", 'Œ'), ("scaron", 'š'), ("Scaron", 'Š'),
            ("zcaron", 'ž'), ("Zcaron", 'Ž'), ("ydieresis", 'ÿ'),
            ("Ydieresis", 'Ÿ'), ("dotlessi", 'ı'), ("lslash", 'ł'),
            ("Lslash", 'Ł'), ("nbspace", ' '), ("softhyphen", '­'),
        ];

        foreach (var (name, ch) in punctuation)
        {
            map[name] = ch;
        }

        return map;
    }
}
