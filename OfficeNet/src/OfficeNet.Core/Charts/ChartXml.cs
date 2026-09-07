// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace OfficeNet.Core.Charts;

/// <summary>
/// Builds the DrawingML chart part.
/// </summary>
/// <remarks>
/// <para>
/// A chart in a deck is a native PowerPoint chart, not a picture: the numbers travel with it, the
/// theme restyles it, and a reader can hover a bar and see its value. That costs a whole extra
/// part with its own schema, which is what this file is.
/// </para>
/// <para>
/// The numbers are written twice on purpose. <c>c:f</c> holds a formula naming a worksheet range,
/// and <c>c:numCache</c> holds the literal values. PowerPoint renders the cache; the formula only
/// matters if someone clicks "Edit Data". Writing the cache is what makes the chart display
/// without shipping an embedded workbook alongside it.
/// </para>
/// </remarks>
public static class ChartXml
{
    // Axis ids are arbitrary but must be consistent between the chart group and its axes, and
    // unique within the part. Two groups sharing an id makes PowerPoint drop one of them.
    private const long CategoryAxisId = 111_111_111;
    private const long ValueAxisId = 222_222_222;
    private const long SecondaryValueAxisId = 333_333_333;

    public static XDocument Build(ChartData data)
    {
        data.Validate();

        var chart = new XElement(Ns.C + "chart");

        if (data.Title is { Length: > 0 } title)
        {
            chart.Add(BuildTitle(title, data));
            chart.Add(Val(Ns.C + "autoTitleDeleted", "0"));
        }
        else
        {
            // Without this PowerPoint invents a title from the first series name.
            chart.Add(Val(Ns.C + "autoTitleDeleted", "1"));
        }

        chart.Add(BuildPlotArea(data));

        if (data.Legend != LegendPosition.None)
        {
            chart.Add(new XElement(Ns.C + "legend",
                Val(Ns.C + "legendPos", data.Legend switch
                {
                    LegendPosition.Top => "t",
                    LegendPosition.Left => "l",
                    LegendPosition.Right => "r",
                    _ => "b",
                }),
                // overlay=0 keeps the legend out of the plot area rather than on top of it.
                Val(Ns.C + "overlay", "0"),
                TextProperties(data.FontSize)));
        }

        chart.Add(Val(Ns.C + "plotVisOnly", "1"));

        // A NaN leaves a gap rather than being plotted as zero, which would invent data.
        chart.Add(Val(Ns.C + "dispBlanksAs", "gap"));

        var root = new XElement(Ns.C + "chartSpace",
            new XAttribute(XNamespace.Xmlns + "c", Ns.C.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            Val(Ns.C + "date1904", "0"),
            Val(Ns.C + "lang", "en-US"),
            Val(Ns.C + "roundedCorners", "0"),
            chart,
            new XElement(Ns.C + "spPr",
                new XElement(Ns.A + "noFill"),
                new XElement(Ns.A + "ln", new XElement(Ns.A + "noFill"))),
            TextProperties(data.FontSize));

        return XmlUtil.NewDocument(root);
    }

    /// <summary>
    /// Reads a chart part back into a <see cref="ChartData"/>.
    /// </summary>
    /// <remarks>
    /// Only the cached values are read, never the <c>c:f</c> formulas: the cache is what a consumer
    /// renders, and it is the only part that is present when the embedded workbook is absent — which
    /// it is for every chart this library writes.
    /// </remarks>
    public static ChartData Read(XDocument document)
    {
        var plot = document.Root?
            .Element(Ns.C + "chart")?
            .Element(Ns.C + "plotArea");

        if (plot is null)
        {
            return new ChartData();
        }

        var group = plot.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal));

        if (group is null)
        {
            return new ChartData();
        }

        var stacked = group.Val(Ns.C + "grouping") == "stacked";
        var horizontal = group.Val(Ns.C + "barDir") == "bar";

