// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Xml;

namespace ExcelNet.Pivot;

/// <summary>
/// A snapshot of the source range: the field names, their distinct values, and the rows.
/// </summary>
/// <remarks>
/// <para>
/// A pivot table does not read the worksheet. It reads a <em>cache</em> written beside it, which is
/// why changing the source data does nothing until someone refreshes. Building that cache is most
/// of the work in creating a pivot table, and getting it wrong is how a workbook ends up asking to
/// be repaired.
/// </para>
/// <para>
/// The rule that matters: every value a record refers to by index must exist in that field's
/// <c>sharedItems</c>, and the indices must match position exactly. A field whose shared items and
/// records disagree produces a pivot that shows the wrong labels against the right numbers.
/// </para>
/// </remarks>
internal sealed class SourceCache
{
    private readonly List<string[]> _rows;
    private readonly List<Field> _fields;

    private SourceCache(IReadOnlyList<string> headers, List<string[]> rows, List<Field> fields)
    {
        Headers = headers;
        _rows = rows;
        _fields = fields;
    }

    public IReadOnlyList<string> Headers { get; }

    public int IndexOf(string header) =>
        Headers.ToList().FindIndex(h => string.Equals(h, header, StringComparison.Ordinal));

    public IReadOnlyList<string> SharedItems(string header) => _fields[IndexOf(header)].Items;

    /// <summary>The number of cells an axis needs: the product of its fields' distinct values.</summary>
    public int DistinctCount(IReadOnlyList<string> fields)
    {
        var total = 1;

        foreach (var field in fields)
        {
            total *= Math.Max(1, SharedItems(field).Count);
        }

        return fields.Count == 0 ? 0 : total;
    }

    // ---- Reading -------------------------------------------------------------------------------

    public static SourceCache Read(Worksheet sheet, CellRangeReference range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var headers = new List<string>();

        for (var column = range.Start.Column; column <= range.End.Column; column++)
        {
            var text = sheet[range.Start.Row, column].Text;

            headers.Add(text.Length > 0
                ? text
                // A blank header would make the field unaddressable, so it gets a positional name
                // rather than an empty one.
                : $"Column{column - range.Start.Column + 1}");
        }

        if (headers.Count != headers.Distinct(StringComparer.Ordinal).Count())
        {
            throw new OfficeNetException(
                "The source range has duplicate column headers. A pivot field is addressed by its " +
                "header, so they have to be unique: " + string.Join(", ", headers));
        }

        var rows = new List<string[]>();

        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            var values = new string[headers.Count];
            var empty = true;

            for (var i = 0; i < headers.Count; i++)
            {
                values[i] = sheet[row, range.Start.Column + i].Text;
                empty &= values[i].Length == 0;
            }

            // A wholly blank row is a gap in the data, not a record. Keeping it would add a blank
            // item to every axis field.
            if (!empty)
            {
                rows.Add(values);
            }
        }

        var fields = headers
            .Select((header, index) => Field.From(header, rows.Select(r => r[index])))
            .ToList();

