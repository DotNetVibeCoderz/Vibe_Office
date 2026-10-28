// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Globalization;
using ExcelNet.Styles;
using OfficeNet.Core;
using ExcelNet.Charts;
using ExcelNet.Pivot;
using ExcelNet.Validation;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;

namespace ExcelNet;

/// <summary>What a cell stores: its value, its formula, and which format it uses.</summary>
internal struct CellData
{
    public CellValue Value;
    public string? Formula;
    public int StyleIndex;
    public string? Comment;
    public string? HyperlinkTarget;

    /// <summary>The runs of a rich-text value; <c>null</c> for plain text.</summary>
    public IReadOnlyList<RichTextRun>? Rich;
}

/// <summary>A handle to one cell of a worksheet.</summary>
/// <remarks>
/// The handle is a view, not a copy: reading <see cref="Value"/> looks the cell up each time, so
/// two handles to <c>A1</c> can never disagree. Cells are stored sparsely, so a handle to an empty
/// cell allocates nothing until something is written to it.
/// </remarks>
public readonly struct Cell
{
    private readonly Worksheet _sheet;

    internal Cell(Worksheet sheet, CellReference reference)
    {
        _sheet = sheet;
        Reference = reference;
    }

    /// <summary>Where the cell is.</summary>
    public CellReference Reference { get; }

    /// <summary>The worksheet the cell belongs to.</summary>
    public Worksheet Worksheet => _sheet;

    /// <summary>The A1-style address.</summary>
    public string Address => Reference.A1;

    /// <summary>The cell's value.</summary>
    public CellValue Value
    {
        get => _sheet.GetValue(Reference);
        set => _sheet.SetValue(Reference, value);
    }

    /// <summary>
    /// The cell's formula, without the leading <c>=</c>, or <c>null</c> when it has none.
    /// </summary>
    /// <remarks>
    /// Setting a formula does not compute it. Excel evaluates on open, and this library caches the
    /// last known result in <see cref="Value"/> — which is what a consumer that does not evaluate
    /// formulas (including PdfNet's exporter and most viewers) will display. Call
    /// <see cref="Workbook.Recalculate"/> to fill the cached values in.
    /// </remarks>
    public string? Formula
    {
        get => _sheet.GetFormula(Reference);
        set => _sheet.SetFormula(Reference, value);
    }

    /// <summary>The cell's formatting.</summary>
    public CellStyle Style
    {
        get => _sheet.GetStyle(Reference);
        set => _sheet.SetStyle(Reference, value);
    }

    /// <summary>A comment attached to the cell.</summary>
    public string? Comment
    {
        get => _sheet.GetComment(Reference);
        set => _sheet.SetComment(Reference, value);
    }

    /// <summary>A hyperlink target, or <c>null</c>.</summary>
    /// <remarks>
    /// A URI links outside the workbook. A target starting with <c>#</c> (<c>#Sheet2!A1</c>, or
    /// <c>#MyName</c>) links to a place inside it and is written as the hyperlink's
    /// <c>location</c>, with no relationship.
    /// </remarks>
    public string? Hyperlink
    {
        get => _sheet.GetHyperlink(Reference);
        set => _sheet.SetHyperlink(Reference, value);
    }

    /// <summary>True when the cell holds nothing and has no formula.</summary>
    public bool IsEmpty => Value.IsEmpty && Formula is null;

    /// <summary>The value as text.</summary>
    public string Text => Value.AsText();

    /// <summary>The value as a number.</summary>
    public double Number => Value.AsNumber();

    /// <summary>The value as a date.</summary>
    public DateTime DateTime => Value.AsDateTime();

    /// <summary>Sets the value and returns the cell, for chaining.</summary>
    public Cell Set(object? value)
    {
        Value = CellValue.From(value);
        return this;
    }

    /// <summary>Sets the formula and returns the cell.</summary>
    public Cell SetFormula(string formula)
    {
        Formula = formula;
        return this;
    }

    /// <summary>Applies a style and returns the cell.</summary>
    public Cell WithStyle(CellStyle style)
    {
        Style = style;
        return this;
    }

    /// <summary>Makes the cell bold and returns it.</summary>
    public Cell Bold(bool value = true)
    {
        Style = Style.Bold(value);
        return this;
    }

    /// <summary>Sets the cell's background and returns it.</summary>
    public Cell WithBackground(OfficeColor color)
    {
        Style = Style.WithBackground(color);
        return this;
    }

    /// <summary>Sets the cell's number format and returns it.</summary>
    public Cell WithNumberFormat(string format)
    {
        Style = Style.WithNumberFormat(format);
        return this;
    }

    /// <summary>
    /// The runs of a cell whose text is partly formatted, or <c>null</c> when it is plain.
    /// </summary>
    public IReadOnlyList<RichTextRun>? RichText => _sheet.GetRichText(Reference);

    /// <summary>
    /// Sets text made of differently formatted runs:
    /// <c>WithRichText(new("Status: "), new("LUNAS") { Bold = true })</c>.
    /// </summary>
    /// <remarks>
    /// The cell's value becomes the runs' text joined, so reading <see cref="Text"/> still works.
    /// Setting an ordinary value afterwards replaces the runs.
    /// </remarks>
    public Cell WithRichText(params RichTextRun[] runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        _sheet.SetRichText(Reference, runs);
        return this;
    }

    /// <summary>Attaches a comment (<c>null</c> removes it) and returns the cell.</summary>
    /// <remarks>
    /// <see cref="Cell"/> is a value returned by the indexer, so <c>sheet["A1"].Comment = …</c>
    /// does not compile; this is the form that does.
    /// </remarks>
    public Cell WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <summary>Sets the cell's hyperlink (<c>null</c> removes it) and returns the cell.</summary>
    public Cell WithHyperlink(string? target)
    {
        Hyperlink = target;
        return this;
    }

    /// <summary>The cell offset by a number of rows and columns.</summary>
    public Cell Offset(int rows, int columns) => _sheet[Reference.Offset(rows, columns)];

    public override string ToString() => $"{Address}: {(Formula is null ? Text : "=" + Formula)}";
}

