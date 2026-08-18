using VibeDesk.Application.Documents;

namespace VibeDesk.Application.Spreadsheets;

/// <summary>
/// Recalculates a whole workbook. Evaluation is demand-driven with memoisation rather than a
/// pre-built dependency graph: a cell's formula pulls its precedents, which pull theirs, and an
/// "in progress" set turns a dependency loop into <c>#CIRCULAR!</c> instead of a stack overflow.
/// That keeps recalc proportional to the cells that actually have formulas.
/// </summary>
public sealed class FormulaEngine : IFormulaContext
{
    private readonly SpreadsheetModel _model;
    private readonly Dictionary<string, SheetTab> _sheetsByName;
    private readonly string _defaultSheet;

    /// <summary>Cache of evaluated values keyed by <c>sheet!A1</c>.</summary>
    private readonly Dictionary<string, FormulaValue> _memo = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cells currently being evaluated further up the stack — the cycle detector.</summary>
    private readonly HashSet<string> _inProgress = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parsed ASTs, reused across the pass so a formula is tokenised at most once.</summary>
    private readonly Dictionary<string, Node> _astCache = new(StringComparer.Ordinal);

    public DateTimeOffset Now { get; }

    public FormulaEngine(SpreadsheetModel model, DateTimeOffset? now = null)
    {
        _model = model;
        Now = now ?? DateTimeOffset.UtcNow;
        _sheetsByName = model.Sheets.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        _defaultSheet = model.Sheets.Count > 0 ? model.Sheets[0].Name : "Sheet1";
    }

    /// <summary>
    /// Evaluates every formula in the workbook and writes the result back into each cell's cached
    /// value. Returns the number of formula cells recalculated.
    /// </summary>
    public int RecalculateAll()
    {
        _memo.Clear();
        _inProgress.Clear();

        var count = 0;
        foreach (var sheet in _model.Sheets)
        {
            // Snapshot the keys: evaluation must not observe a collection modified mid-iteration.
            foreach (var key in sheet.Cells.Keys.ToArray())
            {
                var cell = sheet.Cells[key];
                if (string.IsNullOrEmpty(cell.F)) continue;
                if (!CellAddress.TryParse(key, out var address)) continue;

                var value = Resolve(sheet.Name, address);
                cell.V = value.ToDisplayString();
                count++;
            }
        }

        return count;
    }

    /// <summary>Evaluates a single formula string in the context of a sheet, without mutating the model.</summary>
    public FormulaValue EvaluateFormula(string formula, string? sheetName = null)
    {
        var body = formula.StartsWith('=') ? formula[1..] : formula;
        var ast = GetAst(body);
        return new FormulaEvaluator(this, sheetName ?? _defaultSheet).Evaluate(ast);
    }

    private Node GetAst(string body)
    {
        if (_astCache.TryGetValue(body, out var cached)) return cached;
        var ast = FormulaParser.Parse(body);
        _astCache[body] = ast;
        return ast;
    }

    // ───────────────────────────────── IFormulaContext ─────────────────────────────────

    public FormulaValue GetCell(string? sheetName, CellAddress address) =>
        Resolve(sheetName ?? _defaultSheet, address);

    public IReadOnlyList<FormulaValue> GetRange(string? sheetName, CellRange range)
    {
        var sheet = sheetName ?? _defaultSheet;

        // Guard against a runaway reference like A1:XFD1048576 flattening the process.
        if (range.CellCount > 250_000) return [FormulaValue.Error(FormulaError.Num)];

        var values = new List<FormulaValue>(Math.Min(range.CellCount, 4096));
        foreach (var address in range.Cells())
        {
            values.Add(Resolve(sheet, address));
        }
        return values;
    }

    public string? ResolveName(string name) =>
        _model.NamedRanges.TryGetValue(name, out var target) ? target : null;

    // ────────────────────────────────── evaluation core ──────────────────────────────────

