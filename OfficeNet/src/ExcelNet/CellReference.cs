// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ExcelNet;

/// <summary>
/// An A1-style cell reference, stored as zero-based row and column indices.
/// </summary>
/// <remarks>
/// <para>
/// SpreadsheetML writes references in A1 notation and one-based rows; this library works in
/// zero-based indices everywhere and converts only at the boundary. Mixing the two is the single
/// most common source of off-by-one bugs in spreadsheet code, so the conversion lives in exactly
/// one place.
/// </para>
/// <para>
/// The column encoding is base-26 <em>bijective</em>, not ordinary base-26: there is no zero digit,
/// so Z is followed by AA rather than by BA. Treating it as plain base-26 gets every column past
/// 26 wrong.
/// </para>
/// </remarks>
public readonly struct CellReference : IEquatable<CellReference>, IComparable<CellReference>
{
    /// <summary>Creates a reference from zero-based indices.</summary>
    public CellReference(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        // Excel's limits: 1,048,576 rows and 16,384 columns. A reference past them cannot be
        // written to a file that Excel will open.
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, 1_048_576);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, 16_384);

        Row = row;
        Column = column;
    }

    /// <summary>The zero-based row index.</summary>
    public int Row { get; }

    /// <summary>The zero-based column index.</summary>
    public int Column { get; }

    /// <summary>The one-based row number, as written in the file.</summary>
    public int RowNumber => Row + 1;

    /// <summary>The column's letters, for example <c>A</c>, <c>Z</c>, <c>AA</c>.</summary>
    public string ColumnName => ColumnIndexToName(Column);

    /// <summary>The A1-style reference.</summary>
    public string A1 => ColumnName + RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Converts a zero-based column index to its letters.</summary>
    public static string ColumnIndexToName(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        // Bijective base-26: subtract one before each division so that index 25 is "Z" and 26 is
        // "AA". The usual base-26 loop produces "@A" for 26 because it has a zero digit.
        Span<char> buffer = stackalloc char[8];
        var position = buffer.Length;
        var value = index;

        do
        {
            buffer[--position] = (char)('A' + value % 26);
            value = value / 26 - 1;
        }
        while (value >= 0);

        return new string(buffer[position..]);
    }

    /// <summary>Converts column letters to a zero-based index.</summary>
    /// <exception cref="FormatException">The text is not a column name.</exception>
    public static int ColumnNameToIndex(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty)
        {
            throw new FormatException("A column name cannot be empty.");
        }

        var index = 0;

        foreach (var c in name)
        {
            var upper = char.ToUpperInvariant(c);

            if (upper is < 'A' or > 'Z')
            {
                throw new FormatException($"'{name}' is not a column name.");
            }

            index = index * 26 + (upper - 'A' + 1);
        }

        return index - 1;
    }

    /// <summary>Parses an A1-style reference, ignoring any <c>$</c> anchors.</summary>
    /// <exception cref="FormatException">The text is not a cell reference.</exception>
    public static CellReference Parse(string text) =>
        TryParse(text, out var reference)
            ? reference
            : throw new FormatException($"'{text}' is not an A1-style cell reference.");

    /// <summary>Parses an A1-style reference.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out CellReference reference)
    {
        reference = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();

        // A sheet-qualified reference ("Sheet1!B2") keeps only the cell part.
        var bang = span.LastIndexOf('!');
        if (bang >= 0)
        {
            span = span[(bang + 1)..];
        }

        var letters = 0;
        var start = 0;

        // '$' marks an absolute reference and has no bearing on which cell it names.
        if (start < span.Length && span[start] == '$')
        {
            start++;
        }

        var letterStart = start;
        while (start < span.Length && char.IsAsciiLetter(span[start]))
        {
            start++;
            letters++;
        }

        if (letters is 0 or > 3)
        {
            return false;
        }

        if (start < span.Length && span[start] == '$')
        {
            start++;
        }

        if (start >= span.Length)
        {
            return false;
        }

        if (!int.TryParse(span[start..], out var rowNumber) || rowNumber < 1)
        {
            return false;
        }

        int column;
        try
        {
            column = ColumnNameToIndex(span.Slice(letterStart, letters));
        }
        catch (FormatException)
        {
            return false;
        }

        if (rowNumber > 1_048_576 || column >= 16_384)
        {
            return false;
        }

        reference = new CellReference(rowNumber - 1, column);
        return true;
    }

    /// <summary>A reference offset by a number of rows and columns.</summary>
    public CellReference Offset(int rows, int columns) => new(Row + rows, Column + columns);

    public bool Equals(CellReference other) => Row == other.Row && Column == other.Column;

    public override bool Equals(object? obj) => obj is CellReference other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Row, Column);

    /// <summary>Orders by row then column, which is the order cells are written in.</summary>
    public int CompareTo(CellReference other)
    {
        var byRow = Row.CompareTo(other.Row);
        return byRow != 0 ? byRow : Column.CompareTo(other.Column);
    }

    public static bool operator ==(CellReference a, CellReference b) => a.Equals(b);
    public static bool operator !=(CellReference a, CellReference b) => !a.Equals(b);
    public static bool operator <(CellReference a, CellReference b) => a.CompareTo(b) < 0;
    public static bool operator >(CellReference a, CellReference b) => a.CompareTo(b) > 0;
    public static bool operator <=(CellReference a, CellReference b) => a.CompareTo(b) <= 0;
    public static bool operator >=(CellReference a, CellReference b) => a.CompareTo(b) >= 0;

    public override string ToString() => A1;

    public static implicit operator CellReference(string a1) => Parse(a1);
}

