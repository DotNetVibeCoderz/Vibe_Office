// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using OfficeNet.Core.Drawing;
using PdfNet.Document;
using PdfNet.Objects;
using SkiaSharp;
using Xunit;

namespace OfficeNet.Rendering.Tests;

/// <summary>
/// Covers clipping paths and gradients, which the renderer used to ignore entirely.
/// </summary>
/// <remarks>
/// <para>
/// Both failures were of the kind a thumbnail hides: ignoring a clip draws content that the file
/// said to hide, and ignoring a gradient leaves a shape unpainted or flooded with whatever colour
/// was last set. Neither shows up in a test that asserts on structure, so these look at pixels.
/// </para>
/// <para>
/// Colours are checked at named points rather than by comparing whole images, so a failure says
/// which part of the page is wrong instead of that something is.
/// </para>
/// </remarks>
public class ClipAndGradientTests
{
    private const int Side = 300;

    private static SKBitmap Render(Action<PdfPage> build)
    {
        using var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add(PdfRectangle.FromSize(0, 0, Side, Side));

        build(page);

        using var stream = new MemoryStream();
        pdf.Save(stream);

        using var reopened = PdfDocument.Open(new MemoryStream(stream.ToArray(), writable: false));

        // 72 DPI so one PDF point is one pixel and the sample coordinates are the page's own.
        return SKBitmap.Decode(DocumentRenderer.RenderPage(reopened.Pages[0],
            new RenderOptions { Dpi = 72 }));
    }

    /// <summary>Samples a page point, converting from PDF's bottom-left origin.</summary>
    private static SKColor At(SKBitmap bitmap, int x, int yFromBottom) =>
        bitmap.GetPixel(x, Side - yFromBottom);

    private static bool IsWhite(SKColor c) => c.Red > 240 && c.Green > 240 && c.Blue > 240;

    private static PdfDictionary RedToBlue() => new()
    {
        [PdfName.Get("FunctionType")] = new PdfNumber(2),
        [PdfName.Get("Domain")] = new PdfArray(0, 1),
        [PdfName.Get("C0")] = new PdfArray(1, 0, 0),
        [PdfName.Get("C1")] = new PdfArray(0, 0, 1),
        [PdfName.Get("N")] = new PdfNumber(1),
    };

    [Fact]
    public void AClipHidesTheHalfOfAShapeOutsideIt()
    {
        using var bitmap = Render(page =>
        {
            using var canvas = page.OpenCanvas();

            canvas.Save();
            canvas.Rectangle(20, 20, 100, 200).Clip();
            canvas.SetFillColor(OfficeColor.FromRgb(200, 30, 30));
            canvas.Rectangle(20, 20, 260, 200).Fill();
            canvas.Restore();
        });

        // Inside the clip the square is painted; outside it the page stays blank.
        Assert.True(At(bitmap, 60, 120).Red > 150, "The clipped shape did not paint inside its clip.");
        Assert.True(IsWhite(At(bitmap, 220, 120)), "The shape painted outside its clip.");
    }

    [Fact]
    public void RestoringTheStateWidensTheClipAgain()
    {
        // A clip only narrows; it widens only by restoring. A renderer that leaves the clip in place
        // after Q loses everything drawn afterwards, which looks like the content is missing rather
        // than like a clipping bug.
        using var bitmap = Render(page =>
        {
            using var canvas = page.OpenCanvas();

            canvas.Save();
            canvas.Rectangle(20, 200, 60, 60).Clip();
            canvas.SetFillColor(OfficeColor.FromRgb(200, 30, 30));
            canvas.Rectangle(20, 200, 60, 60).Fill();
            canvas.Restore();

            canvas.SetFillColor(OfficeColor.FromRgb(30, 30, 200));
            canvas.Rectangle(20, 40, 260, 40).Fill();
        });

        Assert.True(At(bitmap, 150, 60).Blue > 150,
            "The bar drawn after Q was cut away, so the clip outlived its state.");
    }

    [Fact]
    public void NestedClipsIntersectRatherThanReplace()
    {
        // The inner clip must not reveal what the outer one hid. Replacing rather than intersecting
        // produces a page that looks laid out wrongly rather than clipped wrongly.
        using var bitmap = Render(page =>
        {
            using var canvas = page.OpenCanvas();

            canvas.Save();
            canvas.Rectangle(20, 20, 100, 260).Clip();   // left column
            canvas.Rectangle(20, 20, 260, 100).Clip();   // bottom row
            canvas.SetFillColor(OfficeColor.FromRgb(200, 30, 30));
            canvas.Rectangle(20, 20, 260, 260).Fill();
            canvas.Restore();
        });

        // Only the overlap of the two is painted.
        Assert.True(At(bitmap, 60, 60).Red > 150, "The overlap of the two clips was not painted.");
        Assert.True(IsWhite(At(bitmap, 60, 200)), "The second clip replaced the first instead of narrowing it.");
        Assert.True(IsWhite(At(bitmap, 220, 60)), "The second clip did not apply.");
    }