    private FormulaValue Resolve(string sheetName, CellAddress address)
    {
        var key = $"{sheetName}!{address.ToKey()}";

        if (_memo.TryGetValue(key, out var cached)) return cached;

        if (!_sheetsByName.TryGetValue(sheetName, out var sheet))
            return Remember(key, FormulaValue.Error(FormulaError.Ref));

        if (!sheet.Cells.TryGetValue(address.ToKey(), out var cell))
            return Remember(key, FormulaValue.Empty);

        if (string.IsNullOrEmpty(cell.F))
            return Remember(key, FormulaValue.FromCellText(cell.V));

        // Re-entering a cell that is already on the evaluation stack means the formulas form a loop.
        if (!_inProgress.Add(key))
            return FormulaValue.Error(FormulaError.Circular);

        try
        {
            var body = cell.F.StartsWith('=') ? cell.F[1..] : cell.F;
            var value = new FormulaEvaluator(this, sheetName).Evaluate(GetAst(body));
            return Remember(key, value);
        }
        finally
        {
            _inProgress.Remove(key);
        }
    }

    private FormulaValue Remember(string key, FormulaValue value)
    {
        _memo[key] = value;
        return value;
    }

    // ─────────────────────────────── derived-feature helpers ───────────────────────────────

    /// <summary>
    /// Resolves the numeric series behind a <see cref="ChartSpec"/> so the renderer receives plain
    /// numbers and never has to know about formulas.
    /// </summary>
    public ChartData BuildChartData(ChartSpec spec, string sheetName)
    {
        var categories = new List<string>();
        if (!string.IsNullOrWhiteSpace(spec.CategoryRange)
            && SheetRange.TryParse(spec.CategoryRange, out var catRange))
        {
            foreach (var v in GetRange(catRange.Value.SheetName ?? sheetName, catRange.Value.Range))
                categories.Add(v.ToDisplayString());
        }

        var series = new List<ChartSeries>();
        for (var i = 0; i < spec.SeriesRanges.Count; i++)
        {
            var raw = spec.SeriesRanges[i];
            if (!SheetRange.TryParse(raw, out var seriesRange)) continue;

            var points = new List<double>();
            foreach (var v in GetRange(seriesRange.Value.SheetName ?? sheetName, seriesRange.Value.Range))
            {
                var n = v.ToNumber();
                points.Add(n.IsError ? 0 : n.RawNumber);
            }

            var name = i < spec.SeriesNames.Count && !string.IsNullOrWhiteSpace(spec.SeriesNames[i])
                ? spec.SeriesNames[i]
                : $"Series {i + 1}";

            series.Add(new ChartSeries(name, points));
        }

        // Pad categories so every series has a label to sit against.
        var maxPoints = series.Count == 0 ? 0 : series.Max(s => s.Points.Count);
        while (categories.Count < maxPoints) categories.Add((categories.Count + 1).ToString());

        return new ChartData(spec.Title, spec.Kind, categories, series, spec.Stacked, spec.ShowLegend, spec.ShowGrid);
    }

    /// <summary>
    /// Computes a pivot table from its source range. Aggregation happens here rather than in the UI so
    /// the same result feeds the grid, the REST API and Mr Clippy's "summarise this sheet" answers.
    /// </summary>
    public PivotResult BuildPivot(PivotSpec spec, string sheetName)
    {
        if (!SheetRange.TryParse(spec.SourceRange, out var parsed))
            return new PivotResult([], [], [], []);

        var sheet = parsed.Value.SheetName ?? sheetName;
        var range = parsed.Value.Range;
        if (range.RowCount < 2) return new PivotResult([], [], [], []);

        // First row is the header; everything below is a record.
        var headers = new List<string>();
        for (var c = range.Start.Col; c <= range.End.Col; c++)
            headers.Add(Resolve(sheet, new CellAddress(range.Start.Row, c)).ToDisplayString());

        var records = new List<Dictionary<string, FormulaValue>>();
        for (var r = range.Start.Row + 1; r <= range.End.Row; r++)
        {
            var record = new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase);
            var hasValue = false;
            for (var c = range.Start.Col; c <= range.End.Col; c++)
            {
                var v = Resolve(sheet, new CellAddress(r, c));
                record[headers[c - range.Start.Col]] = v;
                if (!v.IsEmpty) hasValue = true;
            }
            if (hasValue) records.Add(record);
        }

