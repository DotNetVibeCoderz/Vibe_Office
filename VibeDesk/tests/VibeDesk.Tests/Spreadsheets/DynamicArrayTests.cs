using VibeDesk.Application.Documents;
using VibeDesk.Application.Spreadsheets;
using Xunit;

namespace VibeDesk.Tests.Spreadsheets;

/// <summary>
/// SEQUENCE, SORT, UNIQUE, FILTER and LET. The value model is flat, so these are checked the way a
/// user would actually reach them: wrapped in an aggregate, or through INDEX, rather than by
/// inspecting an array nobody can see.
/// </summary>
public class DynamicArrayTests
{
    /// <summary>A1:A6 = 5, 3, 5, "b", "a", 1 — mixed on purpose, since ordering across types matters.</summary>
    private static FormulaEngine Grid() => new(new SpreadsheetModel
    {
        Sheets =
        [
            new SheetTab
            {
                Id = "s1",
                Name = "Sheet1",
                Cells = new()
                {
                    ["A1"] = new Cell { V = "5" },
                    ["A2"] = new Cell { V = "3" },
                    ["A3"] = new Cell { V = "5" },
                    ["A4"] = new Cell { V = "b" },
                    ["A5"] = new Cell { V = "a" },
                    ["A6"] = new Cell { V = "1" },

                    ["B1"] = new Cell { V = "TRUE" },
                    ["B2"] = new Cell { V = "FALSE" },
                    ["B3"] = new Cell { V = "TRUE" },
                },
            },
        ],
    });

    private static string Eval(string formula) => Grid().EvaluateFormula(formula).ToDisplayString();

    // ── SEQUENCE ──

    [Theory]
    [InlineData("=SUM(SEQUENCE(4))", "10")]                 // 1+2+3+4
    [InlineData("=COUNT(SEQUENCE(2,3))", "6")]              // rows * columns
    [InlineData("=SUM(SEQUENCE(3,1,10))", "33")]            // 10+11+12
    [InlineData("=SUM(SEQUENCE(3,1,0,5))", "15")]           // 0+5+10
    [InlineData("=INDEX(SEQUENCE(5),3)", "3")]
    public void SequenceGeneratesEvenlySpacedNumbers(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Theory]
    [InlineData("=SEQUENCE(0)")]
    [InlineData("=SEQUENCE(2,0)")]
    public void SequenceRefusesANonPositiveShape(string formula) =>
        Assert.Equal("#VALUE!", Eval(formula));

    [Fact]
    public void SequenceRefusesAnArrayLargeEnoughToStallARecalculation() =>
        // A mistyped SEQUENCE(1000000) is far more common than an intended one.
        Assert.Equal("#NUM!", Eval("=SEQUENCE(1000000)"));

    // ── SORT ──

    [Theory]
    [InlineData("=INDEX(SORT(A1:A6),1)", "1")]              // numbers first, ascending
    [InlineData("=INDEX(SORT(A1:A6),4)", "5")]              // 1, 3, 5, 5 - the column holds two 5s
    [InlineData("=INDEX(SORT(A1:A6),5)", "a")]              // then text, still ascending
    [InlineData("=INDEX(SORT(A1:A6,1,-1),1)", "b")]         // descending flips the whole order
    [InlineData("=SUM(SORT(A1:A6))", "14")]                 // sorting preserves the values
    public void SortOrdersNumbersBeforeText(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Fact]
    public void SortRefusesAColumnItCannotAddress() =>
        // Values carry no shape here, so sorting "by the second column" cannot be honoured — and
        // silently sorting by the first instead would be a wrong answer that looks right.
        Assert.Equal("#VALUE!", Eval("=SORT(A1:A6,2)"));

    [Fact]
    public void SortRefusesAnUnknownOrder() =>
        Assert.Equal("#VALUE!", Eval("=SORT(A1:A6,1,2)"));

    // ── UNIQUE ──

