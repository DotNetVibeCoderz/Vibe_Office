// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace WordNet.Tables;

/// <summary>How a cell's content sits vertically inside it.</summary>
public enum CellVerticalAlignment
{
    /// <summary>Against the top edge.</summary>
    Top,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Against the bottom edge.</summary>
    Bottom,
}

/// <summary>
/// A cell of a table.
/// </summary>
/// <remarks>
/// A cell always contains at least one paragraph. WordprocessingML requires it and Word reports a
/// document with an empty <c>w:tc</c> as corrupt, which is why the cell constructor creates one and
/// <see cref="Text"/> never removes the last.
/// </remarks>
public sealed class TableCell
{
    private readonly WordDocument _document;

    internal TableCell(WordDocument document, XElement element)
    {
        _document = document;
        Element = element;
    }

    private static readonly XName[] CellPropertyOrder =
    [
        Ns.W + "cnfStyle", Ns.W + "tcW", Ns.W + "gridSpan", Ns.W + "hMerge", Ns.W + "vMerge",
        Ns.W + "tcBorders", Ns.W + "shd", Ns.W + "noWrap", Ns.W + "tcMar", Ns.W + "textDirection",
        Ns.W + "tcFitText", Ns.W + "vAlign", Ns.W + "hideMark",
    ];

    /// <summary>The underlying <c>w:tc</c> element.</summary>
    public XElement Element { get; }

    private XElement Properties => XmlUtil.GetOrCreate(Element, Ns.W + "tcPr", [Ns.W + "tcPr"]);

    /// <summary>The cell's paragraphs.</summary>
    public IReadOnlyList<Paragraph> Paragraphs =>
        [.. Element.Elements(Ns.W + "p").Select(e => new Paragraph(_document, e))];

    /// <summary>The nested tables inside this cell.</summary>
    public IReadOnlyList<Table> Tables =>
        [.. Element.Elements(Ns.W + "tbl").Select(e => new Table(_document, e))];

