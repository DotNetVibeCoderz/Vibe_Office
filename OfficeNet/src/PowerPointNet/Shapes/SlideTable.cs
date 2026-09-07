// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace PowerPointNet.Shapes;

/// <summary>A cell of a slide table.</summary>
public sealed class SlideTableCell
{
    private readonly Presentation _presentation;

    internal SlideTableCell(Presentation presentation, XElement element)
    {
        _presentation = presentation;
        Element = element;
    }

    /// <summary>The underlying <c>a:tc</c> element.</summary>
    public XElement Element { get; }

    /// <summary>The cell's text.</summary>
    public TextFrame TextFrame =>
        new(_presentation, Element.Element(Ns.A + "txBody")
            ?? throw new OfficeNetException("The table cell has no text body."));

    /// <summary>The cell's text as a string.</summary>
    public string Text
    {
        get => TextFrame.Text;
        set => TextFrame.Text = value;
    }

    private XElement Properties
    {
        get
        {
            var existing = Element.Element(Ns.A + "tcPr");

            if (existing is not null)
            {
                return existing;
            }

            // a:tcPr follows a:txBody, unlike WordprocessingML where properties come first.
            var created = new XElement(Ns.A + "tcPr");
            Element.Add(created);
            _presentation.Touch();
            return created;
        }
    }

    /// <summary>The cell's fill colour.</summary>
    public OfficeColor? FillColor
    {
        get
        {
            var raw = Element.Element(Ns.A + "tcPr")?.Element(Ns.A + "solidFill")
                ?.Element(Ns.A + "srgbClr")?.Attr("val");
            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set
        {
            var properties = Properties;
            properties.Elements(Ns.A + "solidFill").Remove();
            properties.Elements(Ns.A + "noFill").Remove();

            properties.Add(value is null
                ? new XElement(Ns.A + "noFill")
                : new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "srgbClr", new XAttribute("val", value.Value.ToHex()))));

            _presentation.Touch();
        }
    }

    /// <summary>How the content sits vertically.</summary>
    public TextAnchor Anchor
    {
        get => Element.Element(Ns.A + "tcPr")?.Attr("anchor") switch
        {
            "ctr" => TextAnchor.Middle,
            "b" => TextAnchor.Bottom,
            _ => TextAnchor.Top,
        };
        set
        {
            Properties.SetAttributeValue("anchor", value switch
            {
                TextAnchor.Middle => "ctr",
                TextAnchor.Bottom => "b",
                _ => "t",
            });
            _presentation.Touch();
        }
    }

    /// <summary>How many columns this cell spans.</summary>
    public int GridSpan
    {
        get => Math.Max(1, Element.IntAttr("gridSpan", 1));
        set
        {
            Element.SetAttributeValue("gridSpan", value <= 1 ? null : value);
            _presentation.Touch();
        }
    }

    public override string ToString() => Text;
}

/// <summary>A row of a slide table.</summary>
public sealed class SlideTableRow
{
    private readonly Presentation _presentation;

    internal SlideTableRow(Presentation presentation, XElement element)
    {
        _presentation = presentation;
        Element = element;
    }

    /// <summary>The underlying <c>a:tr</c> element.</summary>
    public XElement Element { get; }

    /// <summary>The row's cells.</summary>
    public IReadOnlyList<SlideTableCell> Cells =>
        [.. Element.Elements(Ns.A + "tc").Select(e => new SlideTableCell(_presentation, e))];

    /// <summary>A cell by index.</summary>
    public SlideTableCell this[int index] => Cells[index];

    /// <summary>The row's height.</summary>
    public Length Height
    {
        get => Length.FromEmu(Element.LongAttr("h"));
        set
        {
            Element.SetAttributeValue("h", value.Emu);
            _presentation.Touch();
        }
    }

    /// <summary>Sets every cell's text.</summary>
    public SlideTableRow SetValues(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var cells = Cells;

        for (var i = 0; i < values.Length && i < cells.Count; i++)
        {
            cells[i].Text = values[i];
        }

        return this;
    }

    /// <summary>Fills every cell with one colour.</summary>
    public SlideTableRow SetFill(OfficeColor color)
    {
        foreach (var cell in Cells)
        {
            cell.FillColor = color;
        }

        return this;
    }

    public override string ToString() => string.Join(" | ", Cells.Select(c => c.Text));
}

/// <summary>
/// A table on a slide.
/// </summary>
/// <remarks>
/// A PresentationML table is not a shape — it is a <c>p:graphicFrame</c> wrapping a DrawingML
/// <c>a:tbl</c>. The frame carries the position and size; the table inside carries a grid whose
/// column widths must sum to the frame's width, or PowerPoint rescales the columns on open and the
/// layout the caller specified is lost.
/// </remarks>
public sealed class SlideTable : Shape, IReadOnlyList<SlideTableRow>
{
    private const string TableGraphicUri =
        "http://schemas.openxmlformats.org/drawingml/2006/table";

    internal SlideTable(Presentation presentation, XElement element) : base(presentation, element)
    {
    }

    private XElement TableElement =>
        Element.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData")?.Element(Ns.A + "tbl")
        ?? throw new OfficeNetException("The graphic frame does not contain a table.");