/// <summary>A rectangular block of cells, such as <c>A1:D10</c>.</summary>
public readonly struct CellRangeReference : IEquatable<CellRangeReference>
{
    /// <summary>Creates a range from two corners, normalising their order.</summary>
    public CellRangeReference(CellReference first, CellReference last)
    {
        // "D10:A1" names the same block as "A1:D10"; normalising once here means no consumer has
        // to consider the reversed case.
        Start = new CellReference(Math.Min(first.Row, last.Row), Math.Min(first.Column, last.Column));
        End = new CellReference(Math.Max(first.Row, last.Row), Math.Max(first.Column, last.Column));
    }

    /// <summary>Creates a range from zero-based indices.</summary>
    public CellRangeReference(int firstRow, int firstColumn, int lastRow, int lastColumn)
        : this(new CellReference(firstRow, firstColumn), new CellReference(lastRow, lastColumn))
    {
    }

    /// <summary>The top-left corner.</summary>
    public CellReference Start { get; }

    /// <summary>The bottom-right corner.</summary>
    public CellReference End { get; }

    /// <summary>The number of rows.</summary>
    public int RowCount => End.Row - Start.Row + 1;

    /// <summary>The number of columns.</summary>
    public int ColumnCount => End.Column - Start.Column + 1;

    /// <summary>The number of cells.</summary>
    public int CellCount => RowCount * ColumnCount;

    /// <summary>The <c>A1:D10</c> form; a single-cell range renders as one reference.</summary>
    public string A1 => Start == End ? Start.A1 : $"{Start.A1}:{End.A1}";

    /// <summary>Parses an <c>A1:D10</c> range, or a single reference as a one-cell range.</summary>
    /// <exception cref="FormatException">The text is not a range.</exception>
    public static CellRangeReference Parse(string text) =>
        TryParse(text, out var range)
            ? range
            : throw new FormatException($"'{text}' is not an A1-style range.");

    /// <summary>Parses an <c>A1:D10</c> range.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out CellRangeReference range)
    {
        range = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();

        var bang = span.LastIndexOf('!');
        if (bang >= 0)
        {
            span = span[(bang + 1)..];
        }

        var colon = span.IndexOf(':');

        if (colon < 0)
        {
            if (!CellReference.TryParse(span.ToString(), out var single))
            {
                return false;
            }

            range = new CellRangeReference(single, single);
            return true;
        }

        if (!CellReference.TryParse(span[..colon].ToString(), out var first) ||
            !CellReference.TryParse(span[(colon + 1)..].ToString(), out var last))
        {
            return false;
        }

        range = new CellRangeReference(first, last);
        return true;
    }

    /// <summary>True when the range contains a cell.</summary>
    public bool Contains(CellReference reference) =>
        reference.Row >= Start.Row && reference.Row <= End.Row &&
        reference.Column >= Start.Column && reference.Column <= End.Column;

    /// <summary>True when this range and another overlap.</summary>
    public bool Intersects(CellRangeReference other) =>
        Start.Row <= other.End.Row && End.Row >= other.Start.Row &&
        Start.Column <= other.End.Column && End.Column >= other.Start.Column;

    /// <summary>Every cell reference in the range, in row-major order.</summary>
    public IEnumerable<CellReference> Cells()
    {
        for (var row = Start.Row; row <= End.Row; row++)
        {
            for (var column = Start.Column; column <= End.Column; column++)
            {
                yield return new CellReference(row, column);
            }
        }
    }

    public bool Equals(CellRangeReference other) => Start == other.Start && End == other.End;

    public override bool Equals(object? obj) => obj is CellRangeReference other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Start, End);

    public static bool operator ==(CellRangeReference a, CellRangeReference b) => a.Equals(b);
    public static bool operator !=(CellRangeReference a, CellRangeReference b) => !a.Equals(b);

    public override string ToString() => A1;

    public static implicit operator CellRangeReference(string a1) => Parse(a1);
}
