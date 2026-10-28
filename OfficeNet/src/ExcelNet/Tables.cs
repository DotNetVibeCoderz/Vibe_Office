// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace ExcelNet;

/// <summary>
/// An Excel table (a ListObject): a named range with a header row, banded styling and its own
/// filter, which formulas can address by column name.
/// </summary>
/// <remarks>
/// <para>
/// A table lives in a part of its own (<c>xl/tables/tableN.xml</c>), and the sheet names it in
/// <c>tableParts</c>. Rewriting the sheet without that element — which this library did until
/// tables were modelled — leaves the part orphaned, and Excel drops the table.
/// </para>
/// <para>
/// Excel is strict about three things, and breaking any of them makes it "repair" the file by
/// deleting the table: <b>each column's name must be exactly the text of its header cell</b>, the
/// names must be unique within the table, and the table's <c>id</c> and name must be unique in the
/// workbook. Header names are therefore synchronised from the cells every time the workbook is
/// saved, so editing a header after creating the table cannot break it.
/// </para>
/// </remarks>
public sealed class ExcelTable
{
    private static readonly XNamespace S = Ns.S;

    internal ExcelTable(Worksheet sheet, OpcPart part)
    {
        Sheet = sheet;
        Part = part;
    }

    /// <summary>The sheet the table is on.</summary>
    public Worksheet Sheet { get; }

    /// <summary>The <c>xl/tables/tableN.xml</c> part.</summary>
    public OpcPart Part { get; }

    private XElement Root => Part.Xml.Root ?? throw new OfficeNetException($"{Part.Name} is empty.");

    /// <summary>The workbook-unique table id.</summary>
    public int Id => Root.IntAttr("id");

    /// <summary>The name formulas use: <c>SUM(Penjualan[Nilai])</c>.</summary>
    public string Name => Root.Attr("displayName") ?? Root.Attr("name") ?? string.Empty;

    /// <summary>The whole table, header row included.</summary>
    public CellRangeReference Range => CellRangeReference.Parse(Root.Attr("ref") ?? "A1");

    /// <summary>The rows below the header: what <c>Name[Column]</c> refers to.</summary>
    public CellRangeReference DataRange =>
        new(Range.Start.Row + 1, Range.Start.Column, Range.End.Row, Range.End.Column);

    /// <summary>The column names, in order.</summary>
    public IReadOnlyList<string> Columns =>
        [.. Root.Element(S + "tableColumns")?.Elements(S + "tableColumn").Select(c => c.Attr("name") ?? "") ?? []];

    /// <summary>The table style, e.g. <c>TableStyleMedium2</c>.</summary>
    public string? Style => Root.Element(S + "tableStyleInfo")?.Attr("name");

    /// <summary>The cells of one column's data, by column name (case-insensitive).</summary>
    /// <exception cref="KeyNotFoundException">The table has no such column.</exception>
    public CellRangeReference ColumnRange(string column)
    {
        var index = Columns.ToList().FindIndex(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            throw new KeyNotFoundException($"Table {Name} has no column '{column}'.");
        }

        var data = DataRange;
        return new CellRangeReference(data.Start.Row, data.Start.Column + index, data.End.Row, data.Start.Column + index);
    }

    public override string ToString() => $"Table {Name} on {Sheet.Name} at {Range.A1}";

    // ---- Creating ------------------------------------------------------------------------------

