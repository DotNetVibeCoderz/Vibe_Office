using VibeDesk.Scripting.Host;
using Xunit;

namespace VibeDesk.Tests.Scripting;

/// <summary>
/// The tabular type scripts use instead of pandas. The cases worth pinning are the coercion ones:
/// a sheet hands over strings, and "10" must not sort before "9".
/// </summary>
public class DataFrameTests
{
    private static DataFrame Sales() => new(
        ["Region", "Rep", "Revenue", "Units"],
        [
            ["West", "Sari", 4000d, 12d],
            ["East", "Budi", 1500d, 5d],
            ["West", "Dewi", 2500d, 9d],
            ["North", "Rizki", 900d, 3d],
            ["East", "Maya", 3100d, 11d],
        ]);

    [Fact]
    public void ExposesColumnsAndRowCount()
    {
        var df = Sales();

        Assert.Equal(["Region", "Rep", "Revenue", "Units"], df.Columns);
        Assert.Equal(5, df.Count);
    }

    [Fact]
    public void RowsAreKeyedByColumnName()
    {
        var first = Sales().Rows[0];

        Assert.Equal("West", first["Region"]);
        Assert.Equal(4000d, first["Revenue"]);
    }

    [Theory]
    [InlineData("==", "West", 2)]
    [InlineData("!=", "West", 3)]
    [InlineData("contains", "es", 2)]
    [InlineData("startswith", "N", 1)]
    public void FiltersOnText(string op, string value, int expected) =>
        Assert.Equal(expected, Sales().Where("Region", op, value).Count);

    [Theory]
    [InlineData(">", 2000, 3)]
    [InlineData(">=", 2500, 3)]
    [InlineData("<", 1500, 1)]
    public void FiltersOnNumbers(string op, double value, int expected) =>
        Assert.Equal(expected, Sales().Where("Revenue", op, value).Count);

    [Fact]
    public void ComparesNumbersNumericallyEvenWhenTheyArriveAsText()
    {
        // A sheet hands over strings. Compared as text, "9" > "10" — which is the bug this prevents.
        var df = new DataFrame(["N"], [["9"], ["10"], ["100"]]);

        Assert.Equal(2, df.Where("N", ">=", 10).Count);
        Assert.Equal("9", df.SortBy("N").Cell(0, "N"));
        Assert.Equal("100", df.SortBy("N", descending: true).Cell(0, "N"));
    }

    [Fact]
    public void FilteringAnUnknownColumnYieldsNothingRatherThanEverything()
    {
        // Silently returning every row would make a typo look like a successful no-op filter.
        Assert.Equal(0, Sales().Where("Nope", "==", "x").Count);
    }

    [Fact]
    public void GroupsAndAggregates()
    {
        var grouped = Sales().GroupBy("Region", "Revenue", "sum").SortBy("sum(Revenue)", descending: true);

        Assert.Equal(3, grouped.Count);
        Assert.Equal("West", grouped.Cell(0, "Region"));
        Assert.Equal(6500d, grouped.Cell(0, "sum(Revenue)"));
    }

    [Theory]
    [InlineData("sum", 12000d)]
    [InlineData("avg", 2400d)]
    [InlineData("min", 900d)]
    [InlineData("max", 4000d)]
    [InlineData("count", 5d)]
    public void AggregatesAcrossTheWholeColumn(string agg, double expected)
    {
        var result = Sales().GroupBy("Region", "Revenue", agg);

        // One group per region; summing the aggregate only matches the whole-column figure for
        // sum and count, so this asserts through the direct helpers instead.
        Assert.Equal(3, result.Count);

        var direct = agg switch
        {
            "sum" => Sales().Sum("Revenue"),
            "avg" => Sales().Average("Revenue"),
            "min" => Sales().Min("Revenue"),
            "max" => Sales().Max("Revenue"),
            _ => Sales().Count,
        };

        Assert.Equal(expected, direct);
    }

    [Fact]
    public void PivotsIntoOneColumnPerDistinctValue()
    {
        var pivot = Sales().Pivot("Region", "Rep", "Revenue", "sum");

        Assert.Equal("Region", pivot.Columns[0]);
        Assert.Equal(6, pivot.Columns.Count);      // Region + five reps
        Assert.Equal(3, pivot.Count);              // three regions
    }

    [Fact]
    public void SelectsAndReordersColumns()
    {
        var narrowed = Sales().Select("Rep", "Region");

        Assert.Equal(["Rep", "Region"], narrowed.Columns);
        Assert.Equal("Sari", narrowed.Cell(0, "Rep"));
    }

    [Fact]
    public void DistinctKeepsTheFirstRowPerKey()
    {
        var deduped = Sales().Distinct("Region");

        Assert.Equal(3, deduped.Count);
        Assert.Equal("Sari", deduped.Cell(0, "Rep"));
    }

    [Fact]
    public void AddComputedAppendsADerivedColumn()
    {
        var withAverage = Sales().AddComputed("PerUnit", "Revenue", "/", "Units");

        Assert.Equal("PerUnit", withAverage.Columns[^1]);
        Assert.Equal(4000d / 12d, (double)withAverage.Cell(0, "PerUnit")!, 6);
    }

    [Fact]
    public void DivisionByZeroYieldsZeroRatherThanInfinity()
    {
        var df = new DataFrame(["A", "B"], [[10d, 0d]]);

        Assert.Equal(0d, df.AddComputed("R", "A", "/", "B").Cell(0, "R"));
    }

    [Fact]
    public void HeadAndTailBound()
    {
        Assert.Equal(2, Sales().Head(2).Count);
        Assert.Equal("Maya", Sales().Tail(1).Cell(0, "Rep"));
        Assert.Equal(0, Sales().Head(0).Count);
    }

    [Fact]
    public void CsvQuotesFieldsThatNeedIt()
    {
        var df = new DataFrame(["Name"], [["Doe, Jane"], ["He said \"hi\""]]);
        var csv = df.ToCsv();

        Assert.Contains("\"Doe, Jane\"", csv);
        Assert.Contains("\"He said \"\"hi\"\"\"", csv);
    }

    [Fact]
    public void EmptyFrameRendersWithoutThrowing()
    {
        var empty = new DataFrame(["A"], []);

        Assert.Equal(0, empty.Count);
        Assert.Contains("no rows", empty.ToString());
        Assert.Equal(0d, empty.Sum("A"));
    }
}
