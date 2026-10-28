// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ExcelNet.Formulas;

/// <summary>Operations on a formula's text.</summary>
public static class FormulaText
{
    /// <summary>
    /// Shifts every relative row reference by <paramref name="rows"/>, as Excel does when a formula
    /// is copied down: <c>B2*C2*$F$1</c> moved three rows becomes <c>B5*C5*$F$1</c>.
    /// </summary>
    /// <remarks>
    /// Absolute rows (<c>A$1</c>), text in quotes, and function names that look like cells
    /// (<c>LOG10(</c>) are left alone. A reference shifted above row 1 becomes <c>#REF!</c>.
    /// </remarks>
    public static string ShiftRows(string formula, int rows)
    {
        ArgumentNullException.ThrowIfNull(formula);
        return Sorting.ShiftRows(formula, rows);
    }
}
