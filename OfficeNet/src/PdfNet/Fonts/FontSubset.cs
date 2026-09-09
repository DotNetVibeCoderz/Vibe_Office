// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Text;

namespace PdfNet.Fonts;

/// <summary>
/// Builds a font file holding only the glyphs a document uses.
/// </summary>
/// <remarks>
/// <para>
/// Embedding a whole font is the difference between a 30 KB PDF and a 20 MB one — a CJK face has
/// tens of thousands of glyphs and a document uses a few hundred. Subsetting keeps the tables a PDF
/// reader needs and rewrites <c>glyf</c> and <c>loca</c> to hold only the glyphs asked for.
/// </para>
/// <para>
/// Two things make this less simple than copying bytes:
/// </para>
/// <list type="bullet">
/// <item>
/// A <em>composite</em> glyph is built from other glyphs — an "é" is usually an "e" and an acute
/// accent, referenced by glyph id. Subsetting without following those references produces a font
/// whose accented characters are blank, and nothing anywhere says why. The closure below follows
/// them, transitively.
/// </item>
/// <item>
/// Glyph ids are <em>renumbered</em>, densely, from zero. Keeping them would be simpler, and would
/// mean <c>loca</c> and <c>hmtx</c> spanning the highest id used — four bytes each per glyph, so a
/// document that uses one CJK glyph at id 40,000 pays 320 KB for the gap. Renumbering costs
/// rewriting every composite glyph's component references, which is what the copy below does, and
/// a <c>/CIDToGIDMap</c> in the PDF to translate.
/// </item>
/// </list>
/// </remarks>
internal static class FontSubset
{
    /// <summary>
    /// The tables a PDF reader needs, in the order the specification recommends.
    /// </summary>
    /// <remarks>
    /// <c>cmap</c> is deliberately absent: the PDF carries its own encoding, and a reader uses that
    /// rather than the font's. Leaving it out is what every subsetter does and saves a table that
    /// can be tens of kilobytes on its own. <c>post</c> is kept because some readers use it for
    /// text extraction fallbacks, but only when it is the cheap version.
    /// </remarks>
    private static readonly string[] Keep =
        ["head", "hhea", "maxp", "OS/2", "hmtx", "loca", "glyf", "cvt ", "fpgm", "prep", "gasp"];

    /// <summary>A subset font file, and the mapping from the original glyph ids to its own.</summary>
    internal readonly record struct Subset(byte[] Data, IReadOnlyDictionary<int, int> GlyphMap);

    /// <summary>Builds a font file holding the given glyphs.</summary>
    /// <param name="font">The font to take glyphs from.</param>
    /// <param name="glyphs">The glyph ids to keep. Glyph 0 is always included.</param>
    internal static Subset Build(TrueTypeFont font, IReadOnlySet<int> glyphs)
    {
        var wanted = Closure(font, glyphs);

        // Ascending, so glyph 0 stays glyph 0 and the order is stable across builds — which is what
        // makes the same document produce the same bytes twice.
        var ordered = wanted.Order().ToList();
        var map = ordered.Select((old, index) => (old, index)).ToDictionary(x => x.old, x => x.index);

        var (glyf, loca) = BuildOutlines(font, ordered, map);
        var count = ordered.Count;

        var tables = new List<(string Name, byte[] Data)>();

        foreach (var name in Keep)
        {
            byte[]? data = name switch
            {
                "glyf" => glyf,
                "loca" => loca,
                "head" => RewriteHead(font),
                "hhea" => RewriteHhea(font, count),
                "maxp" => RewriteMaxp(font, count),
                "hmtx" => RewriteHmtx(font, ordered),
                _ => font.HasTable(name) ? Copy(font, name) : null,
            };

            if (data is not null)
            {
                tables.Add((name, data));
            }
        }

        return new Subset(Assemble(tables), map);
    }

    /// <summary>
    /// Every glyph the given ones depend on, following composite references.
    /// </summary>
    /// <remarks>
    /// Iterative rather than recursive: a composite can reference a composite, and a malformed font
    /// can reference itself. A visited set makes the cycle harmless instead of a stack overflow.
    /// </remarks>
    private static HashSet<int> Closure(TrueTypeFont font, IReadOnlySet<int> glyphs)
    {
        // Glyph 0 is .notdef, and a font without it is invalid.
        var wanted = new HashSet<int> { 0 };
        var pending = new Stack<int>(glyphs.Where(g => g > 0 && g < font.GlyphCount));

        foreach (var glyph in glyphs.Where(g => g >= 0 && g < font.GlyphCount))
        {
            wanted.Add(glyph);
        }

        while (pending.Count > 0)
        {
            foreach (var component in Components(font, pending.Pop()))
            {
                if (component < font.GlyphCount && wanted.Add(component))
                {
                    pending.Push(component);
                }
            }
        }

        return wanted;
    }

