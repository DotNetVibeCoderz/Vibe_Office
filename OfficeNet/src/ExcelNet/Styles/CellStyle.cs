// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Drawing;

namespace ExcelNet.Styles;

/// <summary>Horizontal alignment inside a cell.</summary>
public enum HorizontalAlignment
{
    /// <summary>Numbers right, text left — the spreadsheet default.</summary>
    General,

    /// <summary>Left.</summary>
    Left,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Right.</summary>
    Right,

    /// <summary>Filled by repeating the content.</summary>
    Fill,

    /// <summary>Stretched to both edges.</summary>
    Justify,

    /// <summary>Centred across the selection without merging.</summary>
    CenterContinuous,

    /// <summary>Evenly distributed.</summary>
    Distributed,
}

/// <summary>Vertical alignment inside a cell.</summary>
/// <remarks>
/// <see cref="Bottom"/> is the zero value because it is the spreadsheet default, so a
/// zero-initialised <see cref="CellStyle"/> aligns the way an unstyled cell does.
/// </remarks>
public enum VerticalAlignment
{
    /// <summary>Bottom — the spreadsheet default.</summary>
    Bottom,

    /// <summary>Top.</summary>
    Top,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Stretched.</summary>
    Justify,

    /// <summary>Evenly distributed.</summary>
    Distributed,
}

/// <summary>A cell border edge style.</summary>
public enum BorderLineStyle
{
    /// <summary>No border.</summary>
    None,

    /// <summary>A hairline.</summary>
    Hair,

    /// <summary>A thin line.</summary>
    Thin,

    /// <summary>A medium line.</summary>
    Medium,

    /// <summary>A thick line.</summary>
    Thick,

    /// <summary>A double line.</summary>
    Double,

    /// <summary>A dotted line.</summary>
    Dotted,

    /// <summary>A dashed line.</summary>
    Dashed,

    /// <summary>A dash-dot line.</summary>
    DashDot,

    /// <summary>A dash-dot-dot line.</summary>
    DashDotDot,
}

/// <summary>How a cell is filled.</summary>
/// <remarks>
/// <see cref="Solid"/> is the zero value deliberately. <see cref="CellStyle"/> is a struct, so a
/// zero-initialised one must still fill a background that was set on it; with None at zero, a
/// style built by <c>with { BackgroundColor = ... }</c> from <c>default</c> would silently paint
/// nothing.
/// </remarks>
public enum FillPattern
{
    /// <summary>A flat colour.</summary>
    Solid,

    /// <summary>No fill.</summary>
    None,

    /// <summary>25% grey.</summary>
    Gray125,

    /// <summary>12.5% grey.</summary>
    Gray0625,
}

/// <summary>A cell's font.</summary>
/// <param name="Name">The family name.</param>
/// <param name="SizePoints">The size in points.</param>
/// <param name="Bold">Whether it is bold.</param>
/// <param name="Italic">Whether it is italic.</param>
/// <param name="Underline">Whether it is underlined.</param>
/// <param name="Strike">Whether it is struck through.</param>
/// <param name="Color">The text colour.</param>
public readonly record struct CellFont(
    string Name = "Calibri",
    double SizePoints = 11,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strike = false,
    OfficeColor? Color = null)
{
    private readonly string? _name = Name;
    private readonly double _size = SizePoints;

    /// <summary>
    /// The family name; never empty.
    /// </summary>
    /// <remarks>
    /// Backed by a field rather than left as the primary-constructor parameter because
    /// <c>new CellFont()</c> on a record struct does <em>not</em> run the primary constructor — it
    /// zeroes the struct, and the parameter defaults never apply. Without this, a default-
    /// constructed font has a null name and writing the stylesheet throws.
    /// </remarks>
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? "Calibri" : _name;
        init => _name = value;
    }

    /// <summary>The size in points; never zero.</summary>
    public double SizePoints
    {
        get => _size <= 0 ? 11 : _size;
        init => _size = value;
    }

    /// <summary>The default workbook font.</summary>
    public static CellFont Default => new("Calibri", 11);

    /// <summary>
    /// Compares fonts by what they display, not by how the defaults happen to be stored.
    /// </summary>
    /// <remarks>
    /// The record's generated equality would compare the raw backing fields, making a
    /// default-constructed font unequal to an explicit Calibri 11 even though both render
    /// identically. That would put two identical entries in the stylesheet and defeat the
    /// deduplication the whole style model is built on.
    /// </remarks>
    public bool Equals(CellFont other) =>
        Name == other.Name &&
        Math.Abs(SizePoints - other.SizePoints) < 1e-9 &&
        Bold == other.Bold && Italic == other.Italic &&
        Underline == other.Underline && Strike == other.Strike &&
        Color == other.Color;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Name, SizePoints, Bold, Italic, Underline, Strike, Color);
}

