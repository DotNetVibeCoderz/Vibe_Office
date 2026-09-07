// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.IO.Compression;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Text;

/// <summary>An image found on a page, as bytes ready to write to a file.</summary>
/// <param name="Data">The image file's bytes.</param>
/// <param name="Extension">The file extension, without the dot.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Name">The resource name the page referred to it by.</param>
public readonly record struct PdfImage(byte[] Data, string Extension, int Width, int Height, string Name)
{
    /// <summary>Writes the image next to a base path, named after its resource name.</summary>
    public string SaveTo(string directory, string? fileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);
        var name = fileName ?? $"{Name}.{Extension}";
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, Data);
        return path;
    }

    public override string ToString() => $"{Name}: {Width}x{Height} {Extension}";
}

/// <summary>
/// Pulls the images out of a page's resources.
/// </summary>
/// <remarks>
/// <para>
/// A PDF image XObject is raw samples plus a colour space and a bit depth — not a file. Getting a
/// usable file back means either handing over the codec's own bytes (a DCTDecode stream <em>is</em>
/// a JPEG) or wrapping the samples in a container. This writes PNG for the uncompressed cases,
/// which covers everything a scanner or an Office export produces.
/// </para>
/// <para>
/// Indexed and CMYK images are converted to RGB rather than skipped, because those are exactly what
/// a print-ready PDF is full of.
/// </para>
/// </remarks>
public static class ImageExtractor
{
    /// <summary>Extracts every image a page draws.</summary>
    public static IEnumerable<PdfImage> Extract(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var document = page.Document;
        var xobjects = document.Follow(page.Resources[PdfName.XObject]) as PdfDictionary;

        if (xobjects is null)
        {
            yield break;
        }

        foreach (var (name, value) in xobjects)
        {
            if (document.Follow(value) is not PdfStream stream)
            {
                continue;
            }

            if (stream.DictionarySubtype != "Image")
            {
                continue;
            }

            var image = Convert(stream, name.Value, document);
            if (image is not null)
            {
                yield return image.Value;
            }
        }
    }

    private static PdfImage? Convert(PdfStream stream, string name, PdfDocument document)
    {
        var width = stream.GetInt(PdfName.Get("Width"));
        var height = stream.GetInt(PdfName.Get("Height"));

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var filters = stream.FilterNames;
        var outermost = filters.Count > 0 ? filters[^1] : null;

        // A DCTDecode stream is a complete JPEG file, headers and all. Re-encoding it would lose
        // quality for no reason.
        if (outermost is "DCTDecode" or "DCT")
        {
            return new PdfImage(stream.Decoded, "jpg", width, height, name);
        }

        if (outermost is "JPXDecode")
        {
            return new PdfImage(stream.Decoded, "jp2", width, height, name);
        }

        if (outermost is "JBIG2Decode" or "CCITTFaxDecode" or "CCF")
        {
            // These are fax and bilevel codecs whose output needs a decoder this library does not
            // ship. Handing over the raw stream is more useful than nothing: the bytes are valid
            // input to a dedicated tool.
            return new PdfImage(stream.Decoded, outermost.StartsWith("JBIG2", StringComparison.Ordinal)
                ? "jbig2"
                : "g4", width, height, name);
        }

        var bitsPerComponent = stream.GetInt(PdfName.Get("BitsPerComponent"), 8);
        var samples = stream.Decoded;

        var rgb = ToRgb(samples, width, height, bitsPerComponent, stream, document);
        return rgb is null ? null : new PdfImage(EncodePng(rgb, width, height), "png", width, height, name);
    }

    private static byte[]? ToRgb(byte[] samples, int width, int height, int bitsPerComponent,
        PdfStream stream, PdfDocument document)
    {
        var colorSpace = document.Follow(stream[PdfName.Get("ColorSpace")]);
        var isMask = stream.GetBool(PdfName.Get("ImageMask"));

        if (isMask || bitsPerComponent == 1 && colorSpace is null)
        {
            return ExpandBilevel(samples, width, height, stream.Get(PdfName.Get("Decode")) is PdfArray d &&
                                                          d.Count >= 1 && d.AsDoubles()[0] == 1);
        }

        var (family, indexedPalette, componentCount) = DescribeColorSpace(colorSpace, document);

        if (componentCount == 0)
        {
            return null;
        }

        var result = new byte[width * height * 3];
        var rowBits = width * componentCount * bitsPerComponent;
        var rowBytes = (rowBits + 7) / 8;

        // Allocated once rather than per pixel: a stackalloc inside the loop would grow the frame
        // by width*height slots before any of it is reclaimed.
        Span<int> components = stackalloc int[Math.Max(componentCount, 4)];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowBytes;

            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < componentCount; c++)
                {
                    components[c] = ReadSample(samples, rowStart, (x * componentCount + c),
                        bitsPerComponent);
                }

