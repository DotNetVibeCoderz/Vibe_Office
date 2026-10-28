// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Xml;
using Xunit;

namespace OfficeNet.Core.Tests;

public class ComboChartTests
{
    private static ChartData RevenueAndMargin() => new()
    {
        Type = ChartType.Column,
        Title = "Pendapatan dan margin",
        Categories = ["Q1", "Q2", "Q3", "Q4"],
        Series =
        [
            new ChartSeries("Pendapatan", [1120, 1480, 1310, 1720]),
            new ChartSeries("Margin", [0.29, 0.36, 0.31, 0.36]) { Type = ChartType.LineMarkers, SecondaryAxis = true },
            new ChartSeries("Biaya", [800, 950, 900, 1100]),
        ],
        ValueFormat = "#,##0",
        ValueAxisTitle = "Juta Rp",
        SecondaryAxisTitle = "Margin",
        SecondaryValueFormat = "0%",
    };

    [Fact]
    public void ACombinationIsOneGroupPerTypeAndAxisWithASecondPairOfAxes()
    {
        var plot = ChartXml.Build(RevenueAndMargin()).Root!.Element(Ns.C + "chart")!.Element(Ns.C + "plotArea")!;

        var groups = plot.Elements().Where(e => e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal)).ToList();
        Assert.Equal(["barChart", "lineChart"], groups.Select(g => g.Name.LocalName));

        // Each series keeps its place in the whole chart across groups.
        Assert.Equal(["0", "2"], groups[0].Elements(Ns.C + "ser").Select(s => s.Element(Ns.C + "order")!.Attr("val")));
        Assert.Equal(["1"], groups[1].Elements(Ns.C + "ser").Select(s => s.Element(Ns.C + "order")!.Attr("val")));

        // Axes in schema order after the groups: the primary pair, then the secondary pair.
        var axes = plot.Elements().Where(e => e.Name.LocalName.EndsWith("Ax", StringComparison.Ordinal)).ToList();
        Assert.Equal(["catAx", "valAx", "catAx", "valAx"], axes.Select(a => a.Name.LocalName));

        var secondaryCategory = axes[2];
        var secondaryValue = axes[3];
        Assert.Equal("1", secondaryCategory.Element(Ns.C + "delete")!.Attr("val"));
        Assert.Equal("r", secondaryValue.Element(Ns.C + "axPos")!.Attr("val"));
        Assert.Equal("max", secondaryValue.Element(Ns.C + "crosses")!.Attr("val"));
        Assert.Equal("0%", secondaryValue.Element(Ns.C + "numFmt")!.Attr("formatCode"));
        Assert.Null(secondaryValue.Element(Ns.C + "majorGridlines"));

        // The line group points at the secondary axes, each of which crosses the other.
        var lineAxes = groups[1].Elements(Ns.C + "axId").Select(a => a.Attr("val")).ToList();
        Assert.Equal([secondaryCategory.Element(Ns.C + "axId")!.Attr("val"), secondaryValue.Element(Ns.C + "axId")!.Attr("val")], lineAxes);
        Assert.Equal(secondaryValue.Element(Ns.C + "axId")!.Attr("val"), secondaryCategory.Element(Ns.C + "crossAx")!.Attr("val"));
    }

    [Fact]
    public void ACombinationReadsBackSeriesBySeriesInItsOrder()
    {
        var back = ChartXml.Read(ChartXml.Build(RevenueAndMargin()));

        Assert.Equal(ChartType.Column, back.Type);
        Assert.Equal(["Pendapatan", "Margin", "Biaya"], back.Series.Select(s => s.Name));
        Assert.Equal([null, ChartType.LineMarkers, null], back.Series.Select(s => s.Type));
        Assert.Equal([false, true, false], back.Series.Select(s => s.SecondaryAxis));
        Assert.Equal("Juta Rp", back.ValueAxisTitle);
        Assert.Equal("Margin", back.SecondaryAxisTitle);
        Assert.Equal("0%", back.SecondaryValueFormat);
        Assert.Equal("#,##0", back.ValueFormat);
        Assert.True(back.IsCombo);
    }

    [Fact]
    public void ALineChartWithMarkersIsOneTypeAndNotACombination()
    {
        var data = new ChartData
        {
            Type = ChartType.LineMarkers,
            Categories = ["a", "b"],
            Series = [new ChartSeries("x", [1, 2]), new ChartSeries("y", [3, 4])],
        };

        var back = ChartXml.Read(ChartXml.Build(data));

        Assert.Equal(ChartType.LineMarkers, back.Type);
        Assert.All(back.Series, s => Assert.Null(s.Type));
        Assert.False(back.IsCombo);
    }

    [Fact]
    public void ASecondaryAxisAloneIsACombinationOfOneType()
    {
        var data = new ChartData
        {
            Type = ChartType.Line,
            Categories = ["a", "b"],
            Series = [new ChartSeries("Jumlah", [100, 200]), new ChartSeries("Persen", [0.1, 0.2]) { SecondaryAxis = true }],
        };

        var plot = ChartXml.Build(data).Root!.Descendants(Ns.C + "plotArea").Single();

        Assert.Equal(2, plot.Elements(Ns.C + "lineChart").Count());
        Assert.Equal([false, true], ChartXml.Read(ChartXml.Build(data)).Series.Select(s => s.SecondaryAxis));
    }

    [Theory]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bar)]
    public void OnlyColumnsLinesAndAreasCombine(ChartType odd)
    {
        var data = new ChartData
        {
            Type = ChartType.Column,
            Categories = ["a", "b"],
            Series = [new ChartSeries("x", [1, 2]), new ChartSeries("y", [3, 4]) { Type = odd, XValues = [1, 2] }],
        };

        var error = Assert.Throws<OfficeNetException>(data.Validate);
        Assert.Contains(odd.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleTypeChartIsWrittenAsBefore()
    {
        var data = new ChartData { Type = ChartType.Column, Categories = ["a"], Series = [new ChartSeries("x", [1])] };
        var plot = ChartXml.Build(data).Root!.Descendants(Ns.C + "plotArea").Single();

        Assert.Single(plot.Elements(Ns.C + "barChart"));
        Assert.Equal(["catAx", "valAx"], plot.Elements().Where(e => e.Name.LocalName.EndsWith("Ax", StringComparison.Ordinal)).Select(e => e.Name.LocalName));
    }
}
