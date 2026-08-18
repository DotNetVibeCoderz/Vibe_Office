using VibeDesk.Application.Documents;
using VibeDesk.Application.Spreadsheets;
using Xunit;

namespace VibeDesk.Tests.Spreadsheets;

/// <summary>
/// The formula engine is the one piece of VibeDesk where a wrong answer looks exactly like a right
/// one, so the cases here are the ones a spreadsheet user would notice: precedence, coercion,
/// blank-vs-zero, error propagation, and cycles.
/// </summary>
public class FormulaEngineTests
{
    private static FormulaEngine Empty() => new(new SpreadsheetModel());

    private static string Eval(string formula) => Empty().EvaluateFormula(formula).ToDisplayString();

    [Theory]
    [InlineData("1+2", "3")]
    [InlineData("2+3*4", "14")]            // * binds tighter than +
    [InlineData("(2+3)*4", "20")]
    [InlineData("2^3^2", "512")]           // ^ is right-associative: 2^(3^2), not (2^3)^2
    [InlineData("-2^2", "4")]              // unary minus binds tighter than ^, as in Excel
    [InlineData("50%", "0.5")]             // postfix percent
    [InlineData("10/4", "2.5")]
    public void EvaluatesArithmeticWithSpreadsheetPrecedence(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Theory]
    [InlineData("\"a\"&\"b\"", "ab")]
    [InlineData("1&2", "12")]              // concatenation coerces numbers to text
    [InlineData("\"3\"+4", "7")]           // arithmetic coerces text back to number
    public void CoercesBetweenTextAndNumber(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Theory]
    [InlineData("1=1", "TRUE")]
    [InlineData("2<1", "FALSE")]
    [InlineData("\"a\"<\"b\"", "TRUE")]
    public void ComparesValues(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Fact]
    public void DivisionByZeroIsAnErrorValueNotAnException() =>
        Assert.Equal("#DIV/0!", Eval("1/0"));

    [Fact]
    public void ErrorsPropagateThroughEnclosingExpressions() =>
        Assert.Equal("#DIV/0!", Eval("SUM(1, 2, 1/0)"));

    [Fact]
    public void UnknownFunctionIsNameError() =>
        Assert.Equal("#NAME?", Eval("NOSUCHFUNCTION(1)"));

    [Theory]
    [InlineData("SUM(1,2,3)", "6")]
    [InlineData("AVERAGE(2,4,6)", "4")]
    [InlineData("ROUND(3.14159, 2)", "3.14")]
    [InlineData("ABS(-7)", "7")]
    [InlineData("SQRT(16)", "4")]
    [InlineData("POWER(2,10)", "1024")]
    [InlineData("MIN(3,1,2)", "1")]
    [InlineData("MAX(3,1,2)", "3")]
    [InlineData("LEN(\"halo\")", "4")]
    [InlineData("UPPER(\"vibe\")", "VIBE")]
    [InlineData("CONCATENATE(\"a\",\"b\",\"c\")", "abc")]
    public void EvaluatesLibraryFunctions(string formula, string expected) =>
        Assert.Equal(expected, Eval(formula));

    [Fact]
    public void ModTakesTheSignOfTheDivisorLikeExcel()
    {
        Assert.Equal("2", Eval("MOD(-7, 3)"));
        Assert.Equal("-2", Eval("MOD(7, -3)"));
    }

    [Fact]
    public void IfSupportsOmittedArguments()
    {
        // IF(cond,,"no") — the empty middle argument is a blank, not a syntax error.
        Assert.Equal("no", Eval("IF(FALSE,,\"no\")"));

        // The omitted branch yields a blank, which displays as nothing rather than as "0". Excel
        // renders 0 here; showing an empty cell is the better answer and the numeric behaviour that
        // actually matters is unchanged — a blank still coerces to zero in arithmetic.
        Assert.Equal(string.Empty, Eval("IF(TRUE,,\"no\")"));
        Assert.Equal("5", Eval("IF(TRUE,,\"no\")+5"));
    }

    [Fact]
    public void ReadsCellsAndFormulasAcrossASheet()
    {
        var model = Sheet(cells: new()
        {
            ["A1"] = new Cell { V = "320" },
            ["B1"] = new Cell { V = "12.5" },
            ["C1"] = new Cell { F = "=A1*B1" },
            ["C2"] = new Cell { F = "=C1*2" },
        });

        var engine = new FormulaEngine(model);

        Assert.Equal("4000", engine.EvaluateFormula("=C1").ToDisplayString());
        Assert.Equal("8000", engine.EvaluateFormula("=C2").ToDisplayString());
        Assert.Equal("4000", engine.EvaluateFormula("=SUM(C1:C1)").ToDisplayString());
    }

    [Fact]
    public void BlankCellsCountAsZeroInArithmeticButAreSkippedByAverage()
    {
        var model = Sheet(cells: new()
        {
            ["A1"] = new Cell { V = "10" },
            // A2 deliberately absent
            ["A3"] = new Cell { V = "20" },
        });

        var engine = new FormulaEngine(model);

        Assert.Equal("30", engine.EvaluateFormula("=SUM(A1:A3)").ToDisplayString());
        Assert.Equal("15", engine.EvaluateFormula("=AVERAGE(A1:A3)").ToDisplayString());
    }

    [Fact]
    public void CircularReferenceYieldsAnErrorRatherThanAStackOverflow()
    {
        var model = Sheet(cells: new()
        {
            ["A1"] = new Cell { F = "=B1" },
            ["B1"] = new Cell { F = "=A1" },
        });

        var engine = new FormulaEngine(model);

        Assert.Equal("#CIRCULAR!", engine.EvaluateFormula("=A1").ToDisplayString());
    }

    [Fact]
    public void CrossSheetReferencesResolveByName()
    {
        var model = new SpreadsheetModel
        {
            Sheets =
            [
                new SheetTab { Id = "s1", Name = "Sheet1", Cells = new() { ["A1"] = new Cell { F = "=Data!A1*2" } } },
                new SheetTab { Id = "s2", Name = "Data", Cells = new() { ["A1"] = new Cell { V = "21" } } },
            ],
        };

        Assert.Equal("42", new FormulaEngine(model).EvaluateFormula("=A1").ToDisplayString());
    }

    private static SpreadsheetModel Sheet(Dictionary<string, Cell> cells) => new()
    {
        Sheets = [new SheetTab { Id = "s1", Name = "Sheet1", Cells = cells }],
    };
}