    /// <summary>
    /// The cell's text, with paragraphs joined by newlines. Setting it replaces the content with a
    /// single paragraph per line.
    /// </summary>
    public string Text
    {
        get => string.Join('\n', Paragraphs.Select(p => p.Text));
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var properties = Element.Element(Ns.W + "tcPr");
            Element.RemoveNodes();

            if (properties is not null)
            {
                Element.Add(properties);
            }

            foreach (var line in value.Replace("\r\n", "\n").Split('\n'))
            {
                AddParagraph(line);
            }

            // A cell must never be empty, so an empty assignment still leaves one paragraph.
            if (Element.Element(Ns.W + "p") is null)
            {
                AddParagraph();
            }

            _document.Touch();
        }
    }

    /// <summary>Appends a paragraph to the cell.</summary>
    public Paragraph AddParagraph(string text = "", string? styleId = null)
    {
        var element = new XElement(Ns.W + "p");
        Element.Add(element);

        var paragraph = new Paragraph(_document, element);

        if (styleId is not null)
        {
            paragraph.StyleId = styleId;
        }

        if (text.Length > 0)
        {
            paragraph.AddRun(text);
        }

        _document.Touch();
        return paragraph;
    }

    /// <summary>Appends a nested table.</summary>
    public Table AddTable(int rows, int columns)
    {
        var table = Table.Create(_document, rows, columns);
        Element.Add(table.Element);

        // A nested table must be followed by a paragraph or Word treats the cell as malformed.
        Element.Add(new XElement(Ns.W + "p"));

        _document.Touch();
        return table;
    }

    /// <summary>The cell's background colour.</summary>
    public OfficeColor? Shading
    {
        get
        {
            var raw = Element.Element(Ns.W + "tcPr")?.Element(Ns.W + "shd")
                ?.Attribute(Ns.W + "fill")?.Value;
            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "shd", value is null
                ? null
                : new XElement(Ns.W + "shd",
                    new XAttribute(Ns.W + "val", "clear"),
                    new XAttribute(Ns.W + "color", "auto"),
                    new XAttribute(Ns.W + "fill", value.Value.ToHex())), CellPropertyOrder);
            _document.Touch();
        }
    }

    /// <summary>How the content sits vertically.</summary>
    public CellVerticalAlignment? VerticalAlignment
    {
        get => Element.Element(Ns.W + "tcPr").Val(Ns.W + "vAlign") switch
        {
            "center" => CellVerticalAlignment.Center,
            "bottom" => CellVerticalAlignment.Bottom,
            "top" => CellVerticalAlignment.Top,
            _ => null,
        };
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "vAlign", value is null
                ? null
                : XmlUtil.ValElement(Ns.W + "vAlign", value.Value switch
                {
                    CellVerticalAlignment.Center => "center",
                    CellVerticalAlignment.Bottom => "bottom",
                    _ => "top",
                }), CellPropertyOrder);
            _document.Touch();
        }
    }

    /// <summary>The cell's width, or <c>null</c> when it is automatic.</summary>
    public Length? Width
    {
        get
        {
            var element = Element.Element(Ns.W + "tcPr")?.Element(Ns.W + "tcW");
            var type = element?.Attribute(Ns.W + "type")?.Value;
            var raw = element?.Attribute(Ns.W + "w")?.Value;

            return type == "dxa" && raw is not null && double.TryParse(raw, out var twips)
                ? Length.FromTwips(twips)
                : null;
        }
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "tcW", value is null
                ? null
                : new XElement(Ns.W + "tcW",
                    new XAttribute(Ns.W + "w", XmlUtil.Num(value.Value.Twips)),
                    new XAttribute(Ns.W + "type", "dxa")), CellPropertyOrder);
            _document.Touch();
        }
    }

    /// <summary>How many grid columns this cell spans.</summary>
    public int GridSpan
    {
        get
        {
            var raw = Element.Element(Ns.W + "tcPr").Val(Ns.W + "gridSpan");
            return raw is not null && int.TryParse(raw, out var span) ? span : 1;
        }
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "gridSpan", value <= 1
                ? null
                : XmlUtil.ValElement(Ns.W + "gridSpan", XmlUtil.Num(value)), CellPropertyOrder);
            _document.Touch();
        }
    }

    /// <summary>Sets a border on every edge of the cell.</summary>
    public TableCell SetBorder(BorderStyle style, OfficeColor? color = null, Length? width = null)
    {
        if (style == BorderStyle.None)
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "tcBorders", null, CellPropertyOrder);
            _document.Touch();
            return this;
        }

        var borders = new XElement(Ns.W + "tcBorders");
        foreach (var edge in (string[])["top", "left", "bottom", "right"])
        {
            borders.Add(ParagraphFormat.BorderElement(Ns.W + edge, style, color, width));
        }

        XmlUtil.SetOrdered(Properties, Ns.W + "tcBorders", borders, CellPropertyOrder);
        _document.Touch();
        return this;
    }

    /// <summary>
    /// Marks the cell as the start or a continuation of a vertical merge.
    /// </summary>
    /// <remarks>
    /// A vertically merged run of cells is not one cell: every row still has its own <c>w:tc</c>,
    /// with the first carrying <c>w:vMerge val="restart"</c> and the rest a bare <c>w:vMerge</c>.
    /// Deleting the continuation cells instead — which looks like the obvious way to merge — makes
    /// the rows shorter than the grid and Word repairs the table by dropping the merge.
    /// </remarks>
    internal void SetVerticalMerge(bool isStart)
    {
        var element = isStart
            ? XmlUtil.ValElement(Ns.W + "vMerge", "restart")
            : new XElement(Ns.W + "vMerge");

        XmlUtil.SetOrdered(Properties, Ns.W + "vMerge", element, CellPropertyOrder);
        _document.Touch();
    }

    public override string ToString() => Text;
}

/// <summary>A row of a table.</summary>
public sealed class TableRow
{
    private readonly WordDocument _document;

    internal TableRow(WordDocument document, XElement element)
    {
        _document = document;
        Element = element;
    }

    /// <summary>The underlying <c>w:tr</c> element.</summary>
    public XElement Element { get; }

    // CT_Row is a sequence: tblPrEx?, trPr?, then the cell content. Passing an order list that
    // holds only w:trPr leaves SetOrdered with no successor to insert before, so it appends — and
    // the properties land after the last w:tc, which Word reports as a repairable error.
    private static readonly XName[] RowOrder =
        [Ns.W + "tblPrEx", Ns.W + "trPr", Ns.W + "tc", Ns.W + "customXml", Ns.W + "sdt"];

