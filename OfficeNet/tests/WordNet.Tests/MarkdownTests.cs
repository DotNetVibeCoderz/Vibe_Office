// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Xml.Linq;
using OfficeNet.Core.Markdown;
using OfficeNet.Core.Xml;
using WordNet.Export;
using WordNet.Import;
using Xunit;

namespace WordNet.Tests;

public class MarkdownTests
{
    private const string Report = """
        # Laporan Kuartal III

        Ringkasan **penjualan** dan *biaya* wilayah `Jawa`, dengan ~~angka lama~~.\
        Baris setelah hard break.

        ## Rincian

        3. Jakarta
        4. Bandung
           - Kota
           - Kabupaten

        | Wilayah | Q3 |
        | --- | ---: |
        | Jakarta | 1.480 |
        | Bandung \| Raya | 1.150 |

        > Angka bersifat ilustratif.
        >
        > Sumber: tim keuangan.

        ```csharp
        var x = 1 * 2;
        Console.WriteLine("**bukan tebal**");
        ```

        Lihat [situs kami](https://gravicode.com/laporan) atau https://www.example.org/a_b.

        ---

        snake_case_name tetap, 2*3=6, dan # bukan judul.
        """;

    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void MarkdownBecomesHeadingsListsATableAQuotationAndCode()
    {
        using var document = MarkdownToWord.CreateDocument(Report);
        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);
        var paragraphs = reopened.Paragraphs.Where(p => p.Text.Length > 0).ToList();

        Assert.Equal("Heading1", paragraphs[0].StyleId);
        Assert.Equal("Heading2", paragraphs.Single(p => p.Text == "Rincian").StyleId);

        Assert.Equal([0, 0, 1, 1], paragraphs.Where(p => p.ListLevel is not null).Select(p => p.ListLevel!.Value));

        var quotes = paragraphs.Where(p => p.StyleId == "Quote").Select(p => p.Text);
        Assert.Equal(["Angka bersifat ilustratif.", "Sumber: tim keuangan."], quotes);

        var code = paragraphs.Single(p => p.Text.StartsWith("var x", StringComparison.Ordinal));
        Assert.Equal("var x = 1 * 2;\nConsole.WriteLine(\"**bukan tebal**\");", code.Text);
        Assert.All(code.Runs.Where(r => r.Text.Trim().Length > 0), r => Assert.Equal("Consolas", r.Format.FontName));

        var table = Assert.Single(reopened.Tables);
        Assert.Equal("Bandung | Raya", table.Rows[2].Cells[0].Text);

        var first = paragraphs[1];
        Assert.Contains(first.Runs, r => r.Text == "penjualan" && r.Format.Bold == true);
        Assert.Contains(first.Runs, r => r.Text == "biaya" && r.Format.Italic == true);
        Assert.Contains(first.Runs, r => r.Text == "angka lama" && r.Format.Strike == true);
        Assert.Contains("\nBaris setelah hard break.", first.Text, StringComparison.Ordinal);

