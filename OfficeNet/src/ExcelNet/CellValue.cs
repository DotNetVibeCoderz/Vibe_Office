// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;

namespace ExcelNet;

/// <summary>What kind of value a cell holds.</summary>
public enum CellValueType
{
    /// <summary>The cell is empty.</summary>
    Empty,

    /// <summary>A number.</summary>
    Number,

    /// <summary>Text.</summary>
    Text,

    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>A date or time.</summary>
    DateTime,

    /// <summary>An error such as <c>#DIV/0!</c>.</summary>
    Error,
}

/// <summary>
/// A cell's value: a number, text, a boolean, a date, or an error.
/// </summary>
/// <remarks>
/// <para>
/// A spreadsheet stores dates as numbers and remembers only through the cell's number format that
/// they are dates. That is why <see cref="CellValueType.DateTime"/> exists as a distinct case here
/// even though the file has no such type: the alternative is that every read of a date column comes
/// back as 45292.0 and the caller has to know to convert.
/// </para>
/// <para>
/// The epoch is 1899-12-30, not 1900-01-01. Lotus 1-2-3 treated 1900 as a leap year, Excel copied
/// the bug for compatibility, and shifting the epoch back two days is how the arithmetic comes out
/// right for every date after 1900-03-01 — which is every date anyone stores.
/// </para>
/// </remarks>
public readonly struct CellValue : IEquatable<CellValue>
{
    private static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    private readonly double _number;
    private readonly string? _text;

    private CellValue(CellValueType type, double number, string? text)
    {
        ValueType = type;
        _number = number;
        _text = text;
    }

    /// <summary>What the cell holds.</summary>
    public CellValueType ValueType { get; }

    /// <summary>An empty cell.</summary>
    public static CellValue Empty => new(CellValueType.Empty, 0, null);

    /// <summary>A numeric cell.</summary>
    public static CellValue FromNumber(double value) => new(CellValueType.Number, value, null);

    /// <summary>A text cell.</summary>
    public static CellValue FromText(string value) =>
        new(CellValueType.Text, 0, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>A boolean cell.</summary>
    public static CellValue FromBoolean(bool value) => new(CellValueType.Boolean, value ? 1 : 0, null);

    /// <summary>A date cell, stored as its serial number.</summary>
    public static CellValue FromDateTime(DateTime value) =>
        new(CellValueType.DateTime, ToSerial(value), null);

    /// <summary>An error cell.</summary>
    public static CellValue FromError(string code) =>
        new(CellValueType.Error, 0, code ?? throw new ArgumentNullException(nameof(code)));

    /// <summary>Wraps a CLR value, choosing the matching cell type.</summary>
    /// <exception cref="ArgumentException">The value has no spreadsheet equivalent.</exception>
    public static CellValue From(object? value) => value switch
    {
        null => Empty,
        CellValue cell => cell,
        string s => s.Length == 0 ? Empty : FromText(s),
        bool b => FromBoolean(b),
        DateTime dt => FromDateTime(dt),
        DateTimeOffset dto => FromDateTime(dto.LocalDateTime),
        DateOnly d => FromDateTime(d.ToDateTime(TimeOnly.MinValue)),
        TimeOnly t => FromNumber(t.ToTimeSpan().TotalDays),
        TimeSpan ts => FromNumber(ts.TotalDays),
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            FromNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        _ => throw new ArgumentException(
            $"A cell cannot hold a {value.GetType().Name}. Use a number, string, bool, DateTime or null.",
            nameof(value)),
    };

    /// <summary>True when the cell is empty.</summary>
    public bool IsEmpty => ValueType == CellValueType.Empty;

    /// <summary>The value as a number; 0 for text and empty cells.</summary>
    public double AsNumber() => ValueType switch
    {
        CellValueType.Number or CellValueType.DateTime or CellValueType.Boolean => _number,
        CellValueType.Text => double.TryParse(_text, NumberStyles.Any, CultureInfo.InvariantCulture,
            out var parsed) ? parsed : 0,
        _ => 0,
    };

    /// <summary>The value as text, formatted invariantly.</summary>
    public string AsText() => ValueType switch
    {
        CellValueType.Empty => string.Empty,
        CellValueType.Text or CellValueType.Error => _text ?? string.Empty,
        CellValueType.Boolean => _number != 0 ? "TRUE" : "FALSE",
        CellValueType.DateTime => AsDateTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        _ => _number.ToString("0.###############", CultureInfo.InvariantCulture),
    };

    /// <summary>The value as a boolean.</summary>
    public bool AsBoolean() => ValueType switch
    {
        CellValueType.Boolean or CellValueType.Number or CellValueType.DateTime => _number != 0,
        CellValueType.Text => bool.TryParse(_text, out var parsed)
            ? parsed
            : !string.IsNullOrEmpty(_text),
        _ => false,
    };

    /// <summary>The value as a date.</summary>
    public DateTime AsDateTime() => ValueType switch
    {
        CellValueType.DateTime or CellValueType.Number => FromSerial(_number),
        CellValueType.Text when DateTime.TryParse(_text, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) => parsed,
        _ => Epoch,
    };

    /// <summary>The value boxed as the closest CLR type.</summary>
    public object? AsObject() => ValueType switch
    {
        CellValueType.Empty => null,
        CellValueType.Number => _number,
        CellValueType.Text or CellValueType.Error => _text,
        CellValueType.Boolean => _number != 0,
        CellValueType.DateTime => AsDateTime(),
        _ => null,
    };

    /// <summary>The raw serial number as stored in the file.</summary>
    public double SerialNumber => _number;

    /// <summary>Converts a date to its spreadsheet serial number.</summary>
    public static double ToSerial(DateTime value) => (value - Epoch).TotalDays;

    /// <summary>Converts a spreadsheet serial number to a date.</summary>
    public static DateTime FromSerial(double serial)
    {
        // Serial 60 is the phantom 29 February 1900 that never existed. Excel's own conversion
        // maps 59 and 60 to the same day, so anything at or below 60 is shifted to keep January
        // and February 1900 correct rather than a day early.
        if (serial is > 0 and < 61)
        {
            return Epoch.AddDays(serial + 1);
        }

        return Epoch.AddDays(serial);
    }

    public bool Equals(CellValue other) =>
        ValueType == other.ValueType &&
        Math.Abs(_number - other._number) < double.Epsilon &&
        _text == other._text;

    public override bool Equals(object? obj) => obj is CellValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ValueType, _number, _text);

    public static bool operator ==(CellValue a, CellValue b) => a.Equals(b);
    public static bool operator !=(CellValue a, CellValue b) => !a.Equals(b);

    public override string ToString() => AsText();

    public static implicit operator CellValue(double value) => FromNumber(value);
    public static implicit operator CellValue(int value) => FromNumber(value);
    public static implicit operator CellValue(string value) => FromText(value);
    public static implicit operator CellValue(bool value) => FromBoolean(value);
    public static implicit operator CellValue(DateTime value) => FromDateTime(value);
}
