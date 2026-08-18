using System.ComponentModel;
using Microsoft.SemanticKernel;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Spreadsheets;

namespace VibeDesk.Ai.Plugins;

/// <summary>
/// Arithmetic, evaluated rather than predicted.
/// </summary>
/// <remarks>
/// This is the spreadsheet formula engine, not a second implementation: the same parser and the same
/// ~110 functions that back Sheets. So <c>ROUND</c>, <c>SUMPRODUCT</c> and the date functions all
/// behave here exactly as they do in a cell, and there is only one place for a bug to live.
/// </remarks>
public sealed class MathPlugin
{
    [KernelFunction("calculate")]
    [Description(
        "Evaluates a spreadsheet-style expression and returns the result. Supports + - * / ^ %, " +
        "comparisons, and functions such as SUM, ROUND, ABS, SQRT, POWER, MIN, MAX, AVERAGE, IF and " +
        "the date functions. Use this for every calculation instead of doing arithmetic yourself.")]
    public string Calculate(
        [Description("The expression, with or without a leading '='. Example: ROUND(1250*0.11, 2)")] string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "#VALUE! (expression kosong)";

        // An empty workbook: cell references resolve to blank, which is what we want for a bare
        // calculator — the model should pass literal numbers, not A1 addresses.
        var engine = new FormulaEngine(new SpreadsheetModel());

        try
        {
            return engine.EvaluateFormula(expression.Trim()).ToDisplayString();
        }
        catch (Exception ex)
        {
            return $"#ERROR! {ex.Message}";
        }
    }
}