    private XElement Properties => XmlUtil.GetOrCreate(Element, Ns.W + "trPr", RowOrder);

    /// <summary>The row's cells.</summary>
    public IReadOnlyList<TableCell> Cells =>
        [.. Element.Elements(Ns.W + "tc").Select(e => new TableCell(_document, e))];

    /// <summary>A cell by index.</summary>
    /// <remarks>
    /// Walks to the element rather than going through <see cref="Cells"/>, which materialises a
    /// wrapper for every cell in the row on each access. Filling a 500-row table cell by cell that
    /// way allocated 55 MB and took 92 ms; the difference is entirely those throwaway wrappers.
    /// </remarks>
    public TableCell this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);

            var element = Element.Elements(Ns.W + "tc").Skip(index).FirstOrDefault()
                ?? throw new ArgumentOutOfRangeException(nameof(index));

            return new TableCell(_document, element);
        }
    }

    /// <summary>The number of cells in the row.</summary>
    public int CellCount => Element.Elements(Ns.W + "tc").Count();

    /// <summary>
    /// Repeats this row as a header at the top of every page the table spans.
    /// </summary>
    public bool IsHeader
    {
        get => Element.Element(Ns.W + "trPr")?.Element(Ns.W + "tblHeader") is not null;
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "tblHeader",
                value ? new XElement(Ns.W + "tblHeader") : null,
                [Ns.W + "cnfStyle", Ns.W + "divId", Ns.W + "gridBefore", Ns.W + "gridAfter",
                 Ns.W + "wBefore", Ns.W + "wAfter", Ns.W + "cantSplit", Ns.W + "trHeight",
                 Ns.W + "tblHeader", Ns.W + "tblCellSpacing", Ns.W + "jc", Ns.W + "hidden"]);
            _document.Touch();
        }
    }

    /// <summary>Keeps the row's content on one page.</summary>
    public bool CannotSplit
    {
        get => Element.Element(Ns.W + "trPr")?.Element(Ns.W + "cantSplit") is not null;
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "cantSplit",
                value ? new XElement(Ns.W + "cantSplit") : null,
                [Ns.W + "cnfStyle", Ns.W + "divId", Ns.W + "gridBefore", Ns.W + "gridAfter",
                 Ns.W + "wBefore", Ns.W + "wAfter", Ns.W + "cantSplit", Ns.W + "trHeight",
                 Ns.W + "tblHeader"]);
            _document.Touch();
        }
    }

    /// <summary>The row's height, or <c>null</c> when it is automatic.</summary>
    public Length? Height
    {
        get
        {
            var raw = Element.Element(Ns.W + "trPr")?.Element(Ns.W + "trHeight")
                ?.Attribute(Ns.W + "val")?.Value;
            return raw is not null && double.TryParse(raw, out var twips) ? Length.FromTwips(twips) : null;
        }
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "trHeight", value is null
                ? null
                : new XElement(Ns.W + "trHeight",
                    new XAttribute(Ns.W + "val", XmlUtil.Num(value.Value.Twips)),
                    // "atLeast" so content taller than the height still fits; "exact" clips it.
                    new XAttribute(Ns.W + "hRule", "atLeast")),
                [Ns.W + "cnfStyle", Ns.W + "divId", Ns.W + "gridBefore", Ns.W + "gridAfter",
                 Ns.W + "wBefore", Ns.W + "wAfter", Ns.W + "cantSplit", Ns.W + "trHeight",
                 Ns.W + "tblHeader"]);
            _document.Touch();
        }
    }

    /// <summary>Sets every cell's text from a sequence of values.</summary>
    public TableRow SetValues(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var cells = Cells;
        for (var i = 0; i < values.Length && i < cells.Count; i++)
        {
            cells[i].Text = values[i];
        }

        return this;
    }

    /// <summary>Shades every cell in the row.</summary>
    public TableRow SetShading(OfficeColor color)
    {
        foreach (var cell in Cells)
        {
            cell.Shading = color;
        }

        return this;
    }

    /// <summary>Removes the row.</summary>
    public void Remove()
    {
        Element.Remove();
        _document.Touch();
    }

    public override string ToString() => string.Join(" | ", Cells.Select(c => c.Text));
}

/// <summary>A table.</summary>
public sealed class Table : IReadOnlyList<TableRow>
{
    private readonly WordDocument _document;

