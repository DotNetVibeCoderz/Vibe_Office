// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Rendering;
using OfficeNet.TestKit;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Objects;
using PdfNet.Text;
using SkiaSharp;
using Xunit;

namespace OfficeNet.Rendering.Tests;

/// <summary>
/// Covers drawing a page with the font the file carries rather than a system substitute.
/// </summary>
/// <remarks>
/// <para>
/// Substitution matches a PDF font by name against whatever is installed. For Latin that is close
/// enough for a thumbnail; for anything the substitute does not cover it produces empty boxes, and
/// every subset this library writes for a non-Latin script hits that.
/// </para>
/// <para>
/// The font here is built by <see cref="SyntheticFont"/> rather than taken from the machine, so
/// what these assert does not depend on which fonts a build agent happens to have — which is the
/// whole point, since the bug being fixed is precisely about the machine's fonts.
/// </para>
/// </remarks>
public class EmbeddedFontRenderingTests
{
    private const string Message = "AXOLOTL";

    private static byte[] BuildPdf(bool embed)
    {
        using var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add(PdfRectangle.FromSize(0, 0, 300, 120));

        using (var canvas = page.OpenCanvas())
        {
            if (embed)
            {
                canvas.SetFont(pdf.EmbedFont(SyntheticFont.Build()), 36);
            }
            else
            {
                canvas.SetFont(StandardFont.Helvetica, 36);
            }

            canvas.DrawText(Message, 20, 50);
        }

        using var stream = new MemoryStream();
        pdf.Save(stream);
        return stream.ToArray();
    }

    private static PdfDocument Open(byte[] bytes) =>
        PdfDocument.Open(new MemoryStream(bytes, writable: false));

    [Fact]
    public void AnEmbeddedFontIsReportedWithItsGlyphsAndTheirPositions()
    {
        // The extractor is where this has to start: a renderer cannot draw the file's own glyphs
        // unless the ids and their offsets survive extraction, because a subset carries no cmap and
        // there is no way back from the characters.
        using var document = Open(BuildPdf(embed: true));

        var fragment = TextExtractor.ExtractFragments(document.Pages[0])
            .Single(f => f.Text.Contains('A', StringComparison.Ordinal));

        Assert.NotNull(fragment.Font);
        Assert.True(fragment.Font!.HasLoadableProgram,
            "The font descriptor carries no loadable program, so rendering must substitute.");

        Assert.NotNull(fragment.Glyphs);
        Assert.Equal(Message.Length, fragment.Glyphs!.Length);

        Assert.NotNull(fragment.GlyphOffsets);
        Assert.Equal(Message.Length, fragment.GlyphOffsets!.Length);

        // Offsets run along the baseline from the fragment's own origin, so the first is zero and
        // they only ever increase.
        Assert.Equal(0, fragment.GlyphOffsets[0]);

        for (var i = 1; i < fragment.GlyphOffsets.Length; i++)
        {
            Assert.True(fragment.GlyphOffsets[i] > fragment.GlyphOffsets[i - 1],
                $"Glyph {i} is placed at {fragment.GlyphOffsets[i]}, not after {fragment.GlyphOffsets[i - 1]}.");
        }

        // The last glyph starts inside the run, never past its right edge.
        Assert.True(fragment.GlyphOffsets[^1] < fragment.Width);
    }

    [Fact]
    public void AStandardFontReportsNoGlyphsBecauseThereIsNoProgramToIndex()
    {
        // The other side of the same rule. A standard-14 font embeds nothing, so there is nothing to
        // index into and the renderer has to substitute — collecting ids would be pretending.
        using var document = Open(BuildPdf(embed: false));

        var fragment = TextExtractor.ExtractFragments(document.Pages[0])
            .Single(f => f.Text.Contains('A', StringComparison.Ordinal));

        Assert.Null(fragment.Glyphs);
        Assert.Null(fragment.GlyphOffsets);
        Assert.False(fragment.Font?.HasLoadableProgram ?? false);
    }

    [Fact]
    public void TheRenderedPageUsesTheEmbeddedFaceRatherThanASubstitute()
    {
        // Pixels, because that is the claim — but "the two images differ" is not enough to make it.
        // Substituting for a synthetic font and substituting for Helvetica also gives two different
        // images, so that assertion passes with the embedding switched off entirely.
        //
        // What separates them is shape. SyntheticFont's glyphs are solid rectangles, so drawn with
        // the file's own face they fill about two thirds of the text's bounding box; drawn with any
        // real face, letters fill about a quarter. Nothing but using the embedded program puts the
        // number above a half.
        var embedded = FillRatio(DocumentRenderer.RenderPage(Open(BuildPdf(embed: true)).Pages[0],
            new RenderOptions { Dpi = 96 }));

        var substituted = FillRatio(DocumentRenderer.RenderPage(Open(BuildPdf(embed: false)).Pages[0],
            new RenderOptions { Dpi = 96 }));

        Assert.True(embedded > 0.5,
            $"The text fills {embedded:P1} of its box, which is letters rather than the font's own " +
            "rectangles — the embedded program was not used.");

        Assert.True(substituted < 0.4,
            $"Helvetica filled {substituted:P1} of its box, so this comparison is measuring " +
            "something other than glyph shape.");
    }

    [Fact]
    public void TheEmbeddedTextIsActuallyDrawnAndNotLeftBlank()
    {
        // The failure this replaces put empty boxes on the page; the failure a fix like it can
        // introduce is putting nothing there at all. Both are caught by asking how much ink landed.
        var png = DocumentRenderer.RenderPage(Open(BuildPdf(embed: true)).Pages[0],
            new RenderOptions { Dpi = 96 });

        var ink = InkFingerprint(png);

        Assert.True(ink > 200, $"Only {ink} pixels were painted, so the text did not render.");
    }

    /// <summary>How much of the inked area's bounding box is actually inked.</summary>
    /// <remarks>
    /// A shape measure rather than a size measure: it does not care how large the text is or where
    /// it sits, only whether the marks are solid blocks or letterforms.
    /// </remarks>
    private static double FillRatio(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, ink = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red >= 128)
                {
                    continue;
                }

                ink++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        if (ink == 0)
        {
            return 0;
        }

        return ink / (double)((maxX - minX + 1) * (maxY - minY + 1));
    }

    /// <summary>Counts the pixels that are not the page's background.</summary>
    private static int InkFingerprint(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);

        var count = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
