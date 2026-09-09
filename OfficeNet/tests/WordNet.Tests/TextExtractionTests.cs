// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Xunit;

namespace WordNet.Tests;

/// <summary>
/// Pins what extracting a document's text produces, character for character.
/// </summary>
/// <remarks>
/// <para>
/// Extraction walks the body's elements directly rather than through the <c>Paragraphs</c> and
/// <c>Runs</c> properties, because those materialise a fresh list and a fresh wrapper on every
/// access — 2.4 MB for a ten-thousand-paragraph document, paid once per paragraph, on top of a
/// string per paragraph.
/// </para>
/// <para>
/// Walking elements means restating which elements count, and that is exactly where such a rewrite
/// goes quietly wrong: a run inside a hyperlink is text, a tab is a tab, a break is a newline, and
/// the two kinds of hyphen are characters rather than markup. Prose exercises none of them.
/// </para>
/// </remarks>
public class TextExtractionTests
{
    private static WordDocument Sample()
    {
        var document = WordDocument.Create();

        document.AddParagraph("Paragraf biasa.");
        document.AddParagraph(string.Empty);

        var link = document.AddParagraph("Lihat ");
        link.AddHyperlink("situs resmi", "https://example.com");
        link.AddRun(" untuk rinciannya.");

        var breaks = document.AddParagraph();
        breaks.AddRun("Sebelum");
        breaks.AddRun("\tsesudah tab");
        breaks.AddRun("\nbaris baru");

        document.AddParagraph("non‑breaking dan soft­hyphen.");

        document.AddTable([
            ["Wilayah", "Kuartal", "Nilai"],
            ["Jakarta", "Q1", "1.250"],
        ]);

        document.AddParagraph("Setelah tabel.");

        return document;
    }

    [Fact]
    public void ExtractingADocumentProducesExactlyThisText()
    {
        using var document = Sample();

        var expected =
            "Paragraf biasa.\n" +
            "\n" +
            "Lihat situs resmi untuk rinciannya.\n" +
            "Sebelum\tsesudah tab\nbaris baru\n" +
            "non‑breaking dan soft­hyphen.\n" +
            "Wilayah\tKuartal\tNilai\n" +
            "Jakarta\tQ1\t1.250\n" +
            "\n" +
            "Setelah tabel.";

        Assert.Equal(expected, document.ExtractText());
    }

    [Fact]
    public void AParagraphReportsTheSameTextTheDocumentDoes()
    {
        // The two walks are separate code, and a document that extracts correctly while its
        // paragraphs disagree is the shape this would fail in.
        using var document = Sample();

        var perParagraph = document.Paragraphs.Select(p => p.Text).ToList();

        Assert.Equal("Paragraf biasa.", perParagraph[0]);
        Assert.Equal(string.Empty, perParagraph[1]);
        Assert.Equal("Lihat situs resmi untuk rinciannya.", perParagraph[2]);
        Assert.Equal("Sebelum\tsesudah tab\nbaris baru", perParagraph[3]);
        Assert.Equal("non‑breaking dan soft­hyphen.", perParagraph[4]);
    }

    [Fact]
    public void TextInsideAHyperlinkIsExtractedAndTextOutsideARunIsNot()
    {
        // A hyperlink wraps its runs in an element of its own, so a walk that only looks at w:r
        // children of the paragraph silently drops every link's text. This is the single most
        // likely thing to lose.
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph();
        paragraph.AddHyperlink("laporan tahunan", "https://example.com/laporan");

        Assert.Equal("laporan tahunan", paragraph.Text);
        Assert.Equal("laporan tahunan", document.ExtractText());
    }

    [Fact]
    public void ExtractionSurvivesARoundTripThroughTheFile()
    {
        // Reading back is the case the walk exists for, and a document that was parsed rather than
        // built has its runs arranged by Word's rules rather than by this library's.
        using var built = Sample();
        using var stream = new MemoryStream();

        built.Save(stream);

        using var reopened = WordDocument.Open(new MemoryStream(stream.ToArray(), writable: false));

        Assert.Equal(built.ExtractText(), reopened.ExtractText());
    }
}