    internal Table(WordDocument document, XElement element)
    {
        _document = document;
        Element = element;
    }

    private static readonly XName[] TablePropertyOrder =
    [
        Ns.W + "tblStyle", Ns.W + "tblpPr", Ns.W + "tblOverlap", Ns.W + "bidiVisual",
        Ns.W + "tblStyleRowBandSize", Ns.W + "tblStyleColBandSize", Ns.W + "tblW", Ns.W + "jc",
        Ns.W + "tblCellSpacing", Ns.W + "tblInd", Ns.W + "tblBorders", Ns.W + "shd",
        Ns.W + "tblLayout", Ns.W + "tblCellMar", Ns.W + "tblLook", Ns.W + "tblCaption",
        Ns.W + "tblDescription",
    ];

    /// <summary>The underlying <c>w:tbl</c> element.</summary>
    public XElement Element { get; }

    // CT_Tbl is a sequence too: tblPr, tblGrid, then the rows. See RowOrder above for why the
    // successors have to be listed rather than just the element being created.
    private static readonly XName[] TableOrder =
        [Ns.W + "tblPr", Ns.W + "tblGrid", Ns.W + "tr", Ns.W + "customXml", Ns.W + "sdt"];

    private XElement Properties => XmlUtil.GetOrCreate(Element, Ns.W + "tblPr", TableOrder);

    internal static Table Create(WordDocument document, int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        var element = new XElement(Ns.W + "tbl",
            new XElement(Ns.W + "tblPr",
                XmlUtil.ValElement(Ns.W + "tblStyle", "TableGrid"),
                new XElement(Ns.W + "tblW",
                    // type="auto" with w="0" is what makes a table fill the text column. A fixed
                    // dxa width does not adapt when the page margins change.
                    new XAttribute(Ns.W + "w", "0"),
                    new XAttribute(Ns.W + "type", "auto")),
                new XElement(Ns.W + "tblLook",
                    new XAttribute(Ns.W + "val", "04A0"),
                    new XAttribute(Ns.W + "firstRow", "1"),
                    new XAttribute(Ns.W + "lastRow", "0"),
                    new XAttribute(Ns.W + "firstColumn", "1"),
                    new XAttribute(Ns.W + "lastColumn", "0"),
                    new XAttribute(Ns.W + "noHBand", "0"),
                    new XAttribute(Ns.W + "noVBand", "1"))));

        // w:tblGrid declares the column structure and must have exactly as many w:gridCol entries
        // as the widest row has grid positions. Word repairs a mismatch by guessing, and the guess
        // is usually equal widths — which silently discards any column sizing.
        //
        // w:w is optional, but an omitted width says nothing at all: consumers fall back to their
        // own guess, and this library's own PDF exporter reads the grid to lay the columns out.
        // Equal shares of the section's content width are the same answer Word's autofit reaches
        // for a table of empty cells, so nothing is lost by stating it.
        var grid = new XElement(Ns.W + "tblGrid");
        var columnWidth = Math.Max(1, document.Section.ContentWidth.Twips / columns);

        for (var c = 0; c < columns; c++)
        {
            grid.Add(new XElement(Ns.W + "gridCol",
                new XAttribute(Ns.W + "w", XmlUtil.Num(columnWidth))));
        }

        element.Add(grid);

        for (var r = 0; r < rows; r++)
        {
            var row = new XElement(Ns.W + "tr");

            for (var c = 0; c < columns; c++)
            {
                row.Add(new XElement(Ns.W + "tc",
                    new XElement(Ns.W + "tcPr"),
                    new XElement(Ns.W + "p")));
            }

            element.Add(row);
        }

        document.Touch();
        return new Table(document, element);
    }

    /// <summary>The table's rows.</summary>
    public IReadOnlyList<TableRow> Rows =>
        [.. Element.Elements(Ns.W + "tr").Select(e => new TableRow(_document, e))];

    /// <inheritdoc />
    public int Count => Element.Elements(Ns.W + "tr").Count();

    /// <inheritdoc />
    public TableRow this[int index] => Rows[index];

