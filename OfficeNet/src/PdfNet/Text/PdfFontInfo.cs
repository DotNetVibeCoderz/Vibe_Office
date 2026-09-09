// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Text;

/// <summary>
/// Everything text extraction needs from a font: how to turn its bytes into characters, and how
/// wide each of those characters is.
/// </summary>
/// <remarks>
/// <para>
/// The two questions are independent and both are needed. Decoding gives the characters; widths
/// give the positions, and positions are what tell a space from a kerning adjustment. A extractor
/// that only decodes produces "Thequickbrownfox".
/// </para>
/// <para>
/// PDF fonts come in two shapes. A <em>simple</em> font maps one byte to one glyph through an
/// encoding table. A <em>composite</em> (Type0) font maps a variable-length code through a CMap to
/// a CID, and the CID through the descendant font to a glyph — which for the near-universal
/// Identity-H means two bytes per glyph and no relation to any character set at all. The only
/// reliable way back to text for the second kind is the font's <c>/ToUnicode</c> CMap, and when
/// that is missing the text genuinely cannot be recovered.
/// </para>
/// </remarks>
public sealed class PdfFontInfo
{
    private readonly char[]? _simpleEncoding;
    private readonly Dictionary<int, string>? _toUnicode;
    private readonly Dictionary<int, double> _widths = [];
    private readonly CMap? _cmap;
    private readonly double _defaultWidth;
    private byte[]? _cidToGid;

    private PdfFontInfo(string subtype, bool isComposite, char[]? simpleEncoding,
        Dictionary<int, string>? toUnicode, CMap? cmap, double defaultWidth)
    {
        Subtype = subtype;
        IsComposite = isComposite;
        _simpleEncoding = simpleEncoding;
        _toUnicode = toUnicode;
        _cmap = cmap;
        _defaultWidth = defaultWidth;
    }

    /// <summary>The font's <c>/Subtype</c>.</summary>
    public string Subtype { get; }

    /// <summary>True for a Type0 (composite) font, where codes may be multiple bytes.</summary>
    public bool IsComposite { get; }

    /// <summary>The base font name, when the font declares one.</summary>
    public string? BaseFont { get; private init; }

    /// <summary>
    /// The embedded font program, when the file carries one.
    /// </summary>
    /// <remarks>
    /// This is what a renderer needs in order to draw the file's own glyphs rather than a system
    /// face that happens to share the name. Substitution is fine for Latin text and produces empty
    /// boxes the moment a script the substitute does not cover appears — which is every subset this
    /// library writes for non-Latin text.
    /// </remarks>
    public byte[]? EmbeddedProgram { get; private init; }

    /// <summary>
    /// Which key the program came from: <c>FontFile2</c>, <c>FontFile3</c> or <c>FontFile</c>.
    /// </summary>
    /// <remarks>
    /// Only <c>FontFile2</c> is a bare TrueType file that a font engine can load as-is.
    /// <c>FontFile3</c> is CFF and <c>FontFile</c> is Type 1, and both need wrapping first, so a
    /// consumer has to know which it was handed.
    /// </remarks>
    public string? EmbeddedProgramKind { get; private init; }

    /// <summary>
    /// Maps a character code to a glyph id in <see cref="EmbeddedProgram"/>, or -1 when unknown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a composite font the code is the CID, and <c>/CIDToGIDMap</c> translates it. That table
    /// exists because a subset renumbers its glyphs densely: the CIDs in the content stream are the
    /// original font's ids, and the subset's are not the same numbers.
    /// </para>
    /// <para>
    /// For a simple font this returns -1. The mapping there runs through the font program's own
    /// <c>cmap</c>, which a subset is entitled to omit, so the answer is genuinely not available
    /// from the PDF alone.
    /// </para>
    /// </remarks>
    public int GlyphOf(int code)
    {
        if (!IsComposite)
        {
            return -1;
        }

        if (_cidToGid is null)
        {
            // /Identity, or absent: the CID is the glyph id.
            return code;
        }

        var at = code * 2;

        return at + 1 < _cidToGid.Length
            ? (_cidToGid[at] << 8) | _cidToGid[at + 1]
            : 0;
    }

