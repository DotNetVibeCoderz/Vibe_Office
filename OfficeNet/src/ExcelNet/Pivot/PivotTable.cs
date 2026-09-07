// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace ExcelNet.Pivot;

/// <summary>How a pivot table summarises the values under it.</summary>
public enum PivotFunction
{
    /// <summary>Add them up. The default, and what a numeric field almost always wants.</summary>
    Sum,

    /// <summary>Count every value, including text.</summary>
    Count,

    /// <summary>Count only the numeric ones.</summary>
    CountNumbers,

    /// <summary>Arithmetic mean.</summary>
    Average,

    /// <summary>Largest value.</summary>
    Max,

    /// <summary>Smallest value.</summary>
    Min,

    /// <summary>All of them multiplied together.</summary>
    Product,

    /// <summary>Sample standard deviation.</summary>
    StdDev,

    /// <summary>Population standard deviation.</summary>
    StdDevP,

    /// <summary>Sample variance.</summary>
    Var,

    /// <summary>Population variance.</summary>
    VarP,
}

/// <summary>One summarised column in a pivot table.</summary>
/// <param name="Field">The source column's header.</param>
/// <param name="Function">How to summarise it.</param>
/// <param name="Caption">The label shown above it; the field name with the function otherwise.</param>
/// <param name="NumberFormat">An Excel number format for the results, for example <c>#,##0</c>.</param>
public sealed record PivotValue(
    string Field,
    PivotFunction Function = PivotFunction.Sum,
    string? Caption = null,
    string? NumberFormat = null);

/// <summary>What a pivot table should summarise, and how.</summary>
public sealed record PivotTableDefinition
{
    /// <summary>The sheet holding the source data.</summary>
    public required Worksheet Source { get; init; }

    /// <summary>
    /// The source range, header row included.
    /// </summary>
    /// <remarks>
    /// The first row must be the headers: every field is addressed by its header text, and a range
    /// that starts at the data gives fields named after the first record.
    /// </remarks>
    public required CellRangeReference SourceRange { get; init; }

    /// <summary>The top-left cell of the pivot table on its own sheet.</summary>
    public required CellReference Target { get; init; }

    /// <summary>Fields laid down the left, one group per distinct value.</summary>
    public IReadOnlyList<string> Rows { get; init; } = [];

    /// <summary>Fields laid across the top.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>The summarised columns.</summary>
    public IReadOnlyList<PivotValue> Values { get; init; } = [];

    /// <summary>The table's name; generated when omitted.</summary>
    public string? Name { get; init; }
}

/// <summary>
/// A pivot table on a worksheet.
/// </summary>
/// <remarks>
/// <para>
/// A pivot is four parts, not one:
/// </para>
/// <code>
/// workbook.xml   --pivotCacheDefinition--&gt;  pivotCacheDefinition1.xml
///                                                   |
///                                        pivotCacheRecords
///                                                   v
/// sheet2.xml     --pivotTable--&gt;  pivotTable1.xml   pivotCacheRecords1.xml
///                                        |
///                             pivotCacheDefinition
/// </code>
/// <para>
/// The cache holds a snapshot of the source data; the table holds only the layout. Both the
/// workbook and the pivot table must reference the same cache, by the same <c>cacheId</c>, or Excel
/// reports the file as damaged.
/// </para>
/// <para>
/// <b>The result grid is not written.</b> Excel computes the cells from the cache when it opens the
/// file, which is what <c>refreshOnLoad</c> asks it to do. That means Excel shows a complete pivot
/// table, and a non-Excel consumer — including this library's own PDF export — sees the area as
/// empty. Computing the grid here would mean reimplementing Excel's aggregation and subtotal
/// layout, and any disagreement would show as a table that changes the moment someone opens it.
/// </para>
/// </remarks>
public sealed class PivotTable
{
    private PivotTable(Worksheet sheet, OpcPart part, OpcPart cacheDefinition, OpcPart cacheRecords,
        string name, CellReference location, IReadOnlyList<string> fields)
    {
        Sheet = sheet;
        Part = part;
        CacheDefinitionPart = cacheDefinition;
        CacheRecordsPart = cacheRecords;
        Name = name;
        Location = location;
        Fields = fields;
    }

    /// <summary>The sheet the table sits on.</summary>
    public Worksheet Sheet { get; }

    /// <summary>The pivot table definition part.</summary>
    public OpcPart Part { get; }

    /// <summary>The cache definition: the field list and their distinct values.</summary>
    public OpcPart CacheDefinitionPart { get; }

    /// <summary>The cache records: the snapshot of the source rows.</summary>
    public OpcPart CacheRecordsPart { get; }

    /// <summary>The table's name, as Excel shows it in the field list.</summary>
    public string Name { get; }

    /// <summary>The top-left cell of the table.</summary>
    public CellReference Location { get; }

    /// <summary>The source column headers, in source order.</summary>
    public IReadOnlyList<string> Fields { get; }

    public override string ToString() =>
        $"PivotTable \"{Name}\" on {Sheet.Name} at {Location.A1}";

