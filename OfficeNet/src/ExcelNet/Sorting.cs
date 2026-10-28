// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OfficeNet.Core;

namespace ExcelNet;

/// <summary>One column to sort by.</summary>
/// <param name="Column">A column letter (<c>"C"</c>) of the sheet, not an offset into the range.</param>
/// <param name="Descending">Largest first.</param>
public readonly record struct SortKey(string Column, bool Descending = false);

/// <summary>
/// Sorts the rows of a range the way Excel does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Excel's order is by type first:</b> numbers (dates are numbers), then text, then logical
/// values, then errors — and blank cells last, whichever direction. Descending reverses the types
/// too, except blanks, which stay at the bottom. Text compares without regard to case. The sort is
/// stable, so rows that tie keep their order and sorting by a second key after a first works.
/// </para>
/// <para>
/// <b>Moving a formula moves what it means.</b> A row whose total is <c>=C2*D2</c> and which lands
/// on row 5 must read <c>=C5*D5</c>, or every total after the sort belongs to some other row.
/// Relative row references are shifted by the distance the row moved, as Excel does; a <c>$</c>
/// row stays put. Formulas elsewhere that point into the sorted range are not rewritten — Excel
/// does not rewrite them either.
/// </para>
/// </remarks>
internal static partial class Sorting
{
    internal static void Sort(Worksheet sheet, CellRangeReference range, IReadOnlyList<SortKey> keys)
    {
        if (keys.Count == 0)
        {
            throw new ArgumentException("Give at least one column to sort by.", nameof(keys));
        }

        var columns = keys.Select(k => CellReference.ColumnNameToIndex(k.Column.Trim().ToUpperInvariant())).ToArray();

        foreach (var column in columns)
        {
            if (column < range.Start.Column || column > range.End.Column)
            {
                throw new ArgumentException(
                    $"Column {CellReference.ColumnIndexToName(column)} is outside the range {range.A1}.", nameof(keys));
            }
        }

        // Excel refuses too: a merge that crosses rows cannot be reordered without breaking it.
        if (sheet.MergedRanges.Any(m => m.Start.Row <= range.End.Row && m.End.Row >= range.Start.Row &&
                                        m.Start.Column <= range.End.Column && m.End.Column >= range.Start.Column))
        {
            throw new OfficeNetException(
                $"The range {range.A1} contains merged cells. Excel cannot sort those either; unmerge them first.");
        }

        var rows = new List<(int Row, CellData?[] Cells)>();

        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var cells = new CellData?[range.ColumnCount];

            for (var column = range.Start.Column; column <= range.End.Column; column++)
            {
                cells[column - range.Start.Column] = sheet.TryGetCellData(new CellReference(row, column), out var data)
                    ? data
                    : null;
            }

            rows.Add((row, cells));
        }

        var comparer = new RowComparer(keys, columns.Select(c => c - range.Start.Column).ToArray());
        var sorted = rows.Order(comparer).ToList(); // LINQ's OrderBy/Order is stable.

        for (var i = 0; i < sorted.Count; i++)
        {
            var target = range.Start.Row + i;
            var delta = target - sorted[i].Row;

            for (var c = 0; c < range.ColumnCount; c++)
            {
                var reference = new CellReference(target, range.Start.Column + c);

                if (sorted[i].Cells[c] is { } data)
                {
                    if (data.Formula is not null && delta != 0)
                    {
                        data.Formula = ShiftRows(data.Formula, delta);
                    }

                    sheet.PutCellData(reference, data);
                }
                else
                {
                    sheet.Clear(reference);
                }
            }
        }

        sheet.Workbook.Package.MarkDirty();
    }

    private sealed class RowComparer(IReadOnlyList<SortKey> keys, int[] offsets)
        : IComparer<(int Row, CellData?[] Cells)>
    {
        public int Compare((int Row, CellData?[] Cells) x, (int Row, CellData?[] Cells) y)
        {
            for (var k = 0; k < keys.Count; k++)
            {
                var a = x.Cells[offsets[k]]?.Value ?? CellValue.Empty;
                var b = y.Cells[offsets[k]]?.Value ?? CellValue.Empty;

                // Blanks sink in both directions.
                if (a.IsEmpty || b.IsEmpty)
                {
                    var blanks = a.IsEmpty.CompareTo(b.IsEmpty);

                    if (blanks != 0)
                    {
                        return blanks;
                    }

                    continue;
                }

                var result = CompareValues(a, b);

                if (result != 0)
                {
                    return keys[k].Descending ? -result : result;
                }
            }

            return 0;
        }
    }

    private static int Rank(CellValue value) => value.ValueType switch
    {
        CellValueType.Number or CellValueType.DateTime => 0,
        CellValueType.Text => 1,
        CellValueType.Boolean => 2,
        _ => 3,
    };

    internal static int CompareValues(CellValue a, CellValue b)
    {
        var rank = Rank(a).CompareTo(Rank(b));

        if (rank != 0)
        {
            return rank;
        }

        return Rank(a) switch
        {
            0 or 2 => a.AsNumber().CompareTo(b.AsNumber()),
            1 => string.Compare(a.AsText(), b.AsText(), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase),
            _ => 0,
        };
    }

    // ---- Shifting references -------------------------------------------------------------------

    [GeneratedRegex(@"(?<![A-Za-z0-9_.$])(\$?)([A-Za-z]{1,3})(\$?)(\d+)(?![A-Za-z0-9_(])")]
    private static partial Regex CellToken();

    /// <summary>
    /// Shifts every relative row reference in a formula by <paramref name="delta"/>, leaving text
    /// in quotes, absolute rows, and function names that merely look like cells (<c>LOG10(</c>)
    /// alone.
    /// </summary>
    internal static string ShiftRows(string formula, int delta)
    {
        var builder = new StringBuilder(formula.Length + 8);
        var position = 0;

        while (position < formula.Length)
        {
            // String literals pass through untouched; "A1" inside quotes is text, not a cell.
            var quote = formula.IndexOf('"', position);
            var end = quote < 0 ? formula.Length : quote;

            builder.Append(ShiftSegment(formula[position..end], delta));

            if (quote < 0)
            {
                break;
            }

            var close = formula.IndexOf('"', quote + 1);

            while (close >= 0 && close + 1 < formula.Length && formula[close + 1] == '"')
            {
                close = formula.IndexOf('"', close + 2);
            }

            close = close < 0 ? formula.Length - 1 : close;
            builder.Append(formula, quote, close - quote + 1);
            position = close + 1;
        }

        return builder.ToString();
    }

    private static string ShiftSegment(string segment, int delta) =>
        CellToken().Replace(segment, match =>
        {
            var columnAbsolute = match.Groups[1].Value;
            var column = match.Groups[2].Value;
            var rowAbsolute = match.Groups[3].Value;
            var row = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);

            // Not a cell after all: column letters beyond XFD.
            if (CellReference.ColumnNameToIndex(column.ToUpperInvariant()) > 16383)
            {
                return match.Value;
            }

            var shifted = rowAbsolute.Length > 0 ? row : row + delta;
            return shifted < 1
                ? "#REF!"
                : $"{columnAbsolute}{column}{rowAbsolute}{shifted.ToString(CultureInfo.InvariantCulture)}";
        });
}
