using VibeDesk.Application.Spreadsheets;

namespace VibeDesk.Ui.Components.Sheets;

/// <summary>
/// The current selection: an anchor cell plus an optional extent.
/// </summary>
/// <remarks>
/// Kept as anchor + extent rather than a normalised rectangle because the anchor is where typing goes
/// and where Enter/Tab move from — normalising would lose which corner the user started at.
/// </remarks>
public readonly record struct SheetSelection(CellAddress Anchor, CellAddress Extent)
{
    public static SheetSelection At(int row, int col)
    {
        var address = new CellAddress(row, col);
        return new SheetSelection(address, address);
    }

    public static SheetSelection At(CellAddress address) => new(address, address);

    /// <summary>Normalised rectangle covering the selection, top-left → bottom-right.</summary>
    public CellRange Range => CellRange.Normalise(Anchor, Extent);

    public bool IsSingleCell => Anchor.Row == Extent.Row && Anchor.Col == Extent.Col;

    public bool Contains(int row, int col)
    {
        var range = Range;
        return row >= range.Start.Row && row <= range.End.Row
            && col >= range.Start.Col && col <= range.End.Col;
    }

    public bool IsAnchor(int row, int col) => Anchor.Row == row && Anchor.Col == col;

    /// <summary>Extends the selection to a new corner, keeping the anchor put (shift-click).</summary>
    public SheetSelection ExtendTo(int row, int col) =>
        this with { Extent = new CellAddress(row, col) };

    /// <summary>Moves the whole selection, collapsing it to one cell (arrow keys, Enter, Tab).</summary>
    public SheetSelection MoveTo(int row, int col) => At(Math.Max(0, row), Math.Max(0, col));

    public string Label => IsSingleCell
        ? Anchor.ToKey()
        : $"{Range.Start.ToKey()}:{Range.End.ToKey()}";
}