    // ---- Construction --------------------------------------------------------------------------

    internal static PivotTable Create(Worksheet target, PivotTableDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var workbook = target.Workbook;
        var package = workbook.Package;
        var cache = SourceCache.Read(definition.Source, definition.SourceRange);

        Validate(definition, cache);

        // A cache id is workbook-wide and ties the workbook's <pivotCaches> entry to the table.
        var cacheId = workbook.NextPivotCacheId();
        var name = definition.Name ?? $"PivotTable{cacheId}";

        var recordsPart = package.AddXmlPart(
            package.NextPartName("/xl/pivotCache/pivotCacheRecords{0}.xml"),
            ContentTypes.ExcelPivotCacheRecords,
            cache.BuildRecords());

        var definitionPart = package.AddXmlPart(
            package.NextPartName("/xl/pivotCache/pivotCacheDefinition{0}.xml"),
            ContentTypes.ExcelPivotCacheDefinition,
            new XDocument());

        var recordsRelationship =
            definitionPart.AddRelationship(recordsPart, RelationshipTypes.PivotCacheRecords);

        definitionPart.Xml = cache.BuildDefinition(
            definition.Source.Name, definition.SourceRange, recordsRelationship.Id);

        var tablePart = package.AddXmlPart(
            package.NextPartName("/xl/pivotTables/pivotTable{0}.xml"),
            ContentTypes.ExcelPivotTable,
            BuildTable(definition, cache, name, cacheId));

        tablePart.AddRelationship(definitionPart, RelationshipTypes.PivotCacheDefinition);
        target.Part.AddRelationship(tablePart, RelationshipTypes.PivotTable);

        workbook.RegisterPivotCache(cacheId, definitionPart);
        package.MarkDirty();

        return new PivotTable(target, tablePart, definitionPart, recordsPart,
            name, definition.Target, cache.Headers);
    }

    private static void Validate(PivotTableDefinition definition, SourceCache cache)
    {
        if (definition.Values.Count == 0)
        {
            throw new OfficeNetException(
                "A pivot table needs at least one value field; without one it has nothing to " +
                "summarise and Excel reports the part as damaged.");
        }

        foreach (var field in definition.Rows
                     .Concat(definition.Columns)
                     .Concat(definition.Values.Select(v => v.Field)))
        {
            if (!cache.Headers.Contains(field, StringComparer.Ordinal))
            {
                throw new OfficeNetException(
                    $"'{field}' is not a column in the source range. Available: " +
                    string.Join(", ", cache.Headers));
            }
        }
    }

    /// <summary>Builds <c>pivotTableDefinition</c>: the layout, not the data.</summary>
    private static XDocument BuildTable(
        PivotTableDefinition definition, SourceCache cache, string name, int cacheId)
    {
        var rows = definition.Rows;
        var columns = definition.Columns;

        // The location must cover the whole table, headers and all, or Excel refuses to place it.
        // Sizing it from the cache's distinct values is what makes the reserved area right before
        // Excel has computed anything.
        var height = 2 + Math.Max(1, cache.DistinctCount(rows)) + 1;
        var width = Math.Max(1, cache.DistinctCount(columns) * Math.Max(1, definition.Values.Count))
                    + rows.Count + 1;

        var end = new CellReference(
            definition.Target.Row + height,
            definition.Target.Column + width);

        var root = new XElement(Ns.S + "pivotTableDefinition",
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute("xmlns", Ns.S.NamespaceName),
            new XAttribute("name", name),
            new XAttribute("cacheId", cacheId),
            new XAttribute("applyNumberFormats", "0"),
            new XAttribute("applyBorderFormats", "0"),
            new XAttribute("applyFontFormats", "0"),
            new XAttribute("applyPatternFormats", "0"),
            new XAttribute("applyAlignmentFormats", "0"),
            new XAttribute("applyWidthHeightFormats", "1"),
            new XAttribute("dataCaption", "Values"),
            new XAttribute("updatedVersion", "8"),
            new XAttribute("minRefreshableVersion", "3"),
            new XAttribute("createdVersion", "8"),
            new XAttribute("indent", "0"),
            new XAttribute("outline", "1"),
            new XAttribute("outlineData", "1"),
            new XAttribute("multipleFieldFilters", "0"),

            // The grid is not written; this is what makes Excel compute it on open.
            new XAttribute("cacheOnLoad", "1"),

            new XElement(Ns.S + "location",
                new XAttribute("ref", new CellRangeReference(definition.Target, end).A1),
                new XAttribute("firstHeaderRow", "1"),
                new XAttribute("firstDataRow", columns.Count > 0 ? "2" : "1"),
                new XAttribute("firstDataCol", rows.Count.ToString(CultureInfo.InvariantCulture))));

        // Every cache field needs a pivotField, in cache order, whether it is used or not. A
        // missing one shifts every index after it and Excel repairs the file.
        var fields = new XElement(Ns.S + "pivotFields",
            new XAttribute("count", cache.Headers.Count));

        foreach (var header in cache.Headers)
        {
            fields.Add(BuildField(header, cache, rows, columns, definition.Values));
        }

        root.Add(fields);

        if (rows.Count > 0)
        {
            root.Add(Axis("rowFields", "rowItems", rows, cache));
        }

        if (columns.Count > 0)
        {
            root.Add(Axis("colFields", "colItems", columns, cache));
        }

        root.Add(BuildDataFields(definition.Values, cache));

        root.Add(new XElement(Ns.S + "pivotTableStyleInfo",
            new XAttribute("name", "PivotStyleLight16"),
            new XAttribute("showRowHeaders", "1"),
            new XAttribute("showColHeaders", "1"),
            new XAttribute("showRowStripes", "0"),
            new XAttribute("showColStripes", "0"),
            new XAttribute("showLastColumn", "1")));

        return XmlUtil.NewDocument(root);
    }

