// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ExcelNet;
using ExcelNet.Styles;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using Xunit;

namespace ExcelNet.Tests;

public class ConditionalFormattingTests
{
    private static readonly CellStyle Red =
        CellStyle.Default.WithBackground(OfficeColor.FromRgb(0xFF, 0xC7, 0xCE)).Bold();

    private static byte[] Save(Workbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream);
        return stream.ToArray();
    }

    private static XElement Part(byte[] xlsx, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(xlsx, writable: false));
        using var stream = archive.GetEntry(name)!.Open();
        return XDocument.Load(stream).Root!;
    }

    [Fact]
    public void AStyledRuleGetsADifferentialFormatThatAppliesIt()
    {
        // Before dxfs existed, this rule was written with no dxfId: the condition was evaluated and
        // nothing was ever highlighted.
        using var workbook = Workbook.Create();
        workbook[0].WriteColumn("A1", 5, 50, 500);
        workbook[0].AddConditionalFormat(CellRangeReference.Parse("A1:A3"), "greaterThan", Red, "10");

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        var rule = Part(bytes, "xl/worksheets/sheet1.xml").Descendants(Ns.S + "cfRule").Single();
        Assert.Equal("0", rule.Attr("dxfId"));

        var dxf = Part(bytes, "xl/styles.xml").Element(Ns.S + "dxfs")!.Element(Ns.S + "dxf")!;
        Assert.NotNull(dxf.Element(Ns.S + "font")!.Element(Ns.S + "b"));

        // In a dxf a solid fill's colour is bgColor — the opposite of a cell format.
        var fill = dxf.Element(Ns.S + "fill")!.Element(Ns.S + "patternFill")!;
        Assert.Equal("FFFFC7CE", fill.Element(Ns.S + "bgColor")!.Attr("rgb"));
        Assert.Null(fill.Element(Ns.S + "fgColor"));
    }

    [Fact]
    public void TheSameStyleSharesOneDifferentialFormat()
    {
        using var workbook = Workbook.Create();
        workbook[0].AddDuplicateValues("A1:A10", Red);
        workbook[0].AddUniqueValues("B1:B10", Red);

        var styles = Part(Save(workbook), "xl/styles.xml");
        Assert.Single(styles.Element(Ns.S + "dxfs")!.Elements(Ns.S + "dxf"));
    }

    [Fact]
    public void EveryNewRuleTypeIsWrittenAsExcelExpectsIt()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        sheet.AddDuplicateValues("A1:A10", Red);
        sheet.AddTopRule("B1:B10", 3, Red);
        sheet.AddTopRule("C1:C10", 10, Red, percent: true, bottom: true);
        sheet.AddAverageRule("D1:D10", Red, below: true);
        sheet.AddTextContains("E2:E10", "lunas \"final\"", Red);
        sheet.AddFormulaRule("A2:F10", "=$F2>1000000", Red);
        sheet.AddIconSet("F1:F10", "3Arrows");

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        var rules = Part(bytes, "xl/worksheets/sheet1.xml").Descendants(Ns.S + "cfRule").ToList();

        Assert.Equal("duplicateValues", rules[0].Attr("type"));
        Assert.Equal(("top10", "3", null, null), (rules[1].Attr("type"), rules[1].Attr("rank"), rules[1].Attr("percent"), rules[1].Attr("bottom")));
        Assert.Equal(("10", "1", "1"), (rules[2].Attr("rank"), rules[2].Attr("percent"), rules[2].Attr("bottom")));
        Assert.Equal("0", rules[3].Attr("aboveAverage"));

        // containsText needs the attribute AND the formula it actually evaluates, anchored at
        // the range's top-left cell, with quotes doubled.
        Assert.Equal("containsText", rules[4].Attr("operator"));
        Assert.Equal("lunas \"final\"", rules[4].Attr("text"));
        Assert.Equal("NOT(ISERROR(SEARCH(\"lunas \"\"final\"\"\",E2)))", rules[4].Element(Ns.S + "formula")!.Value);

        Assert.Equal("$F2>1000000", rules[5].Element(Ns.S + "formula")!.Value);

        var icons = rules[6].Element(Ns.S + "iconSet")!;
        Assert.Equal("3Arrows", icons.Attr("iconSet"));
        Assert.Equal(["0", "33", "66"], icons.Elements(Ns.S + "cfvo").Select(c => c.Attr("val")));

        // An icon set carries no format.
        Assert.Null(rules[6].Attr("dxfId"));
    }

    [Fact]
    public void RulesWrittenByExcelSurviveOpenAndSave()
    {
        // Hand-built the way Excel writes them: two blocks back to back (the second used to be
        // skipped by the reader), a multi-range sqref (which failed to parse and lost its rule),
        // a dxfId, and an icon set with custom thresholds this library does not model.
        const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        // No whitespace between the elements, as Excel writes it. With a newline between the
        // blocks the reader lands on the whitespace and skipping it is harmless, which is how the
        // first version of this test passed against a reader that lost the second block.
        var sheet =
            $"""<worksheet xmlns="{Main}"><sheetData><row r="1"><c r="A1"><v>1</v></c></row></sheetData>""" +
            """<conditionalFormatting sqref="A1:A10 C1:C10"><cfRule type="cellIs" dxfId="0" priority="1" operator="greaterThan"><formula>5</formula></cfRule></conditionalFormatting>""" +
            """<conditionalFormatting sqref="B1:B10"><cfRule type="iconSet" priority="2"><iconSet iconSet="5Rating" showValue="0"><cfvo type="num" val="0"/><cfvo type="num" val="10"/><cfvo type="num" val="20"/><cfvo type="num" val="30"/><cfvo type="num" val="40"/></iconSet></cfRule></conditionalFormatting>""" +
            """</worksheet>""";
        var styles = $"""<styleSheet xmlns="{Main}"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="1"><xf/></cellXfs><dxfs count="1"><dxf><font><color rgb="FF9C0006"/></font><fill><patternFill><bgColor rgb="FFFFC7CE"/></patternFill></fill></dxf></dxfs></styleSheet>""";

        using var workbook = Workbook.Open(Package(sheet, styles));
        Assert.Equal(2, workbook[0].ConditionalRules.Count);

        // Adding a styled rule appends a dxf; it must not disturb the one Excel's rule points at.
        workbook[0].AddDuplicateValues("D1:D10", Red);

        var saved = Save(workbook);
        XlsxValidator.AssertValid(saved);

        var blocks = Part(saved, "xl/worksheets/sheet1.xml").Elements(Ns.S + "conditionalFormatting").ToList();
        Assert.Equal("A1:A10 C1:C10", blocks[0].Attr("sqref"));
        Assert.Equal("0", blocks[0].Element(Ns.S + "cfRule")!.Attr("dxfId"));
        Assert.Equal("5Rating", blocks[1].Descendants(Ns.S + "iconSet").Single().Attr("iconSet"));
        Assert.Equal("40", blocks[1].Descendants(Ns.S + "cfvo").Last().Attr("val"));
        Assert.Equal("1", blocks[2].Element(Ns.S + "cfRule")!.Attr("dxfId"));

        var dxfs = Part(saved, "xl/styles.xml").Element(Ns.S + "dxfs")!.Elements().ToList();
        Assert.Equal("FF9C0006", dxfs[0].Descendants(Ns.S + "color").First().Attr("rgb"));
    }

    [Fact]
    public void FindDuplicatesReturnsEveryRepeatedCellIgnoringCase()
    {
        using var workbook = Workbook.Create();
        workbook[0].WriteColumn("A1", "Jakarta", "Bandung", "JAKARTA", 7, 7.0, "Medan");
        // A7 left blank, A8 blank too: blanks are never duplicates.

        var duplicates = workbook[0].FindDuplicates("A1:A8").Select(c => c.A1).ToList();

        Assert.Equal(["A1", "A3", "A4", "A5"], duplicates);
    }

    private static byte[] Package(string sheet, string styles)
    {
        const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var files = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""",
            ["_rels/.rels"] = $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            ["xl/workbook.xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="{Rel}"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>""",
            ["xl/_rels/workbook.xml.rels"] = $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="{Rel}/styles" Target="styles.xml"/></Relationships>""",
            ["xl/worksheets/sheet1.xml"] = sheet,
            ["xl/styles.xml"] = styles,
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