    [Theory]
    [InlineData("=COUNTA(UNIQUE(A1:A6))", "5")]             // 5, 3, b, a, 1 — one 5 dropped
    [InlineData("=INDEX(UNIQUE(A1:A6),1)", "5")]            // first-seen order, not sorted
    [InlineData("=COUNTA(UNIQUE(A1:A6,FALSE,TRUE))", "4")]  // exactly_once drops 5 entirely
    [InlineData("=INDEX(SORT(UNIQUE(A1:A6)),1)", "1")]      // composes with SORT
    public void UniqueKeepsFirstSeenOrder(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    // ── FILTER ──

    [Theory]
    [InlineData("=SUM(FILTER(A1:A3,B1:B3))", "10")]         // 5 and 5 kept, 3 dropped
    [InlineData("=COUNT(FILTER(A1:A3,B1:B3))", "2")]
    [InlineData("=INDEX(FILTER(A1:A3,B1:B3),2)", "5")]
    public void FilterKeepsThePositionsTheMaskMarks(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Fact]
    public void FilterRefusesAMaskOfADifferentLength() =>
        // Dropping the unmatched tail would compute a plausible number from the wrong rows.
        Assert.Equal("#VALUE!", Eval("=FILTER(A1:A6,B1:B3)"));

    [Fact]
    public void FilterReturnsTheFallbackWhenNothingMatches() =>
        Assert.Equal("none", Eval("=FILTER(A1:A3,{},\"none\")".Replace("{}", "A1:A3>99")));

    [Fact]
    public void FilterWithoutAFallbackIsNotAvailableRatherThanEmpty() =>
        Assert.Equal("#N/A", Eval("=FILTER(A1:A3,A1:A3>99)"));

    // ── LET ──

    [Theory]
    [InlineData("=LET(x,5,x*2)", "10")]
    [InlineData("=LET(a,1,b,a+1,b)", "2")]                  // later values see earlier names
    [InlineData("=LET(total,SUM(A1:A3),total/2)", "6.5")]
    [InlineData("=LET(x,1,LET(x,2,x)+x)", "3")]             // the inner binding is restored, not leaked
    [InlineData("=LET(n,\"Vibe\",n&\"Desk\")", "VibeDesk")]
    public void LetBindsNamesForItsCalculation(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Theory]
    [InlineData("=LET(x,5)")]                               // no calculation
    [InlineData("=LET(x,5,y,6)")]                           // even count: the last pair has no body
    public void LetRefusesAMalformedArgumentList(string formula) =>
        Assert.Equal("#VALUE!", Eval(formula));

    [Fact]
    public void AnUnboundNameIsStillUnknown() =>
        Assert.Equal("#NAME?", Eval("=LET(x,5,y)"));

    // ── operator broadcasting, which is what makes the mask forms usable ──

    [Theory]
    [InlineData("=SUM(A1:A3*2)", "26")]                     // (5+3+5)*2, not first-cell*2
    [InlineData("=SUM(SEQUENCE(3)*10)", "60")]              // 10+20+30
    [InlineData("=COUNT(FILTER(A1:A3,A1:A3>3))", "2")]      // the natural way to write a mask
    [InlineData("=SUM(FILTER(A1:A3,A1:A3>3))", "10")]
    [InlineData("=SUM(FILTER(SEQUENCE(10),SEQUENCE(10)>7))", "27")]   // 8+9+10
    public void OperatorsApplyElementwiseOverAnArray(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Fact]
    public void TwoArraysOfDifferentLengthsAreRefused() =>
        // Pairing them off and dropping the tail would produce a plausible wrong number.
        Assert.Equal("#VALUE!", Eval("=SUM(SEQUENCE(3)+SEQUENCE(4))"));

    [Fact]
    public void AggregatesStillSkipTextInsideAnArray() =>
        // A1:A6 holds two names. Coercing them would refuse the whole sum; a range has always
        // skipped them, and an array result must behave the same way.
        Assert.Equal("14", Eval("=SUM(SORT(A1:A6))"));

    [Fact]
    public void TextPassedDirectlyIsStillCoerced() =>
        Assert.Equal("#VALUE!", Eval("=SUM(1,\"abc\")"));

    // ── inline array literals ──

    [Theory]
    [InlineData("=SUM({1,2,3})", "6")]
    [InlineData("=SUM({1;2;3})", "6")]                      // ; and , are both separators
    [InlineData("=COUNT({1,2;3,4})", "4")]                  // rows collapse into one flat list
    [InlineData("=INDEX({10,20,30},2)", "20")]
    [InlineData("=COUNTA({\"a\",\"b\"})", "2")]
    [InlineData("=SUM(FILTER({1,2,3},{TRUE,FALSE,TRUE}))", "4")]
    [InlineData("=INDEX(SORT({3,1,2}),1)", "1")]
    [InlineData("=COUNTA(UNIQUE({1,2,2,3}))", "3")]
    [InlineData("=MATCH(\"b\",{\"a\",\"b\",\"c\"},0)", "2")]
    public void InlineArrayLiteralsEvaluate(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Fact]
    public void ARangeInsideALiteralFlattensRatherThanNesting() =>
        // {A1:A3, 99} is four values, not a list holding a list.
        Assert.Equal("4", Eval("=COUNT({A1:A3,99})"));

    [Fact]
    public void AnEmptyLiteralCountsNothing() =>
        Assert.Equal("0", Eval("=COUNT({})"));
}
