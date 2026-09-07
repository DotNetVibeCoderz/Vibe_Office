// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace OfficeNet.Core.Drawing;

/// <summary>Image container formats OfficeNet can identify without decoding.</summary>
public enum ImageFormat
{
    /// <summary>Unrecognised.</summary>
    Unknown = 0,

    /// <summary>Portable Network Graphics.</summary>
    Png,

    /// <summary>JPEG / JFIF.</summary>
    Jpeg,

    /// <summary>Graphics Interchange Format.</summary>
    Gif,

    /// <summary>Windows bitmap.</summary>
    Bmp,

    /// <summary>Tagged Image File Format.</summary>
    Tiff,

    /// <summary>WebP.</summary>
    Webp,

    /// <summary>Enhanced metafile.</summary>
    Emf,

    /// <summary>Windows metafile.</summary>
    Wmf,

    /// <summary>Scalable Vector Graphics.</summary>
    Svg,
}

/// <summary>
/// Format, pixel size and resolution of an image, read from its header without decoding pixels.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a picture inserted with no explicit size must come out at its natural size,
/// and "natural size" means pixels divided by the image's own DPI — not pixels divided by 96. A
/// 1200x800 photo scanned at 300 DPI is a 4x2.67 inch picture; treating it as 96 DPI puts a 12.5
/// inch image on a letter page, which is exactly the bug that makes inserted photos overflow the
/// margins.
/// </para>
/// <para>
/// Only the header is read. Decoding a 40 megapixel JPEG to learn two integers is both slow and a
/// dependency this library does not want in its core.
/// </para>
/// </remarks>
public sealed class ImageInfo
{
    private ImageInfo(ImageFormat format, int width, int height, double horizontalDpi, double verticalDpi)
    {
        Format = format;
        PixelWidth = width;
        PixelHeight = height;
        HorizontalDpi = horizontalDpi;
        VerticalDpi = verticalDpi;
    }

    /// <summary>The container format.</summary>
    public ImageFormat Format { get; }

    /// <summary>Width in pixels (or in the metafile's logical units for EMF/WMF/SVG).</summary>
    public int PixelWidth { get; }

    /// <summary>Height in pixels.</summary>
    public int PixelHeight { get; }

    /// <summary>Horizontal resolution; 96 when the file does not say.</summary>
    public double HorizontalDpi { get; }

    /// <summary>Vertical resolution; 96 when the file does not say.</summary>
    public double VerticalDpi { get; }

    /// <summary>The natural width, pixels converted through the image's own resolution.</summary>
    public Length NaturalWidth => Length.FromPixels(PixelWidth, HorizontalDpi);

    /// <summary>The natural height.</summary>
    public Length NaturalHeight => Length.FromPixels(PixelHeight, VerticalDpi);

    /// <summary>Width divided by height; 1 when the height is unknown.</summary>
    public double AspectRatio => PixelHeight == 0 ? 1 : (double)PixelWidth / PixelHeight;

    /// <summary>The file extension this format is stored with inside a package.</summary>
    public string Extension => Format switch
    {
        ImageFormat.Png => "png",
        ImageFormat.Jpeg => "jpeg",
        ImageFormat.Gif => "gif",
        ImageFormat.Bmp => "bmp",
        ImageFormat.Tiff => "tiff",
        ImageFormat.Webp => "webp",
        ImageFormat.Emf => "emf",
        ImageFormat.Wmf => "wmf",
        ImageFormat.Svg => "svg",
        _ => "bin",
    };

    /// <summary>The MIME content type to declare for this image.</summary>
    public string ContentType =>
        Packaging.ContentTypes.ForExtension(Extension) ?? "application/octet-stream";

    /// <summary>Reads the header of an image file.</summary>
    /// <exception cref="OfficeNetException">The bytes are not a recognised image.</exception>
    public static ImageInfo Read(ReadOnlySpan<byte> data)
    {
        return TryRead(data, out var info)
            ? info
            : throw new OfficeNetException(
                "The data is not a PNG, JPEG, GIF, BMP, TIFF, WebP, EMF, WMF or SVG image. " +
                "Convert it to one of those before inserting it into a document.");
    }

