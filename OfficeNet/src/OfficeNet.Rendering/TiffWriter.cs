// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.IO.Compression;

namespace OfficeNet.Rendering;

/// <summary>How the pixels of a TIFF are stored.</summary>
public enum TiffColor
{
    /// <summary>24-bit colour.</summary>
    Rgb,

    /// <summary>8-bit grey — a third of the size of colour, for text that has none.</summary>
    Grayscale,

    /// <summary>1-bit black and white, the form fax and document archives take.</summary>
    BlackAndWhite,
}

/// <summary>How a TIFF's strips are compressed.</summary>
public enum TiffCompression
{
    /// <summary>Deflate (zlib), with horizontal prediction for colour and grey — the smallest lossless choice.</summary>
    Deflate,

    /// <summary>PackBits run-length coding — older readers that lack Deflate all have this.</summary>
    PackBits,

    /// <summary>Uncompressed.</summary>
    None,
}

/// <summary>
/// Writes baseline TIFF, one page after another in one file.
/// </summary>
/// <remarks>
/// <para>
/// SkiaSharp encodes PNG, JPEG and WebP but not TIFF, and a multi-page TIFF is what archive and fax
/// workflows ask for. The format is simple enough to write directly: a header, then for each page an
/// image file directory — a sorted list of tags — pointing at strips of pixel data, and at the next
/// page's directory.
/// </para>
/// <para>
/// Strips are about 64 KB of raw pixels each and compressed independently, so a reader never has to
/// hold more than one in memory. Deflate uses the zlib stream format TIFF specifies, with predictor 2
/// (each sample stored as its difference from the one to its left), which roughly halves a page of
/// text. Black-and-white pages are thresholded at half brightness and stored with 1 as black.
/// </para>
/// </remarks>
internal static class TiffWriter
{
    public sealed record Page(int Width, int Height, byte[] Rgba, double Dpi);

    private const ushort Short = 3, Long = 4, Rational = 5;

    public static byte[] Write(IReadOnlyList<Page> pages, TiffColor color, TiffCompression compression)
    {
        if (pages.Count == 0)
        {
            throw new ArgumentException("A TIFF needs at least one page.", nameof(pages));
        }

        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[8];
        "II"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 42);
        output.Write(header);   // the first directory's offset is patched in below

        var linkAt = 4L;

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var (samples, rowBytes, pixels) = Pixels(page, color);

            var predictor = compression == TiffCompression.Deflate && color != TiffColor.BlackAndWhite;
            var rowsPerStrip = Math.Max(1, 65536 / rowBytes);
            var offsets = new List<uint>();
            var counts = new List<uint>();

            for (var row = 0; row < page.Height; row += rowsPerStrip)
            {
                var rows = Math.Min(rowsPerStrip, page.Height - row);
                var strip = pixels.AsSpan(row * rowBytes, rows * rowBytes).ToArray();

                if (predictor)
                {
                    Difference(strip, rowBytes, samples);
                }

                var stored = compression switch
                {
                    TiffCompression.Deflate => Deflate(strip),
                    TiffCompression.PackBits => PackBits(strip, rowBytes),
                    _ => strip,
                };

                Align(output);
                offsets.Add((uint)output.Position);
                counts.Add((uint)stored.Length);
                output.Write(stored);
            }

            // Values longer than four bytes live outside the directory.
            Align(output);
            var bitsAt = (uint)output.Position;
            if (samples == 3)
            {
                output.Write([8, 0, 8, 0, 8, 0]);
            }

            var resolutionAt = (uint)output.Position;
            var resolution = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(resolution, (uint)Math.Round(page.Dpi * 100));
            BinaryPrimitives.WriteUInt32LittleEndian(resolution.AsSpan(4), 100);
            output.Write(resolution);

            var offsetsAt = WriteArray(output, offsets);
            var countsAt = WriteArray(output, counts);

            var entries = new List<(ushort Tag, ushort Type, uint Count, uint Value)>
            {
                (254, Long, 1, pages.Count > 1 ? 2u : 0u),                // NewSubfileType: a page of many
                (256, Long, 1, (uint)page.Width),
                (257, Long, 1, (uint)page.Height),
                (258, Short, (uint)samples, samples == 3 ? bitsAt : color == TiffColor.BlackAndWhite ? 1u : 8u),
                (259, Short, 1, compression switch { TiffCompression.Deflate => 8u, TiffCompression.PackBits => 32773u, _ => 1u }),
                (262, Short, 1, color switch { TiffColor.Rgb => 2u, TiffColor.Grayscale => 1u, _ => 0u }),
                (273, Long, (uint)offsets.Count, offsets.Count == 1 ? offsets[0] : offsetsAt),
                (277, Short, 1, (uint)samples),
                (278, Long, 1, (uint)rowsPerStrip),
                (279, Long, (uint)counts.Count, counts.Count == 1 ? counts[0] : countsAt),
                (282, Rational, 1, resolutionAt),
                (283, Rational, 1, resolutionAt),
                (284, Short, 1, 1),                                         // chunky
                (296, Short, 1, 2),                                         // inches
                (297, Short, 2, (uint)index | ((uint)pages.Count << 16)),   // page n of m
            };