    /// <summary>True when the file carries a font program a renderer can load directly.</summary>
    public bool HasLoadableProgram =>
        EmbeddedProgram is { Length: > 0 } && EmbeddedProgramKind == "FontFile2";

    /// <summary>A fallback used when a font dictionary cannot be read at all.</summary>
    public static PdfFontInfo Fallback { get; } =
        new("Type1", false, SimpleEncodings.Standard, null, null, 500) { BaseFont = "Helvetica" };

    /// <summary>Reads a font dictionary.</summary>
    public static PdfFontInfo Read(PdfDictionary font, PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(document);

        var subtype = font.DictionarySubtype ?? "Type1";
        var baseFont = font.GetName(PdfName.Get("BaseFont"));

        var toUnicode = ReadToUnicode(font, document);

        return subtype == "Type0"
            ? ReadComposite(font, document, subtype, baseFont, toUnicode)
            : ReadSimple(font, document, subtype, baseFont, toUnicode);
    }

    private static PdfFontInfo ReadSimple(PdfDictionary font, PdfDocument document, string subtype,
        string? baseFont, Dictionary<int, string>? toUnicode)
    {
        var descriptor = font.Get<PdfDictionary>(PdfName.Get("FontDescriptor"));
        var flags = descriptor?.GetInt(PdfName.Get("Flags")) ?? 0;

        // Bit 3 (value 4) marks a symbolic font, whose built-in encoding is the only correct one —
        // applying StandardEncoding to Symbol or a dingbat font yields plausible Latin letters that
        // are entirely wrong.
        var isSymbolic = (flags & 4) != 0 && (flags & 32) == 0;

        var encoding = BuildSimpleEncoding(font, baseFont, isSymbolic);

        var (program, kind) = ReadProgram(descriptor, document);

        var info = new PdfFontInfo(subtype, false, encoding, toUnicode, null,
            descriptor?.GetDouble(PdfName.Get("MissingWidth")) ?? 0)
        {
            BaseFont = baseFont,
            EmbeddedProgram = program,
            EmbeddedProgramKind = kind,
        };

        // /Widths is indexed from /FirstChar and is in glyph-space units (1/1000 em).
        var firstChar = font.GetInt(PdfName.Get("FirstChar"));
        var widths = font.Get(PdfName.Get("Widths")) as PdfArray;

        if (widths is not null)
        {
            for (var i = 0; i < widths.Count; i++)
            {
                if (document.Follow(widths[i]) is PdfNumber width)
                {
                    info._widths[firstChar + i] = width.DoubleValue;
                }
            }
        }
        else if (baseFont is not null)
        {
            // A standard-14 font may omit /Widths entirely, and then the metrics have to come from
            // the font program the consumer is required to have.
            foreach (var (code, width) in Content.StandardFonts.WidthsFor(baseFont, encoding))
            {
                info._widths[code] = width;
            }
        }

        return info;
    }

