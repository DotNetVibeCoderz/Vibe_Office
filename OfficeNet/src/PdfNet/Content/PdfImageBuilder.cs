// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.IO.Compression;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using PdfNet.Objects;

namespace PdfNet.Content;

/// <summary>
/// Turns an image file into the PDF image XObject that draws it.
/// </summary>
/// <remarks>
/// <para>
/// JPEG needs no conversion at all: a <c>DCTDecode</c> stream is the JPEG file, so embedding is a
/// copy. PNG needs none either for the common colour types, and that is not obvious — PDF's
/// <c>FlateDecode</c> with <c>/Predictor 15</c> is <em>exactly</em> PNG's own compression and
/// filtering, so the IDAT payload can be lifted across byte for byte. Decoding and re-encoding a
/// PNG, which most libraries do, costs time and file size for nothing.
/// </para>
/// <para>
/// Only PNGs carrying an alpha channel need real work, because PDF keeps transparency in a separate
/// soft-mask image rather than interleaved with the colour.
/// </para>
/// </remarks>
public static class PdfImageBuilder
{
    /// <summary>Builds an image XObject from an image file's bytes.</summary>
    /// <exception cref="OfficeNetNotSupportedException">The format cannot be embedded.</exception>
    public static PdfStream Build(byte[] imageBytes, Document.PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentNullException.ThrowIfNull(document);

        var info = ImageInfo.Read(imageBytes);

        return info.Format switch
        {
            ImageFormat.Jpeg => BuildJpeg(imageBytes, info),
            ImageFormat.Png => BuildPng(imageBytes, info, document),
            ImageFormat.Bmp => BuildRaw(DecodeBmp(imageBytes, out var w, out var h), w, h),
            ImageFormat.Gif => throw new OfficeNetNotSupportedException(
                "GIF cannot be embedded directly in a PDF. Convert it to PNG first."),
            _ => throw new OfficeNetNotSupportedException(
                $"{info.Format} images cannot be embedded in a PDF. Use PNG or JPEG."),
        };
    }

    private static PdfStream BuildJpeg(byte[] bytes, ImageInfo info)
    {
        var stream = new PdfStream(new PdfDictionary(), bytes);
        stream[PdfName.Type] = PdfName.XObject;
        stream.SetName(PdfName.Subtype, "Image");
        stream.Set(PdfName.Get("Width"), info.PixelWidth);
        stream.Set(PdfName.Get("Height"), info.PixelHeight);
        stream.Set(PdfName.Get("BitsPerComponent"), 8);
        stream.SetName(PdfName.Get("ColorSpace"), "DeviceRGB");
        stream.SetName(PdfName.Filter, "DCTDecode");
        stream.Set(PdfName.Length, bytes.Length);
        return stream;
    }

    private static PdfStream BuildPng(byte[] bytes, ImageInfo info, Document.PdfDocument document)
    {
        var png = PngFile.Parse(bytes);

        // Interlaced PNG interleaves seven passes and is not what /Predictor 15 expects.
        if (png.Interlace != 0)
        {
            throw new OfficeNetNotSupportedException(
                "Interlaced (Adam7) PNG cannot be embedded. Save the image non-interlaced.");
        }

        if (png.ColorType is 4 or 6)
        {
            return BuildPngWithAlpha(png, document);
        }

        var stream = new PdfStream(new PdfDictionary(), png.ImageData);
        stream[PdfName.Type] = PdfName.XObject;
        stream.SetName(PdfName.Subtype, "Image");
        stream.Set(PdfName.Get("Width"), png.Width);
        stream.Set(PdfName.Get("Height"), png.Height);
        stream.Set(PdfName.Get("BitsPerComponent"), png.BitDepth);
        stream.SetName(PdfName.Filter, "FlateDecode");

        var components = png.ColorType switch
        {
            0 => 1, // greyscale
            2 => 3, // truecolour
            3 => 1, // indexed
            _ => 3,
        };

        var parms = new PdfDictionary();
        parms.Set(PdfName.Get("Predictor"), 15);
        parms.Set(PdfName.Get("Colors"), components);
        parms.Set(PdfName.Get("BitsPerComponent"), png.BitDepth);
        parms.Set(PdfName.Get("Columns"), png.Width);
        stream[PdfName.DecodeParms] = parms;

        if (png.ColorType == 3)
        {
            if (png.Palette is null)
            {
                throw new OfficeNetException("The PNG is indexed but carries no PLTE palette.");
            }

            // [/Indexed base hival lookup]
            stream[PdfName.Get("ColorSpace")] = new PdfArray(
            [
                PdfName.Get("Indexed"),
                PdfName.Get("DeviceRGB"),
                new PdfNumber(png.Palette.Length / 3 - 1),
                new PdfString(png.Palette) { IsHex = true },
            ]);

            // A tRNS chunk on an indexed image is per-palette-entry alpha. PDF expresses the fully
            // transparent entries as /Mask, which handles the single-transparent-colour case that
            // logos and icons actually use.
            if (png.Transparency is { Length: > 0 })
            {
                var mask = new PdfArray();
                for (var i = 0; i < png.Transparency.Length; i++)
                {
                    if (png.Transparency[i] == 0)
                    {
                        mask.Add(new PdfNumber(i));
                        mask.Add(new PdfNumber(i));
                    }
                }

                if (mask.Count > 0)
                {
                    stream[PdfName.Get("Mask")] = mask;
                }
            }
        }
        else
        {
            stream.SetName(PdfName.Get("ColorSpace"), components == 1 ? "DeviceGray" : "DeviceRGB");
        }

        stream.Set(PdfName.Length, png.ImageData.Length);
        return stream;
    }