/// <summary>A rectangular block of cells, addressable as a unit.</summary>
public sealed class CellRange : IEnumerable<Cell>
{
    private readonly Worksheet _sheet;

    internal CellRange(Worksheet sheet, CellRangeReference reference)
    {
        _sheet = sheet;
        Reference = reference;
    }

    /// <summary>The block this range covers.</summary>
    public CellRangeReference Reference { get; }

    /// <summary>The worksheet the range belongs to.</summary>
    public Worksheet Worksheet => _sheet;

    /// <summary>The number of rows.</summary>
    public int RowCount => Reference.RowCount;

    /// <summary>The number of columns.</summary>
    public int ColumnCount => Reference.ColumnCount;

    /// <summary>A cell by its position within the range.</summary>
    public Cell this[int row, int column] =>
        _sheet[Reference.Start.Offset(row, column)];

    /// <summary>Fills every cell with the same value.</summary>
    public CellRange Fill(object? value)
    {
        var cellValue = CellValue.From(value);

        foreach (var reference in Reference.Cells())
        {
            _sheet.SetValue(reference, cellValue);
        }

        return this;
    }

    /// <summary>Applies a style to every cell.</summary>
    public CellRange ApplyStyle(CellStyle style)
    {
        foreach (var reference in Reference.Cells())
        {
            _sheet.SetStyle(reference, style);
        }

        return this;
    }

    /// <summary>Applies a transformation to every cell's style.</summary>
    public CellRange ModifyStyle(Func<CellStyle, CellStyle> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        foreach (var reference in Reference.Cells())
        {
            _sheet.SetStyle(reference, transform(_sheet.GetStyle(reference)));
        }

        return this;
    }

    /// <summary>Merges the range into one cell.</summary>
    public CellRange Merge()
    {
        _sheet.MergeCells(Reference);
        return this;
    }

    /// <summary>Sets a border around the range's outer edge.</summary>
    public CellRange SetOutlineBorder(BorderLineStyle style, OfficeColor? color = null)
    {
        foreach (var reference in Reference.Cells())
        {
            var current = _sheet.GetStyle(reference);
            var border = current.Border;

            // Only the cells on the perimeter get an edge, and only the edge that faces outwards.
            var updated = new CellBorder(
                reference.Column == Reference.Start.Column ? style : border.Left,
                reference.Column == Reference.End.Column ? style : border.Right,
                reference.Row == Reference.Start.Row ? style : border.Top,
                reference.Row == Reference.End.Row ? style : border.Bottom,
                color ?? border.Color);

            _sheet.SetStyle(reference, current with { Border = updated });
        }

        return this;
    }

    /// <summary>The range's values as a rectangular array, row-major.</summary>
    public object?[,] ToArray()
    {
        var result = new object?[RowCount, ColumnCount];

        for (var r = 0; r < RowCount; r++)
        {
            for (var c = 0; c < ColumnCount; c++)
            {
                result[r, c] = _sheet.GetValue(Reference.Start.Offset(r, c)).AsObject();
            }
        }

        return result;
    }

