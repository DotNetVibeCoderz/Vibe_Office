// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Xml.Linq;
using ExcelNet;
using OfficeNet.Core;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using Xunit;

namespace ExcelNet.Tests;

public class DefinedNameTests
{
    private static byte[] Save(Workbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream);
        return stream.ToArray();
    }

    private static XElement WorkbookXml(byte[] xlsx)
    {
        using var archive = new ZipArchive(new MemoryStream(xlsx, writable: false));
        using var stream = archive.GetEntry("xl/workbook.xml")!.Open();
        return XDocument.Load(stream).Root!;
    }

    private static Workbook Sales()
    {
        var workbook = Workbook.Create("Data Penjualan");
        var sheet = workbook["Data Penjualan"];
        sheet.WriteColumn("B1", "Nilai", 100, 200, 300);
        return workbook;
    }

    // ---- Formulas ------------------------------------------------------------------------------

    [Fact]
    public void ANameStandsForItsRangeInAFunction()
    {
        using var workbook = Sales();
        workbook.Names.Add("Penjualan", workbook[0], "B2:B4");

        var sheet = workbook.AddSheet("Ringkasan");
        sheet["A1"].SetFormula("SUM(Penjualan)");
        sheet["A2"].SetFormula("AVERAGE(Penjualan)*2");
        workbook.Recalculate();

        Assert.Equal(600, sheet["A1"].Number);
        Assert.Equal(400, sheet["A2"].Number);
    }

    [Fact]
    public void ANameCanBeAConstantOrAFormula()
    {
        using var workbook = Sales();
        workbook.Names.Add("PPN", "0.11");
        workbook.Names.Add("Total", "=SUM('Data Penjualan'!$B$2:$B$4)");

        var sheet = workbook[0];
        sheet["D1"].SetFormula("Total*(1+PPN)");
        workbook.Recalculate();

        Assert.Equal(666, sheet["D1"].Number, 6);
    }

    [Fact]
    public void ALocalNameHidesTheGlobalOneOnItsSheet()
    {
        using var workbook = Sales();
        var other = workbook.AddSheet("Lain");
        workbook.Names.Add("Tarif", "0.1");
        workbook.Names.Add("Tarif", "0.2", scope: other);

        workbook[0]["D1"].SetFormula("Tarif");
        other["D1"].SetFormula("Tarif");
        workbook.Recalculate();

        Assert.Equal(0.1, workbook[0]["D1"].Number);
        Assert.Equal(0.2, other["D1"].Number);
    }

    [Fact]
    public void AnUnknownNameIsNameError()
    {
        using var workbook = Sales();
        workbook[0]["D1"].SetFormula("SUM(TidakAda)");
        workbook.Recalculate();

        Assert.Equal("#NAME?", workbook[0]["D1"].Text);
    }

    [Fact]
    public void NamesThatReferToEachOtherInALoopEndInRefError()
    {
        using var workbook = Sales();
        workbook.Names.Add("A_", "=B_+1");
        workbook.Names.Add("B_", "=A_+1");
        workbook[0]["D1"].SetFormula("A_");
        workbook.Recalculate();

        Assert.StartsWith("#", workbook[0]["D1"].Text);
    }

    // ---- The file ------------------------------------------------------------------------------

    [Fact]
    public void NamesAreWrittenInSchemaOrderAndReadBack()
    {
        using var workbook = Sales();
        workbook.Names.Add("Penjualan", workbook[0], "B2:B4");
        workbook.Names.Add("PPN", "0.11", comment: "Pajak pertambahan nilai");
        workbook[0].PrintArea = "A1:B4";
        workbook[0].PrintTitleRows = "1:1";

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        var root = WorkbookXml(bytes);
        Assert.Null(OpcValidator.CheckChildOrder(root, "bookViews", "sheets", "definedNames", "calcPr", "pivotCaches"));

        var names = root.Element(Ns.S + "definedNames")!.Elements().ToDictionary(e => e.Attr("name")!, e => e.Value);
        Assert.Equal("'Data Penjualan'!$B$2:$B$4", names["Penjualan"]);
        Assert.Equal("'Data Penjualan'!$A$1:$B$4", names["_xlnm.Print_Area"]);
        Assert.Equal("'Data Penjualan'!$1:$1", names["_xlnm.Print_Titles"]);

        using var reopened = Workbook.Open(bytes);
        Assert.Equal("'Data Penjualan'!$B$2:$B$4", reopened.Names["Penjualan"].RefersTo);
        Assert.Equal("Pajak pertambahan nilai", reopened.Names["PPN"].Comment);
        Assert.Equal("A1:B4", reopened[0].PrintArea);
        Assert.Equal("1:1", reopened[0].PrintTitleRows);
    }

    [Fact]
    public void MovingASheetKeepsItsLocalNamesWithIt()
    {
        // localSheetId is a position. Writing the index the sheet had when the name was made
        // would hand its print area to whichever sheet moved into that slot.
        using var workbook = Workbook.Create("Satu");
        workbook.AddSheet("Dua");
        workbook["Satu"]["A1"].Set(1);
        workbook["Dua"]["A1"].Set(2);
        workbook["Dua"].PrintArea = "A1:C10";
        workbook.MoveSheet("Dua", 0);

        var bytes = Save(workbook);
        var printArea = WorkbookXml(bytes).Element(Ns.S + "definedNames")!.Elements().Single();
        Assert.Equal("0", printArea.Attr("localSheetId"));

        using var reopened = Workbook.Open(bytes);
        Assert.Equal("A1:C10", reopened["Dua"].PrintArea);
        Assert.Null(reopened["Satu"].PrintArea);
    }

    [Fact]
    public void RenamingRemovingAndCopyingSheetsKeepNamesConsistent()
    {
        using var workbook = Sales();
        workbook.Names.Add("Penjualan", workbook[0], "B2:B4");
        workbook[0].PrintArea = "A1:B4";

        workbook[0].Name = "Penjualan 2026";
        Assert.Equal("'Penjualan 2026'!$B$2:$B$4", workbook.Names["Penjualan"].RefersTo);

        var copy = workbook.CopySheet("Penjualan 2026", "Salinan");
        Assert.Equal("A1:B4", copy.PrintArea);
        Assert.Contains(workbook.Names, n => n.Scope == copy && n.RefersTo.StartsWith("Salinan!", StringComparison.Ordinal));

        workbook.RemoveSheet("Salinan");
        Assert.DoesNotContain(workbook.Names, n => n.Scope == copy);

        workbook.AddSheet("Rumus")["A1"].SetFormula("SUM(Penjualan)");
        workbook.Recalculate();
        Assert.Equal(600, workbook["Rumus"]["A1"].Number);
    }

    [Fact]
    public void ANameWrittenByExcelIsRead()
    {
        // Hand-built the way Excel writes it, including a chartsheet in the middle that shifts
        // every localSheetId after it.
        const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var files = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/chartsheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml"/></Types>""",
            ["_rels/.rels"] = $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            ["xl/workbook.xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="{Main}" xmlns:r="{Rel}"><sheets><sheet name="Data" sheetId="1" r:id="rId1"/><sheet name="Grafik" sheetId="2" r:id="rId3"/><sheet name="Laporan" sheetId="3" r:id="rId2"/></sheets><definedNames><definedName name="Harga">Data!$A$1:$A$3</definedName><definedName name="_xlnm.Print_Area" localSheetId="2">Laporan!$A$1:$D$20</definedName></definedNames></workbook>""",
            ["xl/_rels/workbook.xml.rels"] = $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="{Rel}/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="{Rel}/chartsheet" Target="chartsheets/sheet1.xml"/></Relationships>""",
            ["xl/worksheets/sheet1.xml"] = $"""<worksheet xmlns="{Main}"><sheetData><row r="1"><c r="A1"><v>5</v></c></row><row r="2"><c r="A2"><v>7</v></c></row></sheetData></worksheet>""",
            ["xl/worksheets/sheet2.xml"] = $"""<worksheet xmlns="{Main}"><sheetData/></worksheet>""",
            ["xl/chartsheets/sheet1.xml"] = $"""<chartsheet xmlns="{Main}"/>""",
        };

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        using var workbook = Workbook.Open(buffer.ToArray());

        // localSheetId 2 is "Laporan" counting the chartsheet, not the third worksheet.
        Assert.Equal("A1:D20", workbook["Laporan"].PrintArea);

        workbook["Laporan"]["A1"].SetFormula("SUM(Harga)");
        workbook.Recalculate();
        Assert.Equal(12, workbook["Laporan"]["A1"].Number);
    }

    // ---- Validation ----------------------------------------------------------------------------

    [Theory]
    [InlineData("A1")]
    [InlineData("XFD100")]
    [InlineData("R1C1")]
    [InlineData("C")]
    [InlineData("1Tahun")]
    [InlineData("dua kata")]
    [InlineData("_xlnm.Print_Area")]
    public void NamesExcelWouldRejectAreRefused(string name)
    {
        using var workbook = Sales();
        Assert.Throws<ArgumentException>(() => workbook.Names.Add(name, "1"));
    }

    [Fact]
    public void ANameIsUniqueInItsScopeIgnoringCase()
    {
        using var workbook = Sales();
        workbook.Names.Add("Tarif", "1");

        Assert.Throws<OfficeNetException>(() => workbook.Names.Add("TARIF", "2"));
        workbook.Names.Add("Tarif", "3", scope: workbook[0]);
    }
}
