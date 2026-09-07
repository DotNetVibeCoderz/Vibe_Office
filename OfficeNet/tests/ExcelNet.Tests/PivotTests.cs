// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet;
using ExcelNet.Pivot;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using Xunit;

namespace ExcelNet.Tests;

public class PivotTableTests
{
    private static Workbook BuildSource()
    {
        var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["Wilayah", "Produk", "Qty", "Total"]);

        var rows = new (string Region, string Product, int Qty, double Total)[]
        {
            ("Jakarta", "WordNet", 10, 1_000_000),
            ("Jakarta", "ExcelNet", 5, 500_000),
            ("Bandung", "WordNet", 8, 800_000),
            ("Bandung", "ExcelNet", 12, 1_200_000),
            ("Surabaya", "WordNet", 3, 300_000),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var row = i + 1;
            sheet[row, 0].Set(rows[i].Region);
            sheet[row, 1].Set(rows[i].Product);
            sheet[row, 2].Set(rows[i].Qty);
            sheet[row, 3].Set(rows[i].Total);
        }

        return workbook;
    }

    private static PivotTableDefinition Definition(Workbook workbook) => new()
    {
        Source = workbook["Data"],
        SourceRange = CellRangeReference.Parse("A1:D6"),
        Target = CellReference.Parse("A1"),
        Rows = ["Wilayah"],
        Columns = ["Produk"],
        Values = [new PivotValue("Total", PivotFunction.Sum)],
    };

    private static byte[] Save(Workbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream);
        return stream.ToArray();
    }

    private static OpcPackage Reopen(byte[] bytes) =>
        OpcPackage.Open(new MemoryStream(bytes, writable: false));

    [Fact]
    public void APivotTableProducesAValidPackage()
    {
        using var workbook = BuildSource();
        workbook.AddSheet("Ringkasan").AddPivotTable(Definition(workbook));

        XlsxValidator.AssertValid(Save(workbook));
    }

    [Fact]
    public void TheFourPartsAreAllPresentAndLinked()
    {
        using var workbook = BuildSource();
        workbook.AddSheet("Ringkasan").AddPivotTable(Definition(workbook));

        using var package = Reopen(Save(workbook));

        // sheet -> pivotTable -> cacheDefinition -> cacheRecords, and workbook -> cacheDefinition.
        var sheet = package.Parts.Single(p => p.Name.ToString() == "/xl/worksheets/sheet2.xml");
        var table = sheet.RelatedPartByType(RelationshipTypes.PivotTable);
        Assert.NotNull(table);

        var definition = table.RelatedPartByType(RelationshipTypes.PivotCacheDefinition);
        Assert.NotNull(definition);

        var records = definition.RelatedPartByType(RelationshipTypes.PivotCacheRecords);
        Assert.NotNull(records);

        var fromWorkbook = package.MainDocumentPart!
            .RelatedPartByType(RelationshipTypes.PivotCacheDefinition);

        Assert.Equal(definition.Name, fromWorkbook?.Name);
    }

    [Fact]
    public void TheWorkbookAndTheTableAgreeOnTheCacheId()
    {
        // A mismatch here is the classic unreadable-content prompt for pivots: both parts parse,
        // and the file still opens damaged.
        using var workbook = BuildSource();
        workbook.AddSheet("Ringkasan").AddPivotTable(Definition(workbook));

        using var package = Reopen(Save(workbook));

        var fromWorkbook = package.MainDocumentPart!.Xml.Root!
            .Element(Ns.S + "pivotCaches")!
            .Elements(Ns.S + "pivotCache")
            .Select(e => e.Attr("cacheId"))
            .Single();

        var table = package.Parts.Single(p => p.Name.ToString().Contains("pivotTable"));

        Assert.Equal(fromWorkbook, table.Xml.Root!.Attr("cacheId"));
    }

    [Fact]
    public void RecordIndicesPointAtTheRightSharedItems()
    {
        // The rule that silently corrupts a pivot: a record's index is a position in that field's
        // sharedItems. Off by one and the labels are right but attached to the wrong numbers.
        using var workbook = BuildSource();
        workbook.AddSheet("Ringkasan").AddPivotTable(Definition(workbook));

        using var package = Reopen(Save(workbook));

        var definition = package.Parts.Single(p => p.Name.ToString().Contains("pivotCacheDefinition"));
        var records = package.Parts.Single(p => p.Name.ToString().Contains("pivotCacheRecords"));

        var regionItems = definition.Xml.Root!.Element(Ns.S + "cacheFields")!
            .Elements(Ns.S + "cacheField").First()
            .Element(Ns.S + "sharedItems")!
            .Elements(Ns.S + "s").Select(e => e.Attr("v")).ToList();

        Assert.Equal(["Jakarta", "Bandung", "Surabaya"], regionItems);

        // Row 0 is Jakarta (0), row 2 is Bandung (1), row 4 is Surabaya (2).
        var rows = records.Xml.Root!.Elements(Ns.S + "r").ToList();

        Assert.Equal(5, rows.Count);
        Assert.Equal("0", rows[0].Elements().First().Attr("v"));
        Assert.Equal("1", rows[2].Elements().First().Attr("v"));
        Assert.Equal("2", rows[4].Elements().First().Attr("v"));

        // recordCount must match the records part or Excel rejects the cache.
        Assert.Equal("5", definition.Xml.Root!.Attr("recordCount"));
    }

    [Fact]
    public void ANumericFieldIsCachedAsNumbersNotSharedStrings()
    {
        using var workbook = BuildSource();
        workbook.AddSheet("Ringkasan").AddPivotTable(Definition(workbook));

        using var package = Reopen(Save(workbook));

        var total = package.Parts
            .Single(p => p.Name.ToString().Contains("pivotCacheDefinition"))
            .Xml.Root!.Element(Ns.S + "cacheFields")!
            .Elements(Ns.S + "cacheField")
            .Single(e => e.Attr("name") == "Total")
            .Element(Ns.S + "sharedItems")!;

        Assert.Equal("1", total.Attr("containsNumber"));
        Assert.Equal("0", total.Attr("containsString"));
        Assert.Equal("300000", total.Attr("minValue"));
        Assert.Equal("1200000", total.Attr("maxValue"));
    }

    [Fact]
    public void EveryCacheFieldGetsAPivotFieldInTheSameOrder()
    {
        // A missing pivotField shifts every index after it, so the axes point at the wrong columns.
        using var workbook = BuildSource();
        workbook.AddSheet("Ringkasan").AddPivotTable(Definition(workbook));

        using var package = Reopen(Save(workbook));

        var fields = package.Parts
            .Single(p => p.Name.ToString().Contains("pivotTable"))
            .Xml.Root!.Element(Ns.S + "pivotFields")!
            .Elements(Ns.S + "pivotField").ToList();

        Assert.Equal(4, fields.Count);
        Assert.Equal("axisRow", fields[0].Attr("axis"));
        Assert.Equal("axisCol", fields[1].Attr("axis"));
        Assert.Null(fields[2].Attr("axis"));
        Assert.Equal("1", fields[3].Attr("dataField"));
    }

    [Fact]
    public void RowFieldsAndRowItemsAreSiblings()
    {
        // They are separate elements in the schema, not nested. Nesting them parses but Excel
        // reports the part as damaged.
        using var workbook = BuildSource();
        workbook.AddSheet("Ringkasan").AddPivotTable(Definition(workbook));

        using var package = Reopen(Save(workbook));

        var root = package.Parts
            .Single(p => p.Name.ToString().Contains("pivotTable"))
            .Xml.Root!;

        Assert.Single(root.Elements(Ns.S + "rowFields"));
        Assert.Single(root.Elements(Ns.S + "rowItems"));
        Assert.Single(root.Elements(Ns.S + "colFields"));
        Assert.Single(root.Elements(Ns.S + "colItems"));
    }

    [Fact]
    public void AnUnknownFieldIsRejectedWithTheAvailableOnesListed()
    {
        using var workbook = BuildSource();
        var summary = workbook.AddSheet("Ringkasan");

        var exception = Assert.Throws<OfficeNetException>(() =>
            summary.AddPivotTable(Definition(workbook) with { Rows = ["Provinsi"] }));

        Assert.Contains("Provinsi", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Wilayah", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APivotWithNoValueFieldIsRejected()
    {
        using var workbook = BuildSource();
        var summary = workbook.AddSheet("Ringkasan");

        Assert.Throws<OfficeNetException>(() =>
            summary.AddPivotTable(Definition(workbook) with { Values = [] }));
    }

    [Fact]
    public void ANonSumFunctionIsNamedInTheDataField()
    {
        using var workbook = BuildSource();

        workbook.AddSheet("Ringkasan").AddPivotTable(Definition(workbook) with
        {
            Values = [new PivotValue("Qty", PivotFunction.Average, Caption: "Rata-rata Qty")],
        });

        using var package = Reopen(Save(workbook));

        var field = package.Parts
            .Single(p => p.Name.ToString().Contains("pivotTable"))
            .Xml.Root!.Element(Ns.S + "dataFields")!
            .Elements(Ns.S + "dataField").Single();

        Assert.Equal("average", field.Attr("subtotal"));
        Assert.Equal("Rata-rata Qty", field.Attr("name"));
    }

    [Fact]
    public void TwoPivotTablesGetDistinctCacheIds()
    {
        using var workbook = BuildSource();
        var summary = workbook.AddSheet("Ringkasan");

        summary.AddPivotTable(Definition(workbook) with { Target = CellReference.Parse("A1") });
        summary.AddPivotTable(Definition(workbook) with { Target = CellReference.Parse("A20") });

        using var package = Reopen(Save(workbook));

        var ids = package.MainDocumentPart!.Xml.Root!
            .Element(Ns.S + "pivotCaches")!
            .Elements(Ns.S + "pivotCache")
            .Select(e => e.Attr("cacheId"))
            .ToList();

        Assert.Equal(2, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ABlankRowInTheSourceIsNotCachedAsARecord()
    {
        // A wholly blank row is a gap, not a record; caching it adds a blank item to every axis.
        using var workbook = BuildSource();

        // Row 7 stays empty, row 8 has data — the range spans the gap.
        var sheet = workbook["Data"];
        sheet[7, 0].Set("Medan");
        sheet[7, 1].Set("PdfNet");
        sheet[7, 2].Set(4);
        sheet[7, 3].Set(400_000);

        workbook.AddSheet("Ringkasan").AddPivotTable(Definition(workbook) with
        {
            SourceRange = CellRangeReference.Parse("A1:D8"),
        });

        using var package = Reopen(Save(workbook));

        var definition = package.Parts.Single(p => p.Name.ToString().Contains("pivotCacheDefinition"));

        // Five original rows plus Medan; the blank row between them is skipped.
        Assert.Equal("6", definition.Xml.Root!.Attr("recordCount"));
    }
}