    /// <inheritdoc />
    public IEnumerator<Cell> GetEnumerator()
    {
        foreach (var reference in Reference.Cells())
        {
            yield return _sheet[reference];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => Reference.A1;
}

/// <summary>A conditional formatting rule.</summary>
/// <param name="Range">The cells the rule applies to.</param>
/// <param name="Type">The rule type, for example <c>cellIs</c> or <c>colorScale</c>.</param>
/// <param name="Operator">The comparison, for a <c>cellIs</c> rule.</param>
/// <param name="Formulas">The rule's operands.</param>
/// <param name="Style">The formatting applied when the rule matches.</param>
/// <param name="Priority">Lower numbers win when rules overlap.</param>
public sealed record ConditionalRule(
    CellRangeReference Range,
    string Type,
    string? Operator,
    IReadOnlyList<string> Formulas,
    CellStyle? Style,
    int Priority)
{
    /// <summary>The colours of a colour-scale rule, low to high.</summary>
    public IReadOnlyList<OfficeColor>? ScaleColors { get; init; }

    /// <summary>The bar colour of a data-bar rule.</summary>
    public OfficeColor? BarColor { get; init; }

    /// <summary>The icon set of an icon-set rule, e.g. <c>3Arrows</c> or <c>3TrafficLights1</c>.</summary>
    public string? IconSet { get; init; }

    /// <summary>Extra <c>cfRule</c> attributes: <c>rank</c>, <c>percent</c>, <c>bottom</c>, <c>aboveAverage</c>, <c>text</c>.</summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }

    /// <summary>
    /// The rule exactly as read from a file, written back as it was. Rules come in more shapes than
    /// this record models — icon sets with custom thresholds, data bars with extensions — and a
    /// round trip must not reduce them to the shapes it does.
    /// </summary>
    internal System.Xml.Linq.XElement? Raw { get; init; }

    /// <summary>The range list as read, which may name several ranges (<c>A1:A10 C1:C10</c>).</summary>
    internal string? Sqref { get; init; }
}

/// <summary>Where a sheet's panes are frozen.</summary>
/// <param name="Rows">How many rows stay visible at the top.</param>
/// <param name="Columns">How many columns stay visible at the left.</param>
public readonly record struct FreezePanes(int Rows, int Columns)
{
    /// <summary>No frozen panes.</summary>
    public static FreezePanes None => new(0, 0);

    /// <summary>True when nothing is frozen.</summary>
    public bool IsNone => Rows == 0 && Columns == 0;
}

/// <summary>
/// A worksheet: a sparse grid of cells plus the sheet-level settings around it.
/// </summary>
/// <remarks>
/// <para>
/// Cells are held in a dictionary rather than an array because spreadsheets are overwhelmingly
/// empty. A sheet whose used range is A1:Z10000 but which has values in one column costs ten
/// thousand entries here and would cost a quarter of a million in a dense array.
/// </para>
/// <para>
/// The model is parsed on open and written back on save, unlike WordNet's live-XML approach. A
/// worksheet's XML is a flat list of rows and cells with no structure worth preserving, and the
/// dictionary makes random access O(1) instead of a linear scan through elements.
/// </para>
/// </remarks>
public sealed class Worksheet
{
    private readonly Dictionary<CellReference, CellData> _cells = [];
    private readonly List<CellRangeReference> _merges = [];
    private readonly Dictionary<int, double> _columnWidths = [];
    private readonly Dictionary<int, double> _rowHeights = [];
    private readonly HashSet<int> _hiddenColumns = [];
    private readonly HashSet<int> _hiddenRows = [];
    private readonly List<ConditionalRule> _conditionalRules = [];

    internal Worksheet(Workbook workbook, string name, OpcPart part)
    {
        Workbook = workbook;
        Name = name;
        Part = part;
    }

    /// <summary>The workbook this sheet belongs to.</summary>
    public Workbook Workbook { get; }

    /// <summary>The sheet's part in the package.</summary>
    public OpcPart Part { get; internal set; }

    /// <summary>
    /// The sheet's name as it appears on its tab.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is empty, longer than 31 characters, or contains one of the characters Excel
    /// forbids.
    /// </exception>
    public string Name
    {
        get;
        set
        {
            ValidateName(value);

            if (field is not null && field != value)
            {
                Workbook.Names.SheetRenamed(field, value);
            }

            field = value;
            Workbook.Package.MarkDirty();
        }
    }

    /// <summary>
    /// The range printed, <c>A1:F40</c>, or <c>null</c> for the whole used range
    /// (<c>_xlnm.Print_Area</c>).
    /// </summary>
    public string? PrintArea
    {
        get => Workbook.Names.GetBuiltIn(this, "Print_Area") is { } refersTo &&
               DefinedNameCollection.TryParseReference(refersTo, out _, out var range)
            ? range.A1
            : null;
        set => Workbook.Names.SetBuiltIn(this, "Print_Area", value is null
            ? null
            : DefinedNameCollection.Qualified(Name, CellRangeReference.Parse(value)));
    }

    /// <summary>
    /// Rows repeated at the top of every printed page, as a one-based range such as <c>1:2</c>
    /// (<c>_xlnm.Print_Titles</c>), or <c>null</c>.
    /// </summary>
    public string? PrintTitleRows
    {
        get => Workbook.Names.GetBuiltIn(this, "Print_Titles") is { } refersTo
            ? refersTo[(refersTo.LastIndexOf('!') + 1)..].Replace("$", "", StringComparison.Ordinal)
            : null;
        set
        {
            if (value is null)
            {
                Workbook.Names.SetBuiltIn(this, "Print_Titles", null);
                return;
            }

            var parts = value.Split(':');

            if (parts.Length != 2 || !int.TryParse(parts[0], out var first) || !int.TryParse(parts[1], out var last) ||
                first < 1 || last < first)
            {
                throw new ArgumentException($"'{value}' is not a row range such as 1:2.", nameof(value));
            }

            Workbook.Names.SetBuiltIn(this, "Print_Titles",
                $"{DefinedNameCollection.Quote(Name)}!${first}:${last}");
        }
    }

    /// <summary>True when the sheet is hidden from the tab bar.</summary>
    public bool IsHidden { get; set; }

    /// <summary>True when the sheet is the one selected on open.</summary>
    public bool IsSelected { get; set; }

    /// <summary>Where the panes are frozen.</summary>
    public FreezePanes Frozen { get; set; } = FreezePanes.None;

    /// <summary>The range an autofilter covers, or <c>null</c>.</summary>
    public CellRangeReference? AutoFilter { get; set; }

    /// <summary>The sheet tab's colour.</summary>
    public OfficeColor? TabColor { get; set; }

    /// <summary>Whether gridlines are shown.</summary>
    public bool ShowGridLines { get; set; } = true;

    /// <summary>The default column width in characters.</summary>
    public double DefaultColumnWidth { get; set; } = 8.43;

    /// <summary>The default row height in points.</summary>
    public double DefaultRowHeight { get; set; } = 15;

    /// <summary>The conditional formatting rules on this sheet.</summary>
    public IReadOnlyList<ConditionalRule> ConditionalRules => _conditionalRules;

    /// <summary>The merged ranges.</summary>
    public IReadOnlyList<CellRangeReference> MergedRanges => _merges;

    /// <summary>Every cell that holds something, in row-major order.</summary>
    public IEnumerable<Cell> UsedCells =>
        _cells.Keys.Order().Select(reference => new Cell(this, reference));

    /// <summary>How many cells hold something.</summary>
    public int CellCount => _cells.Count;

    internal static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Length > 31)
        {
            throw new ArgumentException(
                $"A worksheet name may be at most 31 characters; '{name}' is {name.Length}.", nameof(name));
        }

        // Excel forbids these outright, and a workbook containing one is reported as corrupt
        // rather than repaired.
        const string Forbidden = @":\/?*[]";

        foreach (var c in name)
        {
            if (Forbidden.Contains(c))
            {
                throw new ArgumentException(
                    $"A worksheet name may not contain '{c}'. Forbidden characters: {Forbidden}",
                    nameof(name));
            }
        }

        if (name.StartsWith('\'') || name.EndsWith('\''))
        {
            throw new ArgumentException(
                "A worksheet name may not begin or end with an apostrophe.", nameof(name));
        }
    }