    private static PdfFontInfo ReadComposite(PdfDictionary font, PdfDocument document, string subtype,
        string? baseFont, Dictionary<int, string>? toUnicode)
    {
        var encodingEntry = font.Get(PdfName.Get("Encoding"));
        CMap? cmap = null;

        if (encodingEntry is PdfName encodingName)
        {
            cmap = CMap.Predefined(encodingName.Value);
        }
        else if (encodingEntry is PdfStream encodingStream)
        {
            cmap = CMap.Parse(encodingStream.Decoded);
        }

        cmap ??= CMap.IdentityTwoByte;

        var descendants = font.GetArray(PdfName.Get("DescendantFonts"));
        var descendant = descendants.Count > 0
            ? document.Follow(descendants[0]) as PdfDictionary
            : null;

        var defaultWidth = descendant?.GetDouble(PdfName.Get("DW"), 1000) ?? 1000;

        var descriptor = descendant?.Get<PdfDictionary>(PdfName.Get("FontDescriptor"));
        var (program, kind) = ReadProgram(descriptor, document);

        var info = new PdfFontInfo(subtype, true, null, toUnicode, cmap, defaultWidth)
        {
            BaseFont = baseFont,
            EmbeddedProgram = program,
            EmbeddedProgramKind = kind,
        };

        // /CIDToGIDMap is either the name Identity or a stream of two-byte glyph ids indexed by CID.
        if (descendant?.Get(PdfName.Get("CIDToGIDMap")) is { } mapEntry &&
            document.Follow(mapEntry) is PdfStream mapStream)
        {
            info._cidToGid = mapStream.Decoded;
        }

        if (descendant?.Get(PdfName.Get("W")) is PdfArray widthArray)
        {
            ReadCidWidths(widthArray, document, info._widths);
        }

        return info;
    }

    /// <summary>
    /// Reads the <c>/W</c> array of a CID font, which has two alternating shapes.
    /// </summary>
    /// <remarks>
    /// <c>c [w1 w2 ...]</c> assigns consecutive widths starting at CID <c>c</c>;
    /// <c>c1 c2 w</c> assigns one width to every CID from <c>c1</c> to <c>c2</c>. Both forms appear
    /// in the same array, and telling them apart requires looking at whether the second element is
    /// an array — there is no marker.
    /// </remarks>
    private static void ReadCidWidths(PdfArray array, PdfDocument document, Dictionary<int, double> widths)
    {
        var i = 0;

        while (i < array.Count)
        {
            if (document.Follow(array[i]) is not PdfNumber first)
            {
                i++;
                continue;
            }

            if (i + 1 >= array.Count)
            {
                break;
            }

            var second = document.Follow(array[i + 1]);

            if (second is PdfArray list)
            {
                var start = first.IntValue;
                for (var k = 0; k < list.Count; k++)
                {
                    if (document.Follow(list[k]) is PdfNumber width)
                    {
                        widths[start + k] = width.DoubleValue;
                    }
                }

                i += 2;
                continue;
            }

            if (second is PdfNumber last && i + 2 < array.Count &&
                document.Follow(array[i + 2]) is PdfNumber uniform)
            {
                var from = first.IntValue;
                var to = last.IntValue;

                // A malformed range with an enormous span would allocate without bound.
                if (to >= from && to - from < 65536)
                {
                    for (var cid = from; cid <= to; cid++)
                    {
                        widths[cid] = uniform.DoubleValue;
                    }
                }

                i += 3;
                continue;
            }

            i++;
        }
    }

    private static char[]? BuildSimpleEncoding(PdfDictionary font, string? baseFont, bool isSymbolic)
    {
        var encodingEntry = font.Get(PdfName.Get("Encoding"));

        char[] table;

        if (encodingEntry is PdfName name)
        {
            table = (char[])(SimpleEncodings.ByName(name.Value) ?? DefaultTable()).Clone();
        }
        else if (encodingEntry is PdfDictionary dictionary)
        {
            var baseName = dictionary.GetName(PdfName.Get("BaseEncoding"));
            table = (char[])(SimpleEncodings.ByName(baseName) ?? DefaultTable()).Clone();

            // /Differences is a flat list where a number sets the current code and every name that
            // follows assigns to that code and increments it.
            if (dictionary.Get(PdfName.Get("Differences")) is PdfArray differences)
            {
                var code = 0;
                foreach (var item in differences)
                {
                    switch (item)
                    {
                        case PdfNumber number:
                            code = number.IntValue;
                            break;

                        case PdfName glyph when code is >= 0 and < 256:
                            var resolved = GlyphList.Resolve(glyph.Value);
                            if (resolved is not null)
                            {
                                table[code] = resolved.Value;
                            }

                            code++;
                            break;
                    }
                }
            }
        }
        else
        {
            table = (char[])DefaultTable().Clone();
        }

        return table;

        char[] DefaultTable() =>
            isSymbolic ? SimpleEncodings.Standard
            : baseFont is not null && baseFont.Contains("Arial", StringComparison.OrdinalIgnoreCase)
                ? SimpleEncodings.WinAnsi
                : SimpleEncodings.Standard;
    }

