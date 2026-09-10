// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.RegularExpressions;
using OfficeNet.Core.Drawing;
using WordNet.Export;
using WordNet.Import;
using Xunit;

namespace WordNet.Tests;

/// <summary>
/// Covers writing a document out as HTML.
/// </summary>
/// <remarks>
/// <para>
/// The export goes through the same block model as the import, so the round-trip cases at the end
/// are the strongest evidence here: a page taken into Word and back out has passed through one
/// description of what a heading, a list and a link are, and anything that comes out different was
/// lost in one of the two mappings.
/// </para>
/// <para>
/// Every output is also checked for balance. A list writer that forgets to close an item produces
/// HTML every browser repairs silently — and repairs differently.
/// </para>
/// </remarks>
public class WordToHtmlTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static string Body(WordDocument document, bool embedImages = true)
    {
        var html = WordToHtml.Convert(document,
            new WordHtmlOptions { FullDocument = false, EmbedImages = embedImages });

        AssertBalanced(html);
        return html;
    }

    private static int Count(string html, string token) =>
        Regex.Matches(html, Regex.Escape(token)).Count;

    private static void AssertBalanced(string html)
    {
        foreach (var tag in new[] { "ul", "ol", "li", "p", "table", "tr", "th", "td", "strong", "em", "a", "sup" })
        {
            var opened = Regex.Matches(html, "<" + tag + "[ >]").Count;
            var closed = Count(html, "</" + tag + ">");

            Assert.True(opened == closed, $"<{tag}> is opened {opened} times and closed {closed}:\n{html}");
        }
    }

    [Fact]
    public void HeadingsComeFromTheirStyles()
    {
        using var document = WordDocument.Create();
        document.AddHeading("Judul", 0);
        document.AddHeading("Bab", 1);
        document.AddHeading("Subbab", 2);

        var html = Body(document);

        Assert.Contains("<h1>Judul</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Bab</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Subbab</h2>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineFormattingBecomesElementsAndStyles()
    {
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph();
        paragraph.AddRun("tebal").Format.Bold = true;
        paragraph.AddRun("miring").Format.Italic = true;
        paragraph.AddRun("garis").Format.Underline = UnderlineStyle.Single;
        paragraph.AddRun("coret").Format.Strike = true;
        paragraph.AddRun("merah").Format.Color = OfficeColor.FromRgb(0xC0, 0x39, 0x2B);
        paragraph.AddRun("2").Format.VerticalAlignment = VerticalAlignment.Superscript;

        var html = Body(document);

        Assert.Contains("<strong>tebal</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<em>miring</em>", html, StringComparison.Ordinal);
        Assert.Contains("<u>garis</u>", html, StringComparison.Ordinal);
        Assert.Contains("<s>coret</s>", html, StringComparison.Ordinal);
        Assert.Contains("color:#C0392B", html, StringComparison.Ordinal);
        Assert.Contains("<sup>2</sup>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AHyperlinkKeepsItsTargetEscaped()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Lihat ").AddHyperlink("rincian", "https://example.com/a?x=1&y=2");

        var html = Body(document);

        Assert.Contains("href=\"https://example.com/a?x=1&amp;y=2\"", html, StringComparison.Ordinal);
        Assert.Contains(">rincian</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AScriptLinkKeepsItsTextAndLosesItsLink()
    {
        // HTML written from a document may be served. A javascript: link in a .docx is harmless in
        // Word and an injection vector in a browser.
        using var document = WordDocument.Create();
        document.AddParagraph("Tautan: ").AddHyperlink("klik", "javascript:alert(1)");

        var html = Body(document);

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("klik", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BulletsAndNumbersBecomeUnorderedAndOrderedLists()
    {
        using var document = WordDocument.Create();

        var bullets = document.Numbering.AddBulletList();
        document.AddParagraph("Butir").SetListItem(bullets);

        var numbers = document.Numbering.AddNumberedList();
        document.AddParagraph("Nomor").SetListItem(numbers);

        var html = Body(document);

        Assert.Contains("<ul>\n<li>Butir</li>", html, StringComparison.Ordinal);
        Assert.Contains("<ol>\n<li>Nomor</li>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedListSitsInsideTheItemItHangsFrom()
    {
        // HTML nests a list inside an li, not beside it. A nested ul directly inside a ul is invalid
        // and browsers disagree about where it goes.
        using var document = WordDocument.Create();

        var bullets = document.Numbering.AddBulletList();
        document.AddParagraph("Butir").SetListItem(bullets);
        document.AddParagraph("Anak").SetListItem(bullets, 1);
        document.AddParagraph("Lagi").SetListItem(bullets);

        var html = Body(document);

        Assert.Contains("<li>Butir<ul>\n<li>Anak</li>\n</ul>\n</li>", html, StringComparison.Ordinal);
        Assert.Equal(2, Count(html, "<ul>"));
    }

    [Fact]
    public void AnInterruptedNumberedListResumesAtTheRightNumber()
    {
        // Word counts by definition, so a paragraph between two items does not restart the list.
        // HTML has to be told, or the third item comes out as 1.
        using var document = WordDocument.Create();

        var numbers = document.Numbering.AddNumberedList();
        document.AddParagraph("Pertama").SetListItem(numbers);
        document.AddParagraph("Kedua").SetListItem(numbers);
        document.AddParagraph("Paragraf yang menyela.");
        document.AddParagraph("Ketiga").SetListItem(numbers);

        var html = Body(document);

        Assert.Contains("<ol start=\"3\">\n<li>Ketiga</li>", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, "<ol>"));
    }

    [Fact]
    public void TwoSeparateListsEachStartAtOne()
    {
        using var document = WordDocument.Create();

        var first = document.Numbering.AddNumberedList();
        document.AddParagraph("Satu").SetListItem(first);

        var second = document.Numbering.AddNumberedList();
        document.AddParagraph("Lagi dari satu").SetListItem(second);

        var html = Body(document);

        Assert.Equal(2, Count(html, "<ol>"));
        Assert.DoesNotContain("start=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableKeepsItsHeaderAndEscapesItsCells()
    {
        using var document = WordDocument.Create();
        document.AddTable([["Wilayah", "Nilai"], ["<b>&</b>", "980"]]);

        var html = Body(document);

        Assert.Contains("<th>Wilayah</th>", html, StringComparison.Ordinal);
        Assert.Contains("<td>&lt;b&gt;&amp;&lt;/b&gt;</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void APictureIsEmbeddedWithItsAltText()
    {
        using var document = WordDocument.Create();
        document.AddPicture(Png, altText: "Grafik pendapatan");

        var html = Body(document);

        Assert.Contains("src=\"data:image/png;base64,", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"Grafik pendapatan\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutEmbeddingAPictureBecomesItsAltText()
    {
        using var document = WordDocument.Create();
        document.AddPicture(Png, altText: "Grafik pendapatan");

        var html = Body(document, embedImages: false);

        Assert.DoesNotContain("data:image", html, StringComparison.Ordinal);
        Assert.Contains("<em>Grafik pendapatan</em>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AFullDocumentHasADoctypeAndATitle()
    {
        using var document = WordDocument.Create();
        document.AddHeading("Laporan Tahunan", 1);

        var html = WordToHtml.Convert(document);

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<title>Laporan Tahunan</title>", html, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void APageSurvivesTheTripIntoWordAndBackOut()
    {
        const string Source = """
            <h1>Judul</h1>
            <p>Teks <b>tebal</b>, H<sub>2</sub>O dan x<sup>2</sup>, <a href="https://example.com">tautan</a>.</p>
            <ol><li>Satu</li><li>Dua<ul><li>Anak</li></ul></li></ol>
            <ol><li>Daftar baru</li></ol>
            <table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
            <hr>
            <p style="text-align:center">Tengah</p>
            """;

        using var document = HtmlToWord.CreateDocument(Source);
        var html = Body(document);

        Assert.Contains("<h1>Judul</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>tebal</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<sub>2</sub>", html, StringComparison.Ordinal);
        Assert.Contains("<sup>2</sup>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com\"", html, StringComparison.Ordinal);
        Assert.Contains("<li>Dua<ul>\n<li>Anak</li>", html, StringComparison.Ordinal);
        Assert.Contains("<th>A</th>", html, StringComparison.Ordinal);
        Assert.Contains("<hr>", html, StringComparison.Ordinal);
        Assert.Contains("text-align:center", html, StringComparison.Ordinal);

        // Two lists in, two lists out, and the second starts at one: the fix to the importer and
        // the counter in the exporter have to agree for this to hold.
        Assert.Equal(2, Count(html, "<ol>"));
        Assert.DoesNotContain("start=", html, StringComparison.Ordinal);
    }
}