    // ---- Cell access ---------------------------------------------------------------------------

    /// <summary>A cell by reference.</summary>
    public Cell this[CellReference reference] => new(this, reference);

    /// <summary>A cell by A1 address.</summary>
    public Cell this[string address] => new(this, CellReference.Parse(address));

    /// <summary>A cell by zero-based row and column.</summary>
    public Cell this[int row, int column] => new(this, new CellReference(row, column));

    /// <summary>A range by A1 reference.</summary>
    public CellRange Range(string reference) => new(this, CellRangeReference.Parse(reference));

    /// <summary>A range by corners.</summary>
    public CellRange Range(int firstRow, int firstColumn, int lastRow, int lastColumn) =>
        new(this, new CellRangeReference(firstRow, firstColumn, lastRow, lastColumn));

    /// <summary>A range by reference.</summary>
    public CellRange Range(CellRangeReference reference) => new(this, reference);

    internal CellValue GetValue(CellReference reference) =>
        _cells.TryGetValue(reference, out var data) ? data.Value : CellValue.Empty;

    internal void SetValue(CellReference reference, CellValue value)
    {
        ref var data = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_cells, reference, out var existed);

        if (!existed)
        {
            data = new CellData { Value = value };
        }
        else
        {
            data.Value = value;
            // Writing a literal value replaces whatever formula was there. Leaving the formula
            // would make Excel overwrite the value on its next calculation.
            data.Formula = null;
        }

        // And whatever rich text was there: the runs described the old text, not this value.
        data.Rich = null;

        // A date needs a number format, or it shows as the serial number. The style is only
        // supplied when the cell has none, so an explicit format is never overwritten.
        if (value.ValueType == CellValueType.DateTime && data.StyleIndex == 0)
        {
            data.StyleIndex = Workbook.Styles.IndexOf(
                CellStyle.Default.WithNumberFormat(NumberFormats.ShortDate));
        }

