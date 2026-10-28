// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.IO.Compression;
using OfficeNet.Core.Drawing;
using PdfNet.Document;
using SkiaSharp;
using WordNet;
using Xunit;

namespace OfficeNet.Rendering.Tests;

/// <summary>
/// Multi-page TIFF, read back by a decoder written here from the specification — separate code from
/// the writer, so the two cannot share a mistake. (Pillow, through libtiff, was also used to check
/// every colour and compression pixel for pixel; it is not available to CI.)
/// </summary>
public class TiffTests
{
    private static WordDocument ThreePages()
    {
        var document = WordDocument.Create();
        document.AddHeading("Halaman satu", 1);
        document.AddParagraph("Teks merah").Runs[0].Format.Color = OfficeColor.FromRgb(200, 0, 0);
        document.AddPageBreak();
        document.AddHeading("Halaman dua", 1);
        document.AddPageBreak();
        document.AddHeading("Halaman tiga", 1);
        return document;
    }

    [Theory]
    [InlineData(TiffCompression.Deflate)]
    [InlineData(TiffCompression.PackBits)]
    [InlineData(TiffCompression.None)]
    public void EveryPageIsInOneFileAndMatchesThePngRendering(TiffCompression compression)
    {
        using var document = ThreePages();
        using var pdf = document.ToPdf();

        var tiff = DocumentRenderer.RenderTiff(pdf, new TiffOptions { Dpi = 72, Compression = compression });
        var pages = TiffReader.Read(tiff);

        Assert.Equal(3, pages.Count);
        Assert.Equal([0, 1, 2], pages.Select(p => p.PageNumber));
        Assert.All(pages, p => Assert.Equal(3, p.PageCount));
        Assert.All(pages, p => Assert.Equal(72, p.Dpi, 3));

        for (var i = 0; i < pages.Count; i++)
        {
            using var png = SKBitmap.Decode(DocumentRenderer.RenderPage(pdf.Pages[i], new RenderOptions { Dpi = 72 }));
            var page = pages[i];

            Assert.Equal((png.Width, png.Height), (page.Width, page.Height));

            for (var y = 0; y < png.Height; y += 3)
            {
                for (var x = 0; x < png.Width; x += 3)
                {
                    var expected = png.GetPixel(x, y);
                    Assert.Equal((expected.Red, expected.Green, expected.Blue), page.Rgb(x, y));
                }
            }
        }
    }

    [Fact]
    public void GreyAndBlackAndWhiteAreSmallerAndThresholdedAtHalfBrightness()
    {
        using var document = ThreePages();

        var colour = DocumentRenderer.RenderTiff(document, new TiffOptions { Dpi = 72 });
        var grey = DocumentRenderer.RenderTiff(document, new TiffOptions { Dpi = 72, Color = TiffColor.Grayscale });
        var mono = DocumentRenderer.RenderTiff(document, new TiffOptions { Dpi = 72, Color = TiffColor.BlackAndWhite });

        Assert.True(grey.Length < colour.Length);
        Assert.True(mono.Length < grey.Length);

        var greyPage = TiffReader.Read(grey)[0];
        var monoPage = TiffReader.Read(mono)[0];
        Assert.Equal(1, greyPage.Samples);
        Assert.Equal(1, monoPage.Bits);

        // Grey is the colour page's luminance, by the Rec. 601 weights — red text and all.
        using var pdf = document.ToPdf();
        using var png = SKBitmap.Decode(DocumentRenderer.RenderPage(pdf.Pages[0], new RenderOptions { Dpi = 72 }));

        for (var y = 0; y < png.Height; y += 2)
        {
            for (var x = 0; x < png.Width; x += 2)
            {
                var c = png.GetPixel(x, y);
                var expected = (c.Red * 299 + c.Green * 587 + c.Blue * 114 + 500) / 1000;
                Assert.Equal(expected, greyPage.Rgb(x, y).R);
            }
        }

        // Wherever the grey page is dark the black-and-white one is black, and light is white.
        for (var y = 0; y < greyPage.Height; y += 2)
        {
            for (var x = 0; x < greyPage.Width; x += 2)
            {
                var (g, _, _) = greyPage.Rgb(x, y);
                var (m, _, _) = monoPage.Rgb(x, y);
                Assert.Equal(g < 128 ? 0 : 255, m);
            }
        }
    }

