// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.RegularExpressions;
using OfficeNet.Core.Charts;
using PdfNet.Content;
using PdfNet.Document;
using Xunit;

namespace PdfNet.Tests;

public class ComboChartRenderTests
{
    private static string Render(ChartData data)
    {
        using var pdf = PdfDocument.Create();

        using (var canvas = pdf.Pages.Add(PageSize.A4.Landscape()).OpenCanvas())
        {
            canvas.TopDown = true;
            ChartRenderer.Render(canvas, data, 40, 40, 700, 400);
        }

        using var reopened = PdfDocument.Open(pdf.ToArray());
        return reopened.Pages[0].ExtractText();
    }

    [Fact]
    public void BothAxesAreLabelledAndTheCategoriesOnce()
    {
        var text = Render(new ChartData
        {
            Type = ChartType.Column,
            Categories = ["Q1", "Q2", "Q3", "Q4"],
            Series =
            [
                new ChartSeries("Pendapatan", [1120, 1480, 1310, 1720]),
                new ChartSeries("Margin", [0.29, 0.36, 0.31, 0.36]) { Type = ChartType.LineMarkers, SecondaryAxis = true },
            ],
            ValueFormat = "#,##0",
            ValueAxisTitle = "Juta Rp",
            SecondaryAxisTitle = "Persen",
            SecondaryValueFormat = "0%",
        });

        // The primary scale runs to 2,000; the secondary, on its own scale, to 40%.
        Assert.Contains("2,000", text, StringComparison.Ordinal);
        Assert.Contains("40%", text, StringComparison.Ordinal);
        Assert.Contains("Juta Rp", text, StringComparison.Ordinal);
        Assert.Contains("Persen", text, StringComparison.Ordinal);

        // Two groups draw over one set of slots, and only the first labels them.
        Assert.Single(Regex.Matches(text, @"\bQ3\b"));
    }

    [Fact]
    public void ASecondarySeriesIsPlacedOnItsOwnScale()
    {
        // A 29% margin beside sums in the thousands: on its own 0-40% axis it sits most of the way
        // up; on the primary axis it would lie on the floor.
        var margin = OfficeNet.Core.Drawing.OfficeColor.FromRgb(0x00, 0x80, 0x40);

        using var pdf = PdfDocument.Create();

        using (var canvas = pdf.Pages.Add(PageSize.A4.Landscape()).OpenCanvas())
        {
            canvas.TopDown = true;
            ChartRenderer.Render(canvas, new ChartData
            {
                Type = ChartType.Column,
                Categories = ["Q1", "Q2"],
                Series =
                [
                    new ChartSeries("Pendapatan", [1120, 1480]),
                    new ChartSeries("Margin", [0.29, 0.29]) { Type = ChartType.LineMarkers, SecondaryAxis = true, Color = margin },
                ],
                ShowGridLines = false,
            }, 40, 40, 700, 400);
        }

        var content = string.Concat(pdf.Pages[0].Dictionary.GetArray(Objects.PdfName.Contents)
            .Select(pdf.Follow).OfType<Objects.PdfStream>().Select(s => s.DecodedText));

        // The markers: 5-point squares in the margin's green.
        var marker = Regex.Match(content, @"0 0\.50\d* 0\.25\d* rg\s+(\S+) (\S+) 5 5 re");
        Assert.True(marker.Success, "no marker in the margin's colour");

        var y = double.Parse(marker.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var plotBottom = PageSize.A4.Landscape().Height - 440;

        // Above the middle of the 400-point chart, in PDF's upward coordinates.
        Assert.True(y > plotBottom + 200, $"the marker is at {y}, near the bottom of the plot");
    }

    [Fact]
    public void WithoutASecondaryAxisBothTypesShareOneScale()
    {
        var text = Render(new ChartData
        {
            Type = ChartType.Column,
            Categories = ["a", "b"],
            Series =
            [
                new ChartSeries("Batang", [10, 20]),
                new ChartSeries("Garis", [30, 45]) { Type = ChartType.Line },
            ],
        });

        // One scale over both: the line's 45 sets the top, and nothing is drawn on the right.
        Assert.Contains("50", text, StringComparison.Ordinal);
        Assert.DoesNotContain("%", text, StringComparison.Ordinal);
    }
}
