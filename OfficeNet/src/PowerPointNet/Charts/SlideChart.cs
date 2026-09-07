// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Charts;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using OfficeNet.Core;
using PowerPointNet.Shapes;
using System.Xml.Linq;

namespace PowerPointNet.Charts;

/// <summary>
/// A native PowerPoint chart on a slide.
/// </summary>
/// <remarks>
/// Like a table, a chart is a <c>p:graphicFrame</c> — but where a table's data lives inside the
/// frame, a chart's lives in a separate part that the frame points at by relationship. That is why
/// adding a chart adds a part to the package and why <see cref="Part"/> exists.
/// </remarks>
public sealed class SlideChart : Shape
{
    private const string ChartGraphicUri =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";

    internal SlideChart(Presentation presentation, XElement element, OpcPart part)
        : base(presentation, element)
    {
        Part = part;
    }

    /// <summary>The chart part holding the data and formatting.</summary>
    public OpcPart Part { get; }

    /// <summary>The <c>c:chartSpace</c> root, for edits this library does not model.</summary>
    public XElement ChartSpace => Part.Xml.Root
        ?? throw new OfficeNetException($"{Part.Name} is empty.");

    internal static SlideChart Create(Presentation presentation, Slide slide, XElement shapeTree,
        uint id, ChartData data, Length left, Length top, Length width, Length height)
    {
        ArgumentNullException.ThrowIfNull(data);

        var partName = presentation.Package.NextPartName("/ppt/charts/chart{0}.xml");

        var part = presentation.Package.AddXmlPart(partName, ContentTypes.Chart,
            ChartXml.Build(data));

        var relationship = slide.Part.AddRelationship(part, RelationshipTypes.Chart);

        var frame = new XElement(Ns.P + "graphicFrame",
            new XElement(Ns.P + "nvGraphicFramePr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", data.Title is { Length: > 0 } title
                        ? title
                        : $"Chart {id}")),
                new XElement(Ns.P + "cNvGraphicFramePr"),
                new XElement(Ns.P + "nvPr")),
            new XElement(Ns.P + "xfrm",
                new XElement(Ns.A + "off",
                    new XAttribute("x", left.Emu), new XAttribute("y", top.Emu)),
                new XElement(Ns.A + "ext",
                    new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu))),
            new XElement(Ns.A + "graphic",
                new XElement(Ns.A + "graphicData",
                    new XAttribute("uri", ChartGraphicUri),
                    // The chart element carries only a relationship id; the data is in the part.
                    new XElement(Ns.C + "chart",
                        new XAttribute(XNamespace.Xmlns + "c", Ns.C.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
                        new XAttribute(Ns.R + "id", relationship.Id)))));

        shapeTree.Add(frame);
        presentation.Touch();
        return new SlideChart(presentation, frame, part);
    }

    /// <summary>True when a graphic frame holds a chart rather than a table.</summary>
    internal static bool IsChart(XElement graphicFrame) =>
        graphicFrame.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData")
            ?.Attr("uri") == ChartGraphicUri;

    /// <summary>Replaces the chart's data and formatting.</summary>
    public void SetData(ChartData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Part.Xml = ChartXml.Build(data);
        Presentation.Touch();
    }

    /// <summary>Reads the chart's data back out of the part.</summary>
    /// <remarks>
    /// What comes back is the cached data, which is everything this library writes. A chart authored
    /// in PowerPoint against an embedded workbook returns its cache too — the same numbers a viewer
    /// displays — but not the formulas behind them.
    /// </remarks>
    public ChartData GetData() => ChartXml.Read(Part.Xml);

    public override string ToString() => $"SlideChart \"{Name}\"";
}
