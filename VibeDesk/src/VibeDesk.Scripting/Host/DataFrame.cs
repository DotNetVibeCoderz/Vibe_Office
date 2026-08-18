using System.Globalization;
using System.Text;

namespace VibeDesk.Scripting.Host;

/// <summary>
/// A tabular value with the operations data work actually needs: filter, sort, group, aggregate,
/// pivot.
/// </summary>
/// <remarks>
/// This exists because IronPython cannot load pandas — pandas is a C extension, and a pure-.NET
/// Python cannot host one. Rather than ship a Python runtime that fails on <c>import pandas</c>, the
/// capability is provided here and works identically from JavaScript and C# as well.
///
/// Operations take column names and operator strings rather than callbacks. A callback would have to
/// cross the runtime boundary on every row, which is both slow and the one place where three
/// different engines behave three different ways.
/// </remarks>
public sealed class DataFrame
{
    private readonly List<string> _columns;
    private readonly List<object?[]> _rows;

    public DataFrame(IEnumerable<string> columns, IEnumerable<object?[]> rows)
    {
        _columns = [.. columns];
        _rows = [.. rows];
    }

    public IReadOnlyList<string> Columns => _columns;

    public int Count => _rows.Count;

    /// <summary>Rows as objects keyed by column name — the shape scripts iterate.</summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows =>
        [.. _rows.Select(r => _columns
            .Select((c, i) => (c, v: i < r.Length ? r[i] : null))
            .ToDictionary(x => x.c, x => x.v))];

    /// <summary>Raw rows as arrays, for writing straight back to a sheet.</summary>
    public IReadOnlyList<object?[]> Values => _rows;

    public object? Cell(int row, string column)
    {
        var index = IndexOf(column);
        return row >= 0 && row < _rows.Count && index >= 0 && index < _rows[row].Length
            ? _rows[row][index]
            : null;
    }

    public DataFrame Head(int n) => new(_columns, _rows.Take(Math.Max(0, n)));

    public DataFrame Tail(int n) => new(_columns, _rows.TakeLast(Math.Max(0, n)));

    /// <summary>Keeps the named columns, in the order given.</summary>
    public DataFrame Select(params string[] columns)
    {
        var indexes = columns.Select(IndexOf).ToArray();

        return new DataFrame(
            columns,
            _rows.Select(r => indexes.Select(i => i >= 0 && i < r.Length ? r[i] : null).ToArray()));
    }

    /// <summary>
    /// Filters rows. <paramref name="op"/> is one of <c>== != &gt; &gt;= &lt; &lt;= contains
    /// startswith endswith empty notempty</c>.
    /// </summary>
    public DataFrame Where(string column, string op, object? value = null)
    {
        var index = IndexOf(column);
        if (index < 0) return new DataFrame(_columns, []);

        return new DataFrame(_columns, _rows.Where(r => Matches(Get(r, index), op, value)));
    }

    public DataFrame SortBy(string column, bool descending = false)
    {
        var index = IndexOf(column);
        if (index < 0) return this;

        var ordered = descending
            ? _rows.OrderByDescending(r => Get(r, index), Comparer)
            : _rows.OrderBy(r => Get(r, index), Comparer);

        return new DataFrame(_columns, ordered);
    }

    public DataFrame Distinct(string column)
    {
        var index = IndexOf(column);
        if (index < 0) return this;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new DataFrame(_columns, _rows.Where(r => seen.Add(Text(Get(r, index)))));
    }

    /// <summary>
    /// Groups by one column and aggregates another. <paramref name="agg"/> is
    /// <c>sum avg min max count</c>. The result has two columns: the key and the aggregate.
    /// </summary>
    public DataFrame GroupBy(string keyColumn, string valueColumn, string agg = "sum")
    {
        var keyIndex = IndexOf(keyColumn);
        var valueIndex = IndexOf(valueColumn);

        if (keyIndex < 0) return new DataFrame([keyColumn, agg], []);

        var groups = _rows
            .GroupBy(r => Text(Get(r, keyIndex)))
            .Select(g => new object?[]
            {
                g.Key,
                Aggregate(agg, g.Select(r => Number(Get(r, valueIndex))).ToList()),
            });

        return new DataFrame([keyColumn, $"{agg}({valueColumn})"], groups);
    }