    internal static ExcelTable Create(Worksheet sheet, CellRangeReference range, string? name, string style)
    {
        var workbook = sheet.Workbook;

        if (range.RowCount < 2)
        {
            throw new ArgumentException("A table needs a header row and at least one data row.", nameof(range));
        }

        name ??= UniqueName(workbook);
        DefinedNameCollection.Validate(name);

        if (AllTables(workbook).Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ||
            workbook.Names.Any(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new OfficeNetException($"'{name}' is already used by a table or a defined name in this workbook.");
        }

        foreach (var other in sheet.Tables)
        {
            if (Overlaps(other.Range, range))
            {
                throw new OfficeNetException($"{range.A1} overlaps table {other.Name} at {other.Range.A1}.");
            }
        }

        if (sheet.AutoFilter is { } filter && Overlaps(filter, range))
        {
            // A filter inside the table — the one WriteHeader puts on a header row — is the table's
            // own filter now. One that reaches outside would leave two filters fighting over cells.
            if (Contains(range, filter))
            {
                sheet.AutoFilter = null;
            }
            else
            {
                throw new OfficeNetException(
                    $"{range.A1} overlaps the sheet's AutoFilter at {filter.A1}, which reaches outside it; " +
                    "a table brings its own filter. Clear Worksheet.AutoFilter first.");
            }
        }

        if (sheet.MergedRanges.Any(m => Overlaps(m, range)))
        {
            throw new OfficeNetException($"{range.A1} contains merged cells, which a table cannot hold.");
        }

        // Header cells must hold text, unique within the table; Excel enforces both.
        SynchroniseHeaders(sheet, range, writeCells: true);

        var id = AllTables(workbook).Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;
        var columns = HeaderNames(sheet, range);

        var root = new XElement(S + "table",
            new XAttribute("xmlns", S.NamespaceName),
            new XAttribute("id", id),
            new XAttribute("name", name),
            new XAttribute("displayName", name),
            new XAttribute("ref", range.A1),
            new XAttribute("totalsRowShown", "0"),
            new XElement(S + "autoFilter", new XAttribute("ref", range.A1)),
            new XElement(S + "tableColumns",
                new XAttribute("count", columns.Count),
                columns.Select((c, i) => new XElement(S + "tableColumn",
                    new XAttribute("id", i + 1),
                    new XAttribute("name", c)))),
            new XElement(S + "tableStyleInfo",
                new XAttribute("name", style),
                new XAttribute("showFirstColumn", "0"),
                new XAttribute("showLastColumn", "0"),
                new XAttribute("showRowStripes", "1"),
                new XAttribute("showColumnStripes", "0")));

        var package = workbook.Package;
        var part = package.AddXmlPart(package.NextPartName("/xl/tables/table{0}.xml"), ContentTypes.ExcelTable,
            XmlUtil.NewDocument(root));
        sheet.Part.AddRelationship(part, RelationshipTypes.Table);
        package.MarkDirty();

        return new ExcelTable(sheet, part);
    }

    /// <summary>Writes the header cells' current text into the table's column names.</summary>
    internal void SynchroniseColumns()
    {
        SynchroniseHeaders(Sheet, Range, writeCells: true);
        var names = HeaderNames(Sheet, Range);
        var columns = Root.Element(S + "tableColumns")?.Elements(S + "tableColumn").ToList() ?? [];

        for (var i = 0; i < columns.Count && i < names.Count; i++)
        {
            columns[i].SetAttributeValue("name", names[i]);
        }
    }

    /// <summary>
    /// Makes every header cell non-empty text, unique within the table: an empty header becomes
    /// <c>Column3</c>, a number becomes its text, and a repeat gains a suffix (<c>Nilai2</c>).
    /// </summary>
    private static void SynchroniseHeaders(Worksheet sheet, CellRangeReference range, bool writeCells)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var column = range.Start.Column; column <= range.End.Column; column++)
        {
            var cell = sheet[range.Start.Row, column];
            var text = cell.Value.IsEmpty ? $"Column{column - range.Start.Column + 1}" : cell.Value.AsText();
            var unique = text;

            for (var n = 2; !seen.Add(unique); n++)
            {
                unique = text + n.ToString(CultureInfo.InvariantCulture);
            }

            if (writeCells && (cell.Value.ValueType != CellValueType.Text || cell.Text != unique))
            {
                cell.Set(unique);
            }
        }
    }

    private static List<string> HeaderNames(Worksheet sheet, CellRangeReference range) =>
        [.. Enumerable.Range(range.Start.Column, range.ColumnCount).Select(c => sheet[range.Start.Row, c].Text)];

    private static string UniqueName(Workbook workbook)
    {
        var taken = AllTables(workbook).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; ; i++)
        {
            var candidate = $"Table{i}";

            if (!taken.Contains(candidate) && workbook.Names.Find(candidate) is null)
            {
                return candidate;
            }
        }
    }

    internal static IEnumerable<ExcelTable> AllTables(Workbook workbook) => workbook.SelectMany(s => s.Tables);

    private static bool Contains(CellRangeReference outer, CellRangeReference inner) =>
        inner.Start.Row >= outer.Start.Row && inner.End.Row <= outer.End.Row &&
        inner.Start.Column >= outer.Start.Column && inner.End.Column <= outer.End.Column;

    private static bool Overlaps(CellRangeReference a, CellRangeReference b) =>
        a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row &&
        a.Start.Column <= b.End.Column && a.End.Column >= b.Start.Column;
}
