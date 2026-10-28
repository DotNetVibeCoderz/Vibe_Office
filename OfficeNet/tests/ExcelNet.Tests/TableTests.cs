// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Xml.Linq;
using ExcelNet;
using OfficeNet.Core;
using OfficeNet.Core.Xml;
using Xunit;

namespace ExcelNet.Tests;

public class TableTests
{
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

    private static Workbook Sales()
    {
        var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];
        sheet.WriteHeader("A1", ["Wilayah", "Nilai", "Qty"]);
        sheet.WriteRow("A2", "Jakarta", 1480, 12);
        sheet.WriteRow("A3", "Bandung", 1150, 9);
        sheet.WriteRow("A4", "Medan", 520, 4);
        return workbook;
    }

    [Fact]
    public void ATableIsWrittenWithItsPartAndTheSheetPointsAtIt()
    {
        using var workbook = Sales();
        var table = workbook["Data"].AddTable("A1:C4", "Penjualan", "TableStyleLight9");

        Assert.Equal(["Wilayah", "Nilai", "Qty"], table.Columns);
        Assert.Equal("A2:C4", table.DataRange.A1);

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        var sheet = Part(bytes, "xl/worksheets/sheet1.xml");
        Assert.Single(sheet.Element(Ns.S + "tableParts")!.Elements(Ns.S + "tablePart"));

        var xml = Part(bytes, "xl/tables/table1.xml");
        Assert.Equal("Penjualan", xml.Attr("displayName"));
        Assert.Equal("A1:C4", xml.Attr("ref"));
        Assert.Equal("A1:C4", xml.Element(Ns.S + "autoFilter")!.Attr("ref"));
        Assert.Equal("TableStyleLight9", xml.Element(Ns.S + "tableStyleInfo")!.Attr("name"));
    }

    [Fact]
    public void ATableSurvivesOpenAndSave()
    {
        // The sheet is rewritten on save. Before tableParts was written, a table read from a file
        // was left as a part nothing pointed at, and Excel dropped it.
        using var workbook = Sales();
        workbook["Data"].AddTable("A1:C4", "Penjualan");

        using var reopened = Workbook.Open(Save(workbook));
        reopened["Data"]["A5"].Set("tambahan");

        var bytes = Save(reopened);
        XlsxValidator.AssertValid(bytes);
        Assert.NotNull(Part(bytes, "xl/worksheets/sheet1.xml").Element(Ns.S + "tableParts"));

        using var again = Workbook.Open(bytes);
        Assert.Equal("Penjualan", again["Data"].Tables.Single().Name);
    }

    [Fact]
    public void HeadersAreMadeTextAndUnique()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        sheet.WriteRow("A1", "Nilai", 2026, null, "nilai");
        sheet.WriteRow("A2", 1, 2, 3, 4);

        var table = sheet.AddTable("A1:D2");

        // Excel deletes a table whose column names differ from the header cells, or repeat.
        Assert.Equal(["Nilai", "2026", "Column3", "nilai2"], table.Columns);
        Assert.Equal(CellValueType.Text, sheet["B1"].Value.ValueType);
        Assert.Equal("Column3", sheet["C1"].Text);
    }

    [Fact]
    public void EditingAHeaderAfterwardsIsFollowedOnSave()
    {
        using var workbook = Sales();
        workbook["Data"].AddTable("A1:C4", "Penjualan");
        workbook["Data"]["B1"].Set("Pendapatan");

        var bytes = Save(workbook);
        var names = Part(bytes, "xl/tables/table1.xml").Descendants(Ns.S + "tableColumn").Select(c => c.Attr("name"));
        Assert.Equal(["Wilayah", "Pendapatan", "Qty"], names);
    }

    [Fact]
    public void StructuredReferencesWorkInFormulas()
    {
        using var workbook = Sales();
        workbook["Data"].AddTable("A1:C4", "Penjualan");

        var summary = workbook.AddSheet("Ringkasan");
        summary["A1"].SetFormula("SUM(Penjualan[Nilai])");
        summary["A2"].SetFormula("AVERAGE(Penjualan[qty])");
        summary["A3"].SetFormula("COUNTA(Penjualan)");
        summary["A4"].SetFormula("Penjualan[Wilayah]");
        summary["A5"].SetFormula("SUM(Penjualan[TidakAda])");
        workbook.Recalculate();

        Assert.Equal(3150, summary["A1"].Number);
        Assert.Equal(25.0 / 3, summary["A2"].Number, 9);
        Assert.Equal(9, summary["A3"].Number);
        Assert.Equal("Jakarta", summary["A4"].Text);
        Assert.StartsWith("#", summary["A5"].Text);
    }

    [Fact]
    public void IdsAndNamesAreUniqueInTheWorkbook()
    {
        using var workbook = Sales();
        var other = workbook.AddSheet("Lain");
        other.WriteHeader("A1", ["X"]);
        other.WriteRow("A2", 1);

        var first = workbook["Data"].AddTable("A1:C4");
        var second = other.AddTable("A1:A2");

        Assert.Equal("Table1", first.Name);
        Assert.Equal("Table2", second.Name);
        Assert.NotEqual(first.Id, second.Id);

        Assert.Throws<OfficeNetException>(() => other.AddTable("C1:C2", "table1"));
        workbook.Names.Add("Ringkas", "1");
        Assert.Throws<OfficeNetException>(() => other.AddTable("C1:C2", "Ringkas"));
    }

    [Fact]
    public void OverlapsAreRefused()
    {
        using var workbook = Sales();
        var sheet = workbook["Data"];
        sheet.AddTable("A1:C4", "Penjualan");

        Assert.Throws<OfficeNetException>(() => sheet.AddTable("C2:D5"));

        // A filter reaching outside the table would leave two filters on the same cells.
        sheet.AutoFilter = CellRangeReference.Parse("E1:F10");
        Assert.Throws<OfficeNetException>(() => sheet.AddTable("E1:F3"));

        // One inside it — what WriteHeader puts on a header row — becomes the table's own.
        sheet.AutoFilter = CellRangeReference.Parse("E1:F1");
        sheet.AddTable("E1:F3");
        Assert.Null(sheet.AutoFilter);

        Assert.Throws<ArgumentException>(() => sheet.AddTable("H1:H1"));
    }

    [Fact]
    public void RemovingTheSheetRemovesItsTables()
    {
        using var workbook = Sales();
        workbook.AddSheet("Lain");
        workbook["Data"].AddTable("A1:C4");
        workbook.RemoveSheet("Data");

        using var archive = new ZipArchive(new MemoryStream(Save(workbook)));
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("xl/tables/", StringComparison.Ordinal));
    }
}
