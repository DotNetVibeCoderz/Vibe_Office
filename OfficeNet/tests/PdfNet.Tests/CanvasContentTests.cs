// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using PdfNet.Content;
using PdfNet.Document;
using Xunit;

namespace PdfNet.Tests;

/// <summary>
/// Covers the assembly of a page's content stream from the canvas's buffer.
/// </summary>
/// <remarks>
/// The canvas builds its operators in a <see cref="StringBuilder"/> and writes them out as Latin-1
/// bytes. It used to do that by concatenating into a string and encoding the result, which made two
/// full copies of every page's content; it now narrows the builder's chunks straight into the
/// destination. A <see cref="StringBuilder"/> stores its text as a chain of chunks, so the risk in
/// writing it that way is entirely at the seams between them — a page whose content fits in one
/// chunk would never show the bug.
/// </remarks>
public class CanvasContentTests
{
    private static byte[] Draw(Action<PdfCanvas> draw)
    {
        using var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            draw(canvas);
        }

        using var stream = new MemoryStream();
        pdf.Save(stream);

        using var reopened = PdfDocument.Open(new MemoryStream(stream.ToArray(), writable: false));
        return reopened.Pages[0].GetContent();
    }

    [Fact]
    public void ContentSpanningManyBufferChunksSurvivesIntact()
    {
        // Far past the builder's initial 4 096 characters, so the text is spread over a chain of
        // chunks and every seam is exercised. Each line carries its own index, so a seam that
        // dropped or repeated a run of characters shows up as a missing or duplicated line rather
        // than as a length that happens to match.
        const int Lines = 900;

        var content = Encoding.Latin1.GetString(Draw(canvas =>
        {
            canvas.SetFont(StandardFont.Helvetica, 9);

            for (var i = 0; i < Lines; i++)
            {
                canvas.DrawText($"Baris nomor {i} dari laporan tahunan.", 40, 800 - (i % 90 * 8));
            }
        }));

        Assert.True(content.Length > 8192,
            $"The content is only {content.Length} bytes, which may fit in a single chunk.");

        for (var i = 0; i < Lines; i++)
        {
            var line = $"Baris nomor {i} dari laporan tahunan.";
            var first = content.IndexOf(line, StringComparison.Ordinal);

            Assert.True(first >= 0, $"Line {i} is missing from the content stream.");
            Assert.Equal(-1, content.IndexOf(line, first + line.Length, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheDrawingIsWrappedInSaveAndRestore()
    {
        // The wrapper is what stops a page leaking graphics state into whatever is appended after
        // it. It is written a byte at a time either side of the buffer, so it is the other thing
        // that a hand-assembled stream can lose.
        var content = Encoding.Latin1.GetString(Draw(canvas =>
        {
            canvas.SetFont(StandardFont.Helvetica, 12);
            canvas.DrawText("Laporan.", 56, 700);
        }));

        Assert.StartsWith("q\n", content, StringComparison.Ordinal);
        Assert.EndsWith("Q\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void HighCharactersAreNarrowedNotMangled()
    {
        // Content-stream text is Latin-1, and the encoding now happens chunk by chunk rather than
        // over one string. A character above 0x7F is where a wrong encoding stops being invisible:
        // encode as UTF-8 and each of these becomes two bytes, which reads as mojibake in a viewer.
        var text = "Ékonomi naïf ötökkä";

        var content = Encoding.Latin1.GetString(Draw(canvas =>
        {
            canvas.SetFont(StandardFont.Helvetica, 12);
            canvas.DrawText(text, 56, 700);
        }));

        Assert.Contains(text, content, StringComparison.Ordinal);
    }
}
