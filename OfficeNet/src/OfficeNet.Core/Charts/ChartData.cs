// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Drawing;

namespace OfficeNet.Core.Charts;

/// <summary>The chart types PowerPointNet writes.</summary>
public enum ChartType
{
    /// <summary>Vertical bars, one group per category.</summary>
    Column,

    /// <summary>Vertical bars stacked within each category.</summary>
    ColumnStacked,

    /// <summary>Horizontal bars.</summary>
    Bar,

    /// <summary>Horizontal bars stacked within each category.</summary>
    BarStacked,

    /// <summary>A line per series.</summary>
    Line,

    /// <summary>A line per series with a marker at each point.</summary>
    LineMarkers,

    /// <summary>A filled area per series.</summary>
    Area,

    /// <summary>Filled areas stacked within each category.</summary>
    AreaStacked,

    /// <summary>A pie of one series.</summary>
    Pie,

    /// <summary>A pie with a hole.</summary>
    Doughnut,

    /// <summary>Points plotted against two value axes.</summary>
    Scatter,

    /// <summary>A radar (spider) chart.</summary>
    Radar,
}

/// <summary>Where a chart's legend sits.</summary>
public enum LegendPosition
{
    /// <summary>No legend.</summary>
    None,

    /// <summary>Below the plot.</summary>
    Bottom,

    /// <summary>To the left.</summary>
    Left,

    /// <summary>To the right.</summary>
    Right,

    /// <summary>Above the plot.</summary>
    Top,
}

/// <summary>One data series of a chart.</summary>
/// <param name="Name">The series name, shown in the legend.</param>
/// <param name="Values">One value per category; <c>double.NaN</c> leaves a gap.</param>
public sealed record ChartSeries(string Name, IReadOnlyList<double> Values)
{
    /// <summary>An explicit colour; <c>null</c> takes the next theme accent.</summary>
    public OfficeColor? Color { get; init; }

    /// <summary>
    /// The x values, for a scatter chart only.
    /// </summary>
    /// <remarks>
    /// A scatter chart has two value axes, so its points are pairs. Every other chart type plots
    /// against categories and ignores this.
    /// </remarks>
    public IReadOnlyList<double>? XValues { get; init; }

    /// <summary>Creates a series from values.</summary>
    public static ChartSeries From(string name, params double[] values) => new(name, values);
}

/// <summary>
/// The data and presentation of a chart.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape PptxGenJS's <c>addChart</c> takes, and it is deliberately not a spreadsheet:
/// a chart in a deck is a picture of numbers the author already has, not a live view of a
/// worksheet. The numbers are written into the chart part itself as a literal cache, which is why
/// the chart renders without the workbook that produced it.
/// </para>
/// <para>
/// A record so that <c>with</c> works: the factory methods return a sensible default and the
/// caller adjusts one property, which is how an options object should read.
/// </para>
/// </remarks>
public sealed record ChartData
{
    /// <summary>The chart type.</summary>
    public ChartType Type { get; init; } = ChartType.Column;

    /// <summary>The category labels along the axis.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The series.</summary>
    public IReadOnlyList<ChartSeries> Series { get; init; } = [];

    /// <summary>The chart's title; <c>null</c> for none.</summary>
    public string? Title { get; init; }

    /// <summary>Where the legend goes.</summary>
    public LegendPosition Legend { get; init; } = LegendPosition.Bottom;

    /// <summary>Prints each point's value on the chart.</summary>
    public bool ShowDataLabels { get; init; }

    /// <summary>Prints each slice's share as a percentage; pie and doughnut only.</summary>
    public bool ShowPercentages { get; init; }

    /// <summary>The number format applied to values, for example <c>#,##0</c>.</summary>
    public string ValueFormat { get; init; } = "General";

    /// <summary>The title of the category axis.</summary>
    public string? CategoryAxisTitle { get; init; }

    /// <summary>The title of the value axis.</summary>
    public string? ValueAxisTitle { get; init; }