    private static Dictionary<int, string>? ReadToUnicode(PdfDictionary font, PdfDocument document)
    {
        if (document.Follow(font[PdfName.Get("ToUnicode")]) is not PdfStream stream)
        {
            return null;
        }

        try
        {
            return CMap.ParseToUnicode(stream.Decoded);
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or
                                       ArgumentOutOfRangeException)
        {
            // A broken ToUnicode is better ignored than fatal: the encoding fallback still gets
            // most Latin text out.
            return null;
        }
    }

    /// <summary>
    /// Pulls the embedded font program out of a descriptor, whichever key holds it.
    /// </summary>
    /// <remarks>
    /// The three keys are not interchangeable and the descriptor does not say which to expect, so
    /// which one it came from is reported alongside the bytes.
    /// </remarks>
    private static (byte[]? Program, string? Kind) ReadProgram(PdfDictionary? descriptor,
        PdfDocument document)
    {
        if (descriptor is null)
        {
            return (null, null);
        }

        foreach (var key in new[] { "FontFile2", "FontFile3", "FontFile" })
        {
            if (descriptor.Get(PdfName.Get(key)) is { } entry &&
                document.Follow(entry) is PdfStream stream)
            {
                try
                {
                    var bytes = stream.Decoded;

                    if (bytes.Length > 0)
                    {
                        return (bytes, key);
                    }
                }
                catch (OfficeNet.Core.OfficeNetException)
                {
                    // A font program that will not decode is a broken file, not a reason to fail
                    // extraction: the text is still recoverable and a renderer can still substitute.
                    return (null, null);
                }
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Splits a string's bytes into character codes.
    /// </summary>
    public IEnumerable<int> DecodeCodes(byte[] bytes)
    {
        if (!IsComposite)
        {
            foreach (var b in bytes)
            {
                yield return b;
            }

            yield break;
        }

        var cmap = _cmap ?? CMap.IdentityTwoByte;
        var i = 0;

        while (i < bytes.Length)
        {
            var (code, consumed) = cmap.NextCode(bytes, i);
            yield return code;
            i += Math.Max(1, consumed);
        }
    }

    /// <summary>Turns one character code into the text it represents.</summary>
    public string CodeToText(int code)
    {
        if (_toUnicode is not null && _toUnicode.TryGetValue(code, out var mapped))
        {
            return mapped;
        }

        if (!IsComposite && _simpleEncoding is not null && code is >= 0 and < 256)
        {
            var c = _simpleEncoding[code];
            return c == '\0' ? string.Empty : c.ToString();
        }

        if (IsComposite)
        {
            // No ToUnicode on a composite font: the code is a glyph index and there is genuinely no
            // mapping. Identity-H over a subset font is the common case. Emitting nothing is
            // honest; emitting the code as a character produces CJK-looking noise.
            return string.Empty;
        }

        return code is >= 32 and < 127 ? ((char)code).ToString() : string.Empty;
    }

    /// <summary>The width of a character code in text-space units (em/1000).</summary>
    public double WidthOf(int code)
    {
        if (_widths.TryGetValue(code, out var width))
        {
            return width;
        }

        if (_defaultWidth > 0)
        {
            return _defaultWidth;
        }

        // 500/1000 em is the usual average for a Latin proportional face and is close enough for
        // the only thing the width is used for here: deciding whether a gap is a space.
        return IsComposite ? 1000 : 500;
    }

    /// <summary>True when the font has enough information to recover text.</summary>
    public bool CanDecodeText => _toUnicode is not null || !IsComposite;

    public override string ToString() =>
        $"{BaseFont ?? "(unnamed)"} [{Subtype}{(IsComposite ? ", composite" : "")}]";
}
