// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Xunit;

namespace ExcelNet.Tests;

public class LookupTests
{
    /// <summary>
    /// A three-column price list, deliberately wider than two columns.
    /// </summary>
    /// <remarks>
    /// Two columns was the only shape the old implementation could serve, because the range arrived
    /// flattened and the width had to be guessed from the lookup index. Every table here is three
    /// wide so that a guess would give the wrong answer rather than the right one by luck.
    /// </remarks>
    private static Workbook PriceList()
    {
        var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["Kode", "Nama", "Harga"]);
        sheet.WriteRow("A2", "A100", "Kabel", 15000);
        sheet.WriteRow("A3", "B200", "Adaptor", 45000);
        sheet.WriteRow("A4", "C300", "Baterai", 27500);
        sheet.WriteRow("A5", "D400", "Casing", 89000);

        return workbook;
    }

    private static object? Evaluate(Workbook workbook, string formula)
    {
        var sheet = workbook["Data"];
        sheet["H1"].SetFormula(formula);
        workbook.Recalculate();

        return sheet["H1"].Value.AsObject();
    }

    [Fact]
    public void VLookupReadsAThreeColumnTable()
    {
        using var workbook = PriceList();

        Assert.Equal("Baterai", Evaluate(workbook, "VLOOKUP(\"C300\", A2:C5, 2, FALSE)"));
        Assert.Equal(27500d, Evaluate(workbook, "VLOOKUP(\"C300\", A2:C5, 3, FALSE)"));
    }

    [Fact]
    public void VLookupReturnsRefWhenTheColumnIsPastTheTable()
    {
        // The old implementation widened the table to fit the index instead, so asking for column 5
        // of a three-column table read whatever happened to be at that offset in the flat list.
        using var workbook = PriceList();

        Assert.Equal("#REF!", Evaluate(workbook, "VLOOKUP(\"C300\", A2:C5, 5, FALSE)"));
    }

    [Fact]
    public void VLookupApproximateTakesTheLargestKeyNotOver()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        // A commission table: the classic use, and the one that needs sorted keys.
        sheet.WriteRow("A1", 0, "Bronze");
        sheet.WriteRow("A2", 1000, "Silver");
        sheet.WriteRow("A3", 5000, "Gold");
        sheet.WriteRow("A4", 20000, "Platinum");

        Assert.Equal("Silver", Evaluate(workbook, "VLOOKUP(3000, A1:B4, 2)"));
        Assert.Equal("Gold", Evaluate(workbook, "VLOOKUP(5000, A1:B4, 2)"));
        Assert.Equal("Platinum", Evaluate(workbook, "VLOOKUP(999999, A1:B4, 2)"));

        // Below the first key there is nothing to fall back to.
        Assert.Equal("#N/A", Evaluate(workbook, "VLOOKUP(-1, A1:B4, 2)"));
    }

    [Fact]
    public void ApproximateIsTheDefault()
    {
        // Excel's default, and the top source of a silently wrong answer in a real spreadsheet. It
        // is kept because a formula that behaves differently here than in Excel is worse than one
        // that shares its trap.
        using var workbook = PriceList();

        // "B999" is not in the list; approximate matching finds the largest key not over it.
        Assert.Equal("Adaptor", Evaluate(workbook, "VLOOKUP(\"B999\", A2:C5, 2)"));
        Assert.Equal("#N/A", Evaluate(workbook, "VLOOKUP(\"B999\", A2:C5, 2, FALSE)"));
    }

    [Fact]
    public void AnExactVLookupSupportsWildcards()
    {
        using var workbook = PriceList();

        Assert.Equal("Adaptor", Evaluate(workbook, "VLOOKUP(\"B*\", A2:C5, 2, FALSE)"));
        Assert.Equal("Baterai", Evaluate(workbook, "VLOOKUP(\"C3??\", A2:C5, 2, FALSE)"));
    }

    [Fact]
    public void HLookupReadsAcrossInsteadOfDown()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteRow("A1", "Q1", "Q2", "Q3", "Q4");
        sheet.WriteRow("A2", 1200, 1450, 1310, 1680);
        sheet.WriteRow("A3", "naik", "naik", "turun", "naik");

        Assert.Equal(1310d, Evaluate(workbook, "HLOOKUP(\"Q3\", A1:D3, 2, FALSE)"));
        Assert.Equal("turun", Evaluate(workbook, "HLOOKUP(\"Q3\", A1:D3, 3, FALSE)"));
        Assert.Equal("#REF!", Evaluate(workbook, "HLOOKUP(\"Q3\", A1:D3, 9, FALSE)"));
    }

    [Fact]
    public void IndexTakesARowAndAColumn()
    {
        using var workbook = PriceList();

        Assert.Equal("Adaptor", Evaluate(workbook, "INDEX(A2:C5, 2, 2)"));
        Assert.Equal(89000d, Evaluate(workbook, "INDEX(A2:C5, 4, 3)"));
        Assert.Equal("#REF!", Evaluate(workbook, "INDEX(A2:C5, 9, 1)"));
    }

    [Fact]
    public void IndexOverASingleRowCountsAlongIt()
    {
        // INDEX(A1:E1, 3) is the third cell, not the third row of a one-row range.
        using var workbook = Workbook.Create("Data");
        workbook["Data"].WriteRow("A1", "a", "b", "c", "d");

        Assert.Equal("c", Evaluate(workbook, "INDEX(A1:D1, 3)"));
    }

    [Fact]
    public void IndexOverASingleColumnCountsDownIt()
    {
        using var workbook = PriceList();

        Assert.Equal("C300", Evaluate(workbook, "INDEX(A2:A5, 3)"));
    }

    [Fact]
    public void MatchReturnsAOneBasedPosition()
    {
        using var workbook = PriceList();

        Assert.Equal(3d, Evaluate(workbook, "MATCH(\"C300\", A2:A5, 0)"));
        Assert.Equal("#N/A", Evaluate(workbook, "MATCH(\"Z999\", A2:A5, 0)"));
    }

    [Fact]
    public void MatchDefaultsToTheLargestValueNotOver()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"].WriteColumn("A1", 10, 20, 30, 40);

        Assert.Equal(2d, Evaluate(workbook, "MATCH(25, A1:A4)"));
        Assert.Equal(4d, Evaluate(workbook, "MATCH(40, A1:A4)"));
        Assert.Equal("#N/A", Evaluate(workbook, "MATCH(5, A1:A4)"));
    }

    [Fact]
    public void MatchWithMinusOneWalksDescendingData()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"].WriteColumn("A1", 40, 30, 20, 10);

        Assert.Equal(2d, Evaluate(workbook, "MATCH(25, A1:A4, -1)"));
    }

    [Fact]
    public void IndexAndMatchTogetherLookLeftwards()
    {
        // The reason the pair exists: VLOOKUP cannot return a column to the left of its key.
        using var workbook = PriceList();

        Assert.Equal("B200", Evaluate(workbook, "INDEX(A2:A5, MATCH(\"Adaptor\", B2:B5, 0))"));
    }

    [Fact]
    public void XLookupDefaultsToAnExactMatch()
    {
        // Unlike VLOOKUP, whose default has cost more spreadsheets more silent errors than any other
        // default in the format.
        using var workbook = PriceList();

        Assert.Equal("Baterai", Evaluate(workbook, "XLOOKUP(\"C300\", A2:A5, B2:B5)"));
        Assert.Equal("#N/A", Evaluate(workbook, "XLOOKUP(\"C999\", A2:A5, B2:B5)"));
    }

    [Fact]
    public void XLookupCanReturnAColumnLeftOfItsKey()
    {
        using var workbook = PriceList();

        Assert.Equal("B200", Evaluate(workbook, "XLOOKUP(\"Adaptor\", B2:B5, A2:A5)"));
    }

    [Fact]
    public void XLookupCarriesItsOwnMissingValue()
    {
        using var workbook = PriceList();

        Assert.Equal("tidak ada",
            Evaluate(workbook, "XLOOKUP(\"Z999\", A2:A5, B2:B5, \"tidak ada\")"));
    }

    [Fact]
    public void XLookupFindsTheNearestSmallerOrLargerOnUnsortedData()
    {
        // Unlike VLOOKUP's approximate mode, which assumes ascending order and silently misreads
        // anything else, XLOOKUP scans the whole range.
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteColumn("A1", 30, 10, 40, 20);
        sheet.WriteColumn("B1", "tiga", "satu", "empat", "dua");

        Assert.Equal("dua", Evaluate(workbook, "XLOOKUP(25, A1:A4, B1:B4, \"-\", -1)"));
        Assert.Equal("tiga", Evaluate(workbook, "XLOOKUP(25, A1:A4, B1:B4, \"-\", 1)"));
    }

    [Fact]
    public void XLookupCanSearchBackwards()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteColumn("A1", "x", "y", "x", "z");
        sheet.WriteColumn("B1", "pertama", "kedua", "ketiga", "keempat");

        Assert.Equal("pertama", Evaluate(workbook, "XLOOKUP(\"x\", A1:A4, B1:B4)"));
        Assert.Equal("ketiga", Evaluate(workbook, "XLOOKUP(\"x\", A1:A4, B1:B4, \"-\", 0, -1)"));
    }

    [Fact]
    public void XLookupWildcardModeIsOptIn()
    {
        // Mode 2, not the default: a key that happens to contain an asterisk should match itself.
        using var workbook = PriceList();

        Assert.Equal("#N/A", Evaluate(workbook, "XLOOKUP(\"B*\", A2:A5, B2:B5)"));
        Assert.Equal("Adaptor", Evaluate(workbook, "XLOOKUP(\"B*\", A2:A5, B2:B5, \"-\", 2)"));
    }

    [Fact]
    public void LookupsSurviveASaveAndReopen()
    {
        // The formula is stored as text and its result as a cached value, so a reader that never
        // evaluates still shows the right number. Both have to be there.
        byte[] bytes;

        using (var workbook = PriceList())
        {
            workbook["Data"]["E1"].SetFormula("XLOOKUP(\"C300\", A2:A5, C2:C5)");
            workbook.Recalculate();

            using var stream = new MemoryStream();
            workbook.Save(stream);
            bytes = stream.ToArray();
        }

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));

        Assert.Equal("XLOOKUP(\"C300\", A2:A5, C2:C5)", reopened["Data"]["E1"].Formula);
        Assert.Equal(27500d, reopened["Data"]["E1"].Number);
    }
}
