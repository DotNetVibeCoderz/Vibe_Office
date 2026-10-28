// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ExcelNet;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using Xunit;

namespace ExcelNet.Tests;

public class RichTextTests
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static byte[] Save(Workbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream);
        return stream.ToArray();
    }

    private static XElement SharedStringsXml(byte[] xlsx)
    {
        using var archive = new ZipArchive(new MemoryStream(xlsx, writable: false));
        using var stream = archive.GetEntry("xl/sharedStrings.xml")!.Open();
        return XDocument.Load(stream).Root!;
    }

    [Fact]
    public void RichTextIsWrittenAsRunsAndReadBack()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"]["A1"].WithRichText(
            new RichTextRun("Status: "),
            new RichTextRun("LUNAS") { Bold = true, Color = OfficeColor.FromRgb(0x00, 0x80, 0x00), SizePoints = 12 },
            new RichTextRun(" (dicek)") { Italic = true, SizePoints = 9 });

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        var item = SharedStringsXml(bytes).Element(Ns.S + "si")!;
        Assert.Equal(3, item.Elements(Ns.S + "r").Count());

        // CT_RPrElt order: b before color before sz.
        var bold = item.Elements(Ns.S + "r").ElementAt(1).Element(Ns.S + "rPr")!;
        Assert.Null(OfficeNet.TestKit.OpcValidator.CheckChildOrder(bold,
            "rFont", "charset", "family", "b", "i", "strike", "outline", "shadow", "condense", "extend",
            "color", "sz", "u", "vertAlign", "scheme"));

        using var reopened = Workbook.Open(bytes);
        var cell = reopened["Data"]["A1"];
        Assert.Equal("Status: LUNAS (dicek)", cell.Text);
        Assert.Equal(3, cell.RichText!.Count);
        Assert.True(cell.RichText[1].Bold);
        Assert.Equal("008000", cell.RichText[1].Color!.Value.ToHex());
        Assert.Equal(9, cell.RichText[2].SizePoints);
        Assert.Equal("Status: ", cell.RichText[0].Text);
    }

    [Fact]
    public void ExcelsRichTextSurvivesOpenAndSaveIncludingWhatIsNotModelled()
    {
        // Hand-built the way Excel writes it: a superscript run (vertAlign, not modelled here) and a
        // theme font scheme. Before the runs were kept, opening and saving flattened all of it.
        var bytes = Package(
            """<si><r><t xml:space="preserve">E = mc</t></r><r><rPr><vertAlign val="superscript"/><sz val="11"/><rFont val="Calibri"/><scheme val="minor"/></rPr><t>2</t></r></si><si><t>polos</t></si>""",
            """<row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>""");

        using var workbook = Workbook.Open(bytes);
        Assert.Equal("E = mc2", workbook[0]["A1"].Text);
        Assert.Null(workbook[0]["B1"].RichText);

        var saved = Save(workbook);
        XlsxValidator.AssertValid(saved);

        var runs = SharedStringsXml(saved).Elements(Ns.S + "si")
            .Single(si => si.Elements(Ns.S + "r").Any())
            .Elements(Ns.S + "r").ToList();

        Assert.Equal("superscript", runs[1].Element(Ns.S + "rPr")!.Element(Ns.S + "vertAlign")!.Attr("val"));
        Assert.Equal("minor", runs[1].Element(Ns.S + "rPr")!.Element(Ns.S + "scheme")!.Attr("val"));
        Assert.Equal("E = mc", runs[0].Element(Ns.S + "t")!.Value);
    }

    [Fact]
    public void APhoneticReadingIsNotPartOfTheText()
    {
        // rPh carries its own <t>; reading every <t> in the item appended the reading to the text.
        var bytes = Package(
            """<si><t>東京</t><rPh sb="0" eb="2"><t>トウキョウ</t></rPh></si>""",
            """<row r="1"><c r="A1" t="s"><v>0</v></c></row>""");

        using var workbook = Workbook.Open(bytes);
        Assert.Equal("東京", workbook[0]["A1"].Text);
    }

    [Fact]
    public void SettingAPlainValueDropsTheRuns()
    {
        using var workbook = Workbook.Create();
        var cell = workbook[0]["A1"].WithRichText(new RichTextRun("a") { Bold = true });
        cell.Set("b");

        Assert.Null(cell.RichText);
        Assert.DoesNotContain(SharedStringsXml(Save(workbook)).Descendants(), e => e.Name == Ns.S + "r");
    }

    [Fact]
    public void BoldAndPlainOfTheSameTextAreTwoEntries()
    {
        using var workbook = Workbook.Create();
        workbook[0]["A1"].Set("LUNAS");
        workbook[0]["A2"].WithRichText(new RichTextRun("LUNAS") { Bold = true });
        workbook[0]["A3"].WithRichText(new RichTextRun("LUNAS") { Bold = true });

        var sst = SharedStringsXml(Save(workbook));
        Assert.Equal("2", sst.Attr("uniqueCount"));
        Assert.Equal("3", sst.Attr("count"));
    }

    [Fact]
    public void ARunsSpacesArePreserved()
    {
        using var workbook = Workbook.Create();
        workbook[0]["A1"].WithRichText(new RichTextRun("Nama: "), new RichTextRun("Ani") { Bold = true });

        var bytes = Save(workbook);

        // Checked in the XML, not by reading back: .NET's reader keeps edge spaces even without
        // the attribute, Excel's does not, so a round trip here would pass with it missing.
        var first = SharedStringsXml(bytes).Descendants(Ns.S + "t").First();
        Assert.Equal("preserve", first.Attribute(XNamespace.Xml + "space")?.Value);

        using var reopened = Workbook.Open(bytes);
        Assert.Equal("Nama: Ani", reopened[0]["A1"].Text);
    }

    private static byte[] Package(string sharedStrings, string rows)
    {
        var files = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/></Types>""",
            ["_rels/.rels"] = $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            ["xl/workbook.xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="{Main}" xmlns:r="{Rel}"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>""",
            ["xl/_rels/workbook.xml.rels"] = $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="{Rel}/sharedStrings" Target="sharedStrings.xml"/></Relationships>""",
            ["xl/worksheets/sheet1.xml"] = $"""<worksheet xmlns="{Main}"><sheetData>{rows}</sheetData></worksheet>""",
            ["xl/sharedStrings.xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><sst xmlns="{Main}">{sharedStrings}</sst>""",
        };

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