            if (predictor)
            {
                entries.Add((317, Short, 1, 2));
            }

            Align(output);
            var directoryAt = (uint)output.Position;
            Patch(output, linkAt, directoryAt);

            var directory = new byte[2 + entries.Count * 12 + 4];
            BinaryPrimitives.WriteUInt16LittleEndian(directory, (ushort)entries.Count);

            for (var i = 0; i < entries.Count; i++)
            {
                var (tag, type, count, value) = entries[i];
                var entry = directory.AsSpan(2 + i * 12);
                BinaryPrimitives.WriteUInt16LittleEndian(entry, tag);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], type);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], count);

                // A single SHORT sits in the first two bytes of the value field; PageNumber's two
                // SHORTs fill it; anything else is a LONG or an offset.
                if (type == Short && count <= 2 && tag != 258 || tag == 258 && samples == 1)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], (ushort)(value & 0xFFFF));
                    BinaryPrimitives.WriteUInt16LittleEndian(entry[10..], (ushort)(value >> 16));
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], value);
                }
            }

            output.Write(directory);   // the next-directory offset is zero until a page follows
            linkAt = directoryAt + 2 + entries.Count * 12;
        }

        return output.ToArray();
    }

    /// <summary>The page's pixels in the stored layout: samples per pixel, bytes per row, the rows.</summary>
    private static (int Samples, int RowBytes, byte[] Pixels) Pixels(Page page, TiffColor color)
    {
        var count = page.Width * page.Height;
        var rgba = page.Rgba;

        switch (color)
        {
            case TiffColor.Rgb:
            {
                var pixels = new byte[count * 3];

                for (int i = 0, j = 0; i < count; i++, j += 3)
                {
                    pixels[j] = rgba[i * 4];
                    pixels[j + 1] = rgba[i * 4 + 1];
                    pixels[j + 2] = rgba[i * 4 + 2];
                }

                return (3, page.Width * 3, pixels);
            }

            case TiffColor.Grayscale:
            {
                var pixels = new byte[count];

                for (var i = 0; i < count; i++)
                {
                    pixels[i] = Luminance(rgba, i);
                }

                return (1, page.Width, pixels);
            }

            default:
            {
                var rowBytes = (page.Width + 7) / 8;
                var pixels = new byte[rowBytes * page.Height];

                for (var y = 0; y < page.Height; y++)
                {
                    for (var x = 0; x < page.Width; x++)
                    {
                        if (Luminance(rgba, y * page.Width + x) < 128)
                        {
                            pixels[y * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
                        }
                    }
                }

                return (1, rowBytes, pixels);
            }
        }
    }

    // Rec. 601 weights, in integers.
    private static byte Luminance(byte[] rgba, int pixel) =>
        (byte)((rgba[pixel * 4] * 299 + rgba[pixel * 4 + 1] * 587 + rgba[pixel * 4 + 2] * 114 + 500) / 1000);

    /// <summary>Predictor 2: each sample becomes its difference from the same sample one pixel left.</summary>
    private static void Difference(byte[] strip, int rowBytes, int samples)
    {
        for (var row = 0; row < strip.Length; row += rowBytes)
        {
            for (var i = row + rowBytes - 1; i >= row + samples; i--)
            {
                strip[i] -= strip[i - samples];
            }
        }
    }

    private static byte[] Deflate(byte[] data)
    {
        using var buffer = new MemoryStream();

        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return buffer.ToArray();
    }

    /// <summary>PackBits, row by row — a run may not cross a row boundary.</summary>
    private static byte[] PackBits(byte[] data, int rowBytes)
    {
        var output = new List<byte>(data.Length / 2);

        for (var row = 0; row < data.Length; row += rowBytes)
        {
            var end = row + rowBytes;
            var i = row;

            while (i < end)
            {
                var run = 1;

                while (i + run < end && run < 128 && data[i + run] == data[i])
                {
                    run++;
                }

                if (run > 1)
                {
                    output.Add((byte)(1 - run));
                    output.Add(data[i]);
                    i += run;
                    continue;
                }

                // A literal stretch, up to where a run of two or more begins.
                var start = i;

                while (i < end && i - start < 128 && !(i + 1 < end && data[i + 1] == data[i]))
                {
                    i++;
                }

                if (i == start)
                {
                    i++;
                }

                output.Add((byte)(i - start - 1));

                for (var k = start; k < i; k++)
                {
                    output.Add(data[k]);
                }
            }
        }

        return [.. output];
    }

    private static uint WriteArray(MemoryStream output, List<uint> values)
    {
        Align(output);
        var at = (uint)output.Position;
        Span<byte> value = stackalloc byte[4];

        foreach (var item in values)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(value, item);
            output.Write(value);
        }

        return at;
    }

    /// <summary>TIFF offsets are to word boundaries.</summary>
    private static void Align(MemoryStream output)
    {
        if (output.Position % 2 == 1)
        {
            output.WriteByte(0);
        }
    }

    private static void Patch(MemoryStream output, long at, uint value)
    {
        var end = output.Position;
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        output.Position = at;
        output.Write(bytes);
        output.Position = end;
    }
}
