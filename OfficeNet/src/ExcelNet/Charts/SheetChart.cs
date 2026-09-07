// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace ExcelNet.Charts;

/// <summary>
/// A chart floating over a worksheet.
/// </summary>
/// <remarks>
/// <para>
/// The chart itself is the same DrawingML part a PowerPoint chart uses — byte for byte — which is
/// why <see cref="ChartXml"/> lives in <c>OfficeNet.Core</c> and is shared. What differs is how it
/// is attached: PowerPoint puts a <c>graphicFrame</c> straight on the slide, while a worksheet
/// reaches its chart through an intermediate <em>drawing</em> part.
/// </para>
/// <para>
/// So one chart is three parts and three relationships:
/// </para>
/// <code>
/// sheet1.xml  --drawing--&gt;  drawing1.xml  --chart--&gt;  chart1.xml
/// </code>
/// <para>
/// Miss the drawing part and Excel opens the file with no chart and no complaint, which is the
/// usual way a hand-built workbook loses one.
/// </para>
/// </remarks>
public sealed class SheetChart
{
    private const string ChartGraphicUri =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";

    internal SheetChart(Worksheet sheet, OpcPart drawingPart, OpcPart chartPart,
        CellRangeReference anchor)
    {
        Sheet = sheet;
        DrawingPart = drawingPart;
        Part = chartPart;
        Anchor = anchor;
    }

    /// <summary>The worksheet the chart sits on.</summary>
    public Worksheet Sheet { get; }

    /// <summary>The chart part, holding the data and the formatting.</summary>
    public OpcPart Part { get; }

    /// <summary>The drawing part that anchors the chart to the sheet.</summary>
    public OpcPart DrawingPart { get; }

    /// <summary>The cells the chart is pinned to.</summary>
    public CellRangeReference Anchor { get; }

    /// <summary>The <c>c:chartSpace</c> root, for edits this library does not model.</summary>
    public XElement ChartSpace => Part.Xml.Root
        ?? throw new OfficeNetException($"{Part.Name} is empty.");

    /// <summary>Replaces the chart's data and formatting.</summary>
    public void SetData(ChartData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Part.Xml = ChartXml.Build(data);
        Sheet.Workbook.Package.MarkDirty();
    }

    /// <summary>Reads the chart's data back out of the part.</summary>
    public ChartData GetData() => ChartXml.Read(Part.Xml);

    public override string ToString() => $"SheetChart on {Sheet.Name} at {Anchor.A1}";

    // ---- Construction --------------------------------------------------------------------------

    internal static SheetChart Create(Worksheet sheet, ChartData data, CellRangeReference anchor)
    {
        ArgumentNullException.ThrowIfNull(data);

        var package = sheet.Workbook.Package;

        var chartPart = package.AddXmlPart(
            package.NextPartName("/xl/charts/chart{0}.xml"),
            ContentTypes.Chart,
            ChartXml.Build(data));

        // One drawing part per chart. A single drawing can hold several anchors, but one part per
        // chart keeps removal simple and costs nothing a reader will notice.
        var drawingPart = package.AddXmlPart(
            package.NextPartName("/xl/drawings/drawing{0}.xml"),
            ContentTypes.SpreadsheetDrawing,
            new XDocument());

        var relationship = drawingPart.AddRelationship(chartPart, RelationshipTypes.Chart);

        drawingPart.Xml = BuildDrawing(anchor, relationship.Id, data.Title);
        sheet.Part.AddRelationship(drawingPart, RelationshipTypes.Drawing);

        sheet.Workbook.Package.MarkDirty();

        return new SheetChart(sheet, drawingPart, chartPart, anchor);
    }

