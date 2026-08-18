namespace VibeDesk.Application.Spreadsheets;

/// <summary>
/// The dynamic-array family: SEQUENCE, SORT, UNIQUE, FILTER and LET.
/// </summary>
/// <remarks>
/// These return a <see cref="FormulaValueKind.Array"/>. The value model is flat — a range is already
/// a row-major list with no shape attached — so a result composes with everything that accepts a
/// range (SUM, COUNT, INDEX, MATCH, XLOOKUP) and shows its first value when a cell holds one
/// directly, which is exactly how a plain range reference behaves today.
/// <para>
/// <b>They do not spill into neighbouring cells.</b> Spilling needs a shaped value plus grid
/// ownership so a spill can be blocked, recalculated and cleared; that is a separate change.
/// </para>
/// </remarks>
internal static partial class FormulaFunctions
{
    /// <summary>Upper bound on a generated array, so a mistyped SEQUENCE cannot stall a recalc.</summary>
    private const int MaxArrayCells = 100_000;

    /// <summary>SEQUENCE(rows, [columns], [start], [step]) — a run of evenly spaced numbers.</summary>
    private static FormulaValue Sequence(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count is 0 or > 4) return FormulaValue.Error(FormulaError.Value);

        if (!TryNumber(ev, args, 0, 1, out var rows, out var bad)) return bad;
        if (!TryNumber(ev, args, 1, 1, out var cols, out bad)) return bad;
        if (!TryNumber(ev, args, 2, 1, out var start, out bad)) return bad;
        if (!TryNumber(ev, args, 3, 1, out var step, out bad)) return bad;

        var rowCount = (int)Math.Truncate(rows);
        var colCount = (int)Math.Truncate(cols);

        if (rowCount < 1 || colCount < 1) return FormulaValue.Error(FormulaError.Value);

        // A ceiling, not a memory limit: SEQUENCE(1000000) is a typo far more often than an intent,
        // and materialising it would stall the recalculation every other cell is waiting on.
        var total = (long)rowCount * colCount;
        if (total > MaxArrayCells) return FormulaValue.Error(FormulaError.Num);

        var values = new FormulaValue[total];
        for (var i = 0; i < total; i++) values[i] = FormulaValue.Number(start + (i * step));

