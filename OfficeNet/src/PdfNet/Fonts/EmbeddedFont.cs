// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using OfficeNet.Core;
using PdfNet.Document;
using PdfNet.Io.Filters;
using PdfNet.Objects;

namespace PdfNet.Fonts;

/// <summary>
/// A TrueType font embedded in a document, subset to the glyphs actually used.
/// </summary>
/// <remarks>
/// <para>
/// The standard 14 fonts cover Latin-1 and nothing else. A document in Javanese, Arabic, Thai,
/// Chinese, or one that simply has to use a brand face, needs the font in the file — and this is
/// how it gets there.
/// </para>
/// <para>
/// It is written as a <b>composite</b> font: a <c>/Type0</c> with <c>/Identity-H</c> encoding over a
/// <c>/CIDFontType2</c> descendant. That means text is written as two-byte glyph ids rather than
/// character codes, which is the only arrangement that reaches beyond 256 characters without
/// encoding gymnastics, and it is what every modern producer emits.
/// </para>
/// <para>
/// Writing glyph ids has one consequence that has to be paid for: <b>the text is no longer
/// readable</b>. A reader extracting text sees the ids and has no idea what they mean. The
/// <c>/ToUnicode</c> CMap written alongside is what maps them back, and a composite font without one
/// genuinely cannot be decoded — which is why this class builds one and never offers to skip it.
/// </para>
/// <para>
/// The subset grows as the font is used. Nothing is written into the document until
/// <see cref="Finish"/> runs, which <see cref="PdfDocument.Save(string)"/> does for you: the set of
/// glyphs is not known until the last piece of text has been drawn.
/// </para>
/// <example>
/// <code>
/// using var pdf = PdfDocument.Create();
/// var font = pdf.EmbedFont("NotoSans-Regular.ttf");
///
/// using var canvas = pdf.Pages.Add(PageSize.A4).OpenCanvas();
/// canvas.SetFont(font, 12);
/// canvas.DrawText("ꦲꦏ꧀ꦱꦫꦗꦮ", 72, 700);
/// </code>
/// </example>
/// </remarks>
public sealed class EmbeddedFont
{
    private readonly PdfDocument _document;
    private readonly HashSet<int> _usedGlyphs = [];
    private readonly Dictionary<int, int> _glyphToCodePoint = [];

    internal EmbeddedFont(PdfDocument document, TrueTypeFont font, string resourceName)
    {
        _document = document;
        Font = font;
        ResourceName = resourceName;

        // The six-letter tag marks the font as a subset, and the specification requires it to be
        // unique per subset within the file. Derived from the name so that embedding the same font
        // twice in one document is at least self-consistent.
        SubsetTag = Tag(font.FullName);
    }

    /// <summary>The font this was built from.</summary>
    public TrueTypeFont Font { get; }

    /// <summary>The name this font is known by inside a page's resources.</summary>
    internal string ResourceName { get; }

    /// <summary>The six-letter subset prefix the specification requires.</summary>
    internal string SubsetTag { get; }

    /// <summary>The object holding the font, once <see cref="Finish"/> has run.</summary>
    internal PdfObject? FontObject { get; private set; }

    /// <summary>
    /// How many distinct glyphs the document has drawn.
    /// </summary>
    /// <remarks>
    /// <c>.notdef</c> is counted only if something actually needed it — that is, only if the document
    /// contains a character this font has no glyph for. The subset always carries it regardless, so
    /// this can be one lower than the number of glyphs in the embedded file.
    /// </remarks>
    public int UsedGlyphCount => _usedGlyphs.Count;

    /// <summary>The width of a string at a size, in points.</summary>
    public double MeasureText(string text, double sizePoints) => Font.MeasureText(text, sizePoints);

