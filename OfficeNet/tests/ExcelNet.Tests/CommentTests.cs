// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ExcelNet;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using Xunit;

namespace ExcelNet.Tests;

/// <summary>
/// Cell comments and hyperlinks survive a save and a reopen.
/// </summary>
/// <remarks>
/// Both used to be lost. <c>Cell.Comment</c> was kept in the model and never written, so a comment
/// disappeared on the first save; hyperlinks were written but never read, so they disappeared on
/// the first open-and-save. Neither failure threw.
/// </remarks>
public class CommentTests
{
    private static byte[] Save(Workbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream);
        return stream.ToArray();
    }

    private static string Entry(byte[] xlsx, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(xlsx, writable: false));
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static string[] Entries(byte[] xlsx)
    {
        using var archive = new ZipArchive(new MemoryStream(xlsx, writable: false));
        return [.. archive.Entries.Select(e => e.FullName)];
    }

    [Fact]
    public void ACommentIsWrittenWithItsVmlNote()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];
        sheet["B3"].Set(42);
        sheet["B3"].WithComment("Diperiksa oleh tim audit");

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        var comments = XDocument.Parse(Entry(bytes, "xl/comments1.xml")).Root!;
        var comment = comments.Element(Ns.S + "commentList")!.Element(Ns.S + "comment")!;
        Assert.Equal("B3", comment.Attr("ref"));
        Assert.Equal("Diperiksa oleh tim audit", comment.Value);

        // Without the VML, Excel opens the file and shows nothing at all.
        var vml = Entry(bytes, "xl/drawings/vmlDrawing1.vml");
        Assert.Contains("ObjectType=\"Note\"", vml);
        Assert.Contains("<x:Row>2</x:Row>", vml);
        Assert.Contains("<x:Column>1</x:Column>", vml);

        var contentTypes = Entry(bytes, "[Content_Types].xml");
        Assert.Contains(ContentTypes.ExcelComments, contentTypes);
        Assert.Contains("Extension=\"vml\"", contentTypes);
    }

    [Fact]
    public void ACommentReadsBackAfterSaving()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"]["A1"].WithComment("  spasi di tepi\nbaris kedua  ");
        workbook["Data"]["C7"].WithComment("kedua");

        using var reopened = Workbook.Open(Save(workbook));
        Assert.Equal("  spasi di tepi\nbaris kedua  ", reopened["Data"]["A1"].Comment);
        Assert.Equal("kedua", reopened["Data"]["C7"].Comment);
        Assert.Null(reopened["Data"]["B1"].Comment);
    }

    [Fact]
    public void ACommentOnAnEmptyCellSurvives()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"]["D4"].WithComment("isi nanti");

        using var reopened = Workbook.Open(Save(workbook));
        Assert.Equal("isi nanti", reopened["Data"]["D4"].Comment);
    }

    [Fact]
    public void SavingTwiceDoesNotDuplicateTheParts()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"]["A1"].WithComment("satu");
        Save(workbook);
        workbook["Data"]["A2"].WithComment("dua");

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        var entries = Entries(bytes);
        Assert.Single(entries, e => e.StartsWith("xl/comments", StringComparison.Ordinal));
        Assert.Single(entries, e => e.EndsWith(".vml", StringComparison.Ordinal));
        Assert.Equal(2, XDocument.Parse(Entry(bytes, "xl/comments1.xml")).Descendants(Ns.S + "comment").Count());
    }

    [Fact]
    public void RemovingEveryCommentRemovesBothParts()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"]["A1"].WithComment("sementara");

        using var reopened = Workbook.Open(Save(workbook));
        reopened["Data"]["A1"].WithComment(null);

        var bytes = Save(reopened);
        XlsxValidator.AssertValid(bytes);

        var entries = Entries(bytes);
        Assert.DoesNotContain(entries, e => e.StartsWith("xl/comments", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.EndsWith(".vml", StringComparison.Ordinal));
        Assert.DoesNotContain("legacyDrawing", Entry(bytes, "xl/worksheets/sheet1.xml"));
    }

    [Fact]
    public void ShapeIdsAreUniqueAcrossSheets()
    {
        // Each sheet's VML numbers its shapes from its idmap block. Two sheets both claiming block
        // 1 would give both a _x0000_s1025, and Excel shows one sheet's notes on the other.
        using var workbook = Workbook.Create("Satu");
        workbook.AddSheet("Dua");
        workbook["Satu"]["A1"].WithComment("di satu");
        workbook["Dua"]["A1"].WithComment("di dua");

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        using var reopened = Workbook.Open(bytes);
        Assert.Equal("di satu", reopened["Satu"]["A1"].Comment);
        Assert.Equal("di dua", reopened["Dua"]["A1"].Comment);
    }

    [Fact]
    public void ManyCommentsSpanSeveralIdBlocks()
    {
        using var workbook = Workbook.Create("Data");

        for (var row = 0; row < 1500; row++)
        {
            workbook["Data"][row, 0].WithComment($"catatan {row}");
        }

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);
        Assert.Contains("data=\"1,2\"", Entry(bytes, "xl/drawings/vmlDrawing1.vml"));

        using var reopened = Workbook.Open(bytes);
        Assert.Equal("catatan 1499", reopened["Data"][1499, 0].Comment);
    }

    [Fact]
    public void CopySheetCarriesComments()
    {
        using var workbook = Workbook.Create("Asal");
        workbook["Asal"]["A1"].WithComment("ikut tersalin");
        workbook.CopySheet("Asal", "Salinan");

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        using var reopened = Workbook.Open(bytes);
        Assert.Equal("ikut tersalin", reopened["Salinan"]["A1"].Comment);
    }

    [Fact]
    public void ACommentWrittenByExcelIsReadIncludingRichRuns()
    {
        // Hand-built the way Excel writes it: a bold author run, then the text. The reader must
        // not depend on this library having written the file.
        var bytes = BuildWorkbookWithExcelComment();

        using var workbook = Workbook.Open(bytes);
        Assert.Equal("Fadhil:\nAngka ini sementara", workbook["Sheet1"]["A1"].Comment);

        // Saving keeps it and still displays it.
        var saved = Save(workbook);
        XlsxValidator.AssertValid(saved);

        using var again = Workbook.Open(saved);
        Assert.Equal("Fadhil:\nAngka ini sementara", again["Sheet1"]["A1"].Comment);
    }

    [Fact]
    public void HyperlinksReadBackAfterSaving()
    {
        using var workbook = Workbook.Create("Data");
        workbook.AddSheet("Ringkasan");
        workbook["Data"]["A1"].WithHyperlink("https://example.org/laporan");
        workbook["Data"]["A2"].WithHyperlink("#Ringkasan!B2");

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        // An internal link is a location, not a relationship; Excel would try to open it as a file.
        var sheetXml = XDocument.Parse(Entry(bytes, "xl/worksheets/sheet1.xml")).Root!;
        var internalLink = sheetXml.Descendants(Ns.S + "hyperlink").Single(h => h.Attr("ref") == "A2");
        Assert.Equal("Ringkasan!B2", internalLink.Attr("location"));
        Assert.Null(internalLink.Attr(Ns.R + "id"));

        using var reopened = Workbook.Open(bytes);
        Assert.Equal("https://example.org/laporan", reopened["Data"]["A1"].Hyperlink);
        Assert.Equal("#Ringkasan!B2", reopened["Data"]["A2"].Hyperlink);

        // And survives a second round trip, which is where the old reader lost it.
        using var twice = Workbook.Open(Save(reopened));
        Assert.Equal("https://example.org/laporan", twice["Data"]["A1"].Hyperlink);
    }

    [Fact]
    public void AHyperlinkOverARangeAppliesToEveryCell()
    {
        var bytes = BuildWorkbookWithRangeHyperlink();

        using var workbook = Workbook.Open(bytes);
        Assert.Equal("https://example.org/", workbook["Sheet1"]["A1"].Hyperlink);
        Assert.Equal("https://example.org/", workbook["Sheet1"]["B2"].Hyperlink);
        Assert.Null(workbook["Sheet1"]["C3"].Hyperlink);
    }

    // ---- Fixtures written without ExcelNet -----------------------------------------------------

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static byte[] BuildWorkbookWithExcelComment() => Package(
        sheet: $"""<worksheet xmlns="{Main}" xmlns:r="{Rel}"><sheetData><row r="1"><c r="A1"><v>1</v></c></row></sheetData><legacyDrawing r:id="rId2"/></worksheet>""",
        sheetRels: $"""<Relationship Id="rId1" Type="{Rel}/comments" Target="../comments1.xml"/><Relationship Id="rId2" Type="{Rel}/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>""",
        extra: new()
        {
            ["xl/comments1.xml"] = $"""<comments xmlns="{Main}"><authors><author>Fadhil</author></authors><commentList><comment ref="A1" authorId="0"><text><r><rPr><b/><sz val="9"/></rPr><t>Fadhil:</t></r><r><rPr><sz val="9"/></rPr><t xml:space="preserve">&#10;Angka ini sementara</t></r><rPh sb="0" eb="1"><t>ふりがな</t></rPh></text></comment></commentList></comments>""",
            ["xl/drawings/vmlDrawing1.vml"] = """<xml xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel"><o:shapelayout v:ext="edit"><o:idmap v:ext="edit" data="1"/></o:shapelayout><v:shape id="_x0000_s1025" type="#_x0000_t202"><x:ClientData ObjectType="Note"><x:Row>0</x:Row><x:Column>0</x:Column></x:ClientData></v:shape></xml>""",
        },
        overrides: """<Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/><Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>""");

    private static byte[] BuildWorkbookWithRangeHyperlink() => Package(
        sheet: $"""<worksheet xmlns="{Main}" xmlns:r="{Rel}"><sheetData/><hyperlinks><hyperlink ref="A1:B2" r:id="rId1"/></hyperlinks></worksheet>""",
        sheetRels: $"""<Relationship Id="rId1" Type="{Rel}/hyperlink" Target="https://example.org/" TargetMode="External"/>""",
        extra: [],
        overrides: "");

    private static byte[] Package(string sheet, string sheetRels, Dictionary<string, string> extra,
        string overrides)
    {
        var files = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>{overrides}</Types>""",
            ["_rels/.rels"] = $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            ["xl/workbook.xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="{Main}" xmlns:r="{Rel}"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>""",
            ["xl/_rels/workbook.xml.rels"] = $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""",
            ["xl/worksheets/sheet1.xml"] = sheet,
            ["xl/worksheets/_rels/sheet1.xml.rels"] = $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{sheetRels}</Relationships>""",
        };

        foreach (var (name, content) in extra)
        {
            files[name] = content;
        }

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