    /// <summary>A cell by row and column.</summary>
    /// <summary>A cell by row and column.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is O(row).</b> LINQ to XML stores children as a linked list, so reaching row 900
    /// means walking past 899 others. Filling a whole table through this indexer is therefore
    /// quadratic in the row count — fine for the hundreds, slow in the thousands.
    /// </para>
    /// <para>
    /// To fill a large table, walk it once instead:
    /// </para>
    /// <code>
    /// foreach (var row in table.Rows)
    /// {
    ///     var cells = row.Cells;
    ///     for (var c = 0; c &lt; cells.Count; c++) cells[c].Text = values[c];
    /// }
    /// </code>
    /// <para>
    /// Measured on a 2000-row table: 236 ms through the indexer against 36 ms walking the rows.
    /// </para>
    /// </remarks>
    public TableCell this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(row);

            var element = Element.Elements(Ns.W + "tr").Skip(row).FirstOrDefault()
                ?? throw new ArgumentOutOfRangeException(nameof(row));

            return new TableRow(_document, element)[column];
        }
    }

    /// <summary>The number of grid columns.</summary>
    public int ColumnCount => Element.Element(Ns.W + "tblGrid")?.Elements(Ns.W + "gridCol").Count()
                              ?? Rows.FirstOrDefault()?.CellCount ?? 0;

    /// <summary>The table style id.</summary>
    public string? StyleId
    {
        get => Element.Element(Ns.W + "tblPr").Val(Ns.W + "tblStyle");
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "tblStyle", value is null
                ? null
                : XmlUtil.ValElement(Ns.W + "tblStyle", value), TablePropertyOrder);
            _document.Touch();
        }
    }

    /// <summary>Horizontal alignment of the whole table.</summary>
    public ParagraphAlignment? Alignment
    {
        get => Element.Element(Ns.W + "tblPr").Val(Ns.W + "jc") switch
        {
            "center" => ParagraphAlignment.Center,
            "right" or "end" => ParagraphAlignment.Right,
            "left" or "start" => ParagraphAlignment.Left,
            _ => null,
        };
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "jc", value is null
                ? null
                : XmlUtil.ValElement(Ns.W + "jc", value.Value switch
                {
                    ParagraphAlignment.Center => "center",
                    ParagraphAlignment.Right => "right",
                    _ => "left",
                }), TablePropertyOrder);
            _document.Touch();
        }
    }

    /// <summary>Appends a row with the same number of cells as the grid.</summary>
    public TableRow AddRow()
    {
        var columns = Math.Max(1, ColumnCount);
        var element = new XElement(Ns.W + "tr");

        for (var c = 0; c < columns; c++)
        {
            element.Add(new XElement(Ns.W + "tc",
                new XElement(Ns.W + "tcPr"),
                new XElement(Ns.W + "p")));
        }

        Element.Add(element);
        _document.Touch();
        return new TableRow(_document, element);
    }

    /// <summary>Appends a row and fills it.</summary>
    public TableRow AddRow(params string[] values) => AddRow().SetValues(values);

    /// <summary>Appends a column to every row and to the grid.</summary>
    public void AddColumn()
    {
        var grid = Element.Element(Ns.W + "tblGrid");

        // Match the existing columns rather than leaving the new one unstated, so a table stays
        // evenly gridded after a column is appended.
        var existing = grid?.Elements(Ns.W + "gridCol").LastOrDefault();
        var newColumn = new XElement(Ns.W + "gridCol");

        if (existing.Attr(Ns.W + "w") is { } width)
        {
            newColumn.SetAttributeValue(Ns.W + "w", width);
        }

        grid?.Add(newColumn);

        foreach (var row in Element.Elements(Ns.W + "tr"))
        {
            row.Add(new XElement(Ns.W + "tc",
                new XElement(Ns.W + "tcPr"),
                new XElement(Ns.W + "p")));
        }

        _document.Touch();
    }

    /// <summary>Sets the width of a grid column.</summary>
    public void SetColumnWidth(int columnIndex, Length width)
    {
        var grid = Element.Element(Ns.W + "tblGrid");
        var columns = grid?.Elements(Ns.W + "gridCol").ToList();

        if (columns is null || columnIndex < 0 || columnIndex >= columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        columns[columnIndex].SetAttributeValue(Ns.W + "w", XmlUtil.Num(width.Twips));

        // The grid is only a hint unless the layout is fixed: with the default autofit layout Word
        // recomputes every column from the content and the widths set here are ignored.
        XmlUtil.SetOrdered(Properties, Ns.W + "tblLayout",
            new XElement(Ns.W + "tblLayout", new XAttribute(Ns.W + "type", "fixed")),
            TablePropertyOrder);

        foreach (var row in Element.Elements(Ns.W + "tr"))
        {
            var cells = row.Elements(Ns.W + "tc").ToList();
            if (columnIndex < cells.Count)
            {
                new TableCell(_document, cells[columnIndex]).Width = width;
            }
        }

        _document.Touch();
    }

    /// <summary>Sets a border on every edge and between every cell.</summary>
    public Table SetBorders(BorderStyle style, OfficeColor? color = null, Length? width = null)
    {
        if (style == BorderStyle.None)
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "tblBorders", null, TablePropertyOrder);
            _document.Touch();
            return this;
        }

        var borders = new XElement(Ns.W + "tblBorders");
        foreach (var edge in (string[])["top", "left", "bottom", "right", "insideH", "insideV"])
        {
            borders.Add(ParagraphFormat.BorderElement(Ns.W + edge, style, color, width));
        }

        XmlUtil.SetOrdered(Properties, Ns.W + "tblBorders", borders, TablePropertyOrder);
        _document.Touch();
        return this;
    }

    /// <summary>
    /// Merges a rectangular block of cells.
    /// </summary>
    /// <remarks>
    /// Horizontal merging widens the first cell's <c>w:gridSpan</c> and removes the others;
    /// vertical merging keeps every cell and marks them with <c>w:vMerge</c>. Doing it the other
    /// way round in either direction produces a table Word silently repairs.
    /// </remarks>
    public void MergeCells(int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstRow);
        ArgumentOutOfRangeException.ThrowIfNegative(firstColumn);
        ArgumentOutOfRangeException.ThrowIfLessThan(lastRow, firstRow);
        ArgumentOutOfRangeException.ThrowIfLessThan(lastColumn, firstColumn);

        var rows = Element.Elements(Ns.W + "tr").ToList();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lastRow, rows.Count);

        var span = lastColumn - firstColumn + 1;

        for (var r = firstRow; r <= lastRow; r++)
        {
            var cells = rows[r].Elements(Ns.W + "tc").ToList();

            if (firstColumn >= cells.Count)
            {
                continue;
            }

            var first = new TableCell(_document, cells[firstColumn]);

            if (span > 1)
            {
                first.GridSpan = span;

                for (var c = Math.Min(lastColumn, cells.Count - 1); c > firstColumn; c--)
                {
                    cells[c].Remove();
                }
            }

            if (lastRow > firstRow)
            {
                first.SetVerticalMerge(isStart: r == firstRow);
            }
        }

        _document.Touch();
    }

    /// <summary>Fills the table from a rectangular sequence of values.</summary>
    public Table SetData(IEnumerable<IEnumerable<string>> data, bool firstRowIsHeader = false)
    {
        ArgumentNullException.ThrowIfNull(data);

        var rows = Rows;
        var r = 0;

        foreach (var record in data)
        {
            var row = r < rows.Count ? rows[r] : AddRow();
            var cells = row.Cells;
            var c = 0;

            foreach (var value in record)
            {
                if (c < cells.Count)
                {
                    cells[c].Text = value;
                }

                c++;
            }

            if (r == 0 && firstRowIsHeader)
            {
                row.IsHeader = true;

                foreach (var cell in cells)
                {
                    foreach (var paragraph in cell.Paragraphs)
                    {
                        foreach (var run in paragraph.Runs)
                        {
                            run.Format.Bold = true;
                        }
                    }
                }
            }

            r++;
        }

        return this;
    }

    /// <summary>Removes the table from the document.</summary>
    public void Remove()
    {
        Element.Remove();
        _document.Touch();
    }

    /// <summary>The table's text, rows on separate lines and cells tab-separated.</summary>
    public string ExtractText()
    {
        var builder = new StringBuilder();

        foreach (var row in Rows)
        {
            // '\n' rather than AppendLine, which emits Environment.NewLine and would make the
            // same table extract differently on Windows and on Linux.
            builder.Append(string.Join('\t', row.Cells.Select(c => c.Text.Replace('\n', ' '))))
                .Append('\n');
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public IEnumerator<TableRow> GetEnumerator() => Rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"Table {Count}x{ColumnCount}";
}