        return FormulaValue.Array(values);
    }

    /// <summary>SORT(array, [sort_index], [sort_order], [by_col]).</summary>
    private static FormulaValue SortValues(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count is 0 or > 4) return FormulaValue.Error(FormulaError.Value);

        var items = ev.EvaluateToList(args[0]);
        if (FirstError(items) is { } error) return error;

        // sort_index selects a column of a two-dimensional array. Values here carry no shape, so the
        // only honest answer for anything but the first column is a refusal rather than a wrong sort.
        if (!TryNumber(ev, args, 1, 1, out var index, out var bad)) return bad;
        if ((int)index != 1) return FormulaValue.Error(FormulaError.Value);

        if (!TryNumber(ev, args, 2, 1, out var order, out bad)) return bad;
        if (order is not (1 or -1)) return FormulaValue.Error(FormulaError.Value);

        var sorted = items.ToArray();
        Array.Sort(sorted, (a, b) => CompareForSort(a, b) * (int)order);

        return FormulaValue.Array(sorted);
    }

    /// <summary>UNIQUE(array, [by_col], [exactly_once]).</summary>
    private static FormulaValue Unique(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count is 0 or > 3) return FormulaValue.Error(FormulaError.Value);

        var items = ev.EvaluateToList(args[0]);
        if (FirstError(items) is { } error) return error;

        var exactlyOnce = args.Count > 2 && ev.Evaluate(args[2]).ToBooleanValue().RawBoolean;

        // Insertion order, not sorted order: UNIQUE keeps the order it met the values in, and a
        // caller who wants them sorted composes SORT(UNIQUE(...)).
        var seen = new List<FormulaValue>();
        var counts = new List<int>();

        foreach (var item in items)
        {
            var at = seen.FindIndex(v => CompareForSort(v, item) == 0);

            if (at < 0) { seen.Add(item); counts.Add(1); }
            else counts[at]++;
        }

        var kept = exactlyOnce
            ? seen.Where((_, i) => counts[i] == 1).ToArray()
            : [.. seen];

        return kept.Length == 0 ? FormulaValue.Error(FormulaError.NA) : FormulaValue.Array(kept);
    }

    /// <summary>FILTER(array, include, [if_empty]).</summary>
    private static FormulaValue Filter(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count is < 2 or > 3) return FormulaValue.Error(FormulaError.Value);

        var items = ev.EvaluateToList(args[0]);
        var include = ev.EvaluateToList(args[1]);

        if (FirstError(items) is { } error) return error;
        if (FirstError(include) is { } maskError) return maskError;

        // Mismatched lengths quietly dropping the tail would be a wrong answer that looks right.
        if (items.Count != include.Count) return FormulaValue.Error(FormulaError.Value);

        var kept = new List<FormulaValue>();

        for (var i = 0; i < items.Count; i++)
        {
            var flag = include[i].ToBooleanValue();
            if (flag.IsError) return flag;
            if (flag.RawBoolean) kept.Add(items[i]);
        }

        if (kept.Count > 0) return FormulaValue.Array([.. kept]);

        // "Nothing matched" is a normal outcome, so the caller may say what to show for it.
        return args.Count > 2 ? ev.Evaluate(args[2]) : FormulaValue.Error(FormulaError.NA);
    }

    /// <summary>LET(name, value, [name, value, ...], calculation).</summary>
    private static FormulaValue Let(List<Node> args, FormulaEvaluator ev)
    {
        // Pairs plus one trailing calculation, so the count is odd and at least three.
        if (args.Count < 3 || args.Count % 2 == 0) return FormulaValue.Error(FormulaError.Value);

        if (args[0] is not NameNode name) return FormulaValue.Error(FormulaError.Name);

        var value = ev.Evaluate(args[1]);
        if (value.IsError) return value;

        // Bindings nest rather than accumulate, so each value sees the ones declared before it:
        // LET(a,1,b,a+1,b) is 2, and the restore inside EvaluateWithBinding keeps the scope honest.
        var rest = args.Count == 3
            ? args[2]
            : new FunctionNode("LET", args.GetRange(2, args.Count - 2));

        return ev.EvaluateWithBinding(name.Name, value, rest);
    }

    // ─────────────────────────────────── shared helpers ───────────────────────────────────

    private static FormulaValue? FirstError(IReadOnlyList<FormulaValue> values)
    {
        foreach (var v in values) if (v.IsError) return v;
        return null;
    }

    /// <summary>
    /// Reads an optional numeric argument, falling back to <paramref name="fallback"/> when it was
    /// omitted and surfacing the coercion error when it was given but is not a number.
    /// </summary>
    private static bool TryNumber(
        FormulaEvaluator ev,
        List<Node> args,
        int index,
        double fallback,
        out double number,
        out FormulaValue error)
    {
        number = fallback;
        error = FormulaValue.Empty;

        if (args.Count <= index) return true;

        var value = ev.Evaluate(args[index]).ToNumber();

        if (value.IsError) { error = value; return false; }

        number = value.RawNumber;
        return true;
    }

    /// <summary>
    /// Orders values the way a spreadsheet does: numbers, then text, then logicals, with blanks last.
    /// Text compares case-insensitively, which is what a sorted column of names should look like.
    /// </summary>
    private static int CompareForSort(FormulaValue a, FormulaValue b)
    {
        var rankA = SortRank(a);
        var rankB = SortRank(b);

        if (rankA != rankB) return rankA.CompareTo(rankB);

        return rankA switch
        {
            0 => a.RawNumber.CompareTo(b.RawNumber),
            1 => string.Compare(a.RawText, b.RawText, StringComparison.OrdinalIgnoreCase),
            2 => a.RawBoolean.CompareTo(b.RawBoolean),
            _ => 0,
        };
    }

    private static int SortRank(FormulaValue v) => v.Kind switch
    {
        FormulaValueKind.Number => 0,
        FormulaValueKind.Text => 1,
        FormulaValueKind.Boolean => 2,
        _ => 3,
    };

    /// <summary>
    /// INDEX into a flat array. One index reads the nth value; a row and column pair is refused,
    /// because an array carries no width to resolve the pair against.
    /// </summary>
    private static FormulaValue IndexArray(
        IReadOnlyList<FormulaValue> items, List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count > 2)
        {
            var column = ev.Evaluate(args[2]).ToNumber();
            if (column.IsError) return column;

            // A single index arrives as INDEX(array, n) or INDEX(array, 1, n); anything else would
            // need a shape this value does not have.
            if ((int)column.RawNumber is not (0 or 1)) return FormulaValue.Error(FormulaError.Ref);
        }

        var position = ev.Evaluate(args[1]).ToNumber();
        if (position.IsError) return position;

        var n = (int)Math.Round(position.RawNumber, MidpointRounding.AwayFromZero);

        return n >= 1 && n <= items.Count ? items[n - 1] : FormulaValue.Error(FormulaError.Ref);
    }
}