        Workbook.Package.MarkDirty();
    }

    internal string? GetFormula(CellReference reference) =>
        _cells.TryGetValue(reference, out var data) ? data.Formula : null;

    internal void SetFormula(CellReference reference, string? formula)
    {
        if (formula is not null)
        {
            formula = formula.TrimStart();

            if (formula.StartsWith('='))
            {
                formula = formula[1..];
            }
        }

        ref var data = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_cells, reference, out _);

        data.Formula = string.IsNullOrWhiteSpace(formula) ? null : formula;
        Workbook.Package.MarkDirty();
    }

    internal CellStyle GetStyle(CellReference reference) =>
        Workbook.Styles.StyleAt(_cells.TryGetValue(reference, out var data) ? data.StyleIndex : 0);

    internal void SetStyle(CellReference reference, CellStyle style)
    {
        ref var data = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_cells, reference, out _);

        data.StyleIndex = Workbook.Styles.IndexOf(style);
        Workbook.Package.MarkDirty();
    }

    internal int GetStyleIndex(CellReference reference) =>
        _cells.TryGetValue(reference, out var data) ? data.StyleIndex : 0;

    internal void SetStyleIndex(CellReference reference, int index)
    {
        ref var data = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_cells, reference, out _);

        data.StyleIndex = index;
    }

    internal string? GetComment(CellReference reference) =>
        _cells.TryGetValue(reference, out var data) ? data.Comment : null;

    internal void SetComment(CellReference reference, string? comment)
    {
        ref var data = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_cells, reference, out _);

        data.Comment = comment;
        Workbook.Package.MarkDirty();
    }

    /// <summary>Sets a comment read from the file, without marking the package dirty.</summary>
    internal void LoadComment(CellReference reference, string comment)
    {
        ref var data = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_cells, reference, out _);

        data.Comment = comment;
    }

    /// <summary>Sets a hyperlink read from the file, without marking the package dirty.</summary>
    internal void LoadHyperlink(CellReference reference, string target)
    {
        ref var data = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_cells, reference, out _);

        data.HyperlinkTarget = target;
    }

    internal IReadOnlyList<RichTextRun>? GetRichText(CellReference reference) =>
        _cells.TryGetValue(reference, out var data) ? data.Rich : null;

    internal void SetRichText(CellReference reference, IReadOnlyList<RichTextRun> runs)
    {
        SetValue(reference, CellValue.FromText(string.Concat(runs.Select(r => r.Text))));

        ref var data = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_cells, reference, out _);

        data.Rich = runs.Count == 0 ? null : runs;
    }

    internal string? GetHyperlink(CellReference reference) =>
        _cells.TryGetValue(reference, out var data) ? data.HyperlinkTarget : null;

    internal void SetHyperlink(CellReference reference, string? target)
    {
        ref var data = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_cells, reference, out _);

        data.HyperlinkTarget = target;
        Workbook.Package.MarkDirty();
    }

    internal bool TryGetCellData(CellReference reference, out CellData data) =>
        _cells.TryGetValue(reference, out data);

    internal void PutCellData(CellReference reference, CellData data) => _cells[reference] = data;

    internal IReadOnlyDictionary<CellReference, CellData> RawCells => _cells;

    /// <summary>
    /// Sorts the rows of a range by one or more columns, the way Excel does: numbers, then text,
    /// then logical values, then errors, blanks last in either direction.
    /// </summary>
    /// <example><c>sheet.Sort("A2:E100", new SortKey("C", Descending: true), new SortKey("A"));</c></example>
    /// <remarks>
    /// Whole rows of the range move — values, formulas, styles, comments, links — and a moved
    /// formula's relative row references move with it. Leave the header row out of the range.
    /// </remarks>
    /// <exception cref="OfficeNetException">The range contains merged cells.</exception>
    public Worksheet Sort(string range, params SortKey[] keys)
    {
        Sorting.Sort(this, CellRangeReference.Parse(range), keys);
        return this;
    }

    /// <summary>Removes a cell entirely, including its style.</summary>
    public void Clear(CellReference reference)
    {
        if (_cells.Remove(reference))
        {
            Workbook.Package.MarkDirty();
        }
    }

    /// <summary>Removes every cell in a range.</summary>
    public void Clear(CellRangeReference range)
    {
        foreach (var reference in range.Cells())
        {
            _cells.Remove(reference);
        }

        Workbook.Package.MarkDirty();
    }

    // ---- Extent --------------------------------------------------------------------------------

    /// <summary>
    /// The block covering every non-empty cell, or <c>null</c> when the sheet is empty.
    /// </summary>
    public CellRangeReference? UsedRange
    {
        get
        {
            if (_cells.Count == 0)
            {
                return null;
            }

            var minRow = int.MaxValue;
            var minColumn = int.MaxValue;
            var maxRow = 0;
            var maxColumn = 0;

            foreach (var reference in _cells.Keys)
            {
                minRow = Math.Min(minRow, reference.Row);
                minColumn = Math.Min(minColumn, reference.Column);
                maxRow = Math.Max(maxRow, reference.Row);
                maxColumn = Math.Max(maxColumn, reference.Column);
            }

            return new CellRangeReference(minRow, minColumn, maxRow, maxColumn);
        }
    }

    /// <summary>The number of rows from row 1 to the last used row.</summary>
    public int RowCount => UsedRange is { } range ? range.End.Row + 1 : 0;

    /// <summary>The number of columns from column A to the last used column.</summary>
    public int ColumnCount => UsedRange is { } range ? range.End.Column + 1 : 0;

    // ---- Rows and columns ----------------------------------------------------------------------

    /// <summary>Sets a column's width in characters.</summary>
    public Worksheet SetColumnWidth(int column, double width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        _columnWidths[column] = width;
        Workbook.Package.MarkDirty();
        return this;
    }

    /// <summary>Sets a column's width by letter.</summary>
    public Worksheet SetColumnWidth(string columnName, double width) =>
        SetColumnWidth(CellReference.ColumnNameToIndex(columnName), width);

    /// <summary>A column's width, or the sheet default.</summary>
    public double GetColumnWidth(int column) =>
        _columnWidths.TryGetValue(column, out var width) ? width : DefaultColumnWidth;

    /// <summary>Sets a row's height in points.</summary>
    public Worksheet SetRowHeight(int row, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        _rowHeights[row] = height;
        Workbook.Package.MarkDirty();
        return this;
    }

    /// <summary>A row's height, or the sheet default.</summary>
    public double GetRowHeight(int row) =>
        _rowHeights.TryGetValue(row, out var height) ? height : DefaultRowHeight;

    /// <summary>Hides a column.</summary>
    public Worksheet HideColumn(int column)
    {
        _hiddenColumns.Add(column);
        Workbook.Package.MarkDirty();
        return this;
    }

    /// <summary>Hides a row.</summary>
    public Worksheet HideRow(int row)
    {
        _hiddenRows.Add(row);
        Workbook.Package.MarkDirty();
        return this;
    }

    internal IReadOnlyDictionary<int, double> ColumnWidths => _columnWidths;

    internal IReadOnlyDictionary<int, double> RowHeights => _rowHeights;

    internal IReadOnlySet<int> HiddenColumns => _hiddenColumns;

    internal IReadOnlySet<int> HiddenRows => _hiddenRows;

    /// <summary>
    /// Sizes every used column to its widest content.
    /// </summary>
    /// <remarks>
    /// Excel's column width unit is "the width of the digit zero in the workbook's default font",
    /// which is not a length and cannot be computed exactly without the font. The approximation
    /// here counts characters and adds padding — close enough that no column is clipped, which is
    /// the failure that matters.
    /// </remarks>
    public Worksheet AutoFitColumns(double minimum = 8, double maximum = 60)
    {
        var widest = new Dictionary<int, int>();

        foreach (var (reference, data) in _cells)
        {
            var text = data.Formula is not null ? data.Value.AsText() : data.Value.AsText();
            var length = text.Length;

            // Bold text is roughly 8% wider at the same point size.
            if (Workbook.Styles.StyleAt(data.StyleIndex).Font.Bold)
            {
                length = (int)Math.Ceiling(length * 1.08);
            }

            if (!widest.TryGetValue(reference.Column, out var current) || length > current)
            {
                widest[reference.Column] = length;
            }
        }

        foreach (var (column, length) in widest)
        {
            SetColumnWidth(column, Math.Clamp(length + 2, minimum, maximum));
        }

        return this;
    }

    // ---- Merging -------------------------------------------------------------------------------

    /// <summary>
    /// Merges a range into a single cell.
    /// </summary>
    /// <exception cref="OfficeNetException">The range overlaps an existing merge.</exception>
    public void MergeCells(CellRangeReference range)
    {
        foreach (var existing in _merges)
        {
            if (existing.Intersects(range))
            {
                throw new OfficeNetException(
                    $"The range {range.A1} overlaps the existing merge {existing.A1}. " +
                    "Excel reports overlapping merges as corruption rather than repairing them.");
            }
        }

        // Only the top-left cell keeps its value; Excel discards the rest and so does this, because
        // leaving them makes the file's content disagree with what is displayed.
        foreach (var reference in range.Cells())
        {
            if (reference != range.Start)
            {
                _cells.Remove(reference);
            }
        }

        _merges.Add(range);
        Workbook.Package.MarkDirty();
    }

    /// <summary>Merges a range given as an A1 reference.</summary>
    public void MergeCells(string range) => MergeCells(CellRangeReference.Parse(range));

    /// <summary>Removes a merge.</summary>
    public bool UnmergeCells(CellRangeReference range)
    {
        var removed = _merges.Remove(range);

        if (removed)
        {
            Workbook.Package.MarkDirty();
        }

        return removed;
    }

    internal void AddMergeUnchecked(CellRangeReference range) => _merges.Add(range);

    // ---- Conditional formatting ------------------------------------------------------------------

    /// <summary>Adds a rule that formats cells whose value satisfies a comparison.</summary>
    /// <param name="range">The cells to test.</param>
    /// <param name="comparison">
    /// One of <c>greaterThan</c>, <c>lessThan</c>, <c>equal</c>, <c>between</c>,
    /// <c>notEqual</c>, <c>greaterThanOrEqual</c>, <c>lessThanOrEqual</c>.
    /// </param>
    /// <param name="style">The formatting to apply.</param>
    /// <param name="operands">The values compared against.</param>
    public ConditionalRule AddConditionalFormat(CellRangeReference range, string comparison,
        CellStyle style, params string[] operands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comparison);
        ArgumentNullException.ThrowIfNull(operands);

        var rule = new ConditionalRule(range, "cellIs", comparison, operands, style,
            _conditionalRules.Count + 1);

        _conditionalRules.Add(rule);
        Workbook.Package.MarkDirty();
        return rule;
    }

    /// <summary>Adds a two- or three-colour scale.</summary>
    public ConditionalRule AddColorScale(CellRangeReference range, params OfficeColor[] colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        if (colors.Length is not (2 or 3))
        {
            throw new ArgumentException("A colour scale needs two or three colours.", nameof(colors));
        }

        var rule = new ConditionalRule(range, "colorScale", null, [], null,
            _conditionalRules.Count + 1)
        {
            ScaleColors = colors,
        };

        _conditionalRules.Add(rule);
        Workbook.Package.MarkDirty();
        return rule;
    }

    /// <summary>Highlights every value that appears more than once in the range.</summary>
    public ConditionalRule AddDuplicateValues(string range, CellStyle style) =>
        AddRule(range, "duplicateValues", style);

    /// <summary>Highlights every value that appears exactly once in the range.</summary>
    public ConditionalRule AddUniqueValues(string range, CellStyle style) =>
        AddRule(range, "uniqueValues", style);

    /// <summary>Highlights the top (or bottom) <paramref name="count"/> values, or percent of values.</summary>
    public ConditionalRule AddTopRule(string range, int count, CellStyle style, bool percent = false,
        bool bottom = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var attributes = new Dictionary<string, string> { ["rank"] = count.ToString(CultureInfo.InvariantCulture) };

        if (percent)
        {
            attributes["percent"] = "1";
        }

        if (bottom)
        {
            attributes["bottom"] = "1";
        }

        return AddRule(range, "top10", style, attributes: attributes);
    }

    /// <summary>Highlights values above (or below) the range's average.</summary>
    public ConditionalRule AddAverageRule(string range, CellStyle style, bool below = false) =>
        AddRule(range, "aboveAverage", style,
            attributes: below ? new Dictionary<string, string> { ["aboveAverage"] = "0" } : null);

    /// <summary>Highlights cells whose text contains <paramref name="text"/>, ignoring case.</summary>
    /// <remarks>
    /// Excel stores the text twice — as an attribute for its dialog and as the formula it actually
    /// evaluates — and a rule with only the attribute matches nothing.
    /// </remarks>
    public ConditionalRule AddTextContains(string range, string text, CellStyle style)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        var topLeft = CellRangeReference.Parse(range).Start.A1;
        var escaped = text.Replace("\"", "\"\"", StringComparison.Ordinal);

        return AddRule(range, "containsText", style, "containsText",
            [$"NOT(ISERROR(SEARCH(\"{escaped}\",{topLeft})))"],
            new Dictionary<string, string> { ["text"] = text });
    }

    /// <summary>
    /// Highlights cells for which a formula is true, written for the range's top-left cell:
    /// <c>AddFormulaRule("A2:E100", "$E2&gt;1000000", style)</c> shades whole rows.
    /// </summary>
    public ConditionalRule AddFormulaRule(string range, string formula, CellStyle style)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);
        return AddRule(range, "expression", style, formulas: [formula.TrimStart('=')]);
    }

    /// <summary>
    /// Adds an icon set — <c>3Arrows</c>, <c>3TrafficLights1</c>, <c>3Symbols</c>, <c>4Rating</c>,
    /// <c>5Arrows</c> and the rest of Excel's names — split into equal percent bands.
    /// </summary>
    public ConditionalRule AddIconSet(string range, string iconSet = "3TrafficLights1")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconSet);

        if (iconSet[0] is not ('3' or '4' or '5'))
        {
            throw new ArgumentException($"'{iconSet}' is not an Excel icon set name, such as 3Arrows.", nameof(iconSet));
        }

        var rule = new ConditionalRule(CellRangeReference.Parse(range), "iconSet", null, [], null,
            _conditionalRules.Count + 1)
        {
            IconSet = iconSet,
        };

        _conditionalRules.Add(rule);
        Workbook.Package.MarkDirty();
        return rule;
    }

    /// <summary>The cells in a range whose value appears more than once, text compared ignoring case.</summary>
    /// <remarks>
    /// The same test as <see cref="AddDuplicateValues"/>, for code that wants the cells rather than
    /// a highlight. Blank cells are never duplicates.
    /// </remarks>
    public IReadOnlyList<CellReference> FindDuplicates(string range)
    {
        var cells = Range(range)
            .Where(c => !c.Value.IsEmpty)
            .Select(c => (c.Reference, Key: c.Value.ValueType == CellValueType.Text
                ? "t:" + c.Value.AsText().ToUpperInvariant()
                : "n:" + c.Value.AsNumber().ToString("R", CultureInfo.InvariantCulture)))
            .ToList();

        var repeated = cells.GroupBy(c => c.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return [.. cells.Where(c => repeated.Contains(c.Key)).Select(c => c.Reference)];
    }

    private ConditionalRule AddRule(string range, string type, CellStyle? style, string? comparison = null,
        IReadOnlyList<string>? formulas = null, IReadOnlyDictionary<string, string>? attributes = null)
    {
        var rule = new ConditionalRule(CellRangeReference.Parse(range), type, comparison, formulas ?? [],
            style, _conditionalRules.Count + 1)
        {
            Attributes = attributes,
        };

        _conditionalRules.Add(rule);
        Workbook.Package.MarkDirty();
        return rule;
    }

    /// <summary>Adds a data-bar rule.</summary>
    public ConditionalRule AddDataBar(CellRangeReference range, OfficeColor color)
    {
        var rule = new ConditionalRule(range, "dataBar", null, [], null, _conditionalRules.Count + 1)
        {
            BarColor = color,
        };

        _conditionalRules.Add(rule);
        Workbook.Package.MarkDirty();
        return rule;
    }

    // ---- Validation and protection ---------------------------------------------------------------

    private readonly List<DataValidation> _validations = [];

    /// <summary>The data-validation rules on this sheet.</summary>
    public IReadOnlyList<DataValidation> Validations => _validations;

    /// <summary>Restricts what can be typed into a range.</summary>
    /// <remarks>
    /// The rule that makes a sheet fillable by a person: a dropdown of valid regions is the
    /// difference between clean data and a column of "Jakarta"/"jakarta"/"DKI Jakarta".
    /// </remarks>
    public DataValidation AddValidation(DataValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        _validations.Add(validation);
        Workbook.Package.MarkDirty();

        return validation;
    }

    /// <summary>Adds a dropdown of fixed values to a range.</summary>
    public DataValidation AddDropdown(string range, params string[] values) =>
        AddValidation(DataValidation.List(CellRangeReference.Parse(range), values));

    /// <summary>Removes every validation rule from the sheet.</summary>
    public void ClearValidations()
    {
        _validations.Clear();
        Workbook.Package.MarkDirty();
    }

    /// <summary>The sheet's protection, or <c>null</c> when it is unprotected.</summary>
    public SheetProtection? Protection { get; set; }

    /// <summary>
    /// Protects the sheet, optionally leaving a range editable.
    /// </summary>
    /// <param name="editable">
    /// Cells that stay editable. Omitting it locks everything, which is rarely what a template
    /// wants.
    /// </param>
    /// <param name="password">
    /// Deters accidents only — the hash is 16 bits and any tool strips it in milliseconds. Never
    /// use it to keep a secret.
    /// </param>
    /// <remarks>
    /// Every cell is locked by default, and protection only bites on locked cells. So protecting a
    /// sheet without unlocking the input cells freezes the whole thing — which is why this method
    /// takes the editable range and unlocks it for you.
    /// </remarks>
    public void Protect(string? editable = null, string? password = null)
    {
        if (editable is { Length: > 0 })
        {
            Range(editable).ModifyStyle(style => style with { Locked = false });
        }

        Protection = password is { Length: > 0 }
            ? SheetProtection.WithPassword(password)
            : new SheetProtection();

        Workbook.Package.MarkDirty();
    }

    /// <summary>Removes protection from the sheet.</summary>
    public void Unprotect()
    {
        Protection = null;
        Workbook.Package.MarkDirty();
    }

    // ---- Pivot tables --------------------------------------------------------------------------

    /// <summary>Adds a pivot table summarising a range on another sheet (or this one).</summary>
    /// <remarks>
    /// Excel computes the result grid when it opens the file; the parts written here are the cache
    /// and the layout. See <see cref="Pivot.PivotTable"/> for why.
    /// </remarks>
    public PivotTable AddPivotTable(PivotTableDefinition definition) =>
        PivotTable.Create(this, definition);

    /// <summary>The pivot tables on this sheet.</summary>
    public IReadOnlyList<OpcPart> PivotTableParts =>
        [.. Part.RelationshipsByType(RelationshipTypes.PivotTable)
            .Select(r => Part.RelatedPart(r.Id))
            .Where(p => p is not null)
            .Select(p => p!)];

    // ---- Charts --------------------------------------------------------------------------------

    /// <summary>The charts floating over this sheet.</summary>
    /// <remarks>
    /// Read from the drawing parts each time rather than cached, so a chart added or removed
    /// through the package layer is not missed.
    /// </remarks>
    public IReadOnlyList<SheetChart> Charts => [.. SheetChart.Read(this)];

    /// <summary>Adds a chart anchored to a range of cells.</summary>
    /// <param name="data">What to plot.</param>
    /// <param name="anchor">
    /// The cells the chart covers, for example <c>H2:P20</c>. Both corners are pinned, so the chart
    /// resizes with the rows and columns beneath it.
    /// </param>
    public SheetChart AddChart(ChartData data, CellRangeReference anchor) =>
        SheetChart.Create(this, data, anchor);

    /// <summary>Adds a chart anchored to a range written in A1 notation.</summary>
    public SheetChart AddChart(ChartData data, string anchor) =>
        AddChart(data, CellRangeReference.Parse(anchor));

    internal void AddConditionalRuleUnchecked(ConditionalRule rule) => _conditionalRules.Add(rule);

    // ---- Bulk writing ----------------------------------------------------------------------------

    /// <summary>Writes a row of values starting at a cell.</summary>
    public Worksheet WriteRow(CellReference start, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        for (var i = 0; i < values.Length; i++)
        {
            SetValue(start.Offset(0, i), CellValue.From(values[i]));
        }

        return this;
    }

    /// <summary>Writes a column of values starting at a cell.</summary>
    public Worksheet WriteColumn(CellReference start, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        for (var i = 0; i < values.Length; i++)
        {
            SetValue(start.Offset(i, 0), CellValue.From(values[i]));
        }

        return this;
    }

    /// <summary>Writes a rectangular block of values.</summary>
    public Worksheet WriteRange(CellReference start, IEnumerable<IEnumerable<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var r = 0;

        foreach (var row in rows)
        {
            var c = 0;

            foreach (var value in row)
            {
                SetValue(start.Offset(r, c), CellValue.From(value));
                c++;
            }

            r++;
        }

        return this;
    }

    /// <summary>
    /// Writes a header row and formats it as one: bold, filled, frozen and filterable.
    /// </summary>
    public Worksheet WriteHeader(CellReference start, IReadOnlyList<string> headers,
        OfficeColor? background = null, bool freeze = true, bool autoFilter = true)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var fill = background ?? OfficeColor.FromRgb(0x1F, 0x38, 0x64);

        var style = CellStyle.Default
            .Bold()
            .WithColor(fill.ContrastingForeground)
            .WithBackground(fill)
            .WithAlignment(HorizontalAlignment.Center, VerticalAlignment.Center);

        for (var i = 0; i < headers.Count; i++)
        {
            var reference = start.Offset(0, i);
            SetValue(reference, CellValue.FromText(headers[i]));
            SetStyle(reference, style);
        }

        if (freeze)
        {
            Frozen = new FreezePanes(start.Row + 1, 0);
        }

        if (autoFilter && headers.Count > 0)
        {
            AutoFilter = new CellRangeReference(start, start.Offset(0, headers.Count - 1));
        }

        return this;
    }

    public override string ToString() => $"Worksheet \"{Name}\" ({CellCount} cells)";
}