        // Apply filters before grouping so aggregates only see the retained rows.
        foreach (var (field, expected) in spec.Filters)
        {
            if (string.IsNullOrWhiteSpace(expected)) continue;
            records = records
                .Where(r => r.TryGetValue(field, out var v)
                            && v.ToDisplayString().Equals(expected, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var rowKeys = DistinctKeys(records, spec.Rows);
        var colKeys = DistinctKeys(records, spec.Columns);

        var cells = new List<PivotCell>();
        foreach (var rowKey in rowKeys)
        {
            foreach (var colKey in colKeys)
            {
                var bucket = records.Where(rec =>
                    KeyOf(rec, spec.Rows) == rowKey && KeyOf(rec, spec.Columns) == colKey).ToList();

                foreach (var value in spec.Values)
                {
                    var numbers = bucket
                        .Select(rec => rec.TryGetValue(value.Field, out var v) ? v.ToNumber() : FormulaValue.Empty)
                        .Where(v => !v.IsError && v.Kind == FormulaValueKind.Number)
                        .Select(v => v.RawNumber)
                        .ToList();

                    var result = value.Aggregate.ToLowerInvariant() switch
                    {
                        "count" => bucket.Count,
                        "average" => numbers.Count == 0 ? 0 : numbers.Average(),
                        "min" => numbers.Count == 0 ? 0 : numbers.Min(),
                        "max" => numbers.Count == 0 ? 0 : numbers.Max(),
                        _ => numbers.Sum(),
                    };

                    cells.Add(new PivotCell(rowKey, colKey, value.Label ?? value.Field, result));
                }
            }
        }

        var valueLabels = spec.Values.Select(v => v.Label ?? v.Field).ToList();
        return new PivotResult(rowKeys, colKeys, valueLabels, cells);
    }

    private static List<string> DistinctKeys(
        List<Dictionary<string, FormulaValue>> records, List<string> fields)
    {
        // No grouping fields still yields one bucket, so a pivot with only values shows a grand total.
        if (fields.Count == 0) return [string.Empty];

        return records
            .Select(r => KeyOf(r, fields))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string KeyOf(Dictionary<string, FormulaValue> record, List<string> fields)
    {
        if (fields.Count == 0) return string.Empty;
        return string.Join(" / ", fields.Select(f =>
            record.TryGetValue(f, out var v) ? v.ToDisplayString() : string.Empty));
    }

    /// <summary>
    /// Returns the style overrides that conditional formatting produces for a sheet, keyed by A1
    /// address. Rules are applied in declaration order, later rules winning.
    /// </summary>
    public Dictionary<string, CellStyle> EvaluateConditionalFormats(SheetTab sheet)
    {
        var result = new Dictionary<string, CellStyle>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in sheet.ConditionalFormats)
        {
            if (!CellRange.TryParse(rule.Range, out var range)) continue;

            if (rule.Op.Equals("colorScale", StringComparison.OrdinalIgnoreCase))
            {
                ApplyColorScale(sheet, rule, range, result);
                continue;
            }

            foreach (var address in range.Cells())
            {
                var value = Resolve(sheet.Name, address);
                if (!Matches(rule, value, sheet.Name, address)) continue;
                result[address.ToKey()] = rule.Style;
            }
        }

        return result;
    }

    private bool Matches(ConditionalFormatRule rule, FormulaValue value, string sheetName, CellAddress address)
    {
        var op = rule.Op.ToLowerInvariant();

        if (op == "isempty") return value.IsEmpty;
        if (op == "notempty") return !value.IsEmpty;

        if (op == "formula")
        {
            if (string.IsNullOrWhiteSpace(rule.Value1)) return false;
            // The rule's formula is evaluated relative to the cell being tested.
            var body = rule.Value1.Replace("#REF", address.ToKey(), StringComparison.OrdinalIgnoreCase);
            var outcome = EvaluateFormula(body, sheetName).ToBooleanValue();
            return !outcome.IsError && outcome.RawBoolean;
        }

        if (op == "contains")
        {
            return value.ToDisplayString()
                .Contains(rule.Value1 ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        var operand = FormulaValue.FromCellText(rule.Value1);
        var cmp = FormulaValue.Compare(value, operand);
        if (cmp is null) return false;

        return op switch
        {
            "greaterthan" => cmp.Value > 0,
            "lessthan" => cmp.Value < 0,
            "equal" => cmp.Value == 0,
            "notequal" => cmp.Value != 0,
            "between" => cmp.Value >= 0
                         && FormulaValue.Compare(value, FormulaValue.FromCellText(rule.Value2)) <= 0,
            _ => false,
        };
    }

    private void ApplyColorScale(
        SheetTab sheet,
        ConditionalFormatRule rule,
        CellRange range,
        Dictionary<string, CellStyle> result)
    {
        var colors = rule.ScaleColors;
        if (colors is null || colors.Count < 2) return;

        var samples = new List<(string Key, double Value)>();
        foreach (var address in range.Cells())
        {
            var n = Resolve(sheet.Name, address).ToNumber();
            if (n.IsError || n.Kind != FormulaValueKind.Number) continue;
            samples.Add((address.ToKey(), n.RawNumber));
        }

        if (samples.Count == 0) return;

        var min = samples.Min(s => s.Value);
        var max = samples.Max(s => s.Value);
        var span = max - min;

        foreach (var (key, value) in samples)
        {
            // A flat range gets the midpoint colour rather than dividing by zero.
            var t = span < double.Epsilon ? 0.5 : (value - min) / span;
            result[key] = new CellStyle { Background = InterpolateHex(colors, t) };
        }
    }

    /// <summary>Linear interpolation across an ordered list of hex stops.</summary>
    internal static string InterpolateHex(List<string> stops, double t)
    {
        t = Math.Clamp(t, 0, 1);
        if (stops.Count == 1) return stops[0];

        var scaled = t * (stops.Count - 1);
        var lower = (int)Math.Floor(scaled);
        var upper = Math.Min(lower + 1, stops.Count - 1);
        var local = scaled - lower;

        var (r1, g1, b1) = ParseHex(stops[lower]);
        var (r2, g2, b2) = ParseHex(stops[upper]);

        var r = (int)Math.Round(r1 + (r2 - r1) * local);
        var g = (int)Math.Round(g1 + (g2 - g1) * local);
        var b = (int)Math.Round(b1 + (b2 - b1) * local);

        return $"#{r:x2}{g:x2}{b:x2}";
    }

    private static (int R, int G, int B) ParseHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length == 3)
        {
            // Expand #abc to #aabbcc.
            s = string.Concat(s.Select(c => new string(c, 2)));
        }
        if (s.Length < 6) return (128, 128, 128);

        return (
            Convert.ToInt32(s[..2], 16),
            Convert.ToInt32(s[2..4], 16),
            Convert.ToInt32(s[4..6], 16));
    }
}

public sealed record ChartSeries(string Name, List<double> Points);

public sealed record ChartData(
    string Title,
    string Kind,
    List<string> Categories,
    List<ChartSeries> Series,
    bool Stacked,
    bool ShowLegend,
    bool ShowGrid);

public sealed record PivotCell(string RowKey, string ColumnKey, string ValueLabel, double Value);

public sealed record PivotResult(
    List<string> RowKeys,
    List<string> ColumnKeys,
    List<string> ValueLabels,
    List<PivotCell> Cells)
{
    /// <summary>Convenience accessor for the renderer; returns 0 for an empty intersection.</summary>
    public double Get(string rowKey, string columnKey, string valueLabel) =>
        Cells.FirstOrDefault(c =>
            c.RowKey == rowKey && c.ColumnKey == columnKey && c.ValueLabel == valueLabel)?.Value ?? 0;
}