/// <summary>A cell's border, one style and colour per edge.</summary>
/// <param name="Left">The left edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Bottom">The bottom edge.</param>
/// <param name="Color">The colour used for every set edge.</param>
public readonly record struct CellBorder(
    BorderLineStyle Left = BorderLineStyle.None,
    BorderLineStyle Right = BorderLineStyle.None,
    BorderLineStyle Top = BorderLineStyle.None,
    BorderLineStyle Bottom = BorderLineStyle.None,
    OfficeColor? Color = null)
{
    /// <summary>No border.</summary>
    public static CellBorder None => new();

    /// <summary>The same style on all four edges.</summary>
    public static CellBorder All(BorderLineStyle style, OfficeColor? color = null) =>
        new(style, style, style, style, color);

    /// <summary>True when no edge is set.</summary>
    public bool IsEmpty => Left == BorderLineStyle.None && Right == BorderLineStyle.None &&
                           Top == BorderLineStyle.None && Bottom == BorderLineStyle.None;
}

/// <summary>
/// The complete formatting of a cell: number format, font, fill, border and alignment.
/// </summary>
/// <remarks>
/// <para>
/// This is a value type on purpose. SpreadsheetML does not store formatting per cell; it stores a
/// table of distinct formats and gives each cell an index into it, because a sheet with a hundred
/// thousand formatted cells usually has a dozen distinct formats. Modelling a style as a value
/// makes that deduplication automatic — two cells given equal styles get the same index without the
/// caller doing anything.
/// </para>
/// </remarks>
public readonly record struct CellStyle
{
    /// <summary>The number format, as an Excel format code such as <c>#,##0.00</c>.</summary>
    public string? NumberFormat { get; init; }

    /// <summary>The font.</summary>
    public CellFont Font { get; init; } = CellFont.Default;

    /// <summary>The background colour; <c>null</c> means no fill.</summary>
    public OfficeColor? BackgroundColor { get; init; }

    /// <summary>The fill pattern.</summary>
    public FillPattern Pattern { get; init; } = FillPattern.Solid;

    /// <summary>The border.</summary>
    public CellBorder Border { get; init; } = CellBorder.None;

    /// <summary>Horizontal alignment.</summary>
    public HorizontalAlignment Horizontal { get; init; } = HorizontalAlignment.General;

    /// <summary>Vertical alignment.</summary>
    public VerticalAlignment Vertical { get; init; } = VerticalAlignment.Bottom;

    /// <summary>Whether text wraps inside the cell.</summary>
    public bool WrapText { get; init; }

    /// <summary>Whether the font shrinks so the content fits the column.</summary>
    public bool ShrinkToFit { get; init; }

    /// <summary>Text rotation in degrees, -90 to 90.</summary>
    public int TextRotation { get; init; }

    /// <summary>Indent steps, each about one character wide.</summary>
    public int Indent { get; init; }

    private readonly bool _unlocked;

    /// <summary>
    /// Whether the cell is locked when the sheet is protected.
    /// </summary>
    /// <remarks>
    /// Stored inverted so that the zero value means locked, which is Excel's own default for every
    /// cell. A struct's default cannot be true, so the storage carries the negation instead.
    /// </remarks>
    public bool Locked
    {
        get => !_unlocked;
        init => _unlocked = !value;
    }

    /// <summary>Creates a default style.</summary>
    public CellStyle()
    {
    }

    /// <summary>The default style: Calibri 11, no fill, no border.</summary>
    public static CellStyle Default => new();

    /// <summary>This style with a different font.</summary>
    public CellStyle WithFont(CellFont font) => this with { Font = font };

    /// <summary>This style made bold.</summary>
    public CellStyle Bold(bool value = true) => this with { Font = Font with { Bold = value } };

    /// <summary>This style made italic.</summary>
    public CellStyle Italic(bool value = true) => this with { Font = Font with { Italic = value } };

    /// <summary>This style with a text colour.</summary>
    public CellStyle WithColor(OfficeColor color) => this with { Font = Font with { Color = color } };

    /// <summary>This style with a font size.</summary>
    public CellStyle WithSize(double points) => this with { Font = Font with { SizePoints = points } };

    /// <summary>This style with a background colour.</summary>
    public CellStyle WithBackground(OfficeColor color) => this with { BackgroundColor = color };

    /// <summary>This style with a number format.</summary>
    public CellStyle WithNumberFormat(string format) => this with { NumberFormat = format };

    /// <summary>This style with a border.</summary>
    public CellStyle WithBorder(CellBorder border) => this with { Border = border };

    /// <summary>This style with a horizontal alignment.</summary>
    public CellStyle WithAlignment(HorizontalAlignment horizontal) => this with { Horizontal = horizontal };

    /// <summary>This style with both alignments.</summary>
    public CellStyle WithAlignment(HorizontalAlignment horizontal, VerticalAlignment vertical) =>
        this with { Horizontal = horizontal, Vertical = vertical };

    /// <summary>This style with wrapping switched on.</summary>
    public CellStyle Wrapped(bool value = true) => this with { WrapText = value };
}

