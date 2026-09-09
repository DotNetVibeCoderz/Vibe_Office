// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Text;

namespace OfficeNet.TestKit;

/// <summary>
/// Builds a small but genuine TrueType font, for tests that need one.
/// </summary>
/// <remarks>
/// <para>
/// A test could load a font off the machine it runs on, and then it would pass on a developer's
/// Windows box and fail on a Linux build agent that has different fonts, or none. Building one here
/// makes the test say what it means: these exact glyphs, these exact metrics, this exact structure.
/// </para>
/// <para>
/// It is a real font, not a stub — a parser that rejects it is right to. It carries the tables an
/// embedder needs, a <c>cmap</c> mapping the characters asked for, and, when asked, a
/// <b>composite</b> glyph, which is the case a subsetter gets wrong: a composite is built from other
/// glyphs by id, and a subset that does not follow those references produces a font whose accented
/// characters are silently blank.
/// </para>
/// </remarks>
public static class SyntheticFont
{
    /// <summary>The characters the default font has glyphs for.</summary>
    public const string DefaultCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,";

    /// <summary>
    /// Builds a font covering the given characters.
    /// </summary>
    /// <param name="characters">What the font can render. Duplicates and order do not matter.</param>
    /// <param name="composite">
    /// A character whose glyph is built from two others, or <c>null</c> for none. It is placed after
    /// the simple glyphs so its component ids are lower than its own, which is what a real font does.
    /// </param>
    /// <param name="longLoca">
    /// Whether to write 32-bit glyph offsets. Both formats are legal, and a reader that assumes the
    /// wrong one gets offsets wrong by a factor of two — worth being able to test.
    /// </param>
    public static byte[] Build(string characters = DefaultCharacters, char? composite = null,
        bool longLoca = false)
    {
        ArgumentNullException.ThrowIfNull(characters);

        // Glyph 0 is .notdef and every font has it, so the mapped characters start at 1.
        var mapped = characters.Distinct().Order().ToList();
        var glyphCount = mapped.Count + 1 + (composite is null ? 0 : 1);

        var outlines = new List<byte[]> { Box(100, 0, 500, 700) };

        for (var i = 0; i < mapped.Count; i++)
        {
            // A different box per glyph, so a subset that mixes two up is visible rather than
            // plausible.
            outlines.Add(mapped[i] == ' '
                ? []
                : Box((short)(50 + (i % 5 * 10)), 0,
                    (short)(400 + (i % 7 * 20)), (short)(600 + (i % 3 * 40))));
        }

        if (composite is not null)
        {
            // Built from glyph 1 and glyph 2 — the accent-over-letter shape, in miniature.
            outlines.Add(Composite(1, 2));
        }

        var glyf = new MemoryStream();
        var offsets = new uint[glyphCount + 1];

        for (var i = 0; i < glyphCount; i++)
        {
            offsets[i] = (uint)glyf.Length;
            glyf.Write(outlines[i]);

            while (glyf.Length % 4 != 0)
            {
                glyf.WriteByte(0);
            }
        }

        offsets[glyphCount] = (uint)glyf.Length;

        var tables = new List<(string Name, byte[] Data)>
        {
            ("head", Head(longLoca)),
            ("hhea", Hhea(glyphCount)),
            ("maxp", Maxp(glyphCount)),
            ("OS/2", Os2()),
            ("hmtx", Hmtx(glyphCount)),
            ("cmap", Cmap(mapped, composite)),
            ("loca", Loca(offsets, longLoca)),
            ("glyf", glyf.ToArray()),
            ("post", Post()),
        };

        return Assemble(tables);
    }

    // ---- Glyphs --------------------------------------------------------------------------------

    /// <summary>A simple glyph: one closed rectangular contour.</summary>
    private static byte[] Box(short left, short bottom, short right, short top)
    {
        var glyph = new MemoryStream();

        Write16(glyph, 1);              // one contour
        Write16(glyph, (ushort)left);
        Write16(glyph, (ushort)bottom);
        Write16(glyph, (ushort)right);
        Write16(glyph, (ushort)top);

        Write16(glyph, 3);              // last point of contour 0
        Write16(glyph, 0);              // no instructions

        // Four on-curve points, each flagged as such.
        for (var i = 0; i < 4; i++)
        {
            glyph.WriteByte(0x01);
        }

        // Deltas, as 16-bit signed values: the x run then the y run, which is the format's order.
        short[] xs = [left, (short)(right - left), 0, (short)(left - right)];
        short[] ys = [bottom, 0, (short)(top - bottom), 0];

        foreach (var x in xs)
        {
            Write16(glyph, (ushort)x);
        }

        foreach (var y in ys)
        {
            Write16(glyph, (ushort)y);
        }

        return glyph.ToArray();
    }

    /// <summary>A composite glyph, referencing two others by id.</summary>
    private static byte[] Composite(ushort first, ushort second)
    {
        var glyph = new MemoryStream();

        Write16(glyph, 0xFFFF);         // -1: a composite
        Write16(glyph, 0);
        Write16(glyph, 0);
        Write16(glyph, 600);
        Write16(glyph, 800);

        // ARG_1_AND_2_ARE_WORDS | ARGS_ARE_XY_VALUES | MORE_COMPONENTS
        Write16(glyph, 0x0001 | 0x0002 | 0x0020);
        Write16(glyph, first);
        Write16(glyph, 0);
        Write16(glyph, 0);

        // The second, offset upwards, and no MORE_COMPONENTS this time.
        Write16(glyph, 0x0001 | 0x0002);
        Write16(glyph, second);
        Write16(glyph, 0);
        Write16(glyph, 400);

        return glyph.ToArray();
    }

