// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Text;
using OfficeNet.Core;

namespace PdfNet.Fonts;

/// <summary>Where a table sits in a font file.</summary>
internal readonly record struct FontTable(uint Offset, uint Length);

/// <summary>
/// A TrueType or OpenType font file, read far enough to embed it.
/// </summary>
/// <remarks>
/// <para>
/// A font file is a directory of tables. Only a handful matter for embedding: <c>head</c> for the
/// units per em and the index format, <c>hhea</c> and <c>hmtx</c> for advance widths, <c>maxp</c>
/// for the glyph count, <c>cmap</c> for the character-to-glyph mapping, <c>loca</c> and <c>glyf</c>
/// for the outlines, <c>name</c> for the family name, and <c>OS/2</c> and <c>post</c> for the
/// descriptor PDF requires.
/// </para>
/// <para>
/// Everything is big-endian, which is worth stating once because every read below depends on it and
/// nothing in the file says so.
/// </para>
/// <para>
/// <b>OpenType fonts with PostScript outlines are refused.</b> Those carry a <c>CFF </c> table
/// rather than <c>glyf</c>, and embedding one means a different PDF font type and a different
/// subsetter. Loading it and quietly producing a PDF with no glyphs in it would be worse than
/// saying so.
/// </para>
/// </remarks>
public sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, FontTable> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _cmap = [];
    private ushort[] _advanceWidths = [];
    private uint[] _glyphOffsets = [];

    private TrueTypeFont(byte[] data)
    {
        _data = data;
    }

    /// <summary>The font's raw bytes.</summary>
    internal ReadOnlySpan<byte> Data => _data;

    /// <summary>The family name, as the font itself records it.</summary>
    public string FamilyName { get; private set; } = "Unknown";

    /// <summary>The full name, including the style.</summary>
    public string FullName { get; private set; } = "Unknown";

    /// <summary>Design units per em. Almost always 1000 or 2048.</summary>
    public int UnitsPerEm { get; private set; } = 1000;

    /// <summary>How many glyphs the font has.</summary>
    public int GlyphCount { get; private set; }

    /// <summary>The bounding box of every glyph, in design units.</summary>
    public (short Left, short Bottom, short Right, short Top) BoundingBox { get; private set; }

    /// <summary>The typographic ascender, in design units.</summary>
    public short Ascender { get; private set; }

    /// <summary>The typographic descender, in design units. Negative.</summary>
    public short Descender { get; private set; }

    /// <summary>The cap height, in design units.</summary>
    public short CapHeight { get; private set; }

    /// <summary>The angle of the italic, in degrees. Zero for an upright face.</summary>
    public double ItalicAngle { get; private set; }

    /// <summary>The weight class, 100 to 900. 400 is regular and 700 bold.</summary>
    public int Weight { get; private set; } = 400;

    /// <summary>Whether the font is fixed-pitch.</summary>
    public bool IsFixedPitch { get; private set; }

    /// <summary>Whether the font is a symbol font with no standard character mapping.</summary>
    public bool IsSymbolic { get; private set; }

    /// <summary>Whether the licence embedded in the font forbids embedding it.</summary>
    /// <remarks>
    /// Read from <c>OS/2</c>'s <c>fsType</c>. This is a statement by the font's publisher and this
    /// library reports it rather than enforcing it: the licence is between the user and the
    /// publisher, and a library that silently refused would be making that call for them. Check it
    /// before shipping a document built with someone else's font.
    /// </remarks>
    public bool EmbeddingRestricted { get; private set; }

    // ---- Loading -------------------------------------------------------------------------------

    /// <summary>Reads a font from a file.</summary>
    public static TrueTypeFont Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Load(File.ReadAllBytes(path));
    }

    /// <summary>Reads a font from bytes.</summary>
    /// <exception cref="OfficeNetException">The bytes are not a TrueType font this can embed.</exception>
    public static TrueTypeFont Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 12)
        {
            throw new OfficeNetException("A font file cannot be shorter than its own header.");
        }

        var font = new TrueTypeFont(data);
        font.ReadTableDirectory();
        font.ReadHead();
        font.ReadMaxProfile();
        font.ReadHorizontalMetrics();
        font.ReadLocations();
        font.ReadCharacterMap();
        font.ReadNames();
        font.ReadOs2();
        font.ReadPost();

        return font;
    }

    private void ReadTableDirectory()
    {
        var tag = ReadUInt32(0);

        // 0x00010000 is TrueType, "true" is the old Apple form, and "ttcf" is a collection.
        if (tag == 0x74746366)
        {
            throw new OfficeNetException(
                "This is a TrueType collection (.ttc), which holds several fonts. Extract the one " +
                "you want first — a PDF embeds one font, not a collection.");
        }

        if (tag == 0x4F54544F)
        {
            throw new OfficeNetException(
                "This is an OpenType font with PostScript outlines (CFF). Embedding it needs a " +
                "different PDF font type and a different subsetter, and neither is implemented. " +
                "A TrueType-outline font (.ttf) works.");
        }

        if (tag is not (0x00010000 or 0x74727565))
        {
            throw new OfficeNetException(
                "These bytes are not a TrueType font: the file starts with " +
                $"0x{tag:X8} rather than a version this recognises.");
        }

        var count = ReadUInt16(4);

        for (var i = 0; i < count; i++)
        {
            var record = 12 + (i * 16);

            if (record + 16 > _data.Length)
            {
                break;
            }

            var name = Encoding.ASCII.GetString(_data, record, 4);
            _tables[name] = new FontTable(ReadUInt32(record + 8), ReadUInt32(record + 12));
        }

        if (!_tables.ContainsKey("glyf") || !_tables.ContainsKey("loca"))
        {
            throw new OfficeNetException(
                "This font has no glyf/loca tables, so it has no TrueType outlines to embed.");
        }
    }

    private void ReadHead()
    {
        var head = Table("head");

        UnitsPerEm = ReadUInt16((int)head.Offset + 18);

        BoundingBox = (
            ReadInt16((int)head.Offset + 36),
            ReadInt16((int)head.Offset + 38),
            ReadInt16((int)head.Offset + 40),
            ReadInt16((int)head.Offset + 42));

        // indexToLocFormat: 0 means loca holds 16-bit halves of the real offsets, 1 means 32-bit
        // offsets. Reading the wrong width gives glyph offsets that are wrong by a factor of two,
        // and the resulting font renders as a page of blanks.
        LongLocationFormat = ReadInt16((int)head.Offset + 50) == 1;

        if (UnitsPerEm == 0)
        {
            UnitsPerEm = 1000;
        }
    }

    internal bool LongLocationFormat { get; private set; }

    private void ReadMaxProfile() => GlyphCount = ReadUInt16((int)Table("maxp").Offset + 4);

    private void ReadHorizontalMetrics()
    {
        var hhea = Table("hhea");

        Ascender = ReadInt16((int)hhea.Offset + 4);
        Descender = ReadInt16((int)hhea.Offset + 6);

        var metricCount = ReadUInt16((int)hhea.Offset + 34);
        var hmtx = Table("hmtx");

        _advanceWidths = new ushort[Math.Max(1, GlyphCount)];

        ushort last = 0;

        for (var glyph = 0; glyph < _advanceWidths.Length; glyph++)
        {
            // Past the last full metric, every remaining glyph repeats the last advance. That is
            // how a monospaced or CJK font stores thousands of identical widths in four bytes.
            if (glyph < metricCount)
            {
                var offset = (int)hmtx.Offset + (glyph * 4);

                if (offset + 2 <= _data.Length)
                {
                    last = ReadUInt16(offset);
                }
            }

            _advanceWidths[glyph] = last;
        }
    }

    private void ReadLocations()
    {
        var loca = Table("loca");
        var count = GlyphCount + 1;

        _glyphOffsets = new uint[count];

        for (var i = 0; i < count; i++)
        {
            if (LongLocationFormat)
            {
                var at = (int)loca.Offset + (i * 4);
                _glyphOffsets[i] = at + 4 <= _data.Length ? ReadUInt32(at) : 0;
            }
            else
            {
                var at = (int)loca.Offset + (i * 2);

                // The short format stores each offset divided by two, which is why a font read with
                // the wrong format is not merely misaligned but half the size it should be.
                _glyphOffsets[i] = at + 2 <= _data.Length ? (uint)(ReadUInt16(at) * 2) : 0;
            }
        }
    }

    /// <summary>
    /// Reads the character-to-glyph map.
    /// </summary>
    /// <remarks>
    /// A font carries several, one per platform and encoding. The one to want is a Windows Unicode
    /// subtable — platform 3, encoding 1 for the BMP or 10 for the full range — and format 4 or 12.
    /// A symbol font offers platform 3 encoding 0 instead, whose codes live in a private-use area
    /// starting at 0xF000, which is why <see cref="IsSymbolic"/> exists.
    /// </remarks>
    private void ReadCharacterMap()
    {
        if (!_tables.TryGetValue("cmap", out var cmap))
        {
            return;
        }

        var count = ReadUInt16((int)cmap.Offset + 2);

        var best = -1;
        var bestScore = -1;
        var symbolic = false;

        for (var i = 0; i < count; i++)
        {
            var record = (int)cmap.Offset + 4 + (i * 8);

            if (record + 8 > _data.Length)
            {
                break;
            }

            var platform = ReadUInt16(record);
            var encoding = ReadUInt16(record + 2);
            var offset = (int)(cmap.Offset + ReadUInt32(record + 4));

            var score = (platform, encoding) switch
            {
                (3, 10) => 5,   // Windows, full Unicode
                (3, 1) => 4,    // Windows, BMP
                (0, _) => 3,    // Unicode platform
                (3, 0) => 2,    // Windows, symbol
                _ => 1,
            };

            if (score > bestScore)
            {
                (best, bestScore, symbolic) = (offset, score, platform == 3 && encoding == 0);
            }
        }

        if (best < 0)
        {
            return;
        }

        IsSymbolic = symbolic;
        ReadCharacterMapSubtable(best);
    }

    private void ReadCharacterMapSubtable(int offset)
    {
        if (offset + 4 > _data.Length)
        {
            return;
        }

        switch (ReadUInt16(offset))
        {
            case 4:
                ReadFormat4(offset);
                break;

            case 12:
                ReadFormat12(offset);
                break;

            case 6:
            {
                var first = ReadUInt16(offset + 6);
                var entries = ReadUInt16(offset + 8);

                for (var i = 0; i < entries; i++)
                {
                    _cmap[first + i] = ReadUInt16(offset + 10 + (i * 2));
                }

                break;
            }

            case 0:
                for (var code = 0; code < 256 && offset + 6 + code < _data.Length; code++)
                {
                    _cmap[code] = _data[offset + 6 + code];
                }

                break;
        }
    }

    /// <summary>Format 4: segmented, and the one nearly every font uses for the BMP.</summary>
    private void ReadFormat4(int offset)
    {
        var segments = ReadUInt16(offset + 6) / 2;

        var endCodes = offset + 14;
        var startCodes = endCodes + (segments * 2) + 2;
        var deltas = startCodes + (segments * 2);
        var rangeOffsets = deltas + (segments * 2);

        for (var segment = 0; segment < segments; segment++)
        {
            var end = ReadUInt16(endCodes + (segment * 2));
            var start = ReadUInt16(startCodes + (segment * 2));
            var delta = ReadInt16(deltas + (segment * 2));
            var rangeOffset = ReadUInt16(rangeOffsets + (segment * 2));

            if (start > end)
            {
                continue;
            }

            for (var code = start; code <= end; code++)
            {
                int glyph;

                if (rangeOffset == 0)
                {
                    glyph = (code + delta) & 0xFFFF;
                }
                else
                {
                    // The offset is measured from the position of the offset itself, which is the
                    // single strangest thing in the format and the usual place to go wrong.
                    var at = rangeOffsets + (segment * 2) + rangeOffset + ((code - start) * 2);

                    if (at + 2 > _data.Length)
                    {
                        continue;
                    }

                    glyph = ReadUInt16(at);

                    if (glyph != 0)
                    {
                        glyph = (glyph + delta) & 0xFFFF;
                    }
                }

                if (glyph != 0)
                {
                    _cmap[code] = glyph;
                }

                if (code == 0xFFFF)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Format 12: flat groups, for fonts that reach past the BMP.</summary>
    private void ReadFormat12(int offset)
    {
        var groups = (int)ReadUInt32(offset + 12);

        for (var i = 0; i < groups; i++)
        {
            var record = offset + 16 + (i * 12);

            if (record + 12 > _data.Length)
            {
                break;
            }

            var start = ReadUInt32(record);
            var end = ReadUInt32(record + 4);
            var glyph = ReadUInt32(record + 8);

            // A font with a corrupt group could otherwise ask for billions of iterations.
            if (end < start || end - start > 0x10FFFF)
            {
                continue;
            }

            for (var code = start; code <= end; code++)
            {
                _cmap[(int)code] = (int)(glyph + (code - start));
            }
        }
    }

    private void ReadNames()
    {
        if (!_tables.TryGetValue("name", out var name))
        {
            return;
        }

        var count = ReadUInt16((int)name.Offset + 2);
        var storage = (int)name.Offset + ReadUInt16((int)name.Offset + 4);

        for (var i = 0; i < count; i++)
        {
            var record = (int)name.Offset + 6 + (i * 12);

            if (record + 12 > _data.Length)
            {
                break;
            }

            var platform = ReadUInt16(record);
            var nameId = ReadUInt16(record + 6);
            var length = ReadUInt16(record + 8);
            var offset = storage + ReadUInt16(record + 10);

            if (nameId is not (1 or 4) || offset + length > _data.Length)
            {
                continue;
            }

            // Platform 3 stores UTF-16BE, platform 1 stores MacRoman. Reading one as the other
            // gives a name with a null byte between every letter, or mojibake.
            var value = platform == 3
                ? Encoding.BigEndianUnicode.GetString(_data, offset, length)
                : Encoding.ASCII.GetString(_data, offset, length);

            if (nameId == 1)
            {
                FamilyName = value;
            }
            else
            {
                FullName = value;
            }
        }

        if (FullName == "Unknown")
        {
            FullName = FamilyName;
        }
    }

    private void ReadOs2()
    {
        if (!_tables.TryGetValue("OS/2", out var os2) || os2.Length < 72)
        {
            CapHeight = (short)(Ascender * 0.7);
            return;
        }

        var at = (int)os2.Offset;

        Weight = ReadUInt16(at + 4);

        // fsType bit 1 set alone means the font may not be embedded at all; bits 2 and 3 allow
        // preview or editing. Anything with bit 1 is what this reports.
        EmbeddingRestricted = (ReadUInt16(at + 8) & 0x0002) != 0;

        var version = ReadUInt16(at);

        // sCapHeight only exists from version 2. Older fonts get an estimate, which is what every
        // producer does and what the PDF descriptor is tolerant of.
        CapHeight = version >= 2 && os2.Length >= 96
            ? ReadInt16(at + 88)
            : (short)(Ascender * 0.7);

        if (CapHeight <= 0)
        {
            CapHeight = (short)(Ascender * 0.7);
        }
    }

    private void ReadPost()
    {
        if (!_tables.TryGetValue("post", out var post) || post.Length < 32)
        {
            return;
        }

        // A 16.16 fixed-point number.
        ItalicAngle = ReadInt32((int)post.Offset + 4) / 65536.0;
        IsFixedPitch = ReadUInt32((int)post.Offset + 12) != 0;
    }

    // ---- Queries -------------------------------------------------------------------------------

    /// <summary>The glyph a character maps to, or 0 when the font has no glyph for it.</summary>
    /// <remarks>
    /// Glyph 0 is <c>.notdef</c> — the empty box a reader draws for a character the font cannot
    /// show. Returning it rather than throwing is right: a document usually has one character the
    /// font lacks, and refusing to write the whole page over it helps nobody.
    /// </remarks>
    public int GlyphFor(int codePoint)
    {
        if (_cmap.TryGetValue(codePoint, out var glyph))
        {
            return glyph;
        }

        // A symbol font's codes live at 0xF000 + the byte, which is why an unmapped ASCII character
        // in one is worth a second look before giving up.
        return IsSymbolic && codePoint < 0x100 && _cmap.TryGetValue(0xF000 + codePoint, out var shifted)
            ? shifted
            : 0;
    }

    /// <summary>A glyph's advance width, in design units.</summary>
    public int AdvanceWidth(int glyph) =>
        glyph >= 0 && glyph < _advanceWidths.Length ? _advanceWidths[glyph] : 0;

    /// <summary>A glyph's advance width in thousandths of an em, which is what PDF wants.</summary>
    public int AdvanceWidthPdf(int glyph) =>
        (int)Math.Round(AdvanceWidth(glyph) * 1000.0 / UnitsPerEm);

    /// <summary>The width of a string at a given size, in points.</summary>
    public double MeasureText(string text, double sizePoints)
    {
        ArgumentNullException.ThrowIfNull(text);

        var total = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            total += AdvanceWidth(GlyphFor(rune.Value));
        }

        return total * sizePoints / UnitsPerEm;
    }

    /// <summary>
    /// Whether a glyph has an outline of its own.
    /// </summary>
    /// <remarks>
    /// A mapping is not a shape. A space maps to a glyph and that glyph is deliberately empty, and a
    /// subset that dropped an outline looks exactly the same from the character map — so this is how
    /// to tell "the font has this character" from "the font can draw it".
    /// </remarks>
    public bool HasOutline(int glyph) => GlyphRange(glyph).Length > 0;

    /// <summary>Where a glyph's outline sits in the <c>glyf</c> table, and how long it is.</summary>
    internal (uint Offset, uint Length) GlyphRange(int glyph)
    {
        if (glyph < 0 || glyph + 1 >= _glyphOffsets.Length)
        {
            return (0, 0);
        }

        var start = _glyphOffsets[glyph];
        var end = _glyphOffsets[glyph + 1];

        // An empty glyph — a space — has start == end, and that is not an error.
        return end <= start ? (start, 0) : (start, end - start);
    }

    internal FontTable Table(string name) =>
        _tables.TryGetValue(name, out var table)
            ? table
            : throw new OfficeNetException($"This font has no '{name}' table, which is required.");

    internal bool HasTable(string name) => _tables.ContainsKey(name);

    internal IEnumerable<string> TableNames => _tables.Keys;

    // ---- Big-endian reads ------------------------------------------------------------------------

    private ushort ReadUInt16(int offset) =>
        offset + 2 <= _data.Length
            ? BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset))
            : (ushort)0;

    private short ReadInt16(int offset) =>
        offset + 2 <= _data.Length
            ? BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(offset))
            : (short)0;

    private uint ReadUInt32(int offset) =>
        offset + 4 <= _data.Length
            ? BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset))
            : 0;

    private int ReadInt32(int offset) =>
        offset + 4 <= _data.Length
            ? BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(offset))
            : 0;

    public override string ToString() =>
        $"{FullName} ({GlyphCount} glyphs, {UnitsPerEm}/em)";
}
