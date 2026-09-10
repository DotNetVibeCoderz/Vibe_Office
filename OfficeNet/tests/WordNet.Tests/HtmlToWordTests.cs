// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Xml.Linq;
using WordNet.Import;
using Xunit;

namespace WordNet.Tests;

/// <summary>
/// Covers converting HTML into WordprocessingML.
/// </summary>
/// <remarks>
/// <para>
/// These read the saved package's XML rather than the library's own object model. A document this
/// library writes and then reads back agrees with itself by construction; the question worth asking
/// is what is actually in the file Word will open.
/// </para>
/// <para>
/// Every case also runs the structural validator, because an importer is exactly where an
/// out-of-order property or a dangling relationship gets introduced: it writes shapes of document
/// that nobody wrote by hand.
/// </para>
/// </remarks>
public class HtmlToWordTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static byte[] Convert(string html)
    {
        using var document = HtmlToWord.CreateDocument(html);
        using var stream = new MemoryStream();

        document.Save(stream);

        var bytes = stream.ToArray();
        var problems = DocxValidator.Validate(bytes);

        Assert.True(problems.Count == 0,
            "The converted document is structurally invalid:\n" + string.Join("\n", problems));

        return bytes;
    }

    private static XDocument Part(byte[] docx, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(docx, writable: false), ZipArchiveMode.Read);
        using var entry = archive.GetEntry(name)!.Open();

        return XDocument.Load(entry);
    }

    private static List<XElement> Paragraphs(byte[] docx) =>
        Part(docx, "word/document.xml").Descendants(W + "body").Elements(W + "p").ToList();

    private static string TextOf(XElement paragraph) =>
        string.Concat(paragraph.Descendants(W + "t").Select(t => t.Value));

    private static string? StyleOf(XElement paragraph) =>
        paragraph.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;

    private static (int? NumId, int? Level) ListOf(XElement paragraph)
    {
        var numbering = paragraph.Element(W + "pPr")?.Element(W + "numPr");

        return (
            (int?)numbering?.Element(W + "numId")?.Attribute(W + "val"),
            (int?)numbering?.Element(W + "ilvl")?.Attribute(W + "val"));
    }

    [Fact]
    public void HeadingsBecomeHeadingStylesAtTheirOwnLevel()
    {
        var docx = Convert("<h1>Laporan</h1><h2>Rincian</h2><h3>Catatan</h3>");

        var styles = Paragraphs(docx).Select(StyleOf).ToList();

        Assert.Equal(["Heading1", "Heading2", "Heading3"], styles);
    }

    [Fact]
    public void InlineFormattingSurvivesRunForRun()
    {
        var docx = Convert(
            "<p>Biasa <b>tebal</b> <i>miring</i> <u>garis</u> <s>coret</s> " +
            "<span style=\"color:#C0392B\">merah</span></p>");

        var runs = Paragraphs(docx).Single().Elements(W + "r").ToList();

        XElement RunWith(string text) =>
            runs.Single(r => string.Concat(r.Elements(W + "t").Select(t => t.Value)) == text);

        Assert.NotNull(RunWith("tebal").Element(W + "rPr")?.Element(W + "b"));
        Assert.NotNull(RunWith("miring").Element(W + "rPr")?.Element(W + "i"));
        Assert.NotNull(RunWith("garis").Element(W + "rPr")?.Element(W + "u"));
        Assert.NotNull(RunWith("coret").Element(W + "rPr")?.Element(W + "strike"));
        Assert.Equal("C0392B",
            RunWith("merah").Element(W + "rPr")?.Element(W + "color")?.Attribute(W + "val")?.Value);

        // And the plain run carries none of them.
        Assert.Null(RunWith("Biasa ").Element(W + "rPr")?.Element(W + "b"));
    }

    [Fact]
    public void ALinkBecomesAHyperlinkWithAnExternalRelationship()
    {
        // The text alone is not a link. Word needs the relationship, marked external, or the run
        // is inert blue text.
        var docx = Convert("<p>Lihat <a href=\"https://example.com/laporan\">laporan lengkap</a>.</p>");

        var hyperlink = Part(docx, "word/document.xml").Descendants(W + "hyperlink").Single();
        var id = hyperlink.Attribute(R + "id")?.Value;

        Assert.Equal("laporan lengkap", TextOf(hyperlink));

        var relationship = Part(docx, "word/_rels/document.xml.rels").Root!.Elements()
            .Single(e => e.Attribute("Id")?.Value == id);

        Assert.Equal("https://example.com/laporan", relationship.Attribute("Target")?.Value);
        Assert.Equal("External", relationship.Attribute("TargetMode")?.Value);
    }

    [Fact]
    public void NestedListsKeepTheirLevels()
    {
        var docx = Convert(
            "<ul><li>Jakarta</li><li>Bandung<ul><li>Kota</li><li>Kabupaten</li></ul></li></ul>");

        var levels = Paragraphs(docx).Select(p => (TextOf(p), ListOf(p).Level)).ToList();

        Assert.Equal(
            [("Jakarta", 0), ("Bandung", 0), ("Kota", 1), ("Kabupaten", 1)],
            levels.Select(l => (l.Item1, l.Level ?? -1)).ToList());

        // A nested list is the same list one level down, not a new one.
        Assert.Single(Paragraphs(docx).Select(p => ListOf(p).NumId).Distinct());
    }

    [Fact]
    public void TwoListsSideBySideAreNumberedSeparately()
    {
        // Word numbers by definition, not by position, so two ordered lists sharing one definition
        // come out "1, 2" then "3" — the second list continues the first. Nothing sits between two
        // adjacent lists to mark where one ends, which is why the list each item came from has to
        // be carried through from the markup.
        var docx = Convert("<ol><li>Pertama</li><li>Kedua</li></ol><ol><li>Lagi dari satu</li></ol>");

        var ids = Paragraphs(docx).Select(p => ListOf(p).NumId).ToList();

        Assert.Equal(ids[0], ids[1]);
        Assert.NotEqual(ids[1], ids[2]);
    }

    [Fact]
    public void ABulletedAndANumberedListAreDifferentDefinitions()
    {
        var docx = Convert("<ul><li>Butir</li></ul><ol><li>Nomor</li></ol>");

        var ids = Paragraphs(docx).Select(p => ListOf(p).NumId).ToList();

        Assert.NotEqual(ids[0], ids[1]);
    }

    [Fact]
    public void ATableKeepsItsShapeAndItsText()
    {
        var docx = Convert(
            "<table><tr><th>Wilayah</th><th>Nilai</th></tr>" +
            "<tr><td>Jakarta</td><td>1.250</td></tr><tr><td>Bandung</td><td>980</td></tr></table>");

        var table = Part(docx, "word/document.xml").Descendants(W + "tbl").Single();
        var rows = table.Elements(W + "tr").ToList();

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(2, row.Elements(W + "tc").Count()));
        Assert.Equal("Wilayah", TextOf(rows[0].Elements(W + "tc").First()));
        Assert.Equal("980", TextOf(rows[2].Elements(W + "tc").Last()));
    }

    [Fact]
    public void PreformattedTextKeepsItsLineBreaksInOneParagraph()
    {
        // A pre block is one unit. Splitting it into a paragraph per line gets the spacing wrong
        // and breaks selection; flattening it into one line loses the point of pre.
        var docx = Convert("<pre>var x = 1;\nvar y = 2;</pre>");

        var paragraph = Paragraphs(docx).Single();

        Assert.Single(paragraph.Descendants(W + "br"));
        Assert.Equal("var x = 1;var y = 2;", TextOf(paragraph));
    }

    [Fact]
    public void AHorizontalRuleIsABottomBorderAndNotABox()
    {
        // There is no hr in WordprocessingML. Word writes an empty paragraph with a bottom border;
        // setting all four edges instead draws a box around nothing.
        var docx = Convert("<p>Atas</p><hr><p>Bawah</p>");

        var rule = Paragraphs(docx)[1];
        var borders = rule.Element(W + "pPr")?.Element(W + "pBdr");

        Assert.NotNull(borders?.Element(W + "bottom"));
        Assert.Null(borders?.Element(W + "top"));
        Assert.Null(borders?.Element(W + "left"));
        Assert.Null(borders?.Element(W + "right"));
    }

    [Fact]
    public void CentredTextIsCentred()
    {
        var docx = Convert("<p style=\"text-align:center\">Tengah</p>");

        Assert.Equal("center",
            Paragraphs(docx).Single().Element(W + "pPr")?.Element(W + "jc")?.Attribute(W + "val")?.Value);
    }

    [Fact]
    public void ScriptsAndStylesContributeNoText()
    {
        var docx = Convert(
            "<style>p { color: red }</style><script>alert('x')</script><p>Isi sebenarnya.</p>");

        Assert.Equal(["Isi sebenarnya."], Paragraphs(docx).Select(TextOf).ToList());
    }

    [Fact]
    public void ALinkTakesItsLookFromTheHyperlinkStyleNotFromDirectFormatting()
    {
        // Every browser draws a link blue and underlined, and the flattener records that as the a
        // tag's default. Writing it onto the run as well as applying the Hyperlink style makes the
        // link impossible to restyle, and exporting the document again wraps every link in a span
        // repeating the browser's own default.
        var docx = Convert("<p>Lihat <a href=\"https://example.com\">laporan</a>.</p>");

        var run = Part(docx, "word/document.xml").Descendants(W + "hyperlink").Single().Element(W + "r")!;
        var properties = run.Element(W + "rPr");

        Assert.Equal("Hyperlink", properties?.Element(W + "rStyle")?.Attribute(W + "val")?.Value);
        Assert.Null(properties?.Element(W + "color"));
        Assert.Null(properties?.Element(W + "u"));
    }

    [Fact]
    public void ALinkGivenAColourOfItsOwnKeepsIt()
    {
        // The other side of the same rule: only the browser's default is dropped. A link the page
        // deliberately coloured says something, and removing that would be the opposite mistake.
        var docx = Convert("<p><a href=\"https://example.com\" style=\"color:#C0392B\">merah</a></p>");

        var run = Part(docx, "word/document.xml").Descendants(W + "hyperlink").Single().Element(W + "r")!;

        Assert.Equal("C0392B", run.Element(W + "rPr")?.Element(W + "color")?.Attribute(W + "val")?.Value);
    }

    [Fact]
    public void TheDocumentReadsBackAfterSaving()
    {
        var docx = Convert("<h1>Judul</h1><p>Paragraf pertama.</p><ul><li>Butir</li></ul>");

        using var reopened = WordDocument.Open(new MemoryStream(docx, writable: false));

        Assert.Equal("Judul\nParagraf pertama.\nButir", reopened.ExtractText());
    }
}