    // ---- Tables --------------------------------------------------------------------------------

    private static byte[] Head(bool longLoca)
    {
        var head = new byte[54];

        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(0), 0x00010000);   // version
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5);  // magic
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);        // unitsPerEm
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(36), 0);            // xMin
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(38), -200);         // yMin
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(40), 1000);         // xMax
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(42), 900);          // yMax
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), (short)(longLoca ? 1 : 0));

        return head;
    }

    private static byte[] Hhea(int glyphCount)
    {
        var hhea = new byte[36];

        BinaryPrimitives.WriteUInt32BigEndian(hhea.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), 800);    // ascender
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), -200);   // descender
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)glyphCount);

        return hhea;
    }

    private static byte[] Maxp(int glyphCount)
    {
        var maxp = new byte[32];

        BinaryPrimitives.WriteUInt32BigEndian(maxp.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)glyphCount);

        return maxp;
    }

    private static byte[] Os2()
    {
        // Version 4, which is long enough to carry sCapHeight.
        var os2 = new byte[96];

        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(0), 4);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(4), 400);   // usWeightClass
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(8), 0);     // fsType: embedding allowed
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(88), 700);   // sCapHeight

        return os2;
    }

    /// <summary>One advance per glyph, each different so a mix-up shows.</summary>
    private static byte[] Hmtx(int glyphCount)
    {
        var hmtx = new byte[glyphCount * 4];

        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(glyph * 4),
                (ushort)(500 + (glyph * 3)));
        }

        return hmtx;
    }

    /// <summary>A format 4 subtable, which is what a real font uses for the BMP.</summary>
    private static byte[] Cmap(List<char> mapped, char? composite)
    {
        var entries = mapped
            .Select((c, i) => ((int)c, i + 1))
            .ToList();

        if (composite is { } extra)
        {
            entries.Add((extra, mapped.Count + 1));
        }

        entries.Sort((a, b) => a.Item1.CompareTo(b.Item1));

        // One segment per character keeps the encoding trivial and exercises the segment walk with
        // many segments rather than one.
        var segments = entries.Count + 1;

        var subtable = new MemoryStream();

        Write16(subtable, 4);                                   // format
        Write16(subtable, (ushort)(16 + (segments * 8)));       // length
        Write16(subtable, 0);                                   // language
        Write16(subtable, (ushort)(segments * 2));              // segCountX2

        var searchRange = 2;
        var selector = 0;

        while (searchRange * 2 <= segments * 2)
        {
            searchRange *= 2;
            selector++;
        }

        Write16(subtable, (ushort)searchRange);
        Write16(subtable, (ushort)selector);
        Write16(subtable, (ushort)((segments * 2) - searchRange));

        foreach (var (code, _) in entries)
        {
            Write16(subtable, (ushort)code);
        }

        Write16(subtable, 0xFFFF);                              // the required final segment

        Write16(subtable, 0);                                   // reservedPad

        foreach (var (code, _) in entries)
        {
            Write16(subtable, (ushort)code);
        }

        Write16(subtable, 0xFFFF);

        foreach (var (code, glyph) in entries)
        {
            // idDelta: glyph = (code + delta) & 0xFFFF.
            Write16(subtable, (ushort)(glyph - code));
        }

        Write16(subtable, 1);                                   // delta for the final segment

        for (var i = 0; i < segments; i++)
        {
            Write16(subtable, 0);                               // idRangeOffset: none used
        }

        var body = subtable.ToArray();
        var cmap = new MemoryStream();

        Write16(cmap, 0);                                       // version
        Write16(cmap, 1);                                       // one subtable
        Write16(cmap, 3);                                       // platform: Windows
        Write16(cmap, 1);                                       // encoding: BMP
        Write32(cmap, 12);                                      // offset to the subtable

        cmap.Write(body);

        return cmap.ToArray();
    }

    private static byte[] Loca(uint[] offsets, bool longLoca)
    {
        if (longLoca)
        {
            var loca = new byte[offsets.Length * 4];

            for (var i = 0; i < offsets.Length; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), offsets[i]);
            }

            return loca;
        }

        var shortLoca = new byte[offsets.Length * 2];

        for (var i = 0; i < offsets.Length; i++)
        {
            // The short format stores each offset halved, which is why a reader that ignores the
            // format reads a font half the size it should be.
            BinaryPrimitives.WriteUInt16BigEndian(shortLoca.AsSpan(i * 2), (ushort)(offsets[i] / 2));
        }

        return shortLoca;
    }

    private static byte[] Post()
    {
        var post = new byte[32];

        BinaryPrimitives.WriteUInt32BigEndian(post.AsSpan(0), 0x00030000);   // version 3
        BinaryPrimitives.WriteInt32BigEndian(post.AsSpan(4), 0);             // italicAngle
        BinaryPrimitives.WriteUInt32BigEndian(post.AsSpan(12), 0);           // isFixedPitch

        return post;
    }

    // ---- Assembly ------------------------------------------------------------------------------

    private static byte[] Assemble(List<(string Name, byte[] Data)> tables)
    {
        tables.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var output = new MemoryStream();
        var count = tables.Count;

        Write32(output, 0x00010000);
        Write16(output, (ushort)count);
        Write16(output, 0);
        Write16(output, 0);
        Write16(output, 0);

        var offset = 12 + (count * 16);
        var records = new List<(int Offset, int Length)>();

        foreach (var (_, data) in tables)
        {
            records.Add((offset, data.Length));
            offset += (data.Length + 3) & ~3;
        }

        for (var i = 0; i < count; i++)
        {
            output.Write(Encoding.ASCII.GetBytes(tables[i].Name.PadRight(4)));
            Write32(output, 0);                                 // checksum, which readers ignore
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