        var type = group.Name.LocalName switch
        {
            "barChart" when horizontal && stacked => ChartType.BarStacked,
            "barChart" when horizontal => ChartType.Bar,
            "barChart" when stacked => ChartType.ColumnStacked,
            "barChart" => ChartType.Column,
            "lineChart" => ChartType.Line,
            "areaChart" when stacked => ChartType.AreaStacked,
            "areaChart" => ChartType.Area,
            "pieChart" => ChartType.Pie,
            "doughnutChart" => ChartType.Doughnut,
            "scatterChart" => ChartType.Scatter,
            "radarChart" => ChartType.Radar,
            _ => ChartType.Column,
        };

        var categories = new List<string>();
        var series = new List<ChartSeries>();

        foreach (var element in group.Elements(Ns.C + "ser"))
        {
            var name = element.Element(Ns.C + "tx")?
                .Element(Ns.C + "strRef")?
                .Element(Ns.C + "strCache")?
                .Elements(Ns.C + "pt")
                .FirstOrDefault()?
                .Element(Ns.C + "v")?.Value ?? string.Empty;

            // Every series repeats the same categories; the first one that has them wins.
            if (categories.Count == 0)
            {
                categories.AddRange(ReadStrings(element.Element(Ns.C + "cat")));
            }

            var values = ReadNumbers(element.Element(Ns.C + "val") ?? element.Element(Ns.C + "yVal"));

            var color = element.Element(Ns.C + "spPr") is { } shape
                ? ReadColor(shape)
                : null;

            var xValues = type == ChartType.Scatter
                ? ReadNumbers(element.Element(Ns.C + "xVal"))
                : null;

            series.Add(new ChartSeries(name, values)
            {
                Color = color,
                XValues = xValues is { Count: > 0 } ? xValues : null,
            });
        }

        var title = document.Root?
            .Element(Ns.C + "chart")?
            .Element(Ns.C + "title")?
            .Descendants(Ns.A + "t")
            .Select(t => t.Value)
            .FirstOrDefault();

        var legend = document.Root?.Element(Ns.C + "chart")?.Element(Ns.C + "legend");

        // Labels are written on the chart group, not per series, so the group's dLbls is the one
        // that says whether values are shown.
        var labels = group.Element(Ns.C + "dLbls");

