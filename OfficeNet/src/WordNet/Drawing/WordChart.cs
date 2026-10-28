// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace WordNet.Drawing;

/// <summary>A native chart in a Word document.</summary>
/// <remarks>
/// <para>
/// The chart is the same DrawingML chart part ExcelNet and PowerPointNet write — one
/// <see cref="ChartXml"/> serves all three — held in a <c>w:drawing</c> whose graphic data is a
/// <c>c:chart</c> pointing at the part. Word draws it from the values cached in the part, so no
/// embedded workbook is needed to show it; editing its data in Word is what would need one.
/// </para>
/// <para>
/// The PDF export draws it with the same renderer the slide and sheet exports use.
/// </para>
/// </remarks>
public sealed class WordChart
{
    internal WordChart(WordDocument document, XElement drawing, OpcPart part)
    {
        Document = document;
        Drawing = drawing;
        Part = part;
    }

    public WordDocument Document { get; }

    /// <summary>The <c>w:drawing</c> holding the chart.</summary>
    public XElement Drawing { get; }

    /// <summary>The chart part (<c>word/charts/chartN.xml</c>).</summary>
    public OpcPart Part { get; }

    /// <summary>The chart's categories, series and options, read from its part.</summary>
    public ChartData GetData() => ChartXml.Read(Part.Xml);

    /// <summary>Replaces the chart's data.</summary>
    public void SetData(ChartData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Part.Xml = ChartXml.Build(data);
        Document.Touch();
    }

    internal static XElement BuildDrawing(WordDocument document, XElement owner, ChartData data, Length width,
        Length height, string? altText)
    {
        var package = document.Package;
        var part = package.AddXmlPart(package.NextPartName("/word/charts/chart{0}.xml"), ContentTypes.Chart,
            ChartXml.Build(data));

        // The relationship lives on the part the paragraph is in — a chart in a header needs it on
        // the header, as a picture does.
        var relationship = document.OwnerPartOf(owner).AddRelationship(part, RelationshipTypes.Chart);

        var drawingId = document.NextDrawingId();
        var name = $"Chart {drawingId}";

        var graphic = new XElement(Ns.A + "graphic",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XElement(Ns.A + "graphicData",
                new XAttribute("uri", Ns.C.NamespaceName),
                new XElement(Ns.C + "chart",
                    new XAttribute(XNamespace.Xmlns + "c", Ns.C.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
                    new XAttribute(Ns.R + "id", relationship.Id))));

        return new XElement(Ns.W + "drawing",
            Shape.BuildContainer(drawingId, name, width, height, altText ?? data.Title, null, graphic));
    }

    internal static IEnumerable<WordChart> Find(WordDocument document) =>
        document.Body.Descendants(Ns.W + "drawing")
            .Select(d => (Drawing: d, Id: d.Descendants(Ns.C + "chart").FirstOrDefault()?.Attr(Ns.R + "id")))
            .Where(x => x.Id is not null)
            .Select(x => (x.Drawing, Part: document.DocumentPart.RelatedPart(x.Id!)))
            .Where(x => x.Part is not null)
            .Select(x => new WordChart(document, x.Drawing, x.Part!));
}