    private static PdfStream BuildPngWithAlpha(PngFile png, Document.PdfDocument document)
    {
        var (color, alpha) = png.SplitAlpha();

        var colorComponents = png.ColorType == 4 ? 1 : 3;

        var stream = new PdfStream();
        stream[PdfName.Type] = PdfName.XObject;
        stream.SetName(PdfName.Subtype, "Image");
        stream.Set(PdfName.Get("Width"), png.Width);
        stream.Set(PdfName.Get("Height"), png.Height);
        stream.Set(PdfName.Get("BitsPerComponent"), 8);
        stream.SetName(PdfName.Get("ColorSpace"), colorComponents == 1 ? "DeviceGray" : "DeviceRGB");
        stream.SetDecoded(color);

        var mask = new PdfStream();
        mask[PdfName.Type] = PdfName.XObject;
        mask.SetName(PdfName.Subtype, "Image");
        mask.Set(PdfName.Get("Width"), png.Width);
        mask.Set(PdfName.Get("Height"), png.Height);
        mask.Set(PdfName.Get("BitsPerComponent"), 8);
        mask.SetName(PdfName.Get("ColorSpace"), "DeviceGray");
        mask.SetDecoded(alpha);

        stream[PdfName.Get("SMask")] = document.AddObject(mask);
        return stream;
    }

    private static PdfStream BuildRaw(byte[] rgb, int width, int height)
    {
        var stream = new PdfStream();
        stream[PdfName.Type] = PdfName.XObject;
        stream.SetName(PdfName.Subtype, "Image");
        stream.Set(PdfName.Get("Width"), width);
        stream.Set(PdfName.Get("Height"), height);
        stream.Set(PdfName.Get("BitsPerComponent"), 8);
        stream.SetName(PdfName.Get("ColorSpace"), "DeviceRGB");
        stream.SetDecoded(rgb);
        return stream;
    }

    private static byte[] DecodeBmp(byte[] bytes, out int width, out int height)
    {
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10));
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14));

        width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18));
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22));
        height = Math.Abs(rawHeight);

        var bitCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(28));

        if (bitCount is not (24 or 32) || headerSize < 40)
        {
            throw new OfficeNetNotSupportedException(
                $"Only 24- and 32-bit uncompressed BMP files can be embedded (this one is {bitCount}-bit).");
        }

        var bytesPerPixel = bitCount / 8;

        // BMP rows are padded to a four-byte boundary and, unless the height is negative, stored
        // bottom-up. Both are silent corruptions when ignored.
        var stride = (width * bytesPerPixel + 3) / 4 * 4;
        var topDown = rawHeight < 0;

        var rgb = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            var sourceRow = topDown ? y : height - 1 - y;
            var rowStart = dataOffset + sourceRow * stride;

            for (var x = 0; x < width; x++)
            {
                var source = rowStart + x * bytesPerPixel;
                var target = (y * width + x) * 3;

                if (source + 2 >= bytes.Length)
                {
                    continue;
                }

                // BMP stores BGR, not RGB.
                rgb[target] = bytes[source + 2];
                rgb[target + 1] = bytes[source + 1];
                rgb[target + 2] = bytes[source];
            }
        }

        return rgb;
    }
}