    internal static SlideTable Create(Presentation presentation, Slide slide, XElement shapeTree,
        uint id, int rows, int columns, Length left, Length top, Length width, Length height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        var grid = new XElement(Ns.A + "tblGrid");
        var columnWidth = Length.FromEmu(width.Emu / columns);

        for (var c = 0; c < columns; c++)
        {
            // The last column absorbs the rounding remainder so the widths sum exactly.
            var w = c == columns - 1
                ? Length.FromEmu(width.Emu - columnWidth.Emu * (columns - 1))
                : columnWidth;

            grid.Add(new XElement(Ns.A + "gridCol", new XAttribute("w", w.Emu)));
        }

        var table = new XElement(Ns.A + "tbl",
            new XElement(Ns.A + "tblPr",
                new XAttribute("firstRow", "1"),
                new XAttribute("bandRow", "1"),
                // The table style is referenced by GUID from tableStyles.xml. This is the built-in
                // "Medium Style 2 - Accent 1" identifier Office ships with.
                new XElement(Ns.A + "tableStyleId", "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}")),
            grid);

        var rowHeight = Length.FromEmu(height.Emu / rows);

        for (var r = 0; r < rows; r++)
        {
            var row = new XElement(Ns.A + "tr", new XAttribute("h", rowHeight.Emu));

            for (var c = 0; c < columns; c++)
            {
                row.Add(new XElement(Ns.A + "tc",
                    new XElement(Ns.A + "txBody",
                        new XElement(Ns.A + "bodyPr"),
                        new XElement(Ns.A + "lstStyle"),
                        new XElement(Ns.A + "p")),
                    new XElement(Ns.A + "tcPr")));
            }

            table.Add(row);
        }

        var frame = new XElement(Ns.P + "graphicFrame",
            new XElement(Ns.P + "nvGraphicFramePr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", $"Table {id}")),
                new XElement(Ns.P + "cNvGraphicFramePr",
                    new XElement(Ns.A + "graphicFrameLocks", new XAttribute("noGrp", "1"))),
                new XElement(Ns.P + "nvPr")),
            // A graphic frame's transform is p:xfrm, not the a:xfrm a shape uses.
            new XElement(Ns.P + "xfrm",
                new XElement(Ns.A + "off",
                    new XAttribute("x", left.Emu), new XAttribute("y", top.Emu)),
                new XElement(Ns.A + "ext",
                    new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu))),
            new XElement(Ns.A + "graphic",
                new XElement(Ns.A + "graphicData",
                    new XAttribute("uri", TableGraphicUri),
                    table)));

        shapeTree.Add(frame);
        presentation.Touch();
        return new SlideTable(presentation, frame);
    }

    /// <summary>The table's rows.</summary>
    public IReadOnlyList<SlideTableRow> Rows =>
        [.. TableElement.Elements(Ns.A + "tr").Select(e => new SlideTableRow(Presentation, e))];

    /// <inheritdoc />
    public int Count => TableElement.Elements(Ns.A + "tr").Count();

    /// <inheritdoc />
    public SlideTableRow this[int index] => Rows[index];

    /// <summary>A cell by row and column.</summary>
    public SlideTableCell this[int row, int column] => Rows[row].Cells[column];

    /// <summary>The number of columns.</summary>
    public int ColumnCount => TableElement.Element(Ns.A + "tblGrid")?.Elements(Ns.A + "gridCol").Count() ?? 0;

    /// <summary>Whether the first row is styled as a header.</summary>
    public bool HasHeaderRow
    {
        get => TableElement.Element(Ns.A + "tblPr")?.Attr("firstRow") is "1";
        set
        {
            TableElement.Element(Ns.A + "tblPr")?.SetAttributeValue("firstRow", value ? "1" : "0");
            Presentation.Touch();
        }
    }

    /// <summary>Whether alternate rows are banded.</summary>
    public bool HasBandedRows
    {
        get => TableElement.Element(Ns.A + "tblPr")?.Attr("bandRow") is "1";
        set
        {
            TableElement.Element(Ns.A + "tblPr")?.SetAttributeValue("bandRow", value ? "1" : "0");
            Presentation.Touch();
        }
    }

    /// <summary>Sets a column's width, keeping the frame's total width unchanged.</summary>
    public SlideTable SetColumnWidth(int columnIndex, Length width)
    {
        var grid = TableElement.Element(Ns.A + "tblGrid");
        var columns = grid?.Elements(Ns.A + "gridCol").ToList();

        if (columns is null || columnIndex < 0 || columnIndex >= columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        var oldWidth = Length.FromEmu(columns[columnIndex].LongAttr("w"));
        columns[columnIndex].SetAttributeValue("w", width.Emu);

        // The difference is spread across the remaining columns so the grid still sums to the
        // frame width; otherwise PowerPoint silently rescales everything.
        var delta = oldWidth.Emu - width.Emu;
        var others = columns.Count - 1;

        if (others > 0 && delta != 0)
        {
            var share = delta / others;

            for (var i = 0; i < columns.Count; i++)
            {
                if (i == columnIndex)
                {
                    continue;
                }

                var current = columns[i].LongAttr("w");
                columns[i].SetAttributeValue("w", Math.Max(1, current + share));
            }
        }

        Presentation.Touch();
        return this;
    }

    /// <summary>Fills the table from a rectangular sequence.</summary>
    public SlideTable SetData(IEnumerable<IEnumerable<string>> data, bool boldFirstRow = true)
    {
        ArgumentNullException.ThrowIfNull(data);

        var rows = Rows;
        var r = 0;

        foreach (var record in data)
        {
            if (r >= rows.Count)
            {
                break;
            }

            var cells = rows[r].Cells;
            var c = 0;

            foreach (var value in record)
            {
                if (c < cells.Count)
                {
                    cells[c].Text = value;

                    if (r == 0 && boldFirstRow)
                    {
                        foreach (var paragraph in cells[c].TextFrame.Paragraphs)
                        {
                            foreach (var run in paragraph.Runs)
                            {
                                run.Bold = true;
                            }
                        }
                    }
                }

                c++;
            }

            r++;
        }

        return this;
    }

    /// <inheritdoc />
    public IEnumerator<SlideTableRow> GetEnumerator() => Rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"SlideTable {Count}x{ColumnCount}";
}