    /// <summary>Reads the header of an image file from disk.</summary>
    public static ImageInfo ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllBytes(path));
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> TiffLittleEndianSignature => [0x49, 0x49, 0x2A, 0x00];

    private static ReadOnlySpan<byte> TiffBigEndianSignature => [0x4D, 0x4D, 0x00, 0x2A];

    /// <summary>Reads the header of an image, returning false rather than throwing.</summary>
    public static bool TryRead(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        info = null!;

        if (data.Length >= 8 && data[..8].SequenceEqual(PngSignature))
        {
            return TryReadPng(data, out info);
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return TryReadJpeg(data, out info);
        }

        if (data.Length >= 6 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        {
            return TryReadGif(data, out info);
        }

        if (data.Length >= 26 && data[0] == 'B' && data[1] == 'M')
        {
            return TryReadBmp(data, out info);
        }

        if (data.Length >= 8 && (data[..4].SequenceEqual(TiffLittleEndianSignature) ||
                                 data[..4].SequenceEqual(TiffBigEndianSignature)))
        {
            return TryReadTiff(data, out info);
        }

        if (data.Length >= 16 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return TryReadWebp(data, out info);
        }

        if (data.Length >= 44 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 1 &&
            data[40..44].SequenceEqual(" EMF"u8))
        {
            return TryReadEmf(data, out info);
        }

        if (data.Length >= 22 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x9AC6CDD7)
        {
            return TryReadWmf(data, out info);
        }

        if (LooksLikeSvg(data))
        {
            return TryReadSvg(data, out info);
        }

        return false;
    }

    private static bool TryReadPng(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        info = null!;
        if (data.Length < 33 || !data[12..16].SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        var height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[20..]);

        // pHYs, when present, gives pixels per unit and a unit code; code 1 is metres. It is an
        // ancillary chunk and can appear anywhere before IDAT, so the chunk list is walked.
        double dpiX = 96, dpiY = 96;
        var offset = 8;
        while (offset + 8 <= data.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length < 0 || offset + 12 + length > data.Length)
            {
                break;
            }

            var type = data.Slice(offset + 4, 4);
            if (type.SequenceEqual("pHYs"u8) && length >= 9)
            {
                var body = data.Slice(offset + 8, 9);
                var ppuX = BinaryPrimitives.ReadUInt32BigEndian(body);
                var ppuY = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                if (body[8] == 1 && ppuX > 0 && ppuY > 0)
                {
                    dpiX = ppuX * 0.0254;
                    dpiY = ppuY * 0.0254;
                }

                break;
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                break;
            }

            offset += 12 + length;
        }

        info = new ImageInfo(ImageFormat.Png, width, height, dpiX, dpiY);
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        info = null!;
        double dpiX = 96, dpiY = 96;

        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset] != 0xFF)
            {
                // Fill bytes (0xFF padding) are legal between segments; anything else is corruption.
                offset++;
                continue;
            }

            var marker = data[offset + 1];
            offset += 2;

            // Standalone markers carry no length.
            if (marker is 0x01 or >= 0xD0 and <= 0xD9)
            {
                continue;
            }

            if (offset + 2 > data.Length)
            {
                break;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            if (segmentLength < 2 || offset + segmentLength > data.Length)
            {
                break;
            }

            var payload = data.Slice(offset + 2, segmentLength - 2);

            // APP0/JFIF carries the density. Units 1 = dots per inch, 2 = dots per cm.
            if (marker == 0xE0 && payload.Length >= 12 && payload[..5].SequenceEqual("JFIF\0"u8))
            {
                var units = payload[7];
                var xDensity = BinaryPrimitives.ReadUInt16BigEndian(payload[8..]);
                var yDensity = BinaryPrimitives.ReadUInt16BigEndian(payload[10..]);
                if (xDensity > 0 && yDensity > 0)
                {
                    if (units == 1)
                    {
                        dpiX = xDensity;
                        dpiY = yDensity;
                    }
                    else if (units == 2)
                    {
                        dpiX = xDensity * 2.54;
                        dpiY = yDensity * 2.54;
                    }
                }
            }

            // Any SOF marker holds the frame size. C4 (DHT), C8 (JPG extension) and CC (DAC) sit in
            // the same numeric range and are not frame headers — reading those as SOF gives a
            // plausible but wrong size.
            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isStartOfFrame && payload.Length >= 5)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
                info = new ImageInfo(ImageFormat.Jpeg, width, height, dpiX, dpiY);
                return width > 0 && height > 0;
            }

            if (marker == 0xDA)
            {
                break;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool TryReadGif(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        info = new ImageInfo(ImageFormat.Gif, width, height, 96, 96);
        return width > 0 && height > 0;
    }

    private static bool TryReadBmp(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        info = null!;
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);

        int width, height;
        double dpiX = 96, dpiY = 96;

        if (headerSize == 12)
        {
            // BITMAPCOREHEADER: 16-bit dimensions, no resolution fields.
            width = BinaryPrimitives.ReadInt16LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadInt16LittleEndian(data[20..]);
        }
        else if (headerSize >= 40 && data.Length >= 46)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);

            var ppmX = BinaryPrimitives.ReadInt32LittleEndian(data[38..]);
            var ppmY = BinaryPrimitives.ReadInt32LittleEndian(data[42..]);
            if (ppmX > 0 && ppmY > 0)
            {
                dpiX = ppmX * 0.0254;
                dpiY = ppmY * 0.0254;
            }
        }
        else
        {
            return false;
        }

        // A negative height means a top-down bitmap; the magnitude is still the height.
        info = new ImageInfo(ImageFormat.Bmp, Math.Abs(width), Math.Abs(height), dpiX, dpiY);
        return width != 0 && height != 0;
    }

    private static bool TryReadTiff(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        info = null!;
        var littleEndian = data[0] == 0x49;

        var ifdOffset = (int)ReadU32(data, littleEndian, 4);
        if (ifdOffset <= 0 || ifdOffset + 2 > data.Length)
        {
            return false;
        }

        var entryCount = ReadU16(data, littleEndian, ifdOffset);
        long width = 0, height = 0;
        double dpiX = 0, dpiY = 0;
        var resolutionUnit = 2; // 2 = inch, the TIFF default.

        for (var i = 0; i < entryCount; i++)
        {
            var entry = ifdOffset + 2 + i * 12;
            if (entry + 12 > data.Length)
            {
                break;
            }

            var tag = ReadU16(data, littleEndian, entry);
            var type = ReadU16(data, littleEndian, entry + 2);

            // SHORT values sit in the low half of the value field; LONG values fill it. A big-endian
            // file puts a SHORT in the *first* two bytes, which is why the read is offset by type.
            long inlineValue = type switch
            {
                3 => ReadU16(data, littleEndian, entry + 8),
                4 => ReadU32(data, littleEndian, entry + 8),
                _ => 0,
            };

            switch (tag)
            {
                case 256:
                    width = inlineValue;
                    break;
                case 257:
                    height = inlineValue;
                    break;
                case 282 or 283:
                {
                    // RATIONAL: an offset to two LONGs, numerator then denominator.
                    var at = (int)ReadU32(data, littleEndian, entry + 8);
                    if (type == 5 && at > 0 && at + 8 <= data.Length)
                    {
                        var numerator = ReadU32(data, littleEndian, at);
                        var denominator = ReadU32(data, littleEndian, at + 4);
                        if (denominator != 0)
                        {
                            var value = (double)numerator / denominator;
                            if (tag == 282)
                            {
                                dpiX = value;
                            }
                            else
                            {
                                dpiY = value;
                            }
                        }
                    }

                    break;
                }

                case 296:
                    resolutionUnit = (int)inlineValue;
                    break;
            }
        }

        if (resolutionUnit == 3)
        {
            dpiX *= 2.54;
            dpiY *= 2.54;
        }

        info = new ImageInfo(
            ImageFormat.Tiff,
            (int)width,
            (int)height,
            dpiX > 0 ? dpiX : 96,
            dpiY > 0 ? dpiY : 96);

        return width > 0 && height > 0;
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, bool littleEndian, int at) => littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..])
        : BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

    private static ushort ReadU16(ReadOnlySpan<byte> data, bool littleEndian, int at) => littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..])
        : BinaryPrimitives.ReadUInt16BigEndian(data[at..]);

    private static bool TryReadWebp(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        info = null!;
        var chunk = data[12..16];

        if (chunk.SequenceEqual("VP8X"u8) && data.Length >= 30)
        {
            // Canvas size is stored minus one, as three-byte little-endian values.
            var w = 1 + (data[24] | data[25] << 8 | data[26] << 16);
            var h = 1 + (data[27] | data[28] << 8 | data[29] << 16);
            info = new ImageInfo(ImageFormat.Webp, w, h, 96, 96);
            return true;
        }

        if (chunk.SequenceEqual("VP8 "u8) && data.Length >= 30)
        {
            var w = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF;
            var h = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF;
            info = new ImageInfo(ImageFormat.Webp, w, h, 96, 96);
            return w > 0 && h > 0;
        }

        if (chunk.SequenceEqual("VP8L"u8) && data.Length >= 25)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            var w = (int)(bits & 0x3FFF) + 1;
            var h = (int)(bits >> 14 & 0x3FFF) + 1;
            info = new ImageInfo(ImageFormat.Webp, w, h, 96, 96);
            return true;
        }

        return false;
    }

    private static bool TryReadEmf(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        // rclFrame at offset 24 is the picture frame in 0.01 mm units, which is the size a
        // consumer should draw the metafile at. rclBounds (offset 8) is device pixels and is not
        // resolution-independent, so it is the wrong field for placement.
        var left = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
        var top = BinaryPrimitives.ReadInt32LittleEndian(data[28..]);
        var right = BinaryPrimitives.ReadInt32LittleEndian(data[32..]);
        var bottom = BinaryPrimitives.ReadInt32LittleEndian(data[36..]);

        var widthMm = (right - left) / 100.0;
        var heightMm = (bottom - top) / 100.0;

        // Express the size as pixels at 96 DPI so the rest of the pipeline needs no special case.
        var w = (int)Math.Round(widthMm / 25.4 * 96);
        var h = (int)Math.Round(heightMm / 25.4 * 96);

        info = new ImageInfo(ImageFormat.Emf, Math.Max(w, 1), Math.Max(h, 1), 96, 96);
        return w > 0 && h > 0;
    }

    private static bool TryReadWmf(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        var left = BinaryPrimitives.ReadInt16LittleEndian(data[6..]);
        var top = BinaryPrimitives.ReadInt16LittleEndian(data[8..]);
        var right = BinaryPrimitives.ReadInt16LittleEndian(data[10..]);
        var bottom = BinaryPrimitives.ReadInt16LittleEndian(data[12..]);
        var unitsPerInch = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (unitsPerInch == 0)
        {
            unitsPerInch = 1440;
        }

        var w = (int)Math.Round((right - left) / (double)unitsPerInch * 96);
        var h = (int)Math.Round((bottom - top) / (double)unitsPerInch * 96);

        info = new ImageInfo(ImageFormat.Wmf, Math.Abs(w), Math.Abs(h), 96, 96);
        return w != 0 && h != 0;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> data)
    {
        var probe = data.Length > 512 ? data[..512] : data;
        var text = Encoding.UTF8.GetString(probe);
        return text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadSvg(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        info = null!;
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(data), LoadOptions.None);
            var root = doc.Root;
            if (root is null)
            {
                return false;
            }

            var width = ParseSvgLength(root.Attribute("width")?.Value);
            var height = ParseSvgLength(root.Attribute("height")?.Value);

            // A responsive SVG has no width/height, only a viewBox — which is exactly the case
            // where falling back to a fixed default would produce a squashed picture.
            if (width <= 0 || height <= 0)
            {
                var viewBox = root.Attribute("viewBox")?.Value;
                if (viewBox is not null)
                {
                    var parts = viewBox.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 4 &&
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vw) &&
                        double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vh))
                    {
                        width = width > 0 ? width : vw;
                        height = height > 0 ? height : vh;
                    }
                }
            }

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            info = new ImageInfo(ImageFormat.Svg, (int)Math.Round(width), (int)Math.Round(height), 96, 96);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static double ParseSvgLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var span = value.AsSpan().Trim();
        var end = 0;
        while (end < span.Length && (char.IsAsciiDigit(span[end]) || span[end] is '.' or '-' or '+'))
        {
            end++;
        }

        if (end == 0 ||
            !double.TryParse(span[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return 0;
        }

        var unit = span[end..].Trim().ToString().ToLowerInvariant();
        return unit switch
        {
            "" or "px" => number,
            "pt" => number * 96 / 72,
            "pc" => number * 16,
            "in" => number * 96,
            "cm" => number * 96 / 2.54,
            "mm" => number * 96 / 25.4,
            "%" => 0,
            _ => number,
        };
    }

    public override string ToString() =>
        $"{Format} {PixelWidth}x{PixelHeight} @ {HorizontalDpi:0.#}x{VerticalDpi:0.#} DPI";
}
