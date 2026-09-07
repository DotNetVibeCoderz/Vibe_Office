// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.TestKit;
using PdfNet.Content;
using PdfNet.Document;
using SkiaSharp;
using WordNet;
using Xunit;

namespace OfficeNet.Rendering.Tests;

/// <summary>
/// Tests for the rasteriser.
/// </summary>
/// <remarks>
/// A renderer cannot be checked by asserting on its own output structure — the output is a bitmap,
/// and "it produced 40 KB of PNG" says nothing about whether the page is right. These tests read
/// pixels back at known positions instead: a rectangle filled red at a known point in user space
/// must be red at the corresponding pixel, or the coordinate flip is wrong.
/// </remarks>
public class RenderingTests
{
    private static SKBitmap Render(PdfDocument document, RenderOptions? options = null)
    {
        var bytes = DocumentRenderer.RenderPage(document.Pages[0], options ?? new RenderOptions
        {
            Dpi = 72,
            DrawPageBorder = false,
        });

        return SKBitmap.Decode(bytes)
               ?? throw new InvalidOperationException("The renderer produced bytes Skia cannot decode.");
    }

    private static PdfDocument PageWith(Action<PdfCanvas> draw)
    {
        var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            canvas.TopDown = true;
            draw(canvas);
        }

        return document;
    }

    [Fact]
    public void PageSizeBecomesPixelsAtTheRequestedDpi()
    {
        using var document = PageWith(_ => { });

        // A4 is 595.28 x 841.89 points. At 72 DPI one point is one pixel.
        using var bitmap = Render(document);

        Assert.Equal(595, bitmap.Width);
        Assert.Equal(842, bitmap.Height);
    }

    [Fact]
    public void DoublingTheDpiDoublesThePixels()
    {
        using var document = PageWith(_ => { });

        using var bitmap = Render(document, new RenderOptions { Dpi = 144, MaxPixels = 0 });

        Assert.Equal(1191, bitmap.Width);
        Assert.Equal(1684, bitmap.Height);
    }

    [Fact]
    public void MaxPixelsScalesTheDpiDownRatherThanCropping()
    {
        using var document = PageWith(_ => { });

        using var bitmap = Render(document, new RenderOptions { Dpi = 600, MaxPixels = 1000 });

        Assert.Equal(1000, Math.Max(bitmap.Width, bitmap.Height));

        // The aspect ratio is what proves it scaled rather than cropped.
        Assert.Equal(595.28 / 841.89, (double)bitmap.Width / bitmap.Height, 2);
    }

    [Fact]
    public void AFilledRectangleLandsWhereItWasDrawn()
    {
        // The whole coordinate story in one test: PDF's origin is bottom-left and the raster's is
        // top-left, so a rectangle 100 points from the top must be red near the top of the image
        // and white near the bottom.
        using var document = PageWith(canvas =>
        {
            canvas.SetFillColor(OfficeColor.FromRgb(0xFF, 0x00, 0x00));
            canvas.Rectangle(100, 100, 200, 50).Fill();
        });

        using var bitmap = Render(document);

        var inside = bitmap.GetPixel(200, 125);
        Assert.Equal(0xFF, inside.Red);
        Assert.Equal(0x00, inside.Green);
        Assert.Equal(0x00, inside.Blue);

        // Just above the rectangle is background.
        var above = bitmap.GetPixel(200, 90);
        Assert.Equal(0xFF, above.Red);
        Assert.Equal(0xFF, above.Green);

        // The mirror position, measured from the bottom, must not be red — that is what fails when
        // the y axis is not flipped.
        var mirrored = bitmap.GetPixel(200, bitmap.Height - 125);
        Assert.False(mirrored is { Red: 0xFF, Green: 0x00, Blue: 0x00 });
    }

    [Fact]
    public void StrokesAreDrawnInTheirOwnColour()
    {
        using var document = PageWith(canvas =>
        {
            canvas.SetStrokeColor(OfficeColor.FromRgb(0x00, 0x00, 0xFF));
            canvas.SetLineWidth(6);
            canvas.MoveTo(50, 200).LineTo(500, 200).Stroke();
        });

        using var bitmap = Render(document);

        var onTheLine = bitmap.GetPixel(300, 200);
        Assert.True(onTheLine.Blue > 200, $"Expected a blue stroke, got {onTheLine}.");
        Assert.True(onTheLine.Red < 80, $"Expected a blue stroke, got {onTheLine}.");
    }

    [Fact]
    public void TextIsDrawnInItsFillColour()
    {
        // Text colour comes from the content stream's fill state, which the extractor does not
        // carry. Rendering every string black is the failure this guards.
        using var document = PageWith(canvas =>
        {
            canvas.SetFont(StandardFont.HelveticaBold, 60);
            canvas.SetFillColor(OfficeColor.FromRgb(0xFF, 0x00, 0x00));
            canvas.DrawText("IIIII", 60, 200);
        });

        using var bitmap = Render(document);

        var red = 0;
        var black = 0;

        for (var x = 55; x < 260; x++)
        {
            for (var y = 145; y < 205; y++)
            {
                var pixel = bitmap.GetPixel(x, y);

                if (pixel.Red > 150 && pixel.Green < 100 && pixel.Blue < 100)
                {
                    red++;
                }
                else if (pixel.Red < 100 && pixel.Green < 100 && pixel.Blue < 100)
                {
                    black++;
                }
            }
        }

        Assert.True(red > 100, $"Expected red glyphs; found {red} red and {black} black pixels.");
        Assert.True(red > black, $"Glyphs came out black: {red} red versus {black} black pixels.");
    }

    [Fact]
    public void AnEmptyPageRendersAsTheBackgroundColour()
    {
        using var document = PageWith(_ => { });

        var bytes = DocumentRenderer.RenderPage(document.Pages[0], new RenderOptions
        {
            Dpi = 72,
            Background = OfficeColor.FromRgb(0x20, 0x40, 0x60),
        });

        using var bitmap = SKBitmap.Decode(bytes);
        var pixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);

        Assert.Equal(0x20, pixel.Red);
        Assert.Equal(0x40, pixel.Green);
        Assert.Equal(0x60, pixel.Blue);
    }

    [Fact]
    public void AWordDocumentRendersOnePagePerPage()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Halaman satu");
        document.AddPageBreak();
        document.AddParagraph("Halaman dua");

        var pages = DocumentRenderer.RenderWord(document, new RenderOptions { Dpi = 50 });

        Assert.Equal(2, pages.Count);

        foreach (var page in pages)
        {
            // The PNG signature, so a failure says "not an image" rather than "wrong size".
            Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47], page[..4]);
        }
    }

    [Fact]
    public void RenderToFilesWritesOneNumberedFilePerPage()
    {
        using var directory = new TempDirectory();

        using (var document = WordDocument.Create())
        {
            document.AddParagraph("satu");
            document.AddPageBreak();
            document.AddParagraph("dua");
            document.Save(Path.Combine(directory.Path, "source.docx"));
        }

        var written = DocumentRenderer.RenderToFiles(
            Path.Combine(directory.Path, "source.docx"),
            directory.Path,
            new RenderOptions { Dpi = 50 },
            "page");

        Assert.Equal(2, written.Count);
        Assert.All(written, path => Assert.True(File.Exists(path)));
        Assert.EndsWith("page-01.png", written[0], StringComparison.Ordinal);
        Assert.EndsWith("page-02.png", written[1], StringComparison.Ordinal);
    }

    [Fact]
    public void AThumbnailIsCappedToTheRequestedWidth()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "source.docx");

        using (var document = WordDocument.Create())
        {
            document.AddParagraph("Halo");
            document.Save(path);
        }

        var bytes = DocumentRenderer.RenderThumbnail(path, 240);

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(240, bitmap.Width);
    }

    [Fact]
    public void AnUnsupportedExtensionSaysSoRatherThanReturningNothing()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "notes.txt");
        File.WriteAllText(path, "hello");

        var exception = Assert.Throws<OfficeNetException>(
            () => DocumentRenderer.Render(path));

        Assert.Contains(".txt", exception.Message, StringComparison.Ordinal);
    }
}
