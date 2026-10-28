// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet;
using OfficeNet.Core;
using Xunit;

namespace ExcelNet.Tests;

public class SortTests
{
    private static string[] Column(Worksheet sheet, string column, int first, int last) =>
        [.. Enumerable.Range(first, last - first + 1).Select(r => sheet[$"{column}{r}"].Text)];

    [Fact]
    public void ExcelsTypeOrderWithBlanksLast()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        sheet.WriteColumn("A1", "b", 10, true, "A", 2);
        sheet["A7"].Set(CellValue.FromError("#N/A"));
        // A6 left blank.

        sheet.Sort("A1:A7", new SortKey("A"));
        Assert.Equal(["2", "10", "A", "b", "TRUE", "#N/A", ""], Column(sheet, "A", 1, 7));

        // Descending reverses the types too — but blanks stay at the bottom.
        sheet.Sort("A1:A7", new SortKey("A", Descending: true));
        Assert.Equal(["#N/A", "TRUE", "b", "A", "10", "2", ""], Column(sheet, "A", 1, 7));
    }

    [Fact]
    public void TextIgnoresCaseAndTheSortIsStable()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        sheet.WriteRow("A1", "bandung", 1);
        sheet.WriteRow("A2", "Aceh", 2);
        sheet.WriteRow("A3", "BANDUNG", 3);
        sheet.WriteRow("A4", "aceh", 4);

        sheet.Sort("A1:B4", new SortKey("A"));

        // Ties keep their original order: Aceh(2) before aceh(4), bandung(1) before BANDUNG(3).
        Assert.Equal(["2", "4", "1", "3"], Column(sheet, "B", 1, 4));
    }

    [Fact]
    public void ManyTiesKeepTheirOrder()
    {
        // .NET's List.Sort switches from insertion sort (stable by accident) to introsort above
        // sixteen elements, so stability has to be shown on a range that size, not on four rows.
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        for (var i = 0; i < 200; i++)
        {
            sheet.WriteRow($"A{i + 1}", i % 3, i);
        }

        sheet.Sort("A1:B200", new SortKey("A"));

        var original = Enumerable.Range(0, 200).Select(i => sheet[$"B{i + 1}"].Number).ToList();
        foreach (var group in original.GroupBy(n => (int)n % 3))
        {
            Assert.Equal(group.Order(), group);
        }
    }

    [Fact]
    public void ASecondKeyBreaksTies()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        sheet.WriteRow("A1", "Jakarta", 300);
        sheet.WriteRow("A2", "Bandung", 500);
        sheet.WriteRow("A3", "Jakarta", 900);
        sheet.WriteRow("A4", "Bandung", 100);

        sheet.Sort("A1:B4", new SortKey("A"), new SortKey("B", Descending: true));

        Assert.Equal(["Bandung", "Bandung", "Jakarta", "Jakarta"], Column(sheet, "A", 1, 4));
        Assert.Equal(["500", "100", "900", "300"], Column(sheet, "B", 1, 4));
    }

    [Fact]
    public void FormulasMoveWithTheirRowsAndStillComputeTheirRow()
    {
        // The reason relative references are shifted: without it, the total of the row that moved
        // to row 2 would still multiply row 4's cells.
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        sheet.WriteHeader("A1", ["Produk", "Qty", "Harga", "Total"]);
        sheet.WriteRow("A2", "Kertas", 2, 50000);
        sheet.WriteRow("A3", "Tinta", 1, 125000);
        sheet.WriteRow("A4", "Map", 10, 3000);

        for (var row = 2; row <= 4; row++)
        {
            sheet[$"D{row}"].SetFormula($"B{row}*C{row}*$F$1");
        }

        sheet["F1"].Set(1);
        sheet.Sort("A2:D4", new SortKey("C"));
        workbook.Recalculate();

        Assert.Equal(["Map", "Kertas", "Tinta"], Column(sheet, "A", 2, 4));
        Assert.Equal("B2*C2*$F$1", sheet["D2"].Formula);
        Assert.Equal(30000, sheet["D2"].Number);
        Assert.Equal(100000, sheet["D3"].Number);
        Assert.Equal(125000, sheet["D4"].Number);
    }

    [Theory]
    [InlineData("A1+B1", 3, "A4+B4")]
    [InlineData("$A1+A$1+$A$1", 2, "$A3+A$1+$A$1")]
    [InlineData("SUM(A1:A3)", 1, "SUM(A2:A4)")]
    [InlineData("Harga!B2*2", 3, "Harga!B5*2")]
    [InlineData("LOG10(A1)", 1, "LOG10(A2)")]
    [InlineData("\"A1 tetap teks\"&A1", 1, "\"A1 tetap teks\"&A2")]
    [InlineData("A2", -1, "A1")]
    [InlineData("A1", -1, "#REF!")]
    public void RelativeRowsShiftAndEverythingElseStays(string formula, int delta, string expected) =>
        Assert.Equal(expected, ExcelNet.Formulas.FormulaText.ShiftRows(formula, delta));

    [Fact]
    public void StylesAndCommentsTravelWithTheRow()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        sheet["A1"].Set("z").Bold().WithComment("baris z");
        sheet["A2"].Set("a");

        sheet.Sort("A1:A2", new SortKey("A"));

        Assert.Equal("z", sheet["A2"].Text);
        Assert.True(sheet["A2"].Style.Font.Bold);
        Assert.Equal("baris z", sheet["A2"].Comment);
        Assert.Null(sheet["A1"].Comment);
    }

    [Fact]
    public void MergedCellsAndKeysOutsideTheRangeAreRefused()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        sheet.WriteColumn("A1", "b", "a");
        sheet.MergeCells("A1:B1");

        Assert.Throws<OfficeNetException>(() => sheet.Sort("A1:B2", new SortKey("A")));
        Assert.Throws<ArgumentException>(() => sheet.Sort("A2:B2", new SortKey("D")));
    }
}