        return new ChartData
        {
            Type = type,
            Title = title,
            Categories = categories,
            Series = series,
            Legend = legend is null ? LegendPosition.None : legend.Val(Ns.C + "legendPos") switch
            {
                "t" => LegendPosition.Top,
                "l" => LegendPosition.Left,
                "r" => LegendPosition.Right,
                _ => LegendPosition.Bottom,
            },
            CategoryAxisTitle = AxisTitle(plot, Ns.C + "catAx"),
            ValueAxisTitle = AxisTitle(plot, Ns.C + "valAx"),
            DoughnutHoleSize = int.TryParse(group.Val(Ns.C + "holeSize"), out var hole) ? hole : 50,
            GapWidth = int.TryParse(group.Val(Ns.C + "gapWidth"), out var gap) ? gap : 150,
            ShowGridLines = plot.Element(Ns.C + "valAx")?.Element(Ns.C + "majorGridlines") is not null,
            ShowDataLabels = labels.Val(Ns.C + "showVal") == "1"
                             || labels.Val(Ns.C + "showPercent") == "1",
            ShowPercentages = labels.Val(Ns.C + "showPercent") == "1",
            ValueFormat = ValueFormatOf(group) ?? "General",
        };
    }

    /// <summary>The number format the value cache declares, which is what a renderer should use.</summary>
    private static string? ValueFormatOf(XElement group) =>
        group.Elements(Ns.C + "ser")
            .Select(s => s.Element(Ns.C + "val") ?? s.Element(Ns.C + "yVal"))
            .Select(v => v?.Descendants(Ns.C + "numCache").FirstOrDefault()
                ?.Element(Ns.C + "formatCode")?.Value)
            .FirstOrDefault(f => f is { Length: > 0 });

    private static string? AxisTitle(XElement plot, XName axis) =>
        plot.Element(axis)?.Element(Ns.C + "title")?
            .Descendants(Ns.A + "t").Select(t => t.Value).FirstOrDefault();

    private static IReadOnlyList<string> ReadStrings(XElement? reference)
    {
        var cache = reference?.Descendants(Ns.C + "strCache").FirstOrDefault()
                    ?? reference?.Descendants(Ns.C + "numCache").FirstOrDefault();

        if (cache is null)
        {
            return [];
        }

        return ReadPoints(cache, (_, text) => text);
    }

    private static List<double> ReadNumbers(XElement? reference)
    {
        var cache = reference?.Descendants(Ns.C + "numCache").FirstOrDefault();

        if (cache is null)
        {
            return [];
        }

        // A blank point is a gap, not a zero — dispBlanksAs="gap" is what the writer emits — and
        // NaN is how the rest of the pipeline spells "no value".
        return [.. ReadPoints(cache, (_, text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.NaN)];
    }

    /// <summary>
    /// Reads a cache's <c>c:pt</c> children in index order, filling gaps.
    /// </summary>
    /// <remarks>
    /// The points carry an <c>idx</c> and are allowed to be sparse and out of order, so reading them
    /// in document order silently shifts every value after a missing one.
    /// </remarks>
    private static List<T> ReadPoints<T>(XElement cache, Func<int, string, T> convert)
    {
        var points = new SortedDictionary<int, string>();
        var highest = -1;

        foreach (var point in cache.Elements(Ns.C + "pt"))
        {
            if (!int.TryParse(point.Attr("idx"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            points[index] = point.Element(Ns.C + "v")?.Value ?? string.Empty;
            highest = Math.Max(highest, index);
        }

        var count = int.TryParse(cache.Val(Ns.C + "ptCount"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var declared)
            ? Math.Max(declared, highest + 1)
            : highest + 1;

        var result = new List<T>(Math.Max(0, count));

        for (var i = 0; i < count; i++)
        {
            result.Add(convert(i, points.TryGetValue(i, out var text) ? text : string.Empty));
        }

        return result;
    }

    private static OfficeColor? ReadColor(XElement shapeProperties)
    {
        var hex = shapeProperties.Descendants(Ns.A + "srgbClr").FirstOrDefault()?.Attr("val");
        return hex is not null && OfficeColor.TryParse(hex, out var color) ? color : null;
    }

    private static XElement BuildTitle(string title, ChartData data) =>
        new(Ns.C + "title",
            new XElement(Ns.C + "tx",
                new XElement(Ns.C + "rich",
                    new XElement(Ns.A + "bodyPr"),
                    new XElement(Ns.A + "lstStyle"),
                    new XElement(Ns.A + "p",
                        new XElement(Ns.A + "pPr",
                            new XElement(Ns.A + "defRPr",
                                new XAttribute("sz", (data.FontSize.Centipoints + 400).ToString(
                                    CultureInfo.InvariantCulture)),
                                new XAttribute("b", "1"))),
                        new XElement(Ns.A + "r",
                            new XElement(Ns.A + "rPr",
                                new XAttribute("lang", "en-US"),
                                new XAttribute("sz", (data.FontSize.Centipoints + 400).ToString(
                                    CultureInfo.InvariantCulture)),
                                new XAttribute("b", "1")),
                            XmlUtil.TextElement(Ns.A + "t", title))))),
            Val(Ns.C + "overlay", "0"));

    private static XElement BuildPlotArea(ChartData data)
    {
        var plot = new XElement(Ns.C + "plotArea", new XElement(Ns.C + "layout"));

        plot.Add(BuildChartGroup(data));

        // A pie or doughnut has no axes at all; adding empty ones makes PowerPoint repair the part.
        if (!data.IsPieFamily)
        {
            if (data.IsScatter)
            {
                plot.Add(BuildValueAxis(data, CategoryAxisId, ValueAxisId, "b",
                    data.CategoryAxisTitle));
                plot.Add(BuildValueAxis(data, ValueAxisId, CategoryAxisId, "l", data.ValueAxisTitle));
            }
            else
            {
                plot.Add(BuildCategoryAxis(data));
                plot.Add(BuildValueAxis(data, ValueAxisId, CategoryAxisId, "l", data.ValueAxisTitle));
            }
        }

        plot.Add(new XElement(Ns.C + "spPr",
            new XElement(Ns.A + "noFill"),
            new XElement(Ns.A + "ln", new XElement(Ns.A + "noFill"))));

        return plot;
    }

    private static XElement BuildChartGroup(ChartData data)
    {
        var (element, grouping) = data.Type switch
        {
            ChartType.Column => (Ns.C + "barChart", "clustered"),
            ChartType.ColumnStacked => (Ns.C + "barChart", "stacked"),
            ChartType.Bar => (Ns.C + "barChart", "clustered"),
            ChartType.BarStacked => (Ns.C + "barChart", "stacked"),
            ChartType.Line or ChartType.LineMarkers => (Ns.C + "lineChart", "standard"),
            ChartType.Area => (Ns.C + "areaChart", "standard"),
            ChartType.AreaStacked => (Ns.C + "areaChart", "stacked"),
            ChartType.Pie => (Ns.C + "pieChart", ""),
            ChartType.Doughnut => (Ns.C + "doughnutChart", ""),
            ChartType.Scatter => (Ns.C + "scatterChart", ""),
            ChartType.Radar => (Ns.C + "radarChart", ""),
            _ => (Ns.C + "barChart", "clustered"),
        };

        var group = new XElement(element);

        // barDir must come first in a bar chart and distinguishes columns from bars — the chart
        // element is the same for both.
        if (element == Ns.C + "barChart")
        {
            group.Add(Val(Ns.C + "barDir",
                data.Type is ChartType.Bar or ChartType.BarStacked ? "bar" : "col"));
        }

        if (grouping.Length > 0)
        {
            group.Add(Val(Ns.C + "grouping", grouping));
        }

        if (element == Ns.C + "radarChart")
        {
            group.Add(Val(Ns.C + "radarStyle", "marker"));
        }

        if (element == Ns.C + "scatterChart")
        {
            group.Add(Val(Ns.C + "scatterStyle", "lineMarker"));
        }

        // varyColors gives a pie its slice colours; on a multi-series chart it would recolour every
        // point and destroy the series distinction.
        group.Add(Val(Ns.C + "varyColors", data.IsPieFamily ? "1" : "0"));

        for (var index = 0; index < data.Series.Count; index++)
        {
            group.Add(BuildSeries(data, index));
        }

        group.Add(BuildDataLabels(data));

        if (element == Ns.C + "barChart")
        {
            group.Add(Val(Ns.C + "gapWidth", data.GapWidth.ToString(CultureInfo.InvariantCulture)));

            if (grouping == "stacked")
            {
                // Without overlap 100 a stacked bar chart renders its segments side by side, which
                // looks like a clustered chart with the wrong spacing.
                group.Add(Val(Ns.C + "overlap", "100"));
            }
        }

        if (element == Ns.C + "doughnutChart")
        {
            group.Add(Val(Ns.C + "firstSliceAng", "0"));
            group.Add(Val(Ns.C + "holeSize",
                Math.Clamp(data.DoughnutHoleSize, 10, 90).ToString(CultureInfo.InvariantCulture)));
        }

        if (element == Ns.C + "pieChart")
        {
            group.Add(Val(Ns.C + "firstSliceAng", "0"));
        }

        if (element == Ns.C + "lineChart")
        {
            group.Add(Val(Ns.C + "marker", "1"));
        }

        if (!data.IsPieFamily)
        {
            group.Add(Val(Ns.C + "axId", CategoryAxisId.ToString(CultureInfo.InvariantCulture)));
            group.Add(Val(Ns.C + "axId", ValueAxisId.ToString(CultureInfo.InvariantCulture)));
        }

        return group;
    }

    /// <summary>
    /// Builds one series.
    /// </summary>
    /// <remarks>
    /// The child order is a schema sequence and PowerPoint enforces it: idx, order, tx, spPr,
    /// marker, dPt, dLbls, cat, val, smooth. Putting <c>c:cat</c> before <c>c:spPr</c> — which
    /// reads more naturally — produces a part PowerPoint offers to repair.
    /// </remarks>
    private static XElement BuildSeries(ChartData data, int index)
    {
        var series = data.Series[index];
        var color = data.ColorFor(index);

        var element = new XElement(Ns.C + "ser",
            Val(Ns.C + "idx", index.ToString(CultureInfo.InvariantCulture)),
            Val(Ns.C + "order", index.ToString(CultureInfo.InvariantCulture)),
            new XElement(Ns.C + "tx",
                new XElement(Ns.C + "strRef",
                    new XElement(Ns.C + "f", SeriesNameFormula(index)),
                    new XElement(Ns.C + "strCache",
                        Val(Ns.C + "ptCount", "1"),
                        new XElement(Ns.C + "pt",
                            new XAttribute("idx", "0"),
                            new XElement(Ns.C + "v", series.Name))))));

        var isLine = data.Type is ChartType.Line or ChartType.LineMarkers or ChartType.Radar
                     or ChartType.Scatter;

        element.Add(new XElement(Ns.C + "spPr",
            isLine
                ? new XElement(Ns.A + "ln",
                    new XAttribute("w", "28575"),
                    new XElement(Ns.A + "solidFill",
                        new XElement(Ns.A + "srgbClr", new XAttribute("val", color.ToHex()))))
                : new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "srgbClr", new XAttribute("val", color.ToHex())))));

        // A line without markers still needs an explicit "none" marker, or PowerPoint draws its
        // default diamonds.
        if (data.Type is ChartType.Line or ChartType.Area or ChartType.AreaStacked)
        {
            element.Add(new XElement(Ns.C + "marker", Val(Ns.C + "symbol", "none")));
        }
        else if (data.Type is ChartType.LineMarkers or ChartType.Scatter or ChartType.Radar)
        {
            element.Add(new XElement(Ns.C + "marker",
                Val(Ns.C + "symbol", "circle"),
                Val(Ns.C + "size", "6"),
                new XElement(Ns.C + "spPr",
                    new XElement(Ns.A + "solidFill",
                        new XElement(Ns.A + "srgbClr", new XAttribute("val", color.ToHex()))))));
        }

        // A pie's slices are one series, so each needs its own colour or the whole pie is one hue.
        if (data.IsPieFamily)
        {
            for (var point = 0; point < series.Values.Count; point++)
            {
                element.Add(new XElement(Ns.C + "dPt",
                    Val(Ns.C + "idx", point.ToString(CultureInfo.InvariantCulture)),
                    Val(Ns.C + "bubble3D", "0"),
                    new XElement(Ns.C + "spPr",
                        new XElement(Ns.A + "solidFill",
                            new XElement(Ns.A + "srgbClr",
                                new XAttribute("val",
                                    ChartData.DefaultPalette[point % ChartData.DefaultPalette.Length]
                                        .ToHex()))))));
            }
        }

        if (data.IsScatter)
        {
            element.Add(new XElement(Ns.C + "xVal", NumberReference(
                ScatterXFormula(index), series.XValues!, "General")));

            element.Add(new XElement(Ns.C + "yVal", NumberReference(
                SeriesValuesFormula(index, series.Values.Count), series.Values, data.ValueFormat)));

            // smooth=0 draws straight segments between points, which is what a scatter of
            // measurements should show.
            element.Add(Val(Ns.C + "smooth", "0"));
            return element;
        }

        element.Add(new XElement(Ns.C + "cat",
            new XElement(Ns.C + "strRef",
                new XElement(Ns.C + "f", CategoriesFormula(data.Categories.Count)),
                new XElement(Ns.C + "strCache",
                    Val(Ns.C + "ptCount",
                        data.Categories.Count.ToString(CultureInfo.InvariantCulture)),
                    data.Categories.Select((label, i) =>
                        new XElement(Ns.C + "pt",
                            new XAttribute("idx", i.ToString(CultureInfo.InvariantCulture)),
                            new XElement(Ns.C + "v", label)))))));

        element.Add(new XElement(Ns.C + "val", NumberReference(
            SeriesValuesFormula(index, series.Values.Count), series.Values, data.ValueFormat)));

        if (data.Type is ChartType.Line or ChartType.LineMarkers)
        {
            element.Add(Val(Ns.C + "smooth", "0"));
        }

        return element;
    }

    private static XElement NumberReference(string formula, IReadOnlyList<double> values,
        string format)
    {
        var cache = new XElement(Ns.C + "numCache",
            new XElement(Ns.C + "formatCode", format),
            Val(Ns.C + "ptCount", values.Count.ToString(CultureInfo.InvariantCulture)));

        for (var i = 0; i < values.Count; i++)
        {
            // A NaN is a gap. Writing it as the literal "NaN" makes the whole chart fail to load.
            if (double.IsNaN(values[i]) || double.IsInfinity(values[i]))
            {
                continue;
            }

            cache.Add(new XElement(Ns.C + "pt",
                new XAttribute("idx", i.ToString(CultureInfo.InvariantCulture)),
                new XElement(Ns.C + "v", values[i].ToString("R", CultureInfo.InvariantCulture))));
        }

        return new XElement(Ns.C + "numRef",
            new XElement(Ns.C + "f", formula),
            cache);
    }

    private static XElement BuildDataLabels(ChartData data)
    {
        var labels = new XElement(Ns.C + "dLbls");

        // Every one of these flags is required, in this order. An omitted flag is not a default;
        // PowerPoint treats the part as malformed.
        labels.Add(Val(Ns.C + "showLegendKey", "0"));
        labels.Add(Val(Ns.C + "showVal", data.ShowDataLabels && !data.ShowPercentages ? "1" : "0"));
        labels.Add(Val(Ns.C + "showCatName", "0"));
        labels.Add(Val(Ns.C + "showSerName", "0"));
        labels.Add(Val(Ns.C + "showPercent",
            data.ShowPercentages && data.IsPieFamily ? "1" : "0"));
        labels.Add(Val(Ns.C + "showBubbleSize", "0"));

        return labels;
    }

    private static XElement BuildCategoryAxis(ChartData data)
    {
        var axis = new XElement(Ns.C + "catAx",
            Val(Ns.C + "axId", CategoryAxisId.ToString(CultureInfo.InvariantCulture)),
            new XElement(Ns.C + "scaling", Val(Ns.C + "orientation", "minMax")),
            Val(Ns.C + "delete", "0"),
            // A bar chart's category axis runs up the left; a column chart's runs along the bottom.
            Val(Ns.C + "axPos", data.Type is ChartType.Bar or ChartType.BarStacked ? "l" : "b"));

        if (data.CategoryAxisTitle is { Length: > 0 } title)
        {
            axis.Add(AxisTitle(title, data, rotated: false));
        }

        axis.Add(Val(Ns.C + "majorTickMark", "none"));
        axis.Add(Val(Ns.C + "minorTickMark", "none"));
        axis.Add(Val(Ns.C + "tickLblPos", "nextTo"));
        axis.Add(AxisLine());
        axis.Add(TextProperties(data.FontSize));
        axis.Add(Val(Ns.C + "crossAx", ValueAxisId.ToString(CultureInfo.InvariantCulture)));
        axis.Add(Val(Ns.C + "crosses", "autoZero"));
        axis.Add(Val(Ns.C + "auto", "1"));
        axis.Add(Val(Ns.C + "lblAlgn", "ctr"));
        axis.Add(Val(Ns.C + "lblOffset", "100"));
        axis.Add(Val(Ns.C + "noMultiLvlLbl", "0"));

        return axis;
    }

    private static XElement BuildValueAxis(ChartData data, long id, long crossId, string position,
        string? title)
    {
        var axis = new XElement(Ns.C + "valAx",
            Val(Ns.C + "axId", id.ToString(CultureInfo.InvariantCulture)),
            new XElement(Ns.C + "scaling", Val(Ns.C + "orientation", "minMax")),
            Val(Ns.C + "delete", "0"),
            Val(Ns.C + "axPos", position));

        if (data.ShowGridLines)
        {
            axis.Add(new XElement(Ns.C + "majorGridlines",
                new XElement(Ns.C + "spPr",
                    new XElement(Ns.A + "ln",
                        new XAttribute("w", "9525"),
                        new XElement(Ns.A + "solidFill",
                            new XElement(Ns.A + "srgbClr",
                                new XAttribute("val", "D9D9D9")))))));
        }

        if (title is { Length: > 0 })
        {
            axis.Add(AxisTitle(title, data, rotated: position is "l" or "r"));
        }

        axis.Add(new XElement(Ns.C + "numFmt",
            new XAttribute("formatCode", data.ValueFormat),
            // sourceLinked=0 makes the format above win; with 1 the axis takes the cell's format
            // from a workbook that is not there.
            new XAttribute("sourceLinked", "0")));

        axis.Add(Val(Ns.C + "majorTickMark", "none"));
        axis.Add(Val(Ns.C + "minorTickMark", "none"));
        axis.Add(Val(Ns.C + "tickLblPos", "nextTo"));
        axis.Add(AxisLine());
        axis.Add(TextProperties(data.FontSize));
        axis.Add(Val(Ns.C + "crossAx", crossId.ToString(CultureInfo.InvariantCulture)));
        axis.Add(Val(Ns.C + "crosses", "autoZero"));
        axis.Add(Val(Ns.C + "crossBetween", "between"));

        return axis;
    }

    private static XElement AxisTitle(string title, ChartData data, bool rotated) =>
        new(Ns.C + "title",
            new XElement(Ns.C + "tx",
                new XElement(Ns.C + "rich",
                    // A left-hand axis title reads bottom-to-top; rot is in 60000ths of a degree.
                    new XElement(Ns.A + "bodyPr",
                        rotated ? new XAttribute("rot", "-5400000") : null,
                        rotated ? new XAttribute("vert", "horz") : null),
                    new XElement(Ns.A + "lstStyle"),
                    new XElement(Ns.A + "p",
                        new XElement(Ns.A + "r",
                            new XElement(Ns.A + "rPr",
                                new XAttribute("lang", "en-US"),
                                new XAttribute("sz",
                                    data.FontSize.Centipoints.ToString(CultureInfo.InvariantCulture))),
                            XmlUtil.TextElement(Ns.A + "t", title))))),
            Val(Ns.C + "overlay", "0"));

    private static XElement AxisLine() =>
        new(Ns.C + "spPr",
            new XElement(Ns.A + "ln",
                new XAttribute("w", "9525"),
                new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "srgbClr", new XAttribute("val", "BFBFBF")))));

    private static XElement TextProperties(Length size) =>
        new(Ns.C + "txPr",
            new XElement(Ns.A + "bodyPr"),
            new XElement(Ns.A + "lstStyle"),
            new XElement(Ns.A + "p",
                new XElement(Ns.A + "pPr",
                    new XElement(Ns.A + "defRPr",
                        new XAttribute("sz",
                            size.Centipoints.ToString(CultureInfo.InvariantCulture)))),
                new XElement(Ns.A + "endParaRPr", new XAttribute("lang", "en-US"))));

    private static XElement Val(XName name, string value) =>
        new(name, new XAttribute("val", value));

    // The formulas name a worksheet that is not embedded. PowerPoint only reads them when the user
    // clicks "Edit Data", and it then offers to create the workbook. Writing plausible ranges
    // keeps that dialogue sensible; the cache is what actually renders.
    private static string SeriesNameFormula(int index) =>
        $"Sheet1!${ColumnName(index + 1)}$1";

    private static string CategoriesFormula(int count) =>
        $"Sheet1!$A$2:$A${count + 1}";

    private static string SeriesValuesFormula(int index, int count) =>
        $"Sheet1!${ColumnName(index + 1)}$2:${ColumnName(index + 1)}${count + 1}";

    private static string ScatterXFormula(int index) =>
        $"Sheet1!$A$2:$A${index + 2}";

    private static string ColumnName(int index)
    {
        Span<char> buffer = stackalloc char[4];
        var position = buffer.Length;
        var value = index;

        do
        {
            buffer[--position] = (char)('A' + value % 26);
            value = value / 26 - 1;
        }
        while (value >= 0);

        return new string(buffer[position..]);
    }
}
