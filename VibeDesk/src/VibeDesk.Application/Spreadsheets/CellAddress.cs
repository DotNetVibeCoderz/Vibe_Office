using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace VibeDesk.Application.Spreadsheets;

/// <summary>
/// A zero-based row/column pair with A1-notation parsing and formatting.
/// Absolute markers (<c>$</c>) are tracked so that copy/fill can rewrite relative parts only.
/// </summary>
public readonly record struct CellAddress(int Row, int Col, bool RowAbsolute = false, bool ColAbsolute = false)
{
    /// <summary>Converts a zero-based column index to letters: 0→A, 25→Z, 26→AA.</summary>
    public static string ColumnName(int col)
    {
        if (col < 0) return "?";
        Span<char> buffer = stackalloc char[8];
        var i = buffer.Length;
        var n = col;
        do
        {
            buffer[--i] = (char)('A' + n % 26);
            n = n / 26 - 1;
        } while (n >= 0);
        return new string(buffer[i..]);
    }

    /// <summary>Converts column letters to a zero-based index. Returns -1 on invalid input.</summary>
    public static int ColumnIndex(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty) return -1;
        var result = 0;
        foreach (var ch in name)
        {
            var upper = char.ToUpperInvariant(ch);
            if (upper is < 'A' or > 'Z') return -1;
            result = result * 26 + (upper - 'A' + 1);
        }
        return result - 1;
    }

    public string ToA1() =>
        $"{(ColAbsolute ? "$" : "")}{ColumnName(Col)}{(RowAbsolute ? "$" : "")}{Row + 1}";

    /// <summary>A1 without the <c>$</c> markers — the canonical key used in <c>SheetTab.Cells</c>.</summary>
    public string ToKey() => $"{ColumnName(Col)}{Row + 1}";

    public static bool TryParse(ReadOnlySpan<char> text, out CellAddress address)
    {
        address = default;
        text = text.Trim();
        if (text.IsEmpty) return false;

        var i = 0;
        var colAbs = false;
        if (text[i] == '$') { colAbs = true; i++; }

        var letterStart = i;
        while (i < text.Length && char.IsAsciiLetter(text[i])) i++;
        if (i == letterStart) return false;
        var col = ColumnIndex(text[letterStart..i]);
        if (col < 0) return false;

        var rowAbs = false;
        if (i < text.Length && text[i] == '$') { rowAbs = true; i++; }

        var digitStart = i;
        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        if (i == digitStart || i != text.Length) return false;

        if (!int.TryParse(text[digitStart..i], NumberStyles.None, CultureInfo.InvariantCulture, out var row1)
            || row1 < 1)
        {
            return false;
        }

        address = new CellAddress(row1 - 1, col, rowAbs, colAbs);
        return true;
    }

    public static CellAddress Parse(string text) =>
        TryParse(text, out var a) ? a : throw new FormatException($"Invalid cell address '{text}'.");
}

/// <summary>An inclusive rectangular span of cells, normalised so Start is always the top-left corner.</summary>
public readonly record struct CellRange(CellAddress Start, CellAddress End)
{
    public int RowCount => End.Row - Start.Row + 1;
    public int ColCount => End.Col - Start.Col + 1;
    public int CellCount => RowCount * ColCount;

    public static CellRange Single(CellAddress a) => new(a, a);

    /// <summary>Parses <c>A1:B10</c> or a bare <c>A1</c>. Sheet prefixes must be stripped by the caller.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out CellRange range)
    {
        range = default;
        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            if (!CellAddress.TryParse(text, out var single)) return false;
            range = Single(single);
            return true;
        }

        if (!CellAddress.TryParse(text[..colon], out var start)) return false;
        if (!CellAddress.TryParse(text[(colon + 1)..], out var end)) return false;

        range = Normalise(start, end);
        return true;
    }

    /// <summary>Accepts corners in any order and returns top-left → bottom-right.</summary>
    public static CellRange Normalise(CellAddress a, CellAddress b) => new(
        new CellAddress(Math.Min(a.Row, b.Row), Math.Min(a.Col, b.Col), a.RowAbsolute, a.ColAbsolute),
        new CellAddress(Math.Max(a.Row, b.Row), Math.Max(a.Col, b.Col), b.RowAbsolute, b.ColAbsolute));

    public IEnumerable<CellAddress> Cells()
    {
        for (var r = Start.Row; r <= End.Row; r++)
            for (var c = Start.Col; c <= End.Col; c++)
                yield return new CellAddress(r, c);
    }

    public bool Contains(CellAddress a) =>
        a.Row >= Start.Row && a.Row <= End.Row && a.Col >= Start.Col && a.Col <= End.Col;

    public string ToA1() => Start == End ? Start.ToA1() : $"{Start.ToA1()}:{End.ToA1()}";

    public override string ToString() => ToA1();
}

/// <summary>A range qualified by sheet name, as produced by <c>Sheet2!A1:B4</c>.</summary>
public readonly record struct SheetRange(string? SheetName, CellRange Range)
{
    /// <summary>Splits an optional <c>SheetName!</c> prefix (quoted or bare) from the range part.</summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out SheetRange? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string? sheet = null;
        var body = text.AsSpan().Trim();

        var bang = body.LastIndexOf('!');
        if (bang >= 0)
        {
            var prefix = body[..bang].Trim();
            if (prefix.Length >= 2 && prefix[0] == '\'' && prefix[^1] == '\'')
                prefix = prefix[1..^1];
            sheet = prefix.ToString().Replace("''", "'");
            body = body[(bang + 1)..];
        }

        if (!CellRange.TryParse(body, out var range)) return false;
        result = new SheetRange(sheet, range);
        return true;
    }
}
