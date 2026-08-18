using System.Globalization;
using System.Text;

namespace VibeDesk.Application.Spreadsheets;

/// <summary>
/// The built-in function library. Dispatch is a switch on the upper-cased name rather than a
/// dictionary of delegates because most functions need the raw argument <em>nodes</em> (IF must not
/// evaluate the branch it isn't taking, COUNTIF needs the criteria range unflattened).
/// </summary>
internal static partial class FormulaFunctions
{
    /// <summary>Serial-number epoch. Day 1 is 1900-01-01, matching Excel's (bug-compatible) offset.</summary>
    private static readonly DateTime SerialEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc);

    public static FormulaValue Invoke(string name, List<Node> args, FormulaEvaluator ev) => name switch
    {
        // ── math & aggregation ────────────────────────────────────────────────────────────────
        "SUM" => Aggregate(args, ev, static xs => xs.Sum()),
        "PRODUCT" => Aggregate(args, ev, static xs => xs.Count == 0 ? 0 : xs.Aggregate(1d, (a, b) => a * b)),
        "AVERAGE" => Aggregate(args, ev, static xs => xs.Count == 0 ? double.NaN : xs.Average()),
        "MIN" => Aggregate(args, ev, static xs => xs.Count == 0 ? 0 : xs.Min()),
        "MAX" => Aggregate(args, ev, static xs => xs.Count == 0 ? 0 : xs.Max()),
        "MEDIAN" => Aggregate(args, ev, Median),
        "STDEV" or "STDEV.S" => Aggregate(args, ev, xs => Deviation(xs, sample: true)),
        "STDEVP" or "STDEV.P" => Aggregate(args, ev, xs => Deviation(xs, sample: false)),
        "VAR" or "VAR.S" => Aggregate(args, ev, xs => Variance(xs, sample: true)),
        "VARP" or "VAR.P" => Aggregate(args, ev, xs => Variance(xs, sample: false)),

        "COUNT" => Count(args, ev, numbersOnly: true),
        "COUNTA" => Count(args, ev, numbersOnly: false),
        "COUNTBLANK" => CountBlank(args, ev),

        "ABS" => Math1(args, ev, Math.Abs),
        "SQRT" => Math1(args, ev, static x => x < 0 ? double.NaN : Math.Sqrt(x)),
        "SIGN" => Math1(args, ev, static x => Math.Sign(x)),
        "EXP" => Math1(args, ev, Math.Exp),
        "LN" => Math1(args, ev, static x => x <= 0 ? double.NaN : Math.Log(x)),
        "LOG10" => Math1(args, ev, static x => x <= 0 ? double.NaN : Math.Log10(x)),
        "SIN" => Math1(args, ev, Math.Sin),
        "COS" => Math1(args, ev, Math.Cos),
        "TAN" => Math1(args, ev, Math.Tan),
        "INT" => Math1(args, ev, Math.Floor),
        "TRUNC" => Math1(args, ev, Math.Truncate),
        "FACT" => Math1(args, ev, Factorial),

        "LOG" => Log(args, ev),
        "ROUND" => Round(args, ev, RoundMode.Nearest),
        "ROUNDUP" => Round(args, ev, RoundMode.Up),
        "ROUNDDOWN" => Round(args, ev, RoundMode.Down),
        "CEILING" => TwoNumbers(args, ev, static (x, s) =>
            Math.Abs(s) < double.Epsilon ? 0 : Math.Ceiling(x / s) * s),
        "FLOOR" => TwoNumbers(args, ev, static (x, s) =>
            Math.Abs(s) < double.Epsilon ? 0 : Math.Floor(x / s) * s),
        "MOD" => Mod(args, ev),
        "POWER" => TwoNumbers(args, ev, Math.Pow),
        "PI" => FormulaValue.Number(Math.PI),
        "RAND" => FormulaValue.Number(Random.Shared.NextDouble()),
        "RANDBETWEEN" => RandBetween(args, ev),
        "SUMPRODUCT" => SumProduct(args, ev),
        "SUMSQ" => Aggregate(args, ev, static xs => xs.Sum(x => x * x)),
        "LARGE" => NthOrdered(args, ev, largest: true),
        "SMALL" => NthOrdered(args, ev, largest: false),
        "RANK" => Rank(args, ev),
        "PERCENTILE" => Percentile(args, ev),

        // ── logic ─────────────────────────────────────────────────────────────────────────────
        "IF" => If(args, ev),
        "IFS" => Ifs(args, ev),
        "IFERROR" => IfError(args, ev, blankToo: false),
        "IFNA" => IfNa(args, ev),
        "AND" => BooleanChain(args, ev, all: true),
        "OR" => BooleanChain(args, ev, all: false),
        "XOR" => Xor(args, ev),
        "NOT" => Not(args, ev),
        "TRUE" => FormulaValue.True,
        "FALSE" => FormulaValue.False,
        "SWITCH" => Switch(args, ev),

        "ISBLANK" => Is(args, ev, static v => v.IsEmpty),
        "ISNUMBER" => Is(args, ev, static v => v.Kind == FormulaValueKind.Number),
        "ISTEXT" => Is(args, ev, static v => v.Kind == FormulaValueKind.Text),
        "ISLOGICAL" => Is(args, ev, static v => v.Kind == FormulaValueKind.Boolean),
        "ISERROR" => Is(args, ev, static v => v.IsError),
        "ISERR" => Is(args, ev, static v => v.IsError && v.ErrorCode != FormulaError.NA),
        "ISNA" => Is(args, ev, static v => v.IsError && v.ErrorCode == FormulaError.NA),
        "ISEVEN" => IsParity(args, ev, even: true),
        "ISODD" => IsParity(args, ev, even: false),
        "NA" => FormulaValue.Error(FormulaError.NA),

        // ── conditional aggregation ───────────────────────────────────────────────────────────
        "SUMIF" => SumIf(args, ev),
        "COUNTIF" => CountIf(args, ev),
        "AVERAGEIF" => AverageIf(args, ev),
        "SUMIFS" => MultiCriteria(args, ev, "sum"),
        "COUNTIFS" => MultiCriteria(args, ev, "count"),
        "AVERAGEIFS" => MultiCriteria(args, ev, "average"),

        // ── text ──────────────────────────────────────────────────────────────────────────────
        "CONCAT" or "CONCATENATE" => Concat(args, ev),
        "TEXTJOIN" => TextJoin(args, ev),
        "LEN" => Text1(args, ev, static s => FormulaValue.Number(s.Length)),
        "UPPER" => Text1(args, ev, static s => FormulaValue.Text(s.ToUpperInvariant())),
        "LOWER" => Text1(args, ev, static s => FormulaValue.Text(s.ToLowerInvariant())),
        "TRIM" => Text1(args, ev, static s => FormulaValue.Text(CollapseSpaces(s))),
        "PROPER" => Text1(args, ev, static s => FormulaValue.Text(ToProper(s))),
        "LEFT" => LeftRight(args, ev, fromLeft: true),
        "RIGHT" => LeftRight(args, ev, fromLeft: false),
        "MID" => Mid(args, ev),
        "FIND" => Find(args, ev, caseSensitive: true),
        "SEARCH" => Find(args, ev, caseSensitive: false),
        "SUBSTITUTE" => Substitute(args, ev),
        "REPLACE" => Replace(args, ev),
        "REPT" => Rept(args, ev),
        "VALUE" => Value(args, ev),
        "TEXT" => TextFormat(args, ev),
        "CHAR" => Math1Int(args, ev, static n => n is >= 1 and <= 0x10FFFF
            ? FormulaValue.Text(char.ConvertFromUtf32(n))
            : FormulaValue.Error(FormulaError.Value)),
        "CODE" => Text1(args, ev, static s => s.Length == 0
            ? FormulaValue.Error(FormulaError.Value)
            : FormulaValue.Number(char.ConvertToUtf32(s, 0))),
        "EXACT" => Exact(args, ev),

        // ── date & time ───────────────────────────────────────────────────────────────────────
        "TODAY" => FormulaValue.Number(ToSerial(ev.Now.UtcDateTime.Date)),
        "NOW" => FormulaValue.Number(ToSerial(ev.Now.UtcDateTime)),
        "DATE" => DateFn(args, ev),
        "TIME" => TimeFn(args, ev),
        "YEAR" => DatePart(args, ev, static d => d.Year),
        "MONTH" => DatePart(args, ev, static d => d.Month),
        "DAY" => DatePart(args, ev, static d => d.Day),
        "HOUR" => DatePart(args, ev, static d => d.Hour),
        "MINUTE" => DatePart(args, ev, static d => d.Minute),
        "SECOND" => DatePart(args, ev, static d => d.Second),
        "WEEKDAY" => DatePart(args, ev, static d => (int)d.DayOfWeek + 1),
        "WEEKNUM" => DatePart(args, ev, static d => ISOWeek.GetWeekOfYear(d)),
        "DAYS" => Days(args, ev),
        "EDATE" => EDate(args, ev),
        "DATEDIF" => DateDif(args, ev),

        // ── lookup ────────────────────────────────────────────────────────────────────────────
        "VLOOKUP" => VLookup(args, ev),
        "HLOOKUP" => HLookup(args, ev),
        "INDEX" => Index(args, ev),
        "MATCH" => Match(args, ev),
        "ROWS" => RangeSize(args, ev, rows: true),
        "COLUMNS" => RangeSize(args, ev, rows: false),
        "ROW" => RowCol(args, ev, row: true),
        "COLUMN" => RowCol(args, ev, row: false),
        "XLOOKUP" => XLookup(args, ev),

        // ── dynamic arrays ────────────────────────────────────────────────────────────────────
        "SEQUENCE" => Sequence(args, ev),
        "SORT" => SortValues(args, ev),
        "UNIQUE" => Unique(args, ev),
        "FILTER" => Filter(args, ev),
        "LET" => Let(args, ev),

        _ => FormulaValue.Error(FormulaError.Name),
    };

    // ─────────────────────────────────── numeric helpers ───────────────────────────────────

    /// <summary>
    /// Collects the numeric values of every argument, ignoring blanks and text the way spreadsheet
    /// aggregates do, but propagating the first error encountered.
    /// </summary>
    private static bool TryCollectNumbers(
        List<Node> args, FormulaEvaluator ev, out List<double> numbers, out FormulaValue error)
    {
        numbers = [];
        error = FormulaValue.Empty;

        foreach (var arg in args)
        {
            var values = ev.EvaluateToList(arg, out var isCollection);

            foreach (var v in values)
            {
                if (v.IsError) { error = v; return false; }
                if (v.IsEmpty) continue;

                // Text inside a collection is skipped; text passed directly is coerced (and may
                // error). Testing the node type instead missed arrays — SUM(SORT(A1:A6)) over a
                // column holding names refused the whole sum rather than ignoring the names.
                if (v.Kind == FormulaValueKind.Text)
                {
                    if (isCollection) continue;
                    var coerced = v.ToNumber();
                    if (coerced.IsError) { error = coerced; return false; }
                    numbers.Add(coerced.RawNumber);
                    continue;
                }

                var n = v.ToNumber();
                if (n.IsError) { error = n; return false; }
                numbers.Add(n.RawNumber);
            }
        }

        return true;
    }

    private static FormulaValue Aggregate(
        List<Node> args, FormulaEvaluator ev, Func<List<double>, double> fn)
    {
        if (!TryCollectNumbers(args, ev, out var numbers, out var error)) return error;
        var result = fn(numbers);
        return double.IsNaN(result) ? FormulaValue.Error(FormulaError.Div0) : FormulaValue.Number(result);
    }

    private static double Median(List<double> xs)
    {
        if (xs.Count == 0) return double.NaN;
        var sorted = xs.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2d;
    }

    private static double Variance(List<double> xs, bool sample)
    {
        var n = xs.Count;
        if (n == 0 || (sample && n < 2)) return double.NaN;
        var mean = xs.Average();
        var ss = xs.Sum(x => (x - mean) * (x - mean));
        return ss / (sample ? n - 1 : n);
    }

    private static double Deviation(List<double> xs, bool sample)
    {
        var v = Variance(xs, sample);
        return double.IsNaN(v) ? double.NaN : Math.Sqrt(v);
    }

    private static double Factorial(double x)
    {
        if (x < 0 || x > 170) return double.NaN;
        var n = (int)Math.Floor(x);
        var result = 1d;
        for (var i = 2; i <= n; i++) result *= i;
        return result;
    }

    private static FormulaValue Count(List<Node> args, FormulaEvaluator ev, bool numbersOnly)
    {
        var count = 0;
        foreach (var arg in args)
        {
            foreach (var v in ev.EvaluateToList(arg))
            {
                if (numbersOnly)
                {
                    if (v.Kind == FormulaValueKind.Number) count++;
                }
                else if (!v.IsEmpty)
                {
                    count++;
                }
            }
        }
        return FormulaValue.Number(count);
    }

    private static FormulaValue CountBlank(List<Node> args, FormulaEvaluator ev)
    {
        var count = 0;
        foreach (var arg in args)
            foreach (var v in ev.EvaluateToList(arg))
                if (v.IsEmpty) count++;
        return FormulaValue.Number(count);
    }

    private static FormulaValue Math1(List<Node> args, FormulaEvaluator ev, Func<double, double> fn)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var n = ev.Evaluate(args[0]).ToNumber();
        if (n.IsError) return n;
        var r = fn(n.RawNumber);
        return double.IsNaN(r) || double.IsInfinity(r)
            ? FormulaValue.Error(FormulaError.Num)
            : FormulaValue.Number(r);
    }

    private static FormulaValue Math1Int(List<Node> args, FormulaEvaluator ev, Func<int, FormulaValue> fn)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var n = ev.Evaluate(args[0]).ToNumber();
        if (n.IsError) return n;
        return fn((int)Math.Round(n.RawNumber, MidpointRounding.AwayFromZero));
    }

    private static FormulaValue TwoNumbers(
        List<Node> args, FormulaEvaluator ev, Func<double, double, double> fn)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var a = ev.Evaluate(args[0]).ToNumber();
        if (a.IsError) return a;
        var b = ev.Evaluate(args[1]).ToNumber();
        if (b.IsError) return b;
        var r = fn(a.RawNumber, b.RawNumber);
        return double.IsNaN(r) || double.IsInfinity(r)
            ? FormulaValue.Error(FormulaError.Num)
            : FormulaValue.Number(r);
    }

    private static FormulaValue Log(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count == 0) return FormulaValue.Error(FormulaError.Value);
        var x = ev.Evaluate(args[0]).ToNumber();
        if (x.IsError) return x;
        if (x.RawNumber <= 0) return FormulaValue.Error(FormulaError.Num);

        var baseValue = 10d;
        if (args.Count > 1)
        {
            var b = ev.Evaluate(args[1]).ToNumber();
            if (b.IsError) return b;
            baseValue = b.RawNumber;
            if (baseValue <= 0 || Math.Abs(baseValue - 1) < double.Epsilon)
                return FormulaValue.Error(FormulaError.Num);
        }

        return FormulaValue.Number(Math.Log(x.RawNumber, baseValue));
    }

    private enum RoundMode { Nearest, Up, Down }

    private static FormulaValue Round(List<Node> args, FormulaEvaluator ev, RoundMode mode)
    {
        if (args.Count == 0) return FormulaValue.Error(FormulaError.Value);
        var x = ev.Evaluate(args[0]).ToNumber();
        if (x.IsError) return x;

        var digits = 0;
        if (args.Count > 1)
        {
            var d = ev.Evaluate(args[1]).ToNumber();
            if (d.IsError) return d;
            digits = (int)Math.Round(d.RawNumber, MidpointRounding.AwayFromZero);
        }

        var factor = Math.Pow(10, digits);
        var scaled = x.RawNumber * factor;

        var rounded = mode switch
        {
            RoundMode.Up => scaled < 0 ? Math.Floor(scaled) : Math.Ceiling(scaled),
            RoundMode.Down => scaled < 0 ? Math.Ceiling(scaled) : Math.Floor(scaled),
            _ => Math.Round(scaled, MidpointRounding.AwayFromZero),
        };

        return FormulaValue.Number(rounded / factor);
    }

    private static FormulaValue Mod(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var a = ev.Evaluate(args[0]).ToNumber();
        if (a.IsError) return a;
        var b = ev.Evaluate(args[1]).ToNumber();
        if (b.IsError) return b;
        if (Math.Abs(b.RawNumber) < double.Epsilon) return FormulaValue.Error(FormulaError.Div0);

        // Spreadsheet MOD takes the sign of the divisor, unlike C#'s % which takes the dividend's.
        var r = a.RawNumber - b.RawNumber * Math.Floor(a.RawNumber / b.RawNumber);
        return FormulaValue.Number(r);
    }

    private static FormulaValue RandBetween(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var lo = ev.Evaluate(args[0]).ToNumber();
        if (lo.IsError) return lo;
        var hi = ev.Evaluate(args[1]).ToNumber();
        if (hi.IsError) return hi;

        var a = (int)Math.Ceiling(lo.RawNumber);
        var b = (int)Math.Floor(hi.RawNumber);
        if (a > b) return FormulaValue.Error(FormulaError.Num);
        return FormulaValue.Number(Random.Shared.Next(a, b + 1));
    }

    private static FormulaValue SumProduct(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count == 0) return FormulaValue.Error(FormulaError.Value);

        var lists = new List<IReadOnlyList<FormulaValue>>();
        foreach (var arg in args)
        {
            var list = ev.EvaluateToList(arg);
            foreach (var v in list) if (v.IsError) return v;
            lists.Add(list);
        }

        var length = lists[0].Count;
        if (lists.Any(l => l.Count != length)) return FormulaValue.Error(FormulaError.Value);

        var total = 0d;
        for (var i = 0; i < length; i++)
        {
            var product = 1d;
            foreach (var list in lists)
            {
                var n = list[i].ToNumber();
                // Non-numeric entries contribute 0 to the product, matching Excel.
                product *= n.IsError ? 0 : n.RawNumber;
            }
            total += product;
        }

        return FormulaValue.Number(total);
    }

    private static FormulaValue NthOrdered(List<Node> args, FormulaEvaluator ev, bool largest)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        if (!TryCollectNumbers([args[0]], ev, out var numbers, out var error)) return error;

        var k = ev.Evaluate(args[1]).ToNumber();
        if (k.IsError) return k;

        var n = (int)Math.Round(k.RawNumber, MidpointRounding.AwayFromZero);
        if (n < 1 || n > numbers.Count) return FormulaValue.Error(FormulaError.Num);

        var sorted = largest
            ? numbers.OrderDescending().ToArray()
            : numbers.Order().ToArray();

        return FormulaValue.Number(sorted[n - 1]);
    }

    private static FormulaValue Rank(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var target = ev.Evaluate(args[0]).ToNumber();
        if (target.IsError) return target;
        if (!TryCollectNumbers([args[1]], ev, out var numbers, out var error)) return error;

        var ascending = false;
        if (args.Count > 2)
        {
            var order = ev.Evaluate(args[2]).ToBooleanValue();
            if (order.IsError) return order;
            ascending = order.RawBoolean;
        }

        var sorted = ascending ? numbers.Order().ToArray() : numbers.OrderDescending().ToArray();
        var index = Array.FindIndex(sorted, x => Math.Abs(x - target.RawNumber) < 1e-12);
        return index < 0 ? FormulaValue.Error(FormulaError.NA) : FormulaValue.Number(index + 1);
    }

    private static FormulaValue Percentile(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        if (!TryCollectNumbers([args[0]], ev, out var numbers, out var error)) return error;
        if (numbers.Count == 0) return FormulaValue.Error(FormulaError.Num);

        var p = ev.Evaluate(args[1]).ToNumber();
        if (p.IsError) return p;
        if (p.RawNumber is < 0 or > 1) return FormulaValue.Error(FormulaError.Num);

        var sorted = numbers.Order().ToArray();
        // Linear interpolation between closest ranks (Excel's PERCENTILE.INC).
        var position = p.RawNumber * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return FormulaValue.Number(sorted[lower]);

        var weight = position - lower;
        return FormulaValue.Number(sorted[lower] * (1 - weight) + sorted[upper] * weight);
    }

    // ──────────────────────────────────────── logic ────────────────────────────────────────

    private static FormulaValue If(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);

        var condition = ev.Evaluate(args[0]).ToBooleanValue();
        if (condition.IsError) return condition;

        if (condition.RawBoolean) return ev.Evaluate(args[1]);
        return args.Count > 2 ? ev.Evaluate(args[2]) : FormulaValue.False;
    }

    private static FormulaValue Ifs(List<Node> args, FormulaEvaluator ev)
    {
        for (var i = 0; i + 1 < args.Count; i += 2)
        {
            var condition = ev.Evaluate(args[i]).ToBooleanValue();
            if (condition.IsError) return condition;
            if (condition.RawBoolean) return ev.Evaluate(args[i + 1]);
        }
        return FormulaValue.Error(FormulaError.NA);
    }

    private static FormulaValue IfError(List<Node> args, FormulaEvaluator ev, bool blankToo)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var v = ev.Evaluate(args[0]);
        if (!v.IsError && !(blankToo && v.IsEmpty)) return v;
        return args.Count > 1 ? ev.Evaluate(args[1]) : FormulaValue.Empty;
    }

    private static FormulaValue IfNa(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var v = ev.Evaluate(args[0]);
        if (!v.IsError || v.ErrorCode != FormulaError.NA) return v;
        return args.Count > 1 ? ev.Evaluate(args[1]) : FormulaValue.Empty;
    }

    private static FormulaValue BooleanChain(List<Node> args, FormulaEvaluator ev, bool all)
    {
        var seen = false;
        var result = all;

        foreach (var arg in args)
        {
            foreach (var v in ev.EvaluateToList(arg))
            {
                if (v.IsError) return v;
                if (v.IsEmpty) continue;

                var b = v.ToBooleanValue();
                if (b.IsError) return b;

                seen = true;
                if (all) result &= b.RawBoolean;
                else result |= b.RawBoolean;
            }
        }

        return seen ? FormulaValue.Boolean(result) : FormulaValue.Error(FormulaError.Value);
    }

    private static FormulaValue Xor(List<Node> args, FormulaEvaluator ev)
    {
        var trueCount = 0;
        foreach (var arg in args)
        {
            foreach (var v in ev.EvaluateToList(arg))
            {
                if (v.IsError) return v;
                if (v.IsEmpty) continue;
                var b = v.ToBooleanValue();
                if (b.IsError) return b;
                if (b.RawBoolean) trueCount++;
            }
        }
        return FormulaValue.Boolean(trueCount % 2 == 1);
    }

    private static FormulaValue Not(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var b = ev.Evaluate(args[0]).ToBooleanValue();
        return b.IsError ? b : FormulaValue.Boolean(!b.RawBoolean);
    }

    private static FormulaValue Switch(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 3) return FormulaValue.Error(FormulaError.Value);
        var subject = ev.Evaluate(args[0]);
        if (subject.IsError) return subject;

        var i = 1;
        for (; i + 1 < args.Count; i += 2)
        {
            var candidate = ev.Evaluate(args[i]);
            if (candidate.IsError) return candidate;
            if (FormulaValue.Compare(subject, candidate) == 0) return ev.Evaluate(args[i + 1]);
        }

        // A trailing odd argument is the default.
        return i < args.Count ? ev.Evaluate(args[i]) : FormulaValue.Error(FormulaError.NA);
    }

    private static FormulaValue Is(List<Node> args, FormulaEvaluator ev, Func<FormulaValue, bool> predicate)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);

        // ISBLANK on a range tests the first cell, not the whole range.
        var v = ev.Evaluate(args[0]);
        if (v.Kind == FormulaValueKind.Array)
            v = v.Items.Count > 0 ? v.Items[0] : FormulaValue.Empty;

        return FormulaValue.Boolean(predicate(v));
    }

    private static FormulaValue IsParity(List<Node> args, FormulaEvaluator ev, bool even)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var n = ev.Evaluate(args[0]).ToNumber();
        if (n.IsError) return n;
        var isEven = Math.Abs(Math.Truncate(n.RawNumber) % 2) < double.Epsilon;
        return FormulaValue.Boolean(even == isEven);
    }

    // ───────────────────────────── conditional aggregation ─────────────────────────────

    /// <summary>
    /// Parses a criteria value into a predicate. Supports the operator prefixes (<c>"&gt;=10"</c>),
    /// wildcards (<c>"North*"</c>) and plain equality, which is the whole of the *IF family's contract.
    /// </summary>
    private static Func<FormulaValue, bool> BuildCriteria(FormulaValue criteria)
    {
        if (criteria.Kind != FormulaValueKind.Text)
        {
            return v => FormulaValue.Compare(v, criteria) == 0;
        }

        var text = criteria.RawText.Trim();

        foreach (var (prefix, comparer) in Operators)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var operand = FormulaValue.FromCellText(text[prefix.Length..].Trim());
            return v =>
            {
                var cmp = FormulaValue.Compare(v, operand);
                return cmp is not null && comparer(cmp.Value);
            };
        }

        if (text.Contains('*') || text.Contains('?'))
        {
            var regex = WildcardToRegex(text);
            return v => v.Kind != FormulaValueKind.Error && regex.IsMatch(v.ToDisplayString());
        }

        var literal = FormulaValue.FromCellText(text);
        return v => FormulaValue.Compare(v, literal) == 0;
    }

    // Ordered longest-prefix-first so ">=" is not mistaken for ">".
    private static readonly (string Prefix, Func<int, bool> Test)[] Operators =
    [
        (">=", c => c >= 0),
        ("<=", c => c <= 0),
        ("<>", c => c != 0),
        (">", c => c > 0),
        ("<", c => c < 0),
        ("=", c => c == 0),
    ];

    private static System.Text.RegularExpressions.Regex WildcardToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (var ch in pattern)
        {
            sb.Append(ch switch
            {
                '*' => ".*",
                '?' => ".",
                _ => System.Text.RegularExpressions.Regex.Escape(ch.ToString()),
            });
        }
        sb.Append('$');
        return new System.Text.RegularExpressions.Regex(
            sb.ToString(),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static FormulaValue SumIf(List<Node> args, FormulaEvaluator ev) =>
        ConditionalOne(args, ev, "sum");

    private static FormulaValue CountIf(List<Node> args, FormulaEvaluator ev) =>
        ConditionalOne(args, ev, "count");

    private static FormulaValue AverageIf(List<Node> args, FormulaEvaluator ev) =>
        ConditionalOne(args, ev, "average");

    private static FormulaValue ConditionalOne(List<Node> args, FormulaEvaluator ev, string mode)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);

        var testValues = ev.EvaluateToList(args[0]);

        var criteriaValue = ev.Evaluate(args[1]);
        if (criteriaValue.IsError) return criteriaValue;
        var predicate = BuildCriteria(criteriaValue);

        // COUNTIF has no sum range; SUMIF/AVERAGEIF default to summing the tested range itself.
        IReadOnlyList<FormulaValue> sumValues = testValues;
        if (mode != "count" && args.Count > 2)
        {
            sumValues = ev.EvaluateToList(args[2]);
        }

        var count = 0;
        var total = 0d;

        for (var i = 0; i < testValues.Count; i++)
        {
            if (testValues[i].IsError) return testValues[i];
            if (!predicate(testValues[i])) continue;

            if (mode == "count") { count++; continue; }

            if (i >= sumValues.Count) continue;
            var n = sumValues[i].ToNumber();
            if (n.IsError) continue;
            total += n.RawNumber;
            count++;
        }

        return mode switch
        {
            "count" => FormulaValue.Number(count),
            "sum" => FormulaValue.Number(total),
            "average" => count == 0
                ? FormulaValue.Error(FormulaError.Div0)
                : FormulaValue.Number(total / count),
            _ => FormulaValue.Error(FormulaError.Value),
        };
    }

    /// <summary>
    /// SUMIFS/COUNTIFS/AVERAGEIFS. SUMIFS puts the aggregate range first then criteria pairs;
    /// COUNTIFS is criteria pairs only.
    /// </summary>
    private static FormulaValue MultiCriteria(List<Node> args, FormulaEvaluator ev, string mode)
    {
        var isCount = mode == "count";
        var firstCriteriaIndex = isCount ? 0 : 1;

        if (args.Count < firstCriteriaIndex + 2) return FormulaValue.Error(FormulaError.Value);

        IReadOnlyList<FormulaValue> aggregate = [];
        if (!isCount)
        {
            aggregate = ev.EvaluateToList(args[0]);
        }

        var pairs = new List<(IReadOnlyList<FormulaValue> Values, Func<FormulaValue, bool> Test)>();
        for (var i = firstCriteriaIndex; i + 1 < args.Count; i += 2)
        {
            var values = ev.EvaluateToList(args[i]);
            var criteria = ev.Evaluate(args[i + 1]);
            if (criteria.IsError) return criteria;
            pairs.Add((values, BuildCriteria(criteria)));
        }

        if (pairs.Count == 0) return FormulaValue.Error(FormulaError.Value);

        var length = pairs[0].Values.Count;
        var count = 0;
        var total = 0d;

        for (var i = 0; i < length; i++)
        {
            var matches = true;
            foreach (var (values, test) in pairs)
            {
                if (i >= values.Count) { matches = false; break; }
                if (values[i].IsError) return values[i];
                if (!test(values[i])) { matches = false; break; }
            }

            if (!matches) continue;

            if (isCount) { count++; continue; }

            if (i >= aggregate.Count) continue;
            var n = aggregate[i].ToNumber();
            if (n.IsError) continue;
            total += n.RawNumber;
            count++;
        }

        return mode switch
        {
            "count" => FormulaValue.Number(count),
            "sum" => FormulaValue.Number(total),
            "average" => count == 0
                ? FormulaValue.Error(FormulaError.Div0)
                : FormulaValue.Number(total / count),
            _ => FormulaValue.Error(FormulaError.Value),
        };
    }

    // ───────────────────────────────────────── text ─────────────────────────────────────────

    private static FormulaValue Text1(List<Node> args, FormulaEvaluator ev, Func<string, FormulaValue> fn)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var v = ev.Evaluate(args[0]);
        if (v.IsError) return v;
        if (v.Kind == FormulaValueKind.Array)
            v = v.Items.Count > 0 ? v.Items[0] : FormulaValue.Empty;
        return fn(v.ToDisplayString());
    }

    private static string CollapseSpaces(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastWasSpace = false;
        foreach (var ch in s.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }

    private static string ToProper(string s)
    {
        var sb = new StringBuilder(s.Length);
        var startOfWord = true;
        foreach (var ch in s)
        {
            sb.Append(startOfWord ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
            startOfWord = !char.IsLetterOrDigit(ch);
        }
        return sb.ToString();
    }

    private static FormulaValue Concat(List<Node> args, FormulaEvaluator ev)
    {
        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            foreach (var v in ev.EvaluateToList(arg))
            {
                if (v.IsError) return v;
                sb.Append(v.ToDisplayString());
            }
        }
        return FormulaValue.Text(sb.ToString());
    }

    private static FormulaValue TextJoin(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 3) return FormulaValue.Error(FormulaError.Value);

        var delimiter = ev.Evaluate(args[0]);
        if (delimiter.IsError) return delimiter;

        var ignoreEmpty = ev.Evaluate(args[1]).ToBooleanValue();
        if (ignoreEmpty.IsError) return ignoreEmpty;

        var parts = new List<string>();
        for (var i = 2; i < args.Count; i++)
        {
            foreach (var v in ev.EvaluateToList(args[i]))
            {
                if (v.IsError) return v;
                if (ignoreEmpty.RawBoolean && v.IsEmpty) continue;
                parts.Add(v.ToDisplayString());
            }
        }

        return FormulaValue.Text(string.Join(delimiter.ToDisplayString(), parts));
    }

    private static FormulaValue LeftRight(List<Node> args, FormulaEvaluator ev, bool fromLeft)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var v = ev.Evaluate(args[0]);
        if (v.IsError) return v;
        var text = v.ToDisplayString();

        var count = 1;
        if (args.Count > 1)
        {
            var n = ev.Evaluate(args[1]).ToNumber();
            if (n.IsError) return n;
            count = (int)Math.Round(n.RawNumber, MidpointRounding.AwayFromZero);
        }

        if (count < 0) return FormulaValue.Error(FormulaError.Value);
        count = Math.Min(count, text.Length);

        return FormulaValue.Text(fromLeft ? text[..count] : text[^count..]);
    }

    private static FormulaValue Mid(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 3) return FormulaValue.Error(FormulaError.Value);
        var v = ev.Evaluate(args[0]);
        if (v.IsError) return v;
        var text = v.ToDisplayString();

        var startValue = ev.Evaluate(args[1]).ToNumber();
        if (startValue.IsError) return startValue;
        var lengthValue = ev.Evaluate(args[2]).ToNumber();
        if (lengthValue.IsError) return lengthValue;

        // MID is 1-based and clamps rather than throwing when the window runs off the end.
        var start = (int)Math.Round(startValue.RawNumber, MidpointRounding.AwayFromZero) - 1;
        var length = (int)Math.Round(lengthValue.RawNumber, MidpointRounding.AwayFromZero);

        if (start < 0 || length < 0) return FormulaValue.Error(FormulaError.Value);
        if (start >= text.Length) return FormulaValue.Text(string.Empty);

        length = Math.Min(length, text.Length - start);
        return FormulaValue.Text(text.Substring(start, length));
    }

    private static FormulaValue Find(List<Node> args, FormulaEvaluator ev, bool caseSensitive)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var needle = ev.Evaluate(args[0]);
        if (needle.IsError) return needle;
        var haystack = ev.Evaluate(args[1]);
        if (haystack.IsError) return haystack;

        var start = 0;
        if (args.Count > 2)
        {
            var n = ev.Evaluate(args[2]).ToNumber();
            if (n.IsError) return n;
            start = (int)Math.Round(n.RawNumber, MidpointRounding.AwayFromZero) - 1;
        }

        var text = haystack.ToDisplayString();
        if (start < 0 || start > text.Length) return FormulaValue.Error(FormulaError.Value);

        var index = text.IndexOf(
            needle.ToDisplayString(),
            start,
            caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        return index < 0 ? FormulaValue.Error(FormulaError.Value) : FormulaValue.Number(index + 1);
    }

    private static FormulaValue Substitute(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 3) return FormulaValue.Error(FormulaError.Value);
        var source = ev.Evaluate(args[0]);
        if (source.IsError) return source;
        var oldText = ev.Evaluate(args[1]);
        if (oldText.IsError) return oldText;
        var newText = ev.Evaluate(args[2]);
        if (newText.IsError) return newText;

        var text = source.ToDisplayString();
        var find = oldText.ToDisplayString();
        var replace = newText.ToDisplayString();

        if (find.Length == 0) return FormulaValue.Text(text);

        if (args.Count < 4)
            return FormulaValue.Text(text.Replace(find, replace, StringComparison.Ordinal));

        // Fourth argument replaces only the Nth occurrence.
        var occurrenceValue = ev.Evaluate(args[3]).ToNumber();
        if (occurrenceValue.IsError) return occurrenceValue;
        var target = (int)Math.Round(occurrenceValue.RawNumber, MidpointRounding.AwayFromZero);
        if (target < 1) return FormulaValue.Error(FormulaError.Value);

        var position = -1;
        for (var seen = 0; seen < target; seen++)
        {
            position = text.IndexOf(find, position + 1, StringComparison.Ordinal);
            if (position < 0) return FormulaValue.Text(text);
        }

        return FormulaValue.Text(text[..position] + replace + text[(position + find.Length)..]);
    }

    private static FormulaValue Replace(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 4) return FormulaValue.Error(FormulaError.Value);
        var source = ev.Evaluate(args[0]);
        if (source.IsError) return source;
        var startValue = ev.Evaluate(args[1]).ToNumber();
        if (startValue.IsError) return startValue;
        var lengthValue = ev.Evaluate(args[2]).ToNumber();
        if (lengthValue.IsError) return lengthValue;
        var replacement = ev.Evaluate(args[3]);
        if (replacement.IsError) return replacement;

        var text = source.ToDisplayString();
        var start = (int)Math.Round(startValue.RawNumber, MidpointRounding.AwayFromZero) - 1;
        var length = (int)Math.Round(lengthValue.RawNumber, MidpointRounding.AwayFromZero);

        if (start < 0 || length < 0) return FormulaValue.Error(FormulaError.Value);
        start = Math.Min(start, text.Length);
        length = Math.Min(length, text.Length - start);

        return FormulaValue.Text(text[..start] + replacement.ToDisplayString() + text[(start + length)..]);
    }

    private static FormulaValue Rept(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var v = ev.Evaluate(args[0]);
        if (v.IsError) return v;
        var n = ev.Evaluate(args[1]).ToNumber();
        if (n.IsError) return n;

        var count = (int)Math.Round(n.RawNumber, MidpointRounding.AwayFromZero);
        if (count < 0) return FormulaValue.Error(FormulaError.Value);

        var text = v.ToDisplayString();
        // Guard against a formula that would otherwise allocate gigabytes.
        if ((long)text.Length * count > 32_000) return FormulaValue.Error(FormulaError.Value);

        return FormulaValue.Text(string.Concat(Enumerable.Repeat(text, count)));
    }

    private static FormulaValue Value(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var v = ev.Evaluate(args[0]);
        if (v.IsError) return v;
        return FormulaValue.Text(v.ToDisplayString()).ToNumber();
    }

    private static FormulaValue TextFormat(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var v = ev.Evaluate(args[0]);
        if (v.IsError) return v;
        var formatValue = ev.Evaluate(args[1]);
        if (formatValue.IsError) return formatValue;

        var format = formatValue.ToDisplayString();
        var number = v.ToNumber();

        // Date-ish formats operate on the serial number; everything else is a numeric format.
        if (!number.IsError && format.IndexOfAny(['y', 'd', 'h', 's']) >= 0)
        {
            var date = FromSerial(number.RawNumber);
            return FormulaValue.Text(date.ToString(ToNetDateFormat(format), CultureInfo.InvariantCulture));
        }

        if (number.IsError) return FormulaValue.Text(v.ToDisplayString());

        try
        {
            return FormulaValue.Text(number.RawNumber.ToString(format, CultureInfo.InvariantCulture));
        }
        catch (FormatException)
        {
            return FormulaValue.Error(FormulaError.Value);
        }
    }

    /// <summary>Translates spreadsheet date placeholders to .NET custom format specifiers.</summary>
    private static string ToNetDateFormat(string format) => format
        .Replace("yyyy", "yyyy", StringComparison.Ordinal)
        .Replace("mmmm", "MMMM", StringComparison.Ordinal)
        .Replace("mmm", "MMM", StringComparison.Ordinal)
        .Replace("mm", "MM", StringComparison.Ordinal)
        .Replace("dddd", "dddd", StringComparison.Ordinal)
        .Replace("AM/PM", "tt", StringComparison.Ordinal);

    private static FormulaValue Exact(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var a = ev.Evaluate(args[0]);
        if (a.IsError) return a;
        var b = ev.Evaluate(args[1]);
        if (b.IsError) return b;
        return FormulaValue.Boolean(string.Equals(a.ToDisplayString(), b.ToDisplayString(), StringComparison.Ordinal));
    }

    // ────────────────────────────────────── date & time ──────────────────────────────────────

    public static double ToSerial(DateTime dt) => (dt - SerialEpoch).TotalDays;

    public static DateTime FromSerial(double serial) => SerialEpoch.AddDays(serial);

    private static FormulaValue DateFn(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 3) return FormulaValue.Error(FormulaError.Value);

        var parts = new int[3];
        for (var i = 0; i < 3; i++)
        {
            var n = ev.Evaluate(args[i]).ToNumber();
            if (n.IsError) return n;
            parts[i] = (int)Math.Round(n.RawNumber, MidpointRounding.AwayFromZero);
        }

        try
        {
            // Month/day overflow rolls over, e.g. DATE(2024,13,1) is Jan 2025 — same as Excel.
            var date = new DateTime(parts[0], 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMonths(parts[1] - 1)
                .AddDays(parts[2] - 1);
            return FormulaValue.Number(ToSerial(date));
        }
        catch (ArgumentOutOfRangeException)
        {
            return FormulaValue.Error(FormulaError.Num);
        }
    }

    private static FormulaValue TimeFn(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 3) return FormulaValue.Error(FormulaError.Value);

        var parts = new double[3];
        for (var i = 0; i < 3; i++)
        {
            var n = ev.Evaluate(args[i]).ToNumber();
            if (n.IsError) return n;
            parts[i] = n.RawNumber;
        }

        var fraction = (parts[0] * 3600 + parts[1] * 60 + parts[2]) / 86400d;
        return FormulaValue.Number(fraction - Math.Floor(fraction));
    }

    private static FormulaValue DatePart(List<Node> args, FormulaEvaluator ev, Func<DateTime, int> fn)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        var v = ev.Evaluate(args[0]);
        if (v.IsError) return v;

        var serial = v.ToNumber();
        if (serial.IsError)
        {
            // Accept an ISO date string as well as a serial number.
            if (DateTime.TryParse(v.ToDisplayString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return FormulaValue.Number(fn(parsed));
            }
            return serial;
        }

        if (serial.RawNumber < 0) return FormulaValue.Error(FormulaError.Num);
        return FormulaValue.Number(fn(FromSerial(serial.RawNumber)));
    }

    private static FormulaValue Days(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var end = ev.Evaluate(args[0]).ToNumber();
        if (end.IsError) return end;
        var start = ev.Evaluate(args[1]).ToNumber();
        if (start.IsError) return start;
        return FormulaValue.Number(Math.Truncate(end.RawNumber) - Math.Truncate(start.RawNumber));
    }

    private static FormulaValue EDate(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);
        var start = ev.Evaluate(args[0]).ToNumber();
        if (start.IsError) return start;
        var months = ev.Evaluate(args[1]).ToNumber();
        if (months.IsError) return months;

        var date = FromSerial(start.RawNumber)
            .AddMonths((int)Math.Round(months.RawNumber, MidpointRounding.AwayFromZero));
        return FormulaValue.Number(ToSerial(date));
    }

    private static FormulaValue DateDif(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 3) return FormulaValue.Error(FormulaError.Value);
        var start = ev.Evaluate(args[0]).ToNumber();
        if (start.IsError) return start;
        var end = ev.Evaluate(args[1]).ToNumber();
        if (end.IsError) return end;
        var unitValue = ev.Evaluate(args[2]);
        if (unitValue.IsError) return unitValue;

        var a = FromSerial(start.RawNumber);
        var b = FromSerial(end.RawNumber);
        if (b < a) return FormulaValue.Error(FormulaError.Num);

        var months = (b.Year - a.Year) * 12 + b.Month - a.Month;
        if (b.Day < a.Day) months--;

        return unitValue.ToDisplayString().ToUpperInvariant() switch
        {
            "D" => FormulaValue.Number(Math.Truncate((b - a).TotalDays)),
            "M" => FormulaValue.Number(months),
            "Y" => FormulaValue.Number(months / 12),
            "MD" => FormulaValue.Number(b.Day >= a.Day
                ? b.Day - a.Day
                : b.Day + DateTime.DaysInMonth(a.Year, a.Month) - a.Day),
            "YM" => FormulaValue.Number(months % 12),
            "YD" => FormulaValue.Number(Math.Truncate((b - a.AddYears(months / 12)).TotalDays)),
            _ => FormulaValue.Error(FormulaError.Num),
        };
    }

    // ──────────────────────────────────────── lookup ────────────────────────────────────────

    private static FormulaValue VLookup(List<Node> args, FormulaEvaluator ev) =>
        Lookup(args, ev, vertical: true);

    private static FormulaValue HLookup(List<Node> args, FormulaEvaluator ev) =>
        Lookup(args, ev, vertical: false);

    private static FormulaValue Lookup(List<Node> args, FormulaEvaluator ev, bool vertical)
    {
        if (args.Count < 3) return FormulaValue.Error(FormulaError.Value);

        var needle = ev.Evaluate(args[0]);
        if (needle.IsError) return needle;

        if (!ev.TryEvaluateRange(args[1], out var table, out var sheet))
            return FormulaValue.Error(FormulaError.Ref);

        var offsetValue = ev.Evaluate(args[2]).ToNumber();
        if (offsetValue.IsError) return offsetValue;
        var offset = (int)Math.Round(offsetValue.RawNumber, MidpointRounding.AwayFromZero);
        if (offset < 1) return FormulaValue.Error(FormulaError.Value);

        // Fourth argument: TRUE/omitted = approximate (assumes sorted), FALSE = exact.
        var approximate = true;
        if (args.Count > 3)
        {
            var flag = ev.Evaluate(args[3]);
            if (!flag.IsEmpty)
            {
                var b = flag.ToBooleanValue();
                if (b.IsError) return b;
                approximate = b.RawBoolean;
            }
        }

        var span = vertical ? table.RowCount : table.ColCount;
        var depth = vertical ? table.ColCount : table.RowCount;
        if (offset > depth) return FormulaValue.Error(FormulaError.Ref);

        var bestIndex = -1;

        for (var i = 0; i < span; i++)
        {
            var probe = vertical
                ? new CellAddress(table.Start.Row + i, table.Start.Col)
                : new CellAddress(table.Start.Row, table.Start.Col + i);

            var candidate = ev.GetCell(probe, sheet);
            var cmp = FormulaValue.Compare(candidate, needle);
            if (cmp is null) continue;

            if (cmp.Value == 0) { bestIndex = i; break; }

            // Approximate match keeps the largest value not greater than the needle.
            if (approximate && cmp.Value < 0) bestIndex = i;
        }

        if (bestIndex < 0) return FormulaValue.Error(FormulaError.NA);

        var result = vertical
            ? new CellAddress(table.Start.Row + bestIndex, table.Start.Col + offset - 1)
            : new CellAddress(table.Start.Row + offset - 1, table.Start.Col + bestIndex);

        return ev.GetCell(result, sheet);
    }

    private static FormulaValue Index(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);

        if (!ev.TryEvaluateRange(args[0], out var range, out var sheet))
        {
            // Not a range, but possibly an array — SORT, UNIQUE, FILTER and SEQUENCE all return one,
            // and INDEX is how you read a single element out of them.
            var array = ev.Evaluate(args[0]);

            return array.Kind == FormulaValueKind.Array
                ? IndexArray(array.Items, args, ev)
                : FormulaValue.Error(FormulaError.Ref);
        }

        var rowValue = ev.Evaluate(args[1]).ToNumber();
        if (rowValue.IsError) return rowValue;
        var row = (int)Math.Round(rowValue.RawNumber, MidpointRounding.AwayFromZero);

        var col = 0;
        if (args.Count > 2)
        {
            var colValue = ev.Evaluate(args[2]).ToNumber();
            if (colValue.IsError) return colValue;
            col = (int)Math.Round(colValue.RawNumber, MidpointRounding.AwayFromZero);
        }

        // A single-row or single-column range lets the caller supply just one index.
        if (col == 0)
        {
            if (range.RowCount == 1) { col = row; row = 1; }
            else col = 1;
        }
        if (row == 0) row = 1;

        if (row < 1 || row > range.RowCount || col < 1 || col > range.ColCount)
            return FormulaValue.Error(FormulaError.Ref);

        return ev.GetCell(new CellAddress(range.Start.Row + row - 1, range.Start.Col + col - 1), sheet);
    }

    private static FormulaValue Match(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 2) return FormulaValue.Error(FormulaError.Value);

        var needle = ev.Evaluate(args[0]);
        if (needle.IsError) return needle;

        var haystack = ev.EvaluateToList(args[1]);

        var matchType = 1;
        if (args.Count > 2)
        {
            var t = ev.Evaluate(args[2]).ToNumber();
            if (t.IsError) return t;
            matchType = (int)Math.Round(t.RawNumber, MidpointRounding.AwayFromZero);
        }

        var best = -1;
        for (var i = 0; i < haystack.Count; i++)
        {
            var cmp = FormulaValue.Compare(haystack[i], needle);
            if (cmp is null) continue;

            switch (matchType)
            {
                case 0 when cmp.Value == 0:
                    return FormulaValue.Number(i + 1);
                case 1 when cmp.Value <= 0:
                    best = i;
                    break;
                case -1 when cmp.Value >= 0:
                    best = i;
                    break;
                case 1 when cmp.Value > 0:
                    return best < 0 ? FormulaValue.Error(FormulaError.NA) : FormulaValue.Number(best + 1);
            }
        }

        return best < 0 ? FormulaValue.Error(FormulaError.NA) : FormulaValue.Number(best + 1);
    }

    private static FormulaValue XLookup(List<Node> args, FormulaEvaluator ev)
    {
        if (args.Count < 3) return FormulaValue.Error(FormulaError.Value);

        var needle = ev.Evaluate(args[0]);
        if (needle.IsError) return needle;

        var lookupValues = ev.EvaluateToList(args[1]);
        var returnValues = ev.EvaluateToList(args[2]);

        for (var i = 0; i < lookupValues.Count && i < returnValues.Count; i++)
        {
            if (FormulaValue.Compare(lookupValues[i], needle) == 0)
                return returnValues[i];
        }

        // Fourth argument is the not-found fallback.
        return args.Count > 3 ? ev.Evaluate(args[3]) : FormulaValue.Error(FormulaError.NA);
    }

    private static FormulaValue RangeSize(List<Node> args, FormulaEvaluator ev, bool rows)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Value);
        if (!ev.TryEvaluateRange(args[0], out var range, out _))
            return FormulaValue.Error(FormulaError.Ref);
        return FormulaValue.Number(rows ? range.RowCount : range.ColCount);
    }

    private static FormulaValue RowCol(List<Node> args, FormulaEvaluator ev, bool row)
    {
        if (args.Count < 1) return FormulaValue.Error(FormulaError.Ref);
        if (!ev.TryEvaluateRange(args[0], out var range, out _))
            return FormulaValue.Error(FormulaError.Ref);
        return FormulaValue.Number(row ? range.Start.Row + 1 : range.Start.Col + 1);
    }
}