        return new SourceCache(headers, rows, fields);
    }

    // ---- Writing -------------------------------------------------------------------------------

    /// <summary>Builds <c>pivotCacheDefinition</c>: the field list and their distinct values.</summary>
    public XDocument BuildDefinition(string sheetName, CellRangeReference range, string recordsRelationshipId)
    {
        var root = new XElement(Ns.S + "pivotCacheDefinition",
            new XAttribute("xmlns", Ns.S.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute(Ns.R + "id", recordsRelationshipId),

            // recordCount must match the records part exactly; Excel checks it.
            new XAttribute("recordCount", _rows.Count),

            // Excel rebuilds the grid on open. Without this the table shows whatever cells happen
            // to be in the sheet, which for a generated file is nothing.
            new XAttribute("refreshOnLoad", "1"),
            new XAttribute("createdVersion", "8"),
            new XAttribute("refreshedVersion", "8"),
            new XAttribute("minRefreshableVersion", "3"),

            new XElement(Ns.S + "cacheSource",
                new XAttribute("type", "worksheet"),
                new XElement(Ns.S + "worksheetSource",
                    new XAttribute("ref", range.A1),
                    new XAttribute("sheet", sheetName))));

        var fields = new XElement(Ns.S + "cacheFields",
            new XAttribute("count", _fields.Count));

        foreach (var field in _fields)
        {
            fields.Add(field.ToCacheField());
        }

        root.Add(fields);
        return XmlUtil.NewDocument(root);
    }

    /// <summary>Builds <c>pivotCacheRecords</c>: one entry per source row.</summary>
    public XDocument BuildRecords()
    {
        var root = new XElement(Ns.S + "pivotCacheRecords",
            new XAttribute("xmlns", Ns.S.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute("count", _rows.Count));

        foreach (var row in _rows)
        {
            var record = new XElement(Ns.S + "r");

            for (var i = 0; i < _fields.Count; i++)
            {
                record.Add(_fields[i].ToRecordValue(row[i]));
            }

            root.Add(record);
        }

        return XmlUtil.NewDocument(root);
    }

    /// <summary>One source column, classified as numeric or shared-string.</summary>
    private sealed class Field
    {
        private Field(string name, bool numeric, List<string> items, double minimum, double maximum)
        {
            Name = name;
            Numeric = numeric;
            Items = items;
            Minimum = minimum;
            Maximum = maximum;
        }

        public string Name { get; }

        /// <summary>True when every value parses as a number.</summary>
        public bool Numeric { get; }

        public List<string> Items { get; }

        public double Minimum { get; }

        public double Maximum { get; }

        public static Field From(string name, IEnumerable<string> values)
        {
            var list = values.ToList();

            // A column counts as numeric only when every non-blank value is a number. One stray
            // label makes the whole field a string field, which is also how Excel treats it.
            var numbers = new List<double>();
            var numeric = list.Count > 0;

            foreach (var value in list)
            {
                if (value.Length == 0)
                {
                    continue;
                }

                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                {
                    numbers.Add(number);
                }
                else
                {
                    numeric = false;
                }
            }

            if (numeric && numbers.Count > 0)
            {
                return new Field(name, true, [], numbers.Min(), numbers.Max());
            }

            // Distinct in first-seen order: the record indices below refer to these positions.
            var items = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var value in list)
            {
                if (seen.Add(value))
                {
                    items.Add(value);
                }
            }

            return new Field(name, false, items, 0, 0);
        }

        public XElement ToCacheField()
        {
            var field = new XElement(Ns.S + "cacheField",
                new XAttribute("name", Name),
                new XAttribute("numFmtId", "0"));

            if (Numeric)
            {
                field.Add(new XElement(Ns.S + "sharedItems",
                    new XAttribute("containsSemiMixedTypes", "0"),
                    new XAttribute("containsString", "0"),
                    new XAttribute("containsNumber", "1"),
                    new XAttribute("minValue", Format(Minimum)),
                    new XAttribute("maxValue", Format(Maximum))));

                return field;
            }

            var shared = new XElement(Ns.S + "sharedItems",
                new XAttribute("count", Items.Count));

            foreach (var item in Items)
            {
                // An empty cell is a "missing" item, not an empty string; Excel distinguishes them.
                shared.Add(item.Length == 0
                    ? new XElement(Ns.S + "m")
                    : new XElement(Ns.S + "s", new XAttribute("v", item)));
            }

            field.Add(shared);
            return field;
        }

        public XElement ToRecordValue(string value)
        {
            if (Numeric)
            {
                return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                    ? new XElement(Ns.S + "n", new XAttribute("v", Format(number)))
                    : new XElement(Ns.S + "m");
            }

            var index = Items.IndexOf(value);

            // Every value was collected from these same rows, so the index is always found. Falling
            // back to "missing" rather than throwing keeps a surprise in the data from producing a
            // corrupt part.
            return index >= 0
                ? new XElement(Ns.S + "x", new XAttribute("v", index.ToString(CultureInfo.InvariantCulture)))
                : new XElement(Ns.S + "m");
        }

        private static string Format(double value) =>
            value.ToString("0.##########", CultureInfo.InvariantCulture);
    }
}