    /// <summary>The glyphs a composite glyph is built from.</summary>
    /// <remarks>
    /// Built into a list rather than yielded, because the font data is a span and a span cannot be
    /// held across a <c>yield</c>. The lists are tiny — a composite has two or three components.
    /// </remarks>
    private static List<int> Components(TrueTypeFont font, int glyph)
    {
        var components = new List<int>();
        var (offset, length) = font.GlyphRange(glyph);

        if (length < 10)
        {
            return components;
        }

        var glyf = font.Table("glyf");
        var data = font.Data;
        var at = (int)(glyf.Offset + offset);

        if (at + 10 > data.Length)
        {
            return components;
        }

        // A negative contour count marks a composite. Simple glyphs have nothing to follow.
        if (BinaryPrimitives.ReadInt16BigEndian(data[at..]) >= 0)
        {
            return components;
        }

        var cursor = at + 10;

        while (cursor + 4 <= data.Length)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(data[cursor..]);

            components.Add(BinaryPrimitives.ReadUInt16BigEndian(data[(cursor + 2)..]));

            cursor += 4;

            // ARG_1_AND_2_ARE_WORDS
            cursor += (flags & 0x0001) != 0 ? 4 : 2;

            // One scale, an x/y scale, or a full 2x2 transform.
            if ((flags & 0x0008) != 0)
            {
                cursor += 2;
            }
            else if ((flags & 0x0040) != 0)
            {
                cursor += 4;
            }
            else if ((flags & 0x0080) != 0)
            {
                cursor += 8;
            }

            // MORE_COMPONENTS
            if ((flags & 0x0020) == 0)
            {
                break;
            }
        }