    /// <summary>Builds the <c>xdr:wsDr</c> that pins the chart to a cell range.</summary>
    /// <remarks>
    /// A <c>twoCellAnchor</c> ties both corners to cells, so the chart resizes when the columns and
    /// rows underneath it do. That is what a person dragging a chart in Excel produces, and it is
    /// the behaviour they expect back.
    /// </remarks>
    private static XDocument BuildDrawing(CellRangeReference anchor, string relationshipId, string? title)
    {
        var root = new XElement(Ns.Xdr + "wsDr",
            new XAttribute(XNamespace.Xmlns + "xdr", Ns.Xdr.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName));

        var frame = new XElement(Ns.Xdr + "graphicFrame",
            new XAttribute("macro", string.Empty),
            new XElement(Ns.Xdr + "nvGraphicFramePr",
                new XElement(Ns.Xdr + "cNvPr",
                    // Id 0 is reserved; drawing object ids start at 1 and must be unique per part.
                    new XAttribute("id", "2"),
                    new XAttribute("name", title is { Length: > 0 } ? title : "Chart")),
                new XElement(Ns.Xdr + "cNvGraphicFramePr")),

            // The frame's own transform is ignored for an anchored chart — the anchor decides the
            // geometry — but Excel repairs the part when it is missing.
            new XElement(Ns.Xdr + "xfrm",
                new XElement(Ns.A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                new XElement(Ns.A + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0"))),

            new XElement(Ns.A + "graphic",
                new XElement(Ns.A + "graphicData",
                    new XAttribute("uri", ChartGraphicUri),
                    new XElement(Ns.C + "chart",
                        new XAttribute(XNamespace.Xmlns + "c", Ns.C.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
                        new XAttribute(Ns.R + "id", relationshipId)))));

        root.Add(new XElement(Ns.Xdr + "twoCellAnchor",
            new XAttribute("editAs", "oneCell"),
            Marker("from", anchor.Start.Column, anchor.Start.Row),
            // The "to" marker is exclusive of the cell it names, so the range's last cell is
            // included by pointing one past it.
            Marker("to", anchor.End.Column + 1, anchor.End.Row + 1),
            frame,
            new XElement(Ns.Xdr + "clientData")));

        return XmlUtil.NewDocument(root);
    }

    private static XElement Marker(string name, int column, int row) =>
        new(Ns.Xdr + name,
            new XElement(Ns.Xdr + "col", column.ToString(CultureInfo.InvariantCulture)),
            new XElement(Ns.Xdr + "colOff", "0"),
            new XElement(Ns.Xdr + "row", row.ToString(CultureInfo.InvariantCulture)),
            new XElement(Ns.Xdr + "rowOff", "0"));

    /// <summary>Reads the charts already attached to a worksheet.</summary>
    internal static IEnumerable<SheetChart> Read(Worksheet sheet)
    {
        foreach (var drawingRelationship in sheet.Part.RelationshipsByType(RelationshipTypes.Drawing))
        {
            if (sheet.Part.RelatedPart(drawingRelationship.Id) is not { } drawingPart)
            {
                continue;
            }

            var root = drawingPart.Xml.Root;

            if (root is null)
            {
                continue;
            }

            foreach (var twoCell in root.Elements(Ns.Xdr + "twoCellAnchor"))
            {
                var chartElement = twoCell
                    .Descendants(Ns.C + "chart")
                    .FirstOrDefault();

                if (chartElement?.Attr(Ns.R + "id") is not { } id ||
                    drawingPart.RelatedPart(id) is not { } chartPart)
                {
                    continue;
                }

                yield return new SheetChart(sheet, drawingPart, chartPart, ReadAnchor(twoCell));
            }
        }
    }

    private static CellRangeReference ReadAnchor(XElement twoCellAnchor)
    {
        var from = twoCellAnchor.Element(Ns.Xdr + "from");
        var to = twoCellAnchor.Element(Ns.Xdr + "to");

        var start = new CellReference(Value(from, "row"), Value(from, "col"));

        // The "to" marker is exclusive, so the last included cell is one back on each axis.
        var end = new CellReference(
            Math.Max(start.Row, Value(to, "row") - 1),
            Math.Max(start.Column, Value(to, "col") - 1));

        return new CellRangeReference(start, end);

        static int Value(XElement? marker, string name) =>
            int.TryParse(marker?.Element(Ns.Xdr + name)?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
    }
}