/// <summary>The built-in number format codes, and the ids SpreadsheetML reserves for them.</summary>
/// <remarks>
/// Ids 0-49 are built in and must not be redefined; a custom format needs an id of 164 or above.
/// Writing a custom code under a built-in id makes Excel display the built-in format instead, which
/// looks like the format was silently ignored.
/// </remarks>
public static class NumberFormats
{
    /// <summary>General — the default.</summary>
    public const string General = "General";

    /// <summary>Whole numbers.</summary>
    public const string Integer = "0";

    /// <summary>Two decimal places.</summary>
    public const string TwoDecimals = "0.00";

    /// <summary>Thousands separated, no decimals.</summary>
    public const string Thousands = "#,##0";

    /// <summary>Thousands separated, two decimals.</summary>
    public const string ThousandsTwoDecimals = "#,##0.00";

    /// <summary>A percentage with no decimals.</summary>
    public const string Percent = "0%";

    /// <summary>A percentage with two decimals.</summary>
    public const string PercentTwoDecimals = "0.00%";

    /// <summary>Scientific notation.</summary>
    public const string Scientific = "0.00E+00";

    /// <summary>A short date.</summary>
    public const string ShortDate = "yyyy-mm-dd";

    /// <summary>A date and time.</summary>
    public const string DateTime = "yyyy-mm-dd hh:mm:ss";

    /// <summary>A time.</summary>
    public const string Time = "hh:mm:ss";

    /// <summary>Indonesian rupiah, no decimals.</summary>
    public const string Rupiah = "\"Rp\"#,##0";

    /// <summary>Indonesian rupiah with two decimals.</summary>
    public const string RupiahTwoDecimals = "\"Rp\"#,##0.00";

    /// <summary>US dollars.</summary>
    public const string Dollar = "\"$\"#,##0.00";

    /// <summary>Euros.</summary>
    public const string Euro = "\"€\"#,##0.00";

    /// <summary>Text, so numeric-looking content is left as typed.</summary>
    public const string Text = "@";

    /// <summary>The id of a built-in format, or <c>null</c> when the code needs a custom id.</summary>
    public static int? BuiltInId(string code) => code switch
    {
        "General" => 0,
        "0" => 1,
        "0.00" => 2,
        "#,##0" => 3,
        "#,##0.00" => 4,
        "0%" => 9,
        "0.00%" => 10,
        "0.00E+00" => 11,
        "# ?/?" => 12,
        "# ??/??" => 13,
        "mm-dd-yy" => 14,
        "d-mmm-yy" => 15,
        "d-mmm" => 16,
        "mmm-yy" => 17,
        "h:mm AM/PM" => 18,
        "h:mm:ss AM/PM" => 19,
        "h:mm" => 20,
        "h:mm:ss" => 21,
        "m/d/yy h:mm" => 22,
        "#,##0 ;(#,##0)" => 37,
        "#,##0 ;[Red](#,##0)" => 38,
        "#,##0.00;(#,##0.00)" => 39,
        "#,##0.00;[Red](#,##0.00)" => 40,
        "mm:ss" => 45,
        "[h]:mm:ss" => 46,
        "mmss.0" => 47,
        "##0.0E+0" => 48,
        "@" => 49,
        _ => null,
    };

    /// <summary>
    /// True when a format code makes a cell display as a date or time.
    /// </summary>
    /// <remarks>
    /// This is the only way to tell a date from a number in a spreadsheet — the file stores both as
    /// doubles. The scan skips anything inside quotes and the colour and condition brackets, so a
    /// currency format like <c>"Rp"#,##0</c> is not mistaken for a date because of its <c>m</c>.
    /// </remarks>
    public static bool IsDateFormat(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == General)
        {
            return false;
        }

        var inQuotes = false;
        var inBrackets = false;

        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];

            switch (c)
            {
                case '"':
                    inQuotes = !inQuotes;
                    continue;
                case '[':
                    inBrackets = true;
                    continue;
                case ']':
                    inBrackets = false;
                    continue;
                case '\\':
                    i++;
                    continue;
            }

            if (inQuotes || inBrackets)
            {
                continue;
            }

            if (c is 'y' or 'Y' or 'd' or 'D' or 'h' or 'H' or 's' or 'S')
            {
                return true;
            }

            // 'm' is minutes or months depending on context, and both mean a date/time format.
            if (c is 'm' or 'M')
            {
                return true;
            }
        }

        return false;
    }
}