/// <summary>A parsed PNG file: its header fields and its concatenated IDAT payload.</summary>
internal sealed class PngFile
{
    public int Width { get; private init; }

    public int Height { get; private init; }

    public int BitDepth { get; private init; }

    public int ColorType { get; private init; }

    public int Interlace { get; private init; }

    public byte[] ImageData { get; private init; } = [];

    public byte[]? Palette { get; private init; }

    public byte[]? Transparency { get; private init; }

    public static PngFile Parse(byte[] bytes)
    {
        if (bytes.Length < 33)
        {
            throw new OfficeNetException("The PNG is too short to contain a header.");
        }

        int width = 0, height = 0, bitDepth = 8, colorType = 6, interlace = 0;
        byte[]? palette = null;
        byte[]? transparency = null;

        using var idat = new MemoryStream();
        var offset = 8;

        while (offset + 8 <= bytes.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            if (length < 0 || offset + 12 + length > bytes.Length)
            {
                break;
            }

            var type = bytes.AsSpan(offset + 4, 4);
            var body = bytes.AsSpan(offset + 8, length);

            if (type.SequenceEqual("IHDR"u8))
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(body);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                bitDepth = body[8];
                colorType = body[9];
                interlace = body[12];
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = body.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = body.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                // IDAT may be split across any number of chunks and the split can fall mid-symbol,
                // so they must be concatenated before inflating.
                idat.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset += 12 + length;
        }

        return new PngFile
        {
            Width = width,
            Height = height,
            BitDepth = bitDepth,
            ColorType = colorType,
            Interlace = interlace,
            ImageData = idat.ToArray(),
            Palette = palette,
            Transparency = transparency,
        };
    }

    /// <summary>Decodes the image and splits colour from alpha, as PDF's soft-mask model needs.</summary>
    public (byte[] Color, byte[] Alpha) SplitAlpha()
    {
        if (BitDepth != 8)
        {
            throw new OfficeNetNotSupportedException(
                $"A {BitDepth}-bit PNG with an alpha channel cannot be embedded. Save it as 8-bit.");
        }

        var samplesPerPixel = ColorType == 4 ? 2 : 4;
        var colorComponents = samplesPerPixel - 1;

        var raw = Inflate(ImageData);
        var stride = Width * samplesPerPixel;
        var unfiltered = Unfilter(raw, stride, samplesPerPixel);

        var color = new byte[Width * Height * colorComponents];
        var alpha = new byte[Width * Height];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var source = y * stride + x * samplesPerPixel;
                var colorTarget = (y * Width + x) * colorComponents;

                if (source + samplesPerPixel > unfiltered.Length)
                {
                    continue;
                }

                for (var c = 0; c < colorComponents; c++)
                {
                    color[colorTarget + c] = unfiltered[source + c];
                }

                alpha[y * Width + x] = unfiltered[source + colorComponents];
            }
        }

        return (color, alpha);
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var decompressor = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(data.Length * 4);
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Unfilter(byte[] raw, int stride, int bytesPerPixel)
    {
        var rows = raw.Length / (stride + 1);
        var output = new byte[rows * stride];
        var previous = new byte[stride];

        for (var row = 0; row < rows; row++)
        {
            var source = row * (stride + 1);
            var filter = raw[source];
            var current = new byte[stride];
            Array.Copy(raw, source + 1, current, 0, stride);

            switch (filter)
            {
                case 1:
                    for (var i = bytesPerPixel; i < stride; i++)
                    {
                        current[i] = (byte)(current[i] + current[i - bytesPerPixel]);
                    }

                    break;

                case 2:
                    for (var i = 0; i < stride; i++)
                    {
                        current[i] = (byte)(current[i] + previous[i]);
                    }

                    break;

                case 3:
                    for (var i = 0; i < stride; i++)
                    {
                        var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        current[i] = (byte)(current[i] + (left + previous[i]) / 2);
                    }

                    break;

                case 4:
                    for (var i = 0; i < stride; i++)
                    {
                        var a = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        var b = previous[i];
                        var c = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;

                        var p = a + b - c;
                        var pa = Math.Abs(p - a);
                        var pb = Math.Abs(p - b);
                        var pc = Math.Abs(p - c);
                        var predictor = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;

                        current[i] = (byte)(current[i] + predictor);
                    }

                    break;
            }

            Array.Copy(current, 0, output, row * stride, stride);
            previous = current;
        }

        return output;
    }
}
