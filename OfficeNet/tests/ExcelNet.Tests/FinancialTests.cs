// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet;
using Xunit;

namespace ExcelNet.Tests;

/// <summary>
/// The financial functions, checked against the worked examples on Microsoft's own function pages.
/// </summary>
/// <remarks>
/// Every expected value here was computed by Excel and published by Microsoft, not derived from
/// the formulas this library implements — a test that re-derives the answer with the same formula
/// proves only that the formula agrees with itself.
/// </remarks>
public class FinancialTests
{
    private static double Evaluate(string formula, Action<Worksheet>? setup = null)
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        setup?.Invoke(sheet);
        sheet["Z1"].SetFormula(formula);
        workbook.Recalculate();
        return sheet["Z1"].Number;
    }

    private static string EvaluateText(string formula, Action<Worksheet>? setup = null)
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];
        setup?.Invoke(sheet);
        sheet["Z1"].SetFormula(formula);
        workbook.Recalculate();
        return sheet["Z1"].Text;
    }

    [Theory]
    [InlineData("PMT(8%/12, 10, 10000)", -1037.03)]
    [InlineData("PMT(8%/12, 10, 10000, 0, 1)", -1030.16)]
    [InlineData("PMT(6%/12, 18*12, 0, 50000)", -129.08)]
    [InlineData("PMT(0, 10, 1000)", -100)]
    public void Pmt(string formula, double expected) =>
        Assert.Equal(expected, Evaluate(formula), 2);

    [Theory]
    [InlineData("FV(0.06/12, 10, -200, -500, 1)", 2581.40)]
    [InlineData("FV(0.12/12, 12, -1000)", 12682.50)]
    [InlineData("FV(0.11/12, 35, -2000, , 1)", 82846.25)]
    public void Fv(string formula, double expected) =>
        Assert.Equal(expected, Evaluate(formula), 2);

    [Fact]
    public void Pv() => Assert.Equal(-59777.15, Evaluate("PV(0.08/12, 12*20, 500, , 0)"), 2);

    [Theory]
    [InlineData("NPER(0.12/12, -100, -1000, 10000, 1)", 59.6738657)]
    [InlineData("NPER(0.12/12, -100, -1000, 10000)", 60.0821229)]
    [InlineData("NPER(0.12/12, -100, -1000)", -9.57859404)]
    public void Nper(string formula, double expected) =>
        Assert.Equal(expected, Evaluate(formula), 6);

    [Theory]
    [InlineData("RATE(4*12, -200, 8000)", 0.0077014725)]
    [InlineData("RATE(4*12, -200, 8000)*12", 0.0924176698)]
    public void Rate(string formula, double expected) =>
        Assert.Equal(expected, Evaluate(formula), 8);

    [Fact]
    public void NpvDiscountsTheFirstValueOnePeriod() =>
        Assert.Equal(1188.44, Evaluate("NPV(10%, -10000, 3000, 4200, 6800)"), 2);

    [Fact]
    public void NpvReadsARangeAndSkipsText()
    {
        // Microsoft's second example: an initial cost outside NPV, the returns in a range.
        Assert.Equal(1922.06, Evaluate("NPV(8%, B2:B7)+B1", s =>
        {
            // Text in the middle of the range is skipped, not read as zero: the values after it
            // keep their periods. At the end of the range a zero would change nothing, so it
            // would prove nothing.
            s["B1"].Set(-40000);
            s["B2"].Set(8000);
            s["B3"].Set(9200);
            s["B4"].Set("teks diabaikan");
            s["B5"].Set(10000);
            s["B6"].Set(12000);
            s["B7"].Set(14500);
        }), 2);
    }

    private static void IrrData(Worksheet s)
    {
        s["A1"].Set(-70000);
        s["A2"].Set(12000);
        s["A3"].Set(15000);
        s["A4"].Set(18000);
        s["A5"].Set(21000);
        s["A6"].Set(26000);
    }

    [Theory]
    [InlineData("IRR(A1:A5)", -0.021244848)]
    [InlineData("IRR(A1:A6)", 0.086630948)]
    [InlineData("IRR(A1:A3, -10%)", -0.443506941)]
    public void Irr(string formula, double expected) =>
        Assert.Equal(expected, Evaluate(formula, IrrData), 6);

    private static void ScheduleData(Worksheet s)
    {
        s["A1"].Set(-10000);
        s["A2"].Set(2750);
        s["A3"].Set(4250);
        s["A4"].Set(3250);
        s["A5"].Set(2750);
        s["B1"].Set(new DateTime(2008, 1, 1));
        s["B2"].Set(new DateTime(2008, 3, 1));
        s["B3"].Set(new DateTime(2008, 10, 30));
        s["B4"].Set(new DateTime(2009, 2, 15));
        s["B5"].Set(new DateTime(2009, 4, 1));
    }

    [Fact]
    public void Xnpv() => Assert.Equal(2086.65, Evaluate("XNPV(0.09, A1:A5, B1:B5)", ScheduleData), 2);

    [Fact]
    public void Xirr() => Assert.Equal(0.373362535, Evaluate("XIRR(A1:A5, B1:B5)", ScheduleData), 6);

    [Theory]
    [InlineData(0.05, 0.10)]
    [InlineData(0.25, 0.20)]
    public void TheGuessPicksBetweenTwoRates(double guess, double expected)
    {
        // -100, 230, -132 has exactly two IRRs: 100x² - 230x + 132 = 0 gives 1+r = 1.1 or 1.2.
        // Which one Newton finds depends on where it starts, which is the point of the argument.
        Assert.Equal(expected, Evaluate($"IRR(A1:A3, {guess.ToString(System.Globalization.CultureInfo.InvariantCulture)})", s =>
        {
            s["A1"].Set(-100);
            s["A2"].Set(230);
            s["A3"].Set(-132);
        }), 8);
    }

    [Fact]
    public void CashFlowsThatNeverChangeSignHaveNoRate()
    {
        // Returning the last Newton iterate here would put a confident, meaningless rate in a model.
        Assert.Equal("#NUM!", EvaluateText("IRR(A1:A3)", s =>
        {
            s["A1"].Set(100);
            s["A2"].Set(200);
            s["A3"].Set(300);
        }));
    }

    [Fact]
    public void APaymentThatNeverCoversTheInterestHasNoTerm() =>
        Assert.Equal("#NUM!", EvaluateText("NPER(10%, -50, 1000)"));
}