    [Fact]
    public void ADocumentOnDiskBecomesATiffFile()
    {
        var folder = Directory.CreateTempSubdirectory("officenet-tiff-");

        try
        {
            var docx = Path.Combine(folder.FullName, "surat.docx");
            using (var document = ThreePages())
            {
                document.Save(docx);
            }

            var tiff = Path.Combine(folder.FullName, "surat.tif");
            DocumentRenderer.RenderTiff(docx, tiff, new TiffOptions { Dpi = 50, Color = TiffColor.BlackAndWhite });

            Assert.Equal(3, TiffReader.Read(File.ReadAllBytes(tiff)).Count);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>Enough of a baseline TIFF reader to check what the writer wrote.</summary>
    private static class TiffReader
    {
        public sealed record Page(int Width, int Height, int Samples, int Bits, int Photometric, byte[] Pixels,
            double Dpi, int PageNumber, int PageCount)
        {
            public (int R, int G, int B) Rgb(int x, int y)
            {
                if (Bits == 1)
                {
                    var rowBytes = (Width + 7) / 8;
                    var bit = (Pixels[y * rowBytes + x / 8] >> (7 - x % 8)) & 1;

                    // WhiteIsZero: a set bit is black.
                    var value = Photometric == 0 ? (bit == 1 ? 0 : 255) : (bit == 1 ? 255 : 0);
                    return (value, value, value);
                }

                var at = (y * Width + x) * Samples;

                if (Samples == 3)
                {
                    return (Pixels[at], Pixels[at + 1], Pixels[at + 2]);
                }

                var grey = Photometric == 0 ? 255 - Pixels[at] : Pixels[at];
                return (grey, grey, grey);
            }
        }

        public static List<Page> Read(byte[] file)
        {
            Assert.Equal("II*\0", System.Text.Encoding.ASCII.GetString(file, 0, 4));

            var pages = new List<Page>();
            var directory = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4));

            while (directory != 0)
            {
                var count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan((int)directory));
                var tags = new Dictionary<int, uint[]>();
                var previous = -1;

                for (var i = 0; i < count; i++)
                {
                    var entry = file.AsSpan((int)directory + 2 + i * 12);
                    var tag = BinaryPrimitives.ReadUInt16LittleEndian(entry);
                    var type = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
                    var values = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);

                    Assert.True(tag > previous, "Tags in a directory must be in ascending order.");
                    previous = tag;

                    var size = type switch { 3 => 2, 4 => 4, 5 => 8, _ => 1 };
                    var data = (size * values <= 4
                        ? entry[8..12]
                        : file.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]), size * values)).ToArray();

                    tags[tag] = [.. Enumerable.Range(0, type == 5 ? values * 2 : values).Select(k => type switch
                    {
                        3 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(k * 2)),
                        _ => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(k * 4)),
                    })];
                }

                var width = (int)tags[256][0];
                var height = (int)tags[257][0];
                var samples = (int)tags[277][0];
                var bits = (int)tags[258][0];
                var rowBytes = bits == 1 ? (width + 7) / 8 : width * samples;
                var compression = tags[259][0];
                var predictor = tags.TryGetValue(317, out var p) ? p[0] : 1;
                var pixels = new MemoryStream();

                for (var s = 0; s < tags[273].Length; s++)
                {
                    var strip = file.AsSpan((int)tags[273][s], (int)tags[279][s]).ToArray();
                    var raw = compression switch
                    {
                        8 => Inflate(strip),
                        32773 => Unpack(strip, rowBytes),
                        1 => strip,
                        _ => throw new InvalidDataException($"compression {compression}"),
                    };

                    if (predictor == 2)
                    {
                        for (var row = 0; row < raw.Length; row += rowBytes)
                        {
                            for (var k = row + samples; k < row + rowBytes; k++)
                            {
                                raw[k] += raw[k - samples];
                            }
                        }
                    }

                    pixels.Write(raw);
                }

                Assert.Equal(rowBytes * height, pixels.Length);

                var dpi = tags[282][0] / (double)tags[282][1];
                pages.Add(new Page(width, height, samples, bits, (int)tags[262][0], pixels.ToArray(), dpi,
                    (int)tags[297][0], (int)tags[297][1]));

                directory = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)directory + 2 + count * 12));
            }

            return pages;
        }

        private static byte[] Inflate(byte[] data)
        {
            using var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>PackBits, one row at a time: a run that crossed into the next row is an error.</summary>
        private static byte[] Unpack(byte[] data, int rowBytes)
        {
            var output = new List<byte>();
            var row = new List<byte>(rowBytes);

            for (var i = 0; i < data.Length;)
            {
                var n = (sbyte)data[i++];

                if (n >= 0)
                {
                    row.AddRange(data.AsSpan(i, n + 1).ToArray());
                    i += n + 1;
                }
                else if (n != -128)
                {
                    row.AddRange(Enumerable.Repeat(data[i++], 1 - n));
                }

                Assert.True(row.Count <= rowBytes, "A PackBits run crossed a row boundary.");

                if (row.Count == rowBytes)
                {
                    output.AddRange(row);
                    row.Clear();
                }
            }

            Assert.Empty(row);
            return [.. output];
        }
    }
}
