// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.Core.Xml;
using PdfNet.Text;
using WordNet.Export;
using WordNet.Import;
using WordNet.Tables;
using Xunit;

namespace WordNet.Tests;

public class PdfImportTests
{
    /// <summary>
    /// Round-trips a document through PDF and back.
    /// </summary>
    /// <remarks>
    /// The only test that means anything here. Asserting on a hand-built PDF would test the
    /// heuristics against a page shaped to suit them; going out through this library's own exporter
    /// and back tests them against a real page laid out by a real layout engine.
    /// </remarks>
    private static WordDocument RoundTrip(WordDocument source, PdfImportOptions? options = null)
    {
        using var pdf = WordToPdf.Convert(source);
        return PdfToWord.Convert(pdf, options);
    }

    [Fact]
    public void ParagraphsComeBackAsParagraphs()
    {
        using var source = WordDocument.Create();

        source.AddParagraph(
            "Pendapatan naik tiga puluh dua persen dibanding kuartal sebelumnya, dengan " +
            "pertumbuhan terbesar datang dari wilayah Jakarta dan Bandung.");

        source.AddParagraph(
            "Biaya operasional turun tipis setelah konsolidasi gudang di Surabaya selesai.");

        using var result = RoundTrip(source);

        var paragraphs = result.Paragraphs.Where(p => p.Text.Trim().Length > 0).ToList();

        Assert.Equal(2, paragraphs.Count);
        Assert.StartsWith("Pendapatan naik", paragraphs[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("Biaya operasional", paragraphs[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrappedParagraphComesBackAsOneParagraph()
    {
        // A PDF line break is where the text ran out of column, not where the author put one.
        // Keeping them would turn every paragraph into a stack of short ones.
        using var source = WordDocument.Create();

        source.AddParagraph(string.Join(' ',
            Enumerable.Repeat("Kalimat panjang yang pasti membungkus lebih dari satu baris.", 6)));

        using var result = RoundTrip(source);

        var paragraph = Assert.Single(result.Paragraphs, p => p.Text.Trim().Length > 0);

        Assert.DoesNotContain('\n', paragraph.Text);
        Assert.Contains("Kalimat panjang", paragraph.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoTextIsEverLost()
    {
        // The promise the whole design rests on: structure the heuristics miss comes back as
        // paragraphs, and nothing disappears.
        using var source = WordDocument.Create();

        string[] sentences =
        [
            "Bagian pertama laporan.",
            "Bagian kedua, dengan angka 1.234 dan tanggal 17 Maret 2026.",
            "Bagian ketiga yang menutup.",
        ];

        foreach (var sentence in sentences)
        {
            source.AddParagraph(sentence);
        }

        using var result = RoundTrip(source);
        var text = result.ExtractText();

        Assert.All(sentences, sentence =>
            Assert.Contains(sentence, text, StringComparison.Ordinal));
    }

    [Fact]
    public void ATableComesBackAsATable()
    {
        using var source = WordDocument.Create();

        var table = source.AddTable(4, 3);

        table[0, 0].Text = "Kode";
        table[0, 1].Text = "Nama";
        table[0, 2].Text = "Harga";

        string[][] rows =
        [
            ["A100", "Kabel", "15000"],
            ["B200", "Adaptor", "45000"],
            ["C300", "Baterai", "27500"],
        ];

        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                table[row + 1, column].Text = rows[row][column];
            }
        }

        using var result = RoundTrip(source);

        var recovered = Assert.Single(result.Tables);

        Assert.Equal(4, recovered.Rows.Count);
        Assert.Equal("Kode", recovered[0, 0].Text.Trim());
        Assert.Equal("Baterai", recovered[3, 1].Text.Trim());
        Assert.Equal("45000", recovered[2, 2].Text.Trim());
    }

    [Fact]
    public void TablesCanBeLeftAsParagraphs()
    {
        // Some callers want the text and not the grid, and a wrongly-recognised table is worse than
        // no table at all when what you wanted was the words.
        using var source = WordDocument.Create();
        var table = source.AddTable(3, 2);

        for (var row = 0; row < 3; row++)
        {
            table[row, 0].Text = $"Kiri {row}";
            table[row, 1].Text = $"Kanan {row}";
        }

        using var result = RoundTrip(source, new PdfImportOptions { ConvertTables = false });

        Assert.Empty(result.Tables);
        Assert.Contains("Kanan 2", result.ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AHeadingComesBackAsAHeading()
    {
        // The only signal is type larger than the body around it, so the body has to be there for
        // the comparison to mean anything.
        using var source = WordDocument.Create();

        source.AddParagraph("Laporan Kuartal", "Heading1");

        for (var i = 0; i < 5; i++)
        {
            source.AddParagraph(
                "Isi laporan yang cukup panjang untuk menjadi ukuran badan teks pada halaman ini.");
        }

        using var result = RoundTrip(source);

        var first = result.Paragraphs.First(p => p.Text.Trim().Length > 0);

        Assert.Equal("Laporan Kuartal", first.Text.Trim());
        Assert.Equal("Heading1", first.StyleId);
    }

    [Fact]
    public void HeadingLevelsFollowTheirSizes()
    {
        // A PDF records type sizes, not outline levels: largest becomes Heading 1 and the next size
        // down Heading 2. Mapping every heading to one level throws away the outline.
        using var source = WordDocument.Create();

        source.AddParagraph("Judul Utama", "Heading1");
        source.AddParagraph("Isi bab pertama yang cukup panjang untuk menetapkan ukuran badan.");
        source.AddParagraph("Sub Bagian", "Heading2");
        source.AddParagraph("Isi bagian kedua yang juga cukup panjang untuk ikut dihitung.");
        source.AddParagraph("Sub Bagian Lain", "Heading2");
        source.AddParagraph("Isi bagian ketiga, sekali lagi cukup panjang untuk badan teks.");

        using var result = RoundTrip(source);

        var headings = result.Paragraphs
            .Where(p => p.StyleId?.StartsWith("Heading", StringComparison.Ordinal) == true)
            .Select(p => (p.Text.Trim(), p.StyleId))
            .ToList();

        Assert.Equal(
            [("Judul Utama", "Heading1"), ("Sub Bagian", "Heading2"), ("Sub Bagian Lain", "Heading2")],
            headings);
    }

    [Fact]
    public void PageBreaksAreKeptByDefaultAndCanBeDropped()
    {
        using var source = WordDocument.Create();

        source.AddParagraph("Halaman satu.");
        source.AddParagraph().AddPageBreak();
        source.AddParagraph("Halaman dua.");

        using var kept = RoundTrip(source);
        using var flowed = RoundTrip(source, new PdfImportOptions { KeepPageBreaks = false });

        Assert.Contains(kept.Paragraphs,
            p => p.Element.Descendants(OfficeNet.Core.Xml.Ns.W + "br")
                .Any(b => b.Attr(OfficeNet.Core.Xml.Ns.W + "type") == "page"));

        Assert.DoesNotContain(flowed.Paragraphs,
            p => p.Element.Descendants(OfficeNet.Core.Xml.Ns.W + "br")
                .Any(b => b.Attr(OfficeNet.Core.Xml.Ns.W + "type") == "page"));

        Assert.Contains("Halaman dua.", flowed.ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void APageRangeTakesOnlyThosePages()
    {
        using var source = WordDocument.Create();

        for (var i = 1; i <= 3; i++)
        {
            source.AddParagraph($"Halaman {i}.");

            if (i < 3)
            {
                source.AddParagraph().AddPageBreak();
            }
        }

        using var result = RoundTrip(source, new PdfImportOptions { Pages = 1..2 });
        var text = result.ExtractText();

        Assert.Contains("Halaman 2.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Halaman 1.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Halaman 3.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APdfWithNoTextGivesAnEmptyDocumentRatherThanThrowing()
    {
        // A scanned page has no text layer. An empty document is the honest answer; reading it needs
        // OCR, which is a different tool.
        using var pdf = PdfNet.Document.PdfDocument.Create();
        pdf.Pages.Add(PdfNet.Document.PageSize.A4);

        using var result = PdfToWord.Convert(pdf);

        Assert.Equal(string.Empty, result.ExtractText());
    }

    [Fact]
    public void TheImportedDocumentIsAValidDocx()
    {
        using var source = WordDocument.Create();

        source.AddParagraph("Judul", "Heading1");
        source.AddParagraph("Isi paragraf pertama yang cukup panjang untuk membungkus baris.");

        var table = source.AddTable(3, 2);

        for (var row = 0; row < 3; row++)
        {
            table[row, 0].Text = $"Kiri {row}";
            table[row, 1].Text = $"Kanan {row}";
        }

        using var result = RoundTrip(source);
        using var stream = new MemoryStream();

        result.Save(stream);

        var report = OfficeNet.TestKit.OpcValidator.Validate(stream.ToArray());
        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void TableDetectionCanBeTurnedOffEntirely()
    {
        using var source = WordDocument.Create();
        var table = source.AddTable(4, 3);

        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                table[row, column].Text = $"S{row}{column}";
            }
        }

        using var result = RoundTrip(source, new PdfImportOptions
        {
            Structure = new StructureOptions { DetectTables = false },
        });

        Assert.Empty(result.Tables);
    }

    [Fact]
    public void ThreeRowsIsTheThresholdForATable()
    {
        // Two lines that line up are usually a coincidence — a heading over a caption, say. Three is
        // the smallest number where the alignment is more likely to be a table.
        using var source = WordDocument.Create();
        var table = source.AddTable(2, 3);

        for (var column = 0; column < 3; column++)
        {
            table[0, column].Text = $"Judul {column}";
            table[1, column].Text = $"Nilai {column}";
        }

        using var lenient = RoundTrip(source, new PdfImportOptions
        {
            Structure = new StructureOptions { MinimumTableRows = 2 },
        });

        using var strict = RoundTrip(source);

        Assert.Single(lenient.Tables);
        Assert.Empty(strict.Tables);
    }
}