    /// <summary>Cross-tabulates: one row per <paramref name="rowColumn"/>, one column per distinct
    /// <paramref name="columnColumn"/> value, cells aggregated from <paramref name="valueColumn"/>.</summary>
    public DataFrame Pivot(string rowColumn, string columnColumn, string valueColumn, string agg = "sum")
    {
        var rowIndex = IndexOf(rowColumn);
        var colIndex = IndexOf(columnColumn);
        var valIndex = IndexOf(valueColumn);

        if (rowIndex < 0 || colIndex < 0) return new DataFrame([rowColumn], []);

        var headers = _rows.Select(r => Text(Get(r, colIndex)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var output = _rows
            .GroupBy(r => Text(Get(r, rowIndex)))
            .Select(g =>
            {
                var cells = new List<object?> { g.Key };

                foreach (var header in headers)
                {
                    var matching = g
                        .Where(r => string.Equals(Text(Get(r, colIndex)), header, StringComparison.OrdinalIgnoreCase))
                        .Select(r => Number(Get(r, valIndex)))
                        .ToList();

                    cells.Add(matching.Count == 0 ? null : Aggregate(agg, matching));
                }

                return cells.ToArray();
            });

        return new DataFrame([rowColumn, .. headers], output);
    }

    public double Sum(string column) => Aggregate("sum", NumbersIn(column));
    public double Average(string column) => Aggregate("avg", NumbersIn(column));
    public double Min(string column) => Aggregate("min", NumbersIn(column));
    public double Max(string column) => Aggregate("max", NumbersIn(column));

    /// <summary>Adds a column whose value is a fixed arithmetic combination of two others.</summary>
    public DataFrame AddComputed(string name, string leftColumn, string op, string rightColumn)
    {
        var left = IndexOf(leftColumn);
        var right = IndexOf(rightColumn);

        var rows = _rows.Select(r =>
        {
            var a = Number(Get(r, left));
            var b = Number(Get(r, right));

            double value = op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b == 0 ? 0 : a / b,
                "%" => b == 0 ? 0 : a / b * 100,
                _ => 0,
            };

            return (object?[])[.. r, value];
        });

        return new DataFrame([.. _columns, name], rows);
    }

    public string ToCsv()
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', _columns.Select(Escape)));

        foreach (var row in _rows)
        {
            builder.AppendLine(string.Join(',', _columns.Select((_, i) => Escape(Text(Get(row, i))))));
        }

        return builder.ToString();

        static string Escape(string value) =>
            value.Contains(',') || value.Contains('"') || value.Contains('\n')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
    }

    /// <summary>A fixed-width rendering, for printing a frame into a script's log.</summary>
    public override string ToString()
    {
        if (_rows.Count == 0) return string.Join(" | ", _columns) + "\n(no rows)";

        var widths = _columns
            .Select((c, i) => Math.Max(c.Length, _rows.Take(50).Max(r => Text(Get(r, i)).Length)))
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(" | ", _columns.Select((c, i) => c.PadRight(widths[i]))));
        builder.AppendLine(string.Join("-+-", widths.Select(w => new string('-', w))));

        foreach (var row in _rows.Take(50))
        {
            builder.AppendLine(string.Join(" | ", _columns.Select((_, i) => Text(Get(row, i)).PadRight(widths[i]))));
        }

        if (_rows.Count > 50) builder.AppendLine($"… {_rows.Count - 50} more rows");

        return builder.ToString();
    }

    private int IndexOf(string column) =>
        _columns.FindIndex(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase));

    private static object? Get(object?[] row, int index) =>
        index >= 0 && index < row.Length ? row[index] : null;

    private List<double> NumbersIn(string column)
    {
        var index = IndexOf(column);
        return index < 0 ? [] : [.. _rows.Select(r => Number(Get(r, index)))];
    }

    private static double Aggregate(string agg, List<double> values) => agg.ToLowerInvariant() switch
    {
        "count" => values.Count,
        "avg" or "mean" or "average" => values.Count == 0 ? 0 : values.Average(),
        "min" => values.Count == 0 ? 0 : values.Min(),
        "max" => values.Count == 0 ? 0 : values.Max(),
        _ => values.Sum(),
    };

    private static bool Matches(object? cell, string op, object? value)
    {
        var text = Text(cell);
        var other = Text(value);

        switch (op.ToLowerInvariant())
        {
            case "empty": return string.IsNullOrWhiteSpace(text);
            case "notempty": return !string.IsNullOrWhiteSpace(text);
            case "contains": return text.Contains(other, StringComparison.OrdinalIgnoreCase);
            case "startswith": return text.StartsWith(other, StringComparison.OrdinalIgnoreCase);
            case "endswith": return text.EndsWith(other, StringComparison.OrdinalIgnoreCase);
        }

        // Numeric comparison where both sides are numbers, text comparison otherwise — the same rule
        // a spreadsheet uses, and the one that stops "10" < "9" being true.
        var numericCell = TryNumber(cell, out var a);
        var numericValue = TryNumber(value, out var b);
        var bothNumeric = numericCell && numericValue;
        var comparison = bothNumeric
            ? a.CompareTo(b)
            : string.Compare(text, other, StringComparison.OrdinalIgnoreCase);

        return op switch
        {
            "==" or "=" => comparison == 0,
            "!=" or "<>" => comparison != 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            _ => false,
        };
    }

    private static readonly IComparer<object?> Comparer = Comparer<object?>.Create((x, y) =>
        TryNumber(x, out var a) && TryNumber(y, out var b)
            ? a.CompareTo(b)
            : string.Compare(Text(x), Text(y), StringComparison.OrdinalIgnoreCase));

    internal static double Number(object? value) => TryNumber(value, out var n) ? n : 0;

    private static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case double d:
                number = d;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case decimal m:
                number = (double)m;
                return true;
        }

        return double.TryParse(
            value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }

    internal static string Text(object? value) => value switch
    {
        null => string.Empty,
        double d => d.ToString("0.############", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O"),
        DateTime dt => dt.ToString("O"),
        bool b => b ? "TRUE" : "FALSE",
        _ => value.ToString() ?? string.Empty,
    };
}