        Assert.Contains(paragraphs, p => p.Text == "snake_case_name tetap, 2*3=6, dan # bukan judul.");
    }

    [Fact]
    public void LinksAndBareUrlsBecomeHyperlinks()
    {
        using var document = MarkdownToWord.CreateDocument(Report);

        var targets = document.DocumentPart.Relationships
            .Where(r => r.Type.EndsWith("/hyperlink", StringComparison.Ordinal))
            .Select(r => r.Target);

        Assert.Equal(["https://gravicode.com/laporan", "https://www.example.org/a_b"], targets);
    }

    [Fact]
    public void APictureBesideTheMarkdownFileIsEmbedded()
    {
        var folder = Directory.CreateTempSubdirectory("md-");

        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "titik.png"),
                OfficeNet.TestKit.TestImages.SolidPng(4, 4, 200, 30, 30));
            File.WriteAllText(Path.Combine(folder.FullName, "laporan.md"), "Gambar:\n\n![Titik merah](titik.png)\n");

            using var document = MarkdownToWord.CreateDocumentFromFile(Path.Combine(folder.FullName, "laporan.md"));
            var bytes = Save(document);
            DocxValidator.AssertValid(bytes);

            using var archive = new ZipArchive(new MemoryStream(bytes));
            Assert.Contains(archive.Entries, e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));

            var docPr = XDocument.Load(archive.GetEntry("word/document.xml")!.Open()).Descendants(Ns.Wp + "docPr").Single();
            Assert.Equal("Titik merah", docPr.Attr("descr"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void ADocumentBuiltInWordIsWrittenAsMarkdown()
    {
        using var document = WordDocument.Create();
        document.AddHeading("Ringkasan", 1);

        var paragraph = document.AddParagraph("Penjualan ");
        paragraph.AddRun("naik").Format.Bold = true;
        paragraph.AddRun(" — lihat ");
        paragraph.AddHyperlink("laporan", "https://gravicode.com/laporan");

        document.AddList(["Jakarta", "Bandung"], numbered: true);
        document.AddList(["catatan"]);

        var table = document.AddTable(2, 2);
        table.Rows[0].Cells[0].Text = "Wilayah";
        table.Rows[0].Cells[1].Text = "Nilai";
        table.Rows[1].Cells[0].Text = "Jakarta";
        table.Rows[1].Cells[1].Text = "1.480";

        document.AddParagraph("Kutipan penting.", "Quote");

        var code = document.AddParagraph();
        code.AddRun("dotnet build").Format.FontName = "Consolas";

        Assert.Equal("""
            # Ringkasan

            Penjualan **naik** — lihat [laporan](https://gravicode.com/laporan)

            1. Jakarta
            2. Bandung

            - catatan

            | Wilayah | Nilai |
            | --- | --- |
            | Jakarta | 1.480 |

            > Kutipan penting.

            ```
            dotnet build
            ```

            """.ReplaceLineEndings("\n"), WordToMarkdown.Convert(document));
    }

    [Fact]
    public void MarkdownSurvivesARoundTripThroughWord()
    {
        using var first = MarkdownToWord.CreateDocument(Report);
        var once = WordToMarkdown.Convert(first);

        using var second = MarkdownToWord.CreateDocument(once);
        var twice = WordToMarkdown.Convert(second);

        // After one trip nothing changes any more.
        Assert.Equal(once, twice);

        // And the trip itself changes only what a Word document has nowhere to keep: a code
        // block's language and a column's alignment.
        Assert.Equal(WithoutWhatWordCannotHold(MarkdownToHtml.Convert(Report)), MarkdownToHtml.Convert(once));

        static string WithoutWhatWordCannotHold(string html) =>
            System.Text.RegularExpressions.Regex.Replace(html, " (?:class|align)=\"[^\"]*\"", string.Empty);
    }

    [Fact]
    public void QuotationsKeepTheirStyleAndLoseTheTagsItalicsInWord()
    {
        using var document = HtmlToWord.CreateDocument("<blockquote><p>satu</p><p>dua</p></blockquote>");

        Assert.All(document.Paragraphs, p => Assert.Equal("Quote", p.StyleId));
        Assert.All(document.Paragraphs.SelectMany(p => p.Runs), r => Assert.Null(r.Format.Italic));

        var html = WordToHtml.Convert(document, new WordHtmlOptions { FullDocument = false });
        Assert.Equal("<blockquote>\n<p>satu</p>\n<p>dua</p>\n</blockquote>\n", html);
    }

    [Fact]
    public void PlainTextIsOneParagraphPerLineAndNeverInterpreted()
    {
        using var document = TextToWord.CreateDocument("# bukan judul\r\n\r\nkolom\tdua\nakhir\n", "Consolas");
        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        Assert.Equal(["# bukan judul", "", "kolom\tdua", "akhir"], document.Paragraphs.Select(p => p.Text));
        Assert.All(document.Paragraphs, p => Assert.Null(p.StyleId));
        Assert.Equal("Consolas", document.Paragraphs[0].Runs[0].Format.FontName);

        using var archive = new ZipArchive(new MemoryStream(bytes));
        var body = XDocument.Load(archive.GetEntry("word/document.xml")!.Open());
        Assert.Single(body.Descendants(Ns.W + "tab"));
    }
}