    /// <summary>Draws horizontal gridlines behind the plot.</summary>
    public bool ShowGridLines { get; init; } = true;

    /// <summary>The doughnut hole as a percentage of the radius, 10 to 90.</summary>
    public int DoughnutHoleSize { get; init; } = 50;

    /// <summary>The gap between bar groups as a percentage of the bar width.</summary>
    public int GapWidth { get; init; } = 150;

    /// <summary>The base font size for the chart's text.</summary>
    public Length FontSize { get; init; } = Units.Pt(12);

    /// <summary>Builds a chart from categories and one series.</summary>
    public static ChartData Simple(ChartType type, string seriesName,
        IReadOnlyList<string> categories, IReadOnlyList<double> values) =>
        new()
        {
            Type = type,
            Categories = categories,
            Series = [new ChartSeries(seriesName, values)],
        };

    /// <summary>Builds a chart from a category-to-value map.</summary>
    public static ChartData FromMap(ChartType type, string seriesName,
        IReadOnlyDictionary<string, double> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new ChartData
        {
            Type = type,
            Categories = [.. data.Keys],
            Series = [new ChartSeries(seriesName, [.. data.Values])],
        };
    }

    /// <summary>True when the type plots a single series as slices rather than against axes.</summary>
    public bool IsPieFamily => Type is ChartType.Pie or ChartType.Doughnut;

    /// <summary>True when the type plots against two value axes.</summary>
    public bool IsScatter => Type == ChartType.Scatter;

    /// <summary>
    /// Checks the data is renderable and explains what is wrong when it is not.
    /// </summary>
    /// <remarks>
    /// A chart with mismatched series lengths, or with no data at all, produces a part PowerPoint
    /// opens and then reports as needing repair — a failure that surfaces to the user long after
    /// the code that caused it. Refusing up front is far more useful.
    /// </remarks>
    public void Validate()
    {
        if (Series.Count == 0)
        {
            throw new OfficeNetException("A chart needs at least one series.");
        }

        if (IsScatter)
        {
            foreach (var series in Series)
            {
                if (series.XValues is null || series.XValues.Count != series.Values.Count)
                {
                    throw new OfficeNetException(
                        $"Scatter series '{series.Name}' needs XValues of the same length as Values.");
                }
            }

            return;
        }

        if (Categories.Count == 0)
        {
            throw new OfficeNetException(
                "A chart needs category labels. Use ChartData.Simple or set Categories.");
        }

        foreach (var series in Series)
        {
            if (series.Values.Count != Categories.Count)
            {
                throw new OfficeNetException(
                    $"Series '{series.Name}' has {series.Values.Count} values but there are " +
                    $"{Categories.Count} categories; every series must cover every category.");
            }
        }

        if (IsPieFamily && Series.Count > 1)
        {
            throw new OfficeNetException(
                $"A {Type} chart plots one series; this one has {Series.Count}. " +
                "Use a doughnut with one series per ring, or a column chart.");
        }
    }

    /// <summary>The default accent colours, matching the theme a new presentation ships with.</summary>
    /// <summary>The default series colours, matching Office's own accent order.</summary>
    public static readonly OfficeColor[] DefaultPalette =
    [
        OfficeColor.FromRgb(0x2E, 0x54, 0x96),
        OfficeColor.FromRgb(0xC5, 0x5A, 0x11),
        OfficeColor.FromRgb(0x54, 0x82, 0x35),
        OfficeColor.FromRgb(0xBF, 0x90, 0x00),
        OfficeColor.FromRgb(0x70, 0x30, 0xA0),
        OfficeColor.FromRgb(0xC0, 0x00, 0x00),
    ];

    /// <summary>The colour for a series: its own if set, otherwise the palette.</summary>
    public OfficeColor ColorFor(int index) =>
        Series.Count > index && Series[index].Color is { } explicitColor
            ? explicitColor
            : DefaultPalette[index % DefaultPalette.Length];
}