        return components;
    }

    /// <summary>Rebuilds <c>glyf</c> and <c>loca</c> with only the wanted glyphs, renumbered.</summary>
    private static (byte[] Glyf, byte[] Loca) BuildOutlines(TrueTypeFont font,
        List<int> ordered, Dictionary<int, int> map)
    {
        var glyf = new MemoryStream();
        var offsets = new uint[ordered.Count + 1];
        var source = font.Table("glyf");
        var data = font.Data;

        for (var index = 0; index < ordered.Count; index++)
        {
            offsets[index] = (uint)glyf.Length;

            var (offset, length) = font.GlyphRange(ordered[index]);
            var at = (int)(source.Offset + offset);

            if (length == 0 || at + length > data.Length)
            {
                continue;
            }

            var outline = data.Slice(at, (int)length).ToArray();

            // A composite glyph names its components by id, and those ids have just moved.
            if (BinaryPrimitives.ReadInt16BigEndian(outline) < 0)
            {
                Renumber(outline, map);
            }

            glyf.Write(outline);

            // Every glyph must start on a word boundary, or a reader that assumes alignment reads
            // the previous glyph's last byte as this one's first.
            while (glyf.Length % 4 != 0)
            {
                glyf.WriteByte(0);
            }
        }

        offsets[ordered.Count] = (uint)glyf.Length;

        // The long format always, so the subset never depends on the total staying under 128 KB.
        var loca = new byte[(ordered.Count + 1) * 4];

        for (var i = 0; i <= ordered.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), offsets[i]);
        }

        return (glyf.ToArray(), loca);
    }

    /// <summary>
    /// Rewrites a composite glyph's component ids in place.
    /// </summary>
    /// <remarks>
    /// Walks the same structure <see cref="Components"/> reads, because the component records are
    /// variable-length: the flags decide whether the arguments are bytes or words and whether a
    /// transform follows, so there is no way to find the next id without decoding the one before it.
    /// A component that somehow is not in the map becomes glyph 0, which draws an empty box rather
    /// than an arbitrary other glyph.
    /// </remarks>
    private static void Renumber(byte[] outline, Dictionary<int, int> map)
    {
        var cursor = 10;

        while (cursor + 4 <= outline.Length)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(outline.AsSpan(cursor));
            var component = BinaryPrimitives.ReadUInt16BigEndian(outline.AsSpan(cursor + 2));

            BinaryPrimitives.WriteUInt16BigEndian(outline.AsSpan(cursor + 2),
                (ushort)map.GetValueOrDefault(component, 0));

            cursor += 4;

            // ARG_1_AND_2_ARE_WORDS
            cursor += (flags & 0x0001) != 0 ? 4 : 2;

            if ((flags & 0x0008) != 0)
            {
                cursor += 2;
            }
            else if ((flags & 0x0040) != 0)
            {
                cursor += 4;
            }
            else if ((flags & 0x0080) != 0)
            {
                cursor += 8;
            }

            // MORE_COMPONENTS
            if ((flags & 0x0020) == 0)
            {
                break;
            }
        }
    }

    // ---- Table rewrites --------------------------------------------------------------------------

    private static byte[] Copy(TrueTypeFont font, string name)
    {
        var table = font.Table(name);
        var end = Math.Min(font.Data.Length, (int)(table.Offset + table.Length));

        return end <= table.Offset ? [] : font.Data[(int)table.Offset..end].ToArray();
    }

    private static byte[] RewriteHead(TrueTypeFont font)
    {
        var head = Copy(font, "head");

        if (head.Length >= 52)
        {
            // indexToLocFormat: the subset always writes long offsets, and this is where a reader
            // learns that. Copying the original value here is how a subset ends up misaligned.
            BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);

            // checkSumAdjustment. Zeroed rather than recomputed: readers do not verify it, and a
            // wrong value is worse than an absent one.
            BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0);
        }

        return head;
    }

    private static byte[] RewriteHhea(TrueTypeFont font, int count)
    {
        var hhea = Copy(font, "hhea");

        if (hhea.Length >= 36)
        {
            // numberOfHMetrics. hmtx below writes one metric per glyph, so this has to agree or a
            // reader takes the last metric as repeating and every glyph gets the same width.
            BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)count);
        }

        return hhea;
    }

    private static byte[] RewriteMaxp(TrueTypeFont font, int count)
    {
        var maxp = Copy(font, "maxp");

        if (maxp.Length >= 6)
        {
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)count);
        }

        return maxp;
    }

    /// <summary>Writes one full metric per glyph, in the subset's own order.</summary>
    private static byte[] RewriteHmtx(TrueTypeFont font, List<int> ordered)
    {
        var hmtx = new byte[ordered.Count * 4];

        for (var index = 0; index < ordered.Count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(index * 4),
                (ushort)font.AdvanceWidth(ordered[index]));

            // The left side bearing is left at zero. It affects only hinting at small sizes, and
            // carrying it would mean reading a second value per glyph out of the original hmtx.
        }

        return hmtx;
    }

    // ---- Assembly ------------------------------------------------------------------------------

    /// <summary>Writes the table directory and the tables, with the alignment the format wants.</summary>
    private static byte[] Assemble(List<(string Name, byte[] Data)> tables)
    {
        tables.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var output = new MemoryStream();
        var count = tables.Count;

        // A binary search hint the format requires. Readers ignore it; validators do not.
        var searchRange = 16;
        var entrySelector = 0;

        while (searchRange * 2 <= count * 16)
        {
            searchRange *= 2;
            entrySelector++;
        }

        Write32(output, 0x00010000);
        Write16(output, (ushort)count);
        Write16(output, (ushort)searchRange);
        Write16(output, (ushort)entrySelector);
        Write16(output, (ushort)((count * 16) - searchRange));

        var directoryEnd = 12 + (count * 16);
        var offset = directoryEnd;
        var records = new List<(int Offset, int Length)>();

        foreach (var (_, data) in tables)
        {
            records.Add((offset, data.Length));

            // Each table starts on a four-byte boundary.
            offset += (data.Length + 3) & ~3;
        }

        for (var i = 0; i < count; i++)
        {
            output.Write(Encoding.ASCII.GetBytes(tables[i].Name.PadRight(4)));
            Write32(output, CheckSum(tables[i].Data));
            Write32(output, (uint)records[i].Offset);
            Write32(output, (uint)records[i].Length);
        }

        foreach (var (_, data) in tables)
        {
            output.Write(data);

            while (output.Length % 4 != 0)
            {
                output.WriteByte(0);
            }
        }

        return output.ToArray();
    }

    /// <summary>The format's checksum: the sum of the table read as big-endian words.</summary>
    private static uint CheckSum(byte[] data)
    {
        uint sum = 0;

        for (var i = 0; i < data.Length; i += 4)
        {
            uint word = 0;

            for (var b = 0; b < 4; b++)
            {
                // Past the end reads as zero, which is the same as the padding the table gets.
                word = (word << 8) | (i + b < data.Length ? data[i + b] : 0u);
            }

            unchecked
            {
                sum += word;
            }
        }

        return sum;
    }

    private static void Write16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void Write32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
