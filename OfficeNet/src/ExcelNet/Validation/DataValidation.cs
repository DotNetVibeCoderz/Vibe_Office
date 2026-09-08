// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using OfficeNet.Core;

namespace ExcelNet.Validation;

/// <summary>What a cell is allowed to contain.</summary>
public enum ValidationType
{
    /// <summary>A value from a fixed list, which Excel offers as a dropdown.</summary>
    List,

    /// <summary>A whole number.</summary>
    WholeNumber,

    /// <summary>Any number.</summary>
    Decimal,

    /// <summary>A date.</summary>
    Date,

    /// <summary>A time.</summary>
    Time,

    /// <summary>Text of a given length.</summary>
    TextLength,

    /// <summary>Whatever a formula says is acceptable.</summary>
    Custom,
}

/// <summary>How a value is compared against the rule's operands.</summary>
public enum ValidationOperator
{
    Between,
    NotBetween,
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
}

/// <summary>What Excel does when someone types something the rule rejects.</summary>
public enum ValidationErrorStyle
{
    /// <summary>Refuse the entry. The default, and what a template usually wants.</summary>
    Stop,

    /// <summary>Warn, but allow it through.</summary>
    Warning,

    /// <summary>Say so, and allow it through.</summary>
    Information,
}

/// <summary>
/// A rule restricting what can be typed into a range of cells.
/// </summary>
/// <remarks>
/// <para>
/// This is the feature that makes a spreadsheet fillable by a person rather than only by a program:
/// a dropdown of valid regions is the difference between clean data and a column of
/// "Jakarta"/"jakarta"/"DKI Jakarta".
/// </para>
/// <para>
/// A list is written inline as <c>"a,b,c"</c> in quotes. Excel caps that string at 255 characters,
/// so a long list has to live in cells and be referenced as a range instead — which is why
/// <see cref="ListFromRange"/> exists alongside <see cref="List"/>.
/// </para>
/// <para>
/// The builders return a plain rule; the messages and the other options are <c>init</c> properties,
/// so add them with a <c>with</c> expression.
/// </para>
/// </remarks>
public sealed record DataValidation
{
    internal DataValidation(CellRangeReference range, ValidationType type)
    {
        Range = range;
        Type = type;
    }

    /// <summary>The cells the rule applies to.</summary>
    public CellRangeReference Range { get; internal set; }

    /// <summary>What kind of value is allowed.</summary>
    public ValidationType Type { get; }

    /// <summary>How the operands are compared. Ignored for <see cref="ValidationType.List"/>.</summary>
    public ValidationOperator Operator { get; init; } = ValidationOperator.Between;

    /// <summary>The first operand, or the list, or the custom formula.</summary>
    public string? Formula1 { get; init; }

    /// <summary>The second operand, for <c>Between</c> and <c>NotBetween</c>.</summary>
    public string? Formula2 { get; init; }

    /// <summary>Whether an empty cell passes. True by default, as Excel does.</summary>
    public bool AllowBlank { get; init; } = true;

    /// <summary>Whether a list shows as a dropdown. True by default; false hides the arrow.</summary>
    public bool ShowDropDown { get; init; } = true;

    /// <summary>What happens on a rejected entry.</summary>
    public ValidationErrorStyle ErrorStyle { get; init; } = ValidationErrorStyle.Stop;

    /// <summary>Title of the box shown when an entry is rejected.</summary>
    public string? ErrorTitle { get; init; }

    /// <summary>Message shown when an entry is rejected.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Title of the hint shown when the cell is selected.</summary>
    public string? PromptTitle { get; init; }

    /// <summary>Hint shown when the cell is selected.</summary>
    public string? PromptMessage { get; init; }

    // ---- Builders ------------------------------------------------------------------------------

    /// <summary>A dropdown of fixed values.</summary>
    /// <exception cref="OfficeNetException">The joined list exceeds Excel's 255-character limit.</exception>
    public static DataValidation List(CellRangeReference range, params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length == 0)
        {
            throw new ArgumentException("A list validation needs at least one value.", nameof(values));
        }

        // Excel stores an inline list as a quoted, comma-separated string and rejects the whole
        // rule past 255 characters. Failing here with the length named beats writing a file that
        // opens with the dropdown silently missing.
        var joined = string.Join(",", values);

