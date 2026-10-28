// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Xml.Linq;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Xml;
using Xunit;

namespace WordNet.Tests;

public class WordChartTests
{
    private static readonly ChartData Revenue = new()
    {
        Type = ChartType.Column,
        Title = "Pendapatan per Wilayah",
        Categories = ["Jakarta", "Bandung", "Surabaya"],
        Series = [new ChartSeries("2026", [1480, 1150, 905])],
        ShowDataLabels = true,
    };

    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void AChartIsAPartAndAnInlineDrawingPointingAtIt()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Ringkasan");
        document.AddChart(Revenue);

        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        using var archive = new ZipArchive(new MemoryStream(bytes));
        Assert.NotNull(archive.GetEntry("word/charts/chart1.xml"));

        var types = XDocument.Load(archive.GetEntry("[Content_Types].xml")!.Open()).Root!;
        Assert.Contains(types.Elements(), e => e.Attribute("PartName")?.Value == "/word/charts/chart1.xml" &&
                                               e.Attribute("ContentType")!.Value.EndsWith("chart+xml", StringComparison.Ordinal));

        var body = XDocument.Load(archive.GetEntry("word/document.xml")!.Open()).Root!;
        var graphicData = body.Descendants(Ns.A + "graphicData").Single();
        Assert.Equal("http://schemas.openxmlformats.org/drawingml/2006/chart", graphicData.Attr("uri"));
        Assert.NotNull(body.Descendants(Ns.Wp + "inline").Single().Element(Ns.Wp + "docPr"));
    }

    [Fact]
    public void TheChartsDataReadsBackAfterSaving()
    {
        using var document = WordDocument.Create();
        document.AddChart(Revenue);

        using var reopened = WordDocument.Open(Save(document));
        var data = reopened.Charts.Single().GetData();

        Assert.Equal(["Jakarta", "Bandung", "Surabaya"], data.Categories);
        Assert.Equal([1480.0, 1150, 905], data.Series[0].Values);
        Assert.Equal("Pendapatan per Wilayah", data.Title);
    }

    [Fact]
    public void ThePdfExportDrawsTheChart()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Sebelum chart");
        document.AddChart(Revenue);
        document.AddParagraph("Sesudah chart");

        using var pdf = document.ToPdf();
        var text = pdf.ExtractText();

        Assert.Contains("Pendapatan per Wilayah", text);
        Assert.Contains("Surabaya", text);
        Assert.Contains("Sesudah chart", text);
    }

    [Fact]
    public void SetDataReplacesTheChart()
    {
        using var document = WordDocument.Create();
        var chart = document.AddChart(Revenue);
        chart.SetData(Revenue with { Title = "Diperbarui" });

        Assert.Equal("Diperbarui", document.Charts.Single().GetData().Title);
    }

    [Fact]
    public void AChartComesAlongWhenDocumentsAreMerged()
    {
        using var target = WordDocument.Create();
        target.AddChart(Revenue);
        using var source = WordDocument.Create();
        source.AddChart(Revenue with { Title = "Lampiran" });

        target.AppendDocument(source);

        var bytes = Save(target);
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);
        Assert.Equal(["Pendapatan per Wilayah", "Lampiran"], reopened.Charts.Select(c => c.GetData().Title));
    }
}
