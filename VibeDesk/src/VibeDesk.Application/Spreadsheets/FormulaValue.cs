using System.Globalization;

namespace VibeDesk.Application.Spreadsheets;

public enum FormulaValueKind { Empty, Number, Text, Boolean, Error, Array }

/// <summary>Spreadsheet error values. <see cref="Circular"/> is ours; the rest match Excel/Sheets.</summary>
public enum FormulaError { None, Div0, Value, Ref, Name, Num, NA, Circular }

/// <summary>
/// A dynamically typed spreadsheet value. Immutable struct so evaluation of large ranges doesn't churn
/// the heap — a 10k-cell SUM allocates for the array payload only, not for every scalar it visits.
/// </summary>
public readonly struct FormulaValue : IEquatable<FormulaValue>
{
    public FormulaValueKind Kind { get; }
    private readonly double _number;
    private readonly string? _text;
    private readonly bool _boolean;
    private readonly FormulaError _error;
    private readonly FormulaValue[]? _array;

    private FormulaValue(
        FormulaValueKind kind,
        double number = 0,
        string? text = null,
        bool boolean = false,
        FormulaError error = FormulaError.None,
        FormulaValue[]? array = null)
    {
        Kind = kind;
        _number = number;
        _text = text;
        _boolean = boolean;
        _error = error;
        _array = array;
    }

    public static readonly FormulaValue Empty = new(FormulaValueKind.Empty);
    public static readonly FormulaValue True = new(FormulaValueKind.Boolean, boolean: true);
    public static readonly FormulaValue False = new(FormulaValueKind.Boolean, boolean: false);
    public static readonly FormulaValue Zero = new(FormulaValueKind.Number);

    public static FormulaValue Number(double v) =>
        double.IsNaN(v) || double.IsInfinity(v)
            ? Error(FormulaError.Num)
            : new FormulaValue(FormulaValueKind.Number, number: v);

    public static FormulaValue Text(string v) => new(FormulaValueKind.Text, text: v);
    public static FormulaValue Boolean(bool v) => v ? True : False;
    public static FormulaValue Error(FormulaError e) => new(FormulaValueKind.Error, error: e);
    public static FormulaValue Array(FormulaValue[] values) => new(FormulaValueKind.Array, array: values);

    public bool IsError => Kind == FormulaValueKind.Error;
    public FormulaError ErrorCode => _error;
    public bool IsEmpty => Kind == FormulaValueKind.Empty;

    public double RawNumber => _number;
    public string RawText => _text ?? string.Empty;
    public bool RawBoolean => _boolean;
    public IReadOnlyList<FormulaValue> Items => _array ?? [];

    /// <summary>
    /// Coerces to a number the way a spreadsheet does: blanks and false are 0, true is 1, and numeric
    /// strings parse. Non-numeric text is <c>#VALUE!</c> rather than 0, which is what users expect.
    /// </summary>
    public FormulaValue ToNumber() => Kind switch
    {
        FormulaValueKind.Number => this,
        FormulaValueKind.Empty => Zero,
        FormulaValueKind.Boolean => Number(_boolean ? 1 : 0),
        FormulaValueKind.Error => this,
        FormulaValueKind.Text => ParseNumeric(_text),
        FormulaValueKind.Array => _array is { Length: > 0 } ? _array[0].ToNumber() : Zero,
        _ => Error(FormulaError.Value),
    };

    private static FormulaValue ParseNumeric(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Zero;
        var s = text.Trim();

        // Percent literals ("12%") and currency-ish prefixes are common in pasted data.
        var isPercent = s.EndsWith('%');
        if (isPercent) s = s[..^1].TrimEnd();

        if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var d))
        {
            return Number(isPercent ? d / 100d : d);
        }

        return Error(FormulaError.Value);
    }

    public FormulaValue ToBooleanValue() => Kind switch
    {
        FormulaValueKind.Boolean => this,
        FormulaValueKind.Number => Boolean(Math.Abs(_number) > double.Epsilon),
        FormulaValueKind.Empty => False,
        FormulaValueKind.Error => this,
        FormulaValueKind.Text => _text!.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? True
            : _text!.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ? False
            : Error(FormulaError.Value),
        FormulaValueKind.Array => _array is { Length: > 0 } ? _array[0].ToBooleanValue() : False,
        _ => Error(FormulaError.Value),
    };

    /// <summary>Display form written back into <c>Cell.V</c> and rendered by the grid.</summary>
    public string ToDisplayString() => Kind switch
    {
        FormulaValueKind.Empty => string.Empty,
        FormulaValueKind.Number => FormatNumber(_number),
        FormulaValueKind.Text => _text ?? string.Empty,
        FormulaValueKind.Boolean => _boolean ? "TRUE" : "FALSE",
        FormulaValueKind.Error => ErrorText(_error),
        FormulaValueKind.Array => _array is { Length: > 0 } ? _array[0].ToDisplayString() : string.Empty,
        _ => string.Empty,
    };

    private static string FormatNumber(double d)
    {
        if (Math.Abs(d) < 1e-10) return "0";

        // Round-trip formatting leaves 0.1+0.2 as 0.30000000000000004; 10 significant decimals is
        // past any real spreadsheet's precision while hiding binary-float noise.
        var rounded = Math.Round(d, 10, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    public static string ErrorText(FormulaError e) => e switch
    {
        FormulaError.Div0 => "#DIV/0!",
        FormulaError.Value => "#VALUE!",
        FormulaError.Ref => "#REF!",
        FormulaError.Name => "#NAME?",
        FormulaError.Num => "#NUM!",
        FormulaError.NA => "#N/A",
        FormulaError.Circular => "#CIRCULAR!",
        _ => "#ERROR!",
    };

    /// <summary>Recognises an error string read back from a cached cell value.</summary>
    public static FormulaError ParseErrorText(string? text) => text switch
    {
        "#DIV/0!" => FormulaError.Div0,
        "#VALUE!" => FormulaError.Value,
        "#REF!" => FormulaError.Ref,
        "#NAME?" => FormulaError.Name,
        "#NUM!" => FormulaError.Num,
        "#N/A" => FormulaError.NA,
        "#CIRCULAR!" => FormulaError.Circular,
        _ => FormulaError.None,
    };

    /// <summary>
    /// Infers a value from raw cell text: error strings, booleans, then numbers, else text.
    /// This is how literal (non-formula) cells enter the evaluator.
    /// </summary>
    public static FormulaValue FromCellText(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return Empty;

        var err = ParseErrorText(raw);
        if (err != FormulaError.None) return Error(err);

        if (raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return True;
        if (raw.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return False;

        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var d))
        {
            return Number(d);
        }

        return Text(raw);
    }

    /// <summary>
    /// Spreadsheet comparison ordering: numbers before text before booleans, text compared
    /// case-insensitively. Returns null when either side is an error.
    /// </summary>
    public static int? Compare(FormulaValue a, FormulaValue b)
    {
        if (a.IsError || b.IsError) return null;

        if (a.Kind == FormulaValueKind.Empty && b.Kind == FormulaValueKind.Empty) return 0;

        // A blank compares as 0 against a number and as "" against text.
        if (a.Kind == FormulaValueKind.Empty)
            a = b.Kind == FormulaValueKind.Text ? Text(string.Empty) : Zero;
        if (b.Kind == FormulaValueKind.Empty)
            b = a.Kind == FormulaValueKind.Text ? Text(string.Empty) : Zero;

        if (a.Kind == FormulaValueKind.Number && b.Kind == FormulaValueKind.Number)
            return a._number.CompareTo(b._number);

        if (a.Kind == FormulaValueKind.Text && b.Kind == FormulaValueKind.Text)
            return string.Compare(a._text, b._text, StringComparison.OrdinalIgnoreCase);

        if (a.Kind == FormulaValueKind.Boolean && b.Kind == FormulaValueKind.Boolean)
            return a._boolean.CompareTo(b._boolean);

        return Rank(a.Kind).CompareTo(Rank(b.Kind));

        static int Rank(FormulaValueKind k) => k switch
        {
            FormulaValueKind.Number => 0,
            FormulaValueKind.Text => 1,
            FormulaValueKind.Boolean => 2,
            _ => 3,
        };
    }

    public bool Equals(FormulaValue other) => Compare(this, other) == 0;
    public override bool Equals(object? obj) => obj is FormulaValue v && Equals(v);
    public override int GetHashCode() => Kind switch
    {
        FormulaValueKind.Number => _number.GetHashCode(),
        FormulaValueKind.Text => _text?.ToUpperInvariant().GetHashCode() ?? 0,
        FormulaValueKind.Boolean => _boolean.GetHashCode(),
        _ => (int)Kind,
    };

    public override string ToString() => ToDisplayString();
}