        if (joined.Length > 255)
        {
            throw new OfficeNetException(
                $"An inline list is limited to 255 characters and this one is {joined.Length}. " +
                "Put the values in cells and use ListFromRange instead.");
        }

        if (values.Any(v => v.Contains(',', StringComparison.Ordinal)))
        {
            throw new OfficeNetException(
                "An inline list value cannot contain a comma — the comma is the separator. " +
                "Put the values in cells and use ListFromRange instead.");
        }

        return new DataValidation(range, ValidationType.List)
        {
            Formula1 = "\"" + joined + "\"",
        };
    }

    /// <summary>A dropdown whose values come from cells.</summary>
    /// <param name="range">The cells the rule applies to.</param>
    /// <param name="source">Where the values live, for example <c>Lists!$A$1:$A$50</c>.</param>
    /// <remarks>
    /// The source reference should be absolute. A relative one shifts per cell, so the dropdown in
    /// row 2 reads a different range from the one in row 3 — which looks like data corruption.
    /// </remarks>
    public static DataValidation ListFromRange(CellRangeReference range, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return new DataValidation(range, ValidationType.List) { Formula1 = source };
    }

    /// <summary>A whole number within a range, inclusive.</summary>
    public static DataValidation WholeNumberBetween(CellRangeReference range, long minimum, long maximum) =>
        new(range, ValidationType.WholeNumber)
        {
            Operator = ValidationOperator.Between,
            Formula1 = minimum.ToString(CultureInfo.InvariantCulture),
            Formula2 = maximum.ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>Any number within a range, inclusive.</summary>
    public static DataValidation DecimalBetween(CellRangeReference range, double minimum, double maximum) =>
        new(range, ValidationType.Decimal)
        {
            Operator = ValidationOperator.Between,
            Formula1 = minimum.ToString("0.##########", CultureInfo.InvariantCulture),
            Formula2 = maximum.ToString("0.##########", CultureInfo.InvariantCulture),
        };

    /// <summary>A date within a range, inclusive.</summary>
    public static DataValidation DateBetween(CellRangeReference range, DateTime from, DateTime to) =>
        new(range, ValidationType.Date)
        {
            Operator = ValidationOperator.Between,
            // Dates are serial numbers here, as everywhere else in the format.
            Formula1 = CellValue.ToSerial(from).ToString("0.##########", CultureInfo.InvariantCulture),
            Formula2 = CellValue.ToSerial(to).ToString("0.##########", CultureInfo.InvariantCulture),
        };

    /// <summary>Text no longer than a given number of characters.</summary>
    public static DataValidation TextLengthAtMost(CellRangeReference range, int maximum) =>
        new(range, ValidationType.TextLength)
        {
            Operator = ValidationOperator.LessThanOrEqual,
            Formula1 = maximum.ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>Whatever a formula accepts. The formula is relative to the range's first cell.</summary>
    public static DataValidation Custom(CellRangeReference range, string formula)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);

        return new DataValidation(range, ValidationType.Custom)
        {
            Formula1 = formula.TrimStart('='),
        };
    }

    // ---- Serialisation -------------------------------------------------------------------------

    internal string TypeAttribute => Type switch
    {
        ValidationType.List => "list",
        ValidationType.WholeNumber => "whole",
        ValidationType.Decimal => "decimal",
        ValidationType.Date => "date",
        ValidationType.Time => "time",
        ValidationType.TextLength => "textLength",
        _ => "custom",
    };

    internal string OperatorAttribute => Operator switch
    {
        ValidationOperator.NotBetween => "notBetween",
        ValidationOperator.Equal => "equal",
        ValidationOperator.NotEqual => "notEqual",
        ValidationOperator.GreaterThan => "greaterThan",
        ValidationOperator.LessThan => "lessThan",
        ValidationOperator.GreaterThanOrEqual => "greaterThanOrEqual",
        ValidationOperator.LessThanOrEqual => "lessThanOrEqual",
        _ => "between",
    };

    internal string ErrorStyleAttribute => ErrorStyle switch
    {
        ValidationErrorStyle.Warning => "warning",
        ValidationErrorStyle.Information => "information",
        _ => "stop",
    };

    public override string ToString() => $"{Type} on {Range.A1}";
}