    private static XElement BuildField(string header, SourceCache cache,
        IReadOnlyList<string> rows, IReadOnlyList<string> columns, IReadOnlyList<PivotValue> values)
    {
        var field = new XElement(Ns.S + "pivotField", new XAttribute("showAll", "0"));

        if (rows.Contains(header, StringComparer.Ordinal))
        {
            field.SetAttributeValue("axis", "axisRow");
        }
        else if (columns.Contains(header, StringComparer.Ordinal))
        {
            field.SetAttributeValue("axis", "axisCol");
        }
        else if (values.Any(v => v.Field == header))
        {
            field.SetAttributeValue("dataField", "1");
            return field;
        }
        else
        {
            return field;
        }

        // An axis field lists its items by cache index, then a "default" item that stands for the
        // subtotal row. Omitting the default gives a table with no totals and a repair prompt.
        var shared = cache.SharedItems(header);

        var items = new XElement(Ns.S + "items",
            new XAttribute("count", shared.Count + 1));

        for (var i = 0; i < shared.Count; i++)
        {
            items.Add(new XElement(Ns.S + "item",
                new XAttribute("x", i.ToString(CultureInfo.InvariantCulture))));
        }

        items.Add(new XElement(Ns.S + "item", new XAttribute("t", "default")));
        field.Add(items);

        return field;
    }

    /// <summary>
    /// Builds an axis: the field list and its item list, which are siblings rather than nested.
    /// </summary>
    /// <remarks>
    /// One placeholder item is written rather than the full cross-product of values. Excel replaces
    /// the item list wholesale when it refreshes on load, and enumerating every combination here
    /// would be computing the result grid by another name.
    /// </remarks>
    private static IEnumerable<XElement> Axis(string fieldsName, string itemsName,
        IReadOnlyList<string> fields, SourceCache cache)
    {
        var element = new XElement(Ns.S + fieldsName,
            new XAttribute("count", fields.Count));

        foreach (var field in fields)
        {
            element.Add(new XElement(Ns.S + "field",
                new XAttribute("x", cache.IndexOf(field).ToString(CultureInfo.InvariantCulture))));
        }

        yield return element;

        yield return new XElement(Ns.S + itemsName,
            new XAttribute("count", "1"),
            new XElement(Ns.S + "i", new XElement(Ns.S + "x")));
    }

    private static XElement BuildDataFields(IReadOnlyList<PivotValue> values, SourceCache cache)
    {
        var element = new XElement(Ns.S + "dataFields",
            new XAttribute("count", values.Count));

        foreach (var value in values)
        {
            var field = new XElement(Ns.S + "dataField",
                new XAttribute("name", value.Caption ?? $"{Label(value.Function)} of {value.Field}"),
                new XAttribute("fld", cache.IndexOf(value.Field).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("baseField", "0"),
                new XAttribute("baseItem", "0"));

            // Sum is the schema default and is omitted; anything else must be named.
            if (value.Function != PivotFunction.Sum)
            {
                field.SetAttributeValue("subtotal", Attribute(value.Function));
            }

            if (value.NumberFormat is { Length: > 0 })
            {
                field.SetAttributeValue("numFmtId", "0");
            }

            element.Add(field);
        }

        return element;
    }

    private static string Attribute(PivotFunction function) => function switch
    {
        PivotFunction.Count => "count",
        PivotFunction.CountNumbers => "countNums",
        PivotFunction.Average => "average",
        PivotFunction.Max => "max",
        PivotFunction.Min => "min",
        PivotFunction.Product => "product",
        PivotFunction.StdDev => "stdDev",
        PivotFunction.StdDevP => "stdDevp",
        PivotFunction.Var => "var",
        PivotFunction.VarP => "varp",
        _ => "sum",
    };

    private static string Label(PivotFunction function) => function switch
    {
        PivotFunction.Count => "Count",
        PivotFunction.CountNumbers => "Count",
        PivotFunction.Average => "Average",
        PivotFunction.Max => "Max",
        PivotFunction.Min => "Min",
        PivotFunction.Product => "Product",
        PivotFunction.StdDev => "StdDev",
        PivotFunction.StdDevP => "StdDevp",
        PivotFunction.Var => "Var",
        PivotFunction.VarP => "Varp",
        _ => "Sum",
    };
}
