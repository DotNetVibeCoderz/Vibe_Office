using VibeDesk.Application.Spreadsheets;
using Xunit;

namespace VibeDesk.Tests.Spreadsheets;

/// <summary>
/// A1 addressing is bijective base-26-without-zero, which is exactly the kind of arithmetic that is
/// right for the first 26 columns and wrong at the boundaries. The Z/AA and ZZ/AAA rows are the
/// point of this class.
/// </summary>
public class CellAddressTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void ColumnIndexRoundTripsThroughLetters(int index, string letters)
    {
        Assert.Equal(letters, CellAddress.ColumnName(index));
        Assert.Equal(index, CellAddress.ColumnIndex(letters));
    }

    [Theory]
    [InlineData("A1", 0, 0)]
    [InlineData("B3", 2, 1)]
    [InlineData("AA10", 9, 26)]
    [InlineData("$C$5", 4, 2)]
    public void ParsesAddressesWithAndWithoutAbsoluteMarkers(string address, int row, int col)
    {
        Assert.True(CellAddress.TryParse(address, out var parsed));
        Assert.Equal(row, parsed.Row);
        Assert.Equal(col, parsed.Col);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1A")]
    [InlineData("A")]
    [InlineData("A0")]
    public void RejectsMalformedAddresses(string address) =>
        Assert.False(CellAddress.TryParse(address, out _));

    [Fact]
    public void RangeCoversEveryCellBetweenItsCorners()
    {
        Assert.True(CellRange.TryParse("A1:B2", out var range));

        var cells = range.Cells().Select(a => a.ToA1()).OrderBy(a => a, StringComparer.Ordinal).ToList();

        Assert.Equal(["A1", "A2", "B1", "B2"], cells);
    }

    [Fact]
    public void RangeNormalisesReversedCorners()
    {
        Assert.True(CellRange.TryParse("C3:A1", out var range));

        Assert.Equal(0, range.Start.Row);
        Assert.Equal(0, range.Start.Col);
        Assert.Equal(2, range.End.Row);
        Assert.Equal(2, range.End.Col);
    }
}