    /// <summary>
    /// Encodes a string as the two-byte glyph ids the font's <c>Identity-H</c> encoding expects,
    /// and records them so the subset keeps them.
    /// </summary>
    /// <remarks>
    /// Runes rather than chars, so a character outside the BMP — an emoji, a rare Han glyph — is one
    /// lookup and not two halves of a surrogate pair that map to nothing.
    /// </remarks>
    internal byte[] Encode(string text)
    {
        var buffer = new List<byte>(text.Length * 2);

        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = Font.GlyphFor(rune.Value);

            _usedGlyphs.Add(glyph);

            // Kept so the ToUnicode CMap can be built later. First writer wins: a glyph shared by
            // two characters — as ligatures and duplicated punctuation are — decodes to one of
            // them, and choosing the first one seen is as good an answer as exists.
            _glyphToCodePoint.TryAdd(glyph, rune.Value);

            buffer.Add((byte)(glyph >> 8));
            buffer.Add((byte)(glyph & 0xFF));
        }

        return [.. buffer];
    }

    /// <summary>Whether the font has a glyph for every character in a string.</summary>
    /// <remarks>
    /// Worth asking before writing a page in a script the font may not cover: a missing glyph is
    /// drawn as an empty box, and finding out at that point costs a reprint.
    /// </remarks>
    public bool CanRender(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.EnumerateRunes().All(rune => Font.GlyphFor(rune.Value) != 0);
    }

    /// <summary>The characters in a string the font has no glyph for.</summary>
    public IReadOnlyList<string> MissingCharacters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return [.. text.EnumerateRunes()
            .Where(rune => Font.GlyphFor(rune.Value) == 0)
            .Select(rune => rune.ToString())
            .Distinct(StringComparer.Ordinal)];
    }

    // ---- Writing -------------------------------------------------------------------------------

    /// <summary>
    /// Builds the font's objects, subset to what was used.
    /// </summary>
    /// <remarks>
    /// Deferred until the document is saved, because the set of glyphs is not known until the last
    /// piece of text has been written. Running twice is harmless and does nothing the second time.
    /// </remarks>
    internal void Finish()
    {
        if (FontObject is not null)
        {
            return;
        }

        var subset = FontSubset.Build(Font, _usedGlyphs);

        var file = Compressed(subset.Data);

        // Length1 is the *uncompressed* size. A reader that inflates the stream has no other way to
        // know how much font it should have got, and a wrong value fails validation in some tools.
        file.Set(PdfName.Get("Length1"), subset.Data.Length);

        var fileObject = _document.AddObject(file);
        var descriptor = _document.AddObject(BuildDescriptor(fileObject));
        var cidToGid = _document.AddObject(BuildCidToGidMap(subset.GlyphMap));
        var descendant = _document.AddObject(BuildDescendant(descriptor, cidToGid));
        var toUnicode = _document.AddObject(BuildToUnicode());

        var font = new PdfDictionary();

        font[PdfName.Type] = PdfName.Font;
        font.SetName(PdfName.Subtype, "Type0");
        font.SetName(PdfName.Get("BaseFont"), $"{SubsetTag}+{PostScriptName}");

        // Identity-H: the code in the string is the glyph id, unchanged, two bytes each.
        font.SetName(PdfName.Get("Encoding"), "Identity-H");

        font[PdfName.Get("DescendantFonts")] = new PdfArray([descendant]);
        font[PdfName.Get("ToUnicode")] = toUnicode;

        FontObject = _document.AddObject(font);
    }

    private PdfDictionary BuildDescendant(PdfObject descriptor, PdfObject cidToGid)
    {
        var scale = 1000.0 / Font.UnitsPerEm;

        var system = new PdfDictionary();
        system[PdfName.Get("Registry")] = new PdfString("Adobe");
        system[PdfName.Get("Ordering")] = new PdfString("Identity");
        system.Set(PdfName.Get("Supplement"), 0);

        var descendant = new PdfDictionary();

        descendant[PdfName.Type] = PdfName.Font;
        descendant.SetName(PdfName.Subtype, "CIDFontType2");
        descendant.SetName(PdfName.Get("BaseFont"), $"{SubsetTag}+{PostScriptName}");
        descendant[PdfName.Get("CIDSystemInfo")] = system;
        descendant[PdfName.Get("FontDescriptor")] = descriptor;

        // The default width for a glyph the W array does not mention.
        descendant.Set(PdfName.Get("DW"), Math.Round(Font.AdvanceWidth(0) * scale));
        descendant[PdfName.Get("W")] = BuildWidths();

        // The CIDs in the content stream are the *original* font's glyph ids; the subset renumbered
        // them. This is the table that translates, and it is why the subset can be dense.
        descendant[PdfName.Get("CIDToGIDMap")] = cidToGid;

        return descendant;
    }

    /// <summary>
    /// The widths array, in the run-length form the format uses.
    /// </summary>
    /// <remarks>
    /// <c>[ first [w1 w2 …] ]</c> lists consecutive glyphs, and a gap starts a new run. Writing one
    /// entry per glyph instead would work and would be several times larger on a font with holes in
    /// its subset — which every subset has, since ids are kept rather than renumbered.
    /// </remarks>
    private PdfArray BuildWidths()
    {
        var widths = new List<PdfObject>();
        var glyphs = _usedGlyphs.Where(g => g > 0).Order().ToList();

        var index = 0;

        while (index < glyphs.Count)
        {
            var start = glyphs[index];
            var run = new List<PdfObject>();

            while (index < glyphs.Count && glyphs[index] == start + run.Count)
            {
                run.Add(new PdfNumber(Font.AdvanceWidthPdf(glyphs[index])));
                index++;
            }

            widths.Add(new PdfNumber(start));
            widths.Add(new PdfArray(run));
        }

        return new PdfArray(widths);
    }

    private PdfDictionary BuildDescriptor(PdfObject fontFile)
    {
        var scale = 1000.0 / Font.UnitsPerEm;

        // Flags, per the specification's table. Bit 3 (value 4) is Symbolic and bit 6 (value 32) is
        // Nonsymbolic, and they are mutually exclusive — setting both, or neither, makes some
        // readers fall back to a substitute font and ignore the one embedded right there.
        var flags = Font.IsFixedPitch ? 1 : 0;
        flags |= Font.IsSymbolic ? 4 : 32;

        if (Font.ItalicAngle != 0)
        {
            flags |= 64;
        }

        if (Font.Weight >= 600)
        {
            flags |= 1 << 18;
        }

        var descriptor = new PdfDictionary();

        descriptor.SetName(PdfName.Type, "FontDescriptor");
        descriptor.SetName(PdfName.Get("FontName"), $"{SubsetTag}+{PostScriptName}");
        descriptor.Set(PdfName.Get("Flags"), flags);

        descriptor[PdfName.Get("FontBBox")] = new PdfArray(
            Math.Round(Font.BoundingBox.Left * scale),
            Math.Round(Font.BoundingBox.Bottom * scale),
            Math.Round(Font.BoundingBox.Right * scale),
            Math.Round(Font.BoundingBox.Top * scale));

        descriptor.Set(PdfName.Get("ItalicAngle"), Font.ItalicAngle);
        descriptor.Set(PdfName.Get("Ascent"), Math.Round(Font.Ascender * scale));
        descriptor.Set(PdfName.Get("Descent"), Math.Round(Font.Descender * scale));
        descriptor.Set(PdfName.Get("CapHeight"), Math.Round(Font.CapHeight * scale));

        // StemV is the vertical stem width, which no table records. Estimating it from the weight is
        // what producers do; readers use it only when synthesising a substitute.
        descriptor.Set(PdfName.Get("StemV"), Math.Round(10 + ((Font.Weight - 400) * 0.06)));

        descriptor[PdfName.Get("FontFile2")] = fontFile;

        return descriptor;
    }

    /// <summary>
    /// Builds the CID-to-glyph table for the subset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two bytes per CID, big-endian, indexed by CID. The CIDs here are the original font's glyph
    /// ids — that is what the content stream already contains, written as text was drawn and long
    /// before the subset existed — and the values are the ids those glyphs took in the subset.
    /// </para>
    /// <para>
    /// It is as long as the highest glyph id used, which sounds expensive and is not: everything
    /// except the used ids is zero, and a run of zeros is what Flate is best at. A hundred glyphs
    /// scattered across a CJK font gives a table of tens of kilobytes that compresses to about one.
    /// </para>
    /// </remarks>
    private static PdfStream BuildCidToGidMap(IReadOnlyDictionary<int, int> map)
    {
        var highest = map.Count == 0 ? 0 : map.Keys.Max();
        var table = new byte[(highest + 1) * 2];

        foreach (var (original, subset) in map)
        {
            table[original * 2] = (byte)(subset >> 8);
            table[(original * 2) + 1] = (byte)(subset & 0xFF);
        }

        return Compressed(table);
    }

    /// <summary>
    /// Builds the CMap that maps glyph ids back to characters.
    /// </summary>
    /// <remarks>
    /// Without this the document's text cannot be extracted, searched, or copied — a reader sees
    /// two-byte glyph ids and has nothing to turn them into. It is the price of a composite font and
    /// it is not optional, so it is always written.
    /// </remarks>
    private PdfStream BuildToUnicode()
    {
        var builder = new StringBuilder();

        builder.Append("""
            /CIDInit /ProcSet findresource begin
            12 dict begin
            begincmap
            /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def
            /CMapName /Adobe-Identity-UCS def
            /CMapType 2 def
            1 begincodespacerange
            <0000> <FFFF>
            endcodespacerange

            """);

        var entries = _glyphToCodePoint
            .Where(pair => pair.Key > 0)
            .OrderBy(pair => pair.Key)
            .ToList();

        // The format caps a block at 100 entries, and a reader is entitled to stop at the 101st.
        foreach (var chunk in entries.Chunk(100))
        {
            builder.Append(CultureInfo.InvariantCulture, $"{chunk.Length} beginbfchar\n");

            foreach (var (glyph, codePoint) in chunk)
            {
                // The value is UTF-16BE, so a character outside the BMP is a surrogate pair and
                // takes four hex digits twice. Writing the code point raw gives mojibake above
                // U+FFFF.
                var utf16 = char.ConvertFromUtf32(codePoint);
                var hex = new StringBuilder();

                foreach (var unit in utf16)
                {
                    hex.Append(CultureInfo.InvariantCulture, $"{(int)unit:X4}");
                }

                builder.Append(CultureInfo.InvariantCulture, $"<{glyph:X4}> <{hex}>\n");
            }

            builder.Append("endbfchar\n");
        }

        builder.Append("""
            endcmap
            CMapName currentdict /CMap defineresource pop
            end
            end
            """);

        return Compressed(Encoding.ASCII.GetBytes(builder.ToString()));
    }

    /// <summary>
    /// Wraps bytes as a Flate-compressed stream.
    /// </summary>
    /// <remarks>
    /// Done here rather than left to the writer, which does not compress on the way out. It matters
    /// most for the CID table: a hundred kilobytes of mostly zeros is what Flate is best at, and
    /// leaving it raw made the font the largest thing in the document by a wide margin.
    /// </remarks>
    private static PdfStream Compressed(byte[] data)
    {
        var encoded = new FlateFilter().Encode(data, null);
        var stream = new PdfStream(new PdfDictionary(), encoded);

        stream.SetName(PdfName.Filter, "FlateDecode");
        stream.Set(PdfName.Length, encoded.Length);

        return stream;
    }

    /// <summary>The font's name with the characters a PDF name cannot hold removed.</summary>
    private string PostScriptName
    {
        get
        {
            var cleaned = new string([.. Font.FullName
                .Where(c => c is not (' ' or '(' or ')' or '<' or '>' or '[' or ']'
                    or '{' or '}' or '/' or '%' or '#'))]);

            return cleaned.Length == 0 ? "EmbeddedFont" : cleaned;
        }
    }

    /// <summary>
    /// A six-letter uppercase tag, as the format requires for a subset.
    /// </summary>
    /// <remarks>
    /// Derived from the name rather than random, so building the same document twice gives the same
    /// bytes — which is what lets a build be reproducible and a diff of two PDFs mean something.
    /// </remarks>
    private static string Tag(string name)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name));
        var tag = new StringBuilder(6);

        for (var i = 0; i < 6; i++)
        {
            tag.Append((char)('A' + (hash[i] % 26)));
        }

        return tag.ToString();
    }

    public override string ToString() =>
        $"{Font.FullName} ({UsedGlyphCount} glyphs used)";
}