                var target = (y * width + x) * 3;
                var maxValue = (1 << bitsPerComponent) - 1;

                switch (family)
                {
                    case "Indexed":
                    {
                        var index = components[0];
                        var offset = index * 3;
                        if (indexedPalette is not null && offset + 2 < indexedPalette.Length)
                        {
                            result[target] = indexedPalette[offset];
                            result[target + 1] = indexedPalette[offset + 1];
                            result[target + 2] = indexedPalette[offset + 2];
                        }

                        break;
                    }

                    case "DeviceGray":
                    case "CalGray":
                    {
                        var value = (byte)(components[0] * 255 / maxValue);
                        result[target] = value;
                        result[target + 1] = value;
                        result[target + 2] = value;
                        break;
                    }

                    case "DeviceCMYK":
                    {
                        // The naive conversion, which is what every viewer without a colour profile
                        // does. It is not colorimetrically correct and it is what the file looks
                        // like on screen.
                        var c = components[0] / (double)maxValue;
                        var m = components[1] / (double)maxValue;
                        var yy = components[2] / (double)maxValue;
                        var k = components[3] / (double)maxValue;

                        result[target] = (byte)(255 * (1 - Math.Min(1, c + k)));
                        result[target + 1] = (byte)(255 * (1 - Math.Min(1, m + k)));
                        result[target + 2] = (byte)(255 * (1 - Math.Min(1, yy + k)));
                        break;
                    }

                    default:
                    {
                        result[target] = (byte)(components[0] * 255 / maxValue);
                        result[target + 1] = (byte)(components[Math.Min(1, componentCount - 1)] * 255 / maxValue);
                        result[target + 2] = (byte)(components[Math.Min(2, componentCount - 1)] * 255 / maxValue);
                        break;
                    }
                }
            }
        }

        return result;
    }

    private static (string Family, byte[]? Palette, int Components) DescribeColorSpace(
        PdfObject? colorSpace, PdfDocument document)
    {
        switch (colorSpace)
        {
            case PdfName name:
                return name.Value switch
                {
                    "DeviceGray" or "CalGray" or "G" => ("DeviceGray", null, 1),
                    "DeviceRGB" or "CalRGB" or "RGB" => ("DeviceRGB", null, 3),
                    "DeviceCMYK" or "CMYK" => ("DeviceCMYK", null, 4),
                    _ => ("DeviceRGB", null, 3),
                };

            case PdfArray array when array.Count > 0:
            {
                var family = (document.Follow(array[0]) as PdfName)?.Value;

                if (family is "Indexed" or "I" && array.Count >= 4)
                {
                    var baseSpace = document.Follow(array[1]);
                    var (_, _, baseComponents) = DescribeColorSpace(baseSpace, document);

                    var lookup = document.Follow(array[3]) switch
                    {
                        PdfStream stream => stream.Decoded,
                        PdfString text => text.Value,
                        _ => null,
                    };

                    // The palette is stored in the base colour space, so a CMYK-based indexed
                    // image needs its palette converted before it can be indexed as RGB.
                    var palette = lookup is null
                        ? null
                        : ConvertPalette(lookup, baseComponents,
                            (document.Follow(baseSpace) as PdfName)?.Value ?? "DeviceRGB");

                    return ("Indexed", palette, 1);
                }

                if (family is "ICCBased" && array.Count >= 2)
                {
                    var streamObject = document.Follow(array[1]) as PdfStream;
                    var n = streamObject?.GetInt(PdfName.N, 3) ?? 3;
                    return (n switch { 1 => "DeviceGray", 4 => "DeviceCMYK", _ => "DeviceRGB" }, null, n);
                }

                if (family is "Separation" or "DeviceN")
                {
                    // Approximate: treat the tint as ink coverage on white. The tint transform
                    // function would give the exact colour and needs a PostScript calculator.
                    return ("DeviceGrayInverted", null, family == "Separation" ? 1 : 1);
                }

                if (family is "CalRGB" or "Lab")
                {
                    return ("DeviceRGB", null, 3);
                }

                if (family is "CalGray")
                {
                    return ("DeviceGray", null, 1);
                }

                return ("DeviceRGB", null, 3);
            }

            default:
                return ("DeviceRGB", null, 3);
        }
    }

    private static byte[] ConvertPalette(byte[] lookup, int baseComponents, string baseFamily)
    {
        var entries = lookup.Length / Math.Max(1, baseComponents);
        var palette = new byte[entries * 3];

        for (var i = 0; i < entries; i++)
        {
            var source = i * baseComponents;
            var target = i * 3;

            switch (baseComponents)
            {
                case 1:
                    palette[target] = lookup[source];
                    palette[target + 1] = lookup[source];
                    palette[target + 2] = lookup[source];
                    break;

                case 4:
                {
                    var c = lookup[source] / 255.0;
                    var m = lookup[source + 1] / 255.0;
                    var y = lookup[source + 2] / 255.0;
                    var k = lookup[source + 3] / 255.0;
                    palette[target] = (byte)(255 * (1 - Math.Min(1, c + k)));
                    palette[target + 1] = (byte)(255 * (1 - Math.Min(1, m + k)));
                    palette[target + 2] = (byte)(255 * (1 - Math.Min(1, y + k)));
                    break;
                }

                default:
                    palette[target] = lookup[source];
                    palette[target + 1] = source + 1 < lookup.Length ? lookup[source + 1] : lookup[source];
                    palette[target + 2] = source + 2 < lookup.Length ? lookup[source + 2] : lookup[source];
                    break;
            }
        }

        return palette;
    }

    private static int ReadSample(byte[] data, int rowStart, int sampleIndex, int bitsPerComponent)
    {
        switch (bitsPerComponent)
        {
            case 8:
            {
                var at = rowStart + sampleIndex;
                return at < data.Length ? data[at] : 0;
            }

            case 16:
            {
                var at = rowStart + sampleIndex * 2;
                // 16-bit samples are scaled down to 8, which is what the PNG output holds.
                return at + 1 < data.Length ? data[at] : 0;
            }

            default:
            {
                var bitOffset = sampleIndex * bitsPerComponent;
                var byteOffset = rowStart + bitOffset / 8;

                if (byteOffset >= data.Length)
                {
                    return 0;
                }

                var shift = 8 - bitOffset % 8 - bitsPerComponent;
                var mask = (1 << bitsPerComponent) - 1;

                if (shift >= 0)
                {
                    return data[byteOffset] >> shift & mask;
                }

                // A sample straddling a byte boundary (a 4-bit sample never does, a 2-bit one at an
                // odd offset can when the row is not byte-aligned).
                var combined = data[byteOffset] << 8 | (byteOffset + 1 < data.Length ? data[byteOffset + 1] : 0);
                return combined >> 8 + shift & mask;
            }
        }
    }

    private static byte[] ExpandBilevel(byte[] samples, int width, int height, bool inverted)
    {
        var result = new byte[width * height * 3];
        var rowBytes = (width + 7) / 8;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var byteIndex = y * rowBytes + x / 8;
                var bit = byteIndex < samples.Length
                    ? samples[byteIndex] >> 7 - x % 8 & 1
                    : 0;

                if (inverted)
                {
                    bit ^= 1;
                }

                // In an image mask a 0 bit paints and a 1 bit is transparent, so 0 becomes black.
                var value = (byte)(bit == 0 ? 0 : 255);
                var target = (y * width + x) * 3;
                result[target] = value;
                result[target + 1] = value;
                result[target + 2] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Wraps 8-bit RGB samples in a PNG file.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than through an imaging library so the core of PdfNet has no native
    /// dependency. PNG's structure is small: a signature, an IHDR, one zlib-compressed IDAT of
    /// filtered scanlines, and an IEND — with a CRC32 on every chunk that readers do check.
    /// </remarks>
    public static byte[] EncodePng(byte[] rgb, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgb);

        using var output = new MemoryStream(rgb.Length / 2 + 1024);

        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type 2 = truecolour RGB
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // no interlace
        WriteChunk(output, "IHDR"u8, header);

        // Each scanline is prefixed with its filter type. Filter 0 (None) keeps the encoder simple;
        // deflate still compresses photographic data well and this is an export path, not a
        // storage format.
        var stride = width * 3;
        var raw = new byte[(stride + 1) * height];

        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;
            Array.Copy(rgb, y * stride, raw, y * (stride + 1) + 1, Math.Min(stride, rgb.Length - y * stride));
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);

        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data, 0, data.Length);

        var crc = Crc32(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ c >> 1 : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ crc >> 8;
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ crc >> 8;
        }

        return crc ^ 0xFFFFFFFF;
    }
}