    [Fact]
    public void AnAxialShadingRunsFromOneColourToTheOther()
    {
        using var bitmap = Render(page =>
        {
            var shading = new PdfDictionary
            {
                [PdfName.Get("ShadingType")] = new PdfNumber(2),
                [PdfName.Get("ColorSpace")] = PdfName.Get("DeviceRGB"),
                [PdfName.Get("Coords")] = new PdfArray(20, 0, 280, 0),
                [PdfName.Get("Function")] = RedToBlue(),
                [PdfName.Get("Extend")] = new PdfArray([PdfBoolean.True, PdfBoolean.True]),
            };

            page.Resources[PdfName.Get("Shading")] =
                new PdfDictionary { [PdfName.Get("S0")] = shading };

            page.AppendContent(Encoding.Latin1.GetBytes("q 20 100 260 100 re W n /S0 sh Q\n"));
        });

        var left = At(bitmap, 30, 150);
        var right = At(bitmap, 270, 150);

        Assert.True(left.Red > 180 && left.Blue < 80, $"The left end is {left}, not red.");
        Assert.True(right.Blue > 180 && right.Red < 80, $"The right end is {right}, not blue.");

        // And it is clipped to the band the file asked for.
        Assert.True(IsWhite(At(bitmap, 150, 250)), "The shading painted above its clip.");
        Assert.True(IsWhite(At(bitmap, 150, 50)), "The shading painted below its clip.");
    }

    [Fact]
    public void ARadialShadingWithoutExtendStopsAtItsOuterCircle()
    {
        using var bitmap = Render(page =>
        {
            var shading = new PdfDictionary
            {
                [PdfName.Get("ShadingType")] = new PdfNumber(3),
                [PdfName.Get("ColorSpace")] = PdfName.Get("DeviceRGB"),
                [PdfName.Get("Coords")] = new PdfArray(150, 150, 0, 150, 150, 70),
                [PdfName.Get("Function")] = RedToBlue(),
                [PdfName.Get("Extend")] = new PdfArray([PdfBoolean.False, PdfBoolean.False]),
            };

            page.Resources[PdfName.Get("Shading")] =
                new PdfDictionary { [PdfName.Get("S0")] = shading };

            page.AppendContent(Encoding.Latin1.GetBytes("q /S0 sh Q\n"));
        });

        Assert.True(At(bitmap, 150, 150).Red > 180, "The centre of the radial is not the first colour.");
        Assert.True(At(bitmap, 150, 210).Blue > 150, "The rim of the radial is not the last colour.");

        // Extend false means the colour stops rather than flooding the page.
        Assert.True(IsWhite(At(bitmap, 20, 20)),
            "The radial painted beyond its outer circle, so Extend was ignored.");
    }

    [Fact]
    public void AShadingPatternFillIsBoundedByThePathItFills()
    {
        // A fill in the /Pattern colour space names a pattern instead of carrying colour
        // components. Painting it as a flat colour floods the shape with whatever was last set.
        using var bitmap = Render(page =>
        {
            var pattern = new PdfDictionary
            {
                [PdfName.Get("PatternType")] = new PdfNumber(2),
                [PdfName.Get("Shading")] = new PdfDictionary
                {
                    [PdfName.Get("ShadingType")] = new PdfNumber(2),
                    [PdfName.Get("ColorSpace")] = PdfName.Get("DeviceRGB"),
                    [PdfName.Get("Coords")] = new PdfArray(60, 0, 240, 0),
                    [PdfName.Get("Function")] = RedToBlue(),
                    [PdfName.Get("Extend")] = new PdfArray([PdfBoolean.True, PdfBoolean.True]),
                },
            };

            page.Resources[PdfName.Get("Pattern")] =
                new PdfDictionary { [PdfName.Get("P0")] = pattern };

            page.AppendContent(Encoding.Latin1.GetBytes(
                "q /Pattern cs /P0 scn 60 120 180 60 re f Q\n"));
        });

        var left = At(bitmap, 70, 150);
        var right = At(bitmap, 230, 150);

        Assert.True(left.Red > 150 && left.Blue < 110, $"The fill's left end is {left}, not red.");
        Assert.True(right.Blue > 150 && right.Red < 110, $"The fill's right end is {right}, not blue.");

        // The gradient stops at the edge of the rectangle it filled.
        Assert.True(IsWhite(At(bitmap, 150, 250)), "The pattern painted outside the filled path.");
        Assert.True(IsWhite(At(bitmap, 20, 150)), "The pattern painted left of the filled path.");
    }
}
