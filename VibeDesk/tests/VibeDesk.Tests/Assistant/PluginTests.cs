using VibeDesk.Ai.Plugins;
using Xunit;

namespace VibeDesk.Tests.Assistant;

/// <summary>
/// Mr Clippy's tools. These are the functions a model calls instead of guessing, so what matters is
/// that they return computed answers and fail closed rather than throwing into the chat loop.
/// </summary>
public class MathPluginTests
{
    private readonly MathPlugin _math = new();

    [Theory]
    [InlineData("1+1", "2")]
    [InlineData("=1+1", "2")]                     // a leading '=' is accepted, since models copy cells
    [InlineData("ROUND(1250*0.11, 2)", "137.5")]
    [InlineData("SUM(1,2,3,4)", "10")]
    [InlineData("SQRT(144)", "12")]
    public void CalculatesUsingTheSpreadsheetEngine(string expression, string expected) =>
        Assert.Equal(expected, _math.Calculate(expression));

    [Fact]
    public void ReturnsAnErrorValueRatherThanThrowing()
    {
        Assert.Equal("#DIV/0!", _math.Calculate("1/0"));
        Assert.StartsWith("#", _math.Calculate("NOSUCHFN(1)"));
    }

    [Fact]
    public void EmptyExpressionIsReportedNotCrashed() =>
        Assert.Contains("#VALUE!", _math.Calculate("   "));
}

public class TimePluginTests
{
    private readonly TimePlugin _time = new("Asia/Jakarta");

    [Fact]
    public void ReportsTheCurrentDateInTheConfiguredZone()
    {
        var answer = _time.Now();

        Assert.Contains(DateTime.UtcNow.Year.ToString(), answer);
        Assert.Contains("UTC", answer);
    }

    [Theory]
    [InlineData("2026-01-01", 1, "2026-01-02")]
    [InlineData("2026-01-01", -1, "2025-12-31")]
    [InlineData("2026-02-28", 1, "2026-03-01")]   // 2026 is not a leap year
    public void ShiftsDatesAcrossMonthAndYearBoundaries(string from, int days, string expected) =>
        Assert.StartsWith(expected, _time.DateAdd(days: days, date: from));

    [Fact]
    public void CountsWholeDaysBetweenDates()
    {
        Assert.Equal(30, _time.DaysBetween("2026-01-01", "2026-01-31"));
        Assert.Equal(-30, _time.DaysBetween("2026-01-31", "2026-01-01"));
    }

    [Fact]
    public void UnknownTimeZoneFallsBackToUtcInsteadOfThrowing()
    {
        var plugin = new TimePlugin("Not/AZone");

        Assert.Contains("UTC", plugin.Now());
    }
}
