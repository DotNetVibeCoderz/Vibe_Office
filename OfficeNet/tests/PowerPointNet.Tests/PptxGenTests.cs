// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Html;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using OfficeNet.Core;
using OfficeNet.TestKit;
using PowerPointNet.Charts;
using PowerPointNet.Html;
using PowerPointNet.Media;
using PowerPointNet.Shapes;
using PowerPointNet;
using System.Xml.Linq;
using Xunit;

namespace PowerPointNet.Tests;

public class HtmlParserTests
{
    [Fact]
    public void ParsesElementsAttributesAndText()
    {
        var root = HtmlParser.Parse("""<p class="lead" data-x=7>Halo <b>dunia</b></p>""");

        var paragraph = Assert.Single(root.Elements("p"));
        Assert.Equal("lead", paragraph.Attribute("class"));
        Assert.Equal("7", paragraph.Attribute("data-x"));
        Assert.Equal("Halo dunia", paragraph.InnerText);
    }

    [Fact]
    public void ImplicitClosesMatchWhatABrowserDoes()
    {
        // "<li>a<li>b" is two items, not a nested one. Without the implicit close a converted list
        // comes out indented one level deeper on every item.
        var root = HtmlParser.Parse("<ul><li>a<li>b<li>c</ul>");
        var list = Assert.Single(root.Elements("ul"));

        Assert.Equal(3, list.Elements("li").Count());
        Assert.Equal(["a", "b", "c"], list.Elements("li").Select(li => li.InnerText));
    }

    [Fact]
    public void ABlockElementClosesAnOpenParagraph()
    {
        var root = HtmlParser.Parse("<p>satu<p>dua<ul><li>x</li></ul>");

        Assert.Equal(2, root.Elements("p").Count());
        Assert.Single(root.Elements("ul"));
    }

    [Fact]
    public void VoidElementsNeedNoClosingTag()
    {
        var root = HtmlParser.Parse("<div><img src=\"a.png\"><br>teks</div>");
        var div = Assert.Single(root.Elements("div"));

        Assert.Single(div.Elements("img"));
        Assert.Single(div.Elements("br"));
        Assert.Equal("\nteks", div.InnerText);
    }

    [Theory]
    [InlineData("a &amp; b", "a & b")]
    [InlineData("&lt;tag&gt;", "<tag>")]
    [InlineData("&mdash;", "—")]
    [InlineData("&#8212;", "—")]
    [InlineData("&#x2014;", "—")]
    [InlineData("R&D", "R&D")]
    [InlineData("&notareference;", "&notareference;")]
    public void DecodesCharacterReferencesAndLeavesBareAmpersands(string html, string expected) =>
        // A bare ampersand is legal in HTML text; treating every '&' as a reference eats it.
        Assert.Equal(expected, HtmlParser.Parse(html).InnerText);

    [Fact]
    public void RawTextElementsAreNotParsedAsMarkup()
    {
        // A '<' inside <style> is a CSS child combinator, not a tag.
        var root = HtmlParser.Parse("<style>div > p { color: red }</style><p>isi</p>");

        Assert.Single(root.Elements("style"));
        Assert.Equal("isi", Assert.Single(root.Elements("p")).InnerText);
    }

    [Fact]
    public void CommentsAndDoctypesAreSkipped()
    {
        var root = HtmlParser.Parse("<!DOCTYPE html><!-- catatan --><p>isi</p>");

        Assert.Equal("isi", Assert.Single(root.Elements("p")).InnerText);
    }

    [Fact]
    public void AStrayCloseTagDoesNotDestroyTheTree()
    {
        var root = HtmlParser.Parse("<div>satu</span>dua</div>");

        Assert.Equal("satudua", Assert.Single(root.Elements("div")).InnerText);
    }

    [Fact]
    public void AnUnclosedTagStillYieldsItsContent()
    {
        var root = HtmlParser.Parse("<div><p>isi");

        Assert.Equal("isi", root.Descendants("p").Single().InnerText);
    }
}

public class CssStyleTests
{
    [Theory]
    [InlineData("color: #C00000", 0xC0, 0x00, 0x00)]
    [InlineData("color: rgb(192, 0, 0)", 0xC0, 0x00, 0x00)]
    [InlineData("color: red", 0xFF, 0x00, 0x00)]
    [InlineData("color: rgb(100%, 0%, 0%)", 0xFF, 0x00, 0x00)]
    public void ParsesColourForms(string declaration, byte r, byte g, byte b)
    {
        var color = CssStyle.Parse(declaration).Color;

        Assert.NotNull(color);
        Assert.Equal(OfficeColor.FromRgb(r, g, b), color!.Value);
    }

    [Theory]
    [InlineData("20px", 15)]
    [InlineData("12pt", 12)]
    [InlineData("1in", 72)]
    [InlineData("1em", 12)]
    public void ParsesLengthUnits(string value, double expectedPoints)
    {
        var length = CssStyle.ParseLength(value);

        Assert.NotNull(length);
        Assert.Equal(expectedPoints, length!.Value.Points, 3);
    }

    [Fact]
    public void MergeLetsTheNestedStyleWin()
    {
        var outer = CssStyle.Parse("color: red; font-weight: bold");
        var inner = CssStyle.Parse("color: blue");

        var merged = outer.Merge(inner);

        Assert.Equal(OfficeColor.FromRgb(0, 0, 255), merged.Color);
        // Bold is inherited because the inner style says nothing about it.
        Assert.True(merged.Bold);
    }

    [Theory]
    [InlineData("b")]
    [InlineData("strong")]
    public void PresentationalTagsImplyFormatting(string tag) =>
        Assert.True(CssStyle.ForTag(tag).Bold);

    [Fact]
    public void NumericFontWeightsMapToBold()
    {
        Assert.True(CssStyle.Parse("font-weight: 700").Bold);
        Assert.False(CssStyle.Parse("font-weight: 400").Bold);
    }
}

public class HtmlToSlidesTests
{
    private const string Sample = """
        <h1>Judul Dokumen</h1>
        <p>Paragraf dengan <strong>tebal</strong> dan <em>miring</em>.</p>
        <h2>Daftar</h2>
        <ul><li>Satu<ul><li>Bersarang</li></ul></li><li>Dua</li></ul>
        <h2>Tabel</h2>
        <table>
          <tr><th>A</th><th>B</th></tr>
          <tr><td>1</td><td>2</td></tr>
        </table>
        """;

    [Fact]
    public void HeadingsBecomeSlideTitles()
    {
        using var presentation = HtmlToSlides.CreatePresentation(Sample);

        var titles = presentation.Slides.Select(s => s.Title?.Text).ToList();

        Assert.Contains("Judul Dokumen", titles);
        Assert.Contains("Daftar", titles);
        Assert.Contains("Tabel", titles);

        PptxValidator.AssertValid(presentation.ToArray());
    }

    [Fact]
    public void NestedListsKeepTheirIndentLevel()
    {
        using var presentation = HtmlToSlides.CreatePresentation(Sample);

        var slide = presentation.Slides.First(s => s.Title?.Text == "Daftar");
        var paragraphs = slide.Body!.TextFrame!.Paragraphs;

        Assert.Equal(["Satu", "Bersarang", "Dua"], paragraphs.Select(p => p.Text));
        Assert.Equal([0, 1, 0], paragraphs.Select(p => p.Level));
        Assert.All(paragraphs, p => Assert.True(p.HasBullet));
    }

    [Fact]
    public void InlineFormattingSurvivesRunByRun()
    {
        using var presentation = HtmlToSlides.CreatePresentation(
            "<h1>T</h1><p>biasa <b>tebal</b> <i>miring</i> <u>garis</u></p>");

        var runs = presentation.Slides.First(s => s.Body is not null)
            .Body!.TextFrame!.Paragraphs[0].Runs;

        Assert.Equal("biasa ", runs[0].Text);
        Assert.Equal("tebal", runs[1].Text);
        Assert.True(runs[1].Bold);
        Assert.Equal("miring", runs[3].Text);
        Assert.True(runs[3].Italic);
        Assert.Equal("garis", runs[5].Text);
        Assert.True(runs[5].Underline);
    }

    [Fact]
    public void InlineStyleSetsColourAndSize()
    {
        using var presentation = HtmlToSlides.CreatePresentation(
            """<h1>T</h1><p style="color:#C00000;font-size:20px">berwarna</p>""");

        var run = presentation.Slides.First(s => s.Body is not null)
            .Body!.TextFrame!.Paragraphs[0].Runs[0];

        Assert.Equal(OfficeColor.FromRgb(0xC0, 0, 0), run.Color);
        // 20 CSS pixels at 96 DPI is 15 points.
        Assert.Equal(15, run.FontSize!.Value.Points, 3);
    }

    [Fact]
    public void LinksBecomeHyperlinkRelationships()
    {
        using var presentation = HtmlToSlides.CreatePresentation(
            """<h1>T</h1><p>lihat <a href="https://gravicode.com">situs</a></p>""");

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var slide = reopened.Slides.First(s => s.Body is not null);

        var external = slide.Part.RelationshipsByType(RelationshipTypes.Hyperlink)
            .Where(r => r.TargetMode == TargetMode.External)
            .ToList();

        Assert.Single(external);
        Assert.Equal("https://gravicode.com", external[0].Target);
    }

    [Fact]
    public void AnInPageAnchorIsNotWrittenAsARelationship()
    {
        // A "#section" link points at nothing in a deck; writing it produces a relationship whose
        // target does not exist.
        using var presentation = HtmlToSlides.CreatePresentation(
            """<h1>T</h1><p>lihat <a href="#bagian">bawah</a></p>""");

        PptxValidator.AssertValid(presentation.ToArray());

        var slide = presentation.Slides.First(s => s.Body is not null);
        Assert.Empty(slide.Part.RelationshipsByType(RelationshipTypes.Hyperlink));
    }

    [Fact]
    public void TablesBecomeSlideTables()
    {
        using var presentation = HtmlToSlides.CreatePresentation(Sample);

        var slide = presentation.Slides.First(s => s.Tables.Any());
        var table = slide.Tables.First();

        Assert.Equal(2, table.Count);
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal("A", table[0, 0].Text);
        Assert.Equal("2", table[1, 1].Text);
        Assert.True(table.HasHeaderRow);
    }

    [Fact]
    public void ARaggedTableIsSquaredOff()
    {
        // A row with fewer cells would shift every column after the gap.
        using var presentation = HtmlToSlides.CreatePresentation(
            "<table><tr><td>a</td><td>b</td><td>c</td></tr><tr><td>d</td></tr></table>");

        var table = presentation.Slides.First(s => s.Tables.Any()).Tables.First();

        Assert.Equal(3, table.ColumnCount);
        Assert.Equal("d", table[1, 0].Text);
        Assert.Equal(string.Empty, table[1, 2].Text);
    }

    [Fact]
    public void SplittingBeforeATableLeavesNoBlankSlide()
    {
        // Flushing before and after a table carries the heading twice; emitting the carried copy
        // produces a slide with a title and nothing else.
        using var presentation = HtmlToSlides.CreatePresentation(Sample);

        var empty = presentation.Slides
            .Where(s => s.Tables.Any() == false && s.Pictures.Any() == false)
            .Where(s => s.Body is null || s.Body.Text.Trim().Length == 0)
            .Where(s => s.Title is not null)
            .ToList();

        Assert.Empty(empty);
    }

    [Fact]
    public void WhitespaceIsCollapsedTheWayHtmlRenderingDoes()
    {
        // Source HTML is indented; copying its text nodes verbatim fills slides with stray gaps.
        using var presentation = HtmlToSlides.CreatePresentation(
            "<h1>T</h1><p>\n    satu     dua\n    tiga\n</p>");

        var text = presentation.Slides.First(s => s.Body is not null).Body!.Text;

        Assert.Equal("satu dua tiga", text);
    }

    [Fact]
    public void AnEmbeddedDataUriImageBecomesAPicture()
    {
        var png = TestImages.SolidPng(64, 32, 0x1F, 0x38, 0x64);
        var uri = "data:image/png;base64," + Convert.ToBase64String(png);

        using var presentation = HtmlToSlides.CreatePresentation(
            $"<h1>Gambar</h1><img src=\"{uri}\" alt=\"logo\">");

        var slide = presentation.Slides.First(s => s.Pictures.Any());
        var picture = slide.Pictures.First();

        Assert.Equal("logo", picture.AltText);
        Assert.NotNull(picture.GetImageBytes());
        PptxValidator.AssertValid(presentation.ToArray());
    }

    [Fact]
    public void ARemoteImageIsNotFetchedWithoutAResolver()
    {
        // Converting a document must not silently make network requests.
        using var presentation = HtmlToSlides.CreatePresentation(
            "<h1>T</h1><img src=\"https://example.invalid/logo.png\">");

        Assert.All(presentation.Slides, s => Assert.Empty(s.Pictures));
    }

    [Fact]
    public void AResolverSuppliesRemoteImages()
    {
        var png = TestImages.SolidPng(32, 32, 1, 2, 3);
        var asked = new List<string>();

        using var presentation = HtmlToSlides.CreatePresentation(
            "<h1>T</h1><img src=\"https://example.invalid/logo.png\">",
            new HtmlSlideOptions
            {
                ImageResolver = url =>
                {
                    asked.Add(url);
                    return png;
                },
            });

        Assert.Equal(["https://example.invalid/logo.png"], asked);
        Assert.Contains(presentation.Slides, s => s.Pictures.Any());
    }

    [Fact]
    public void LongContentPaginatesOntoContinuationSlides()
    {
        var items = string.Concat(Enumerable.Range(1, 30).Select(i => $"<li>Butir {i}</li>"));

        using var presentation = HtmlToSlides.CreatePresentation(
            $"<h1>Panjang</h1><ul>{items}</ul>", new HtmlSlideOptions { MaxLinesPerSlide = 6 });

        Assert.True(presentation.SlideCount >= 5,
            $"30 bullets at 6 per slide should paginate, got {presentation.SlideCount} slides");

        // Every continuation keeps the heading so the reader knows where they are.
        Assert.All(presentation.Slides, s => Assert.Equal("Panjang", s.Title?.Text));
    }

    [Fact]
    public void AFragmentWithNoContentStillProducesASlide()
    {
        // A presentation with zero slides is a file PowerPoint cannot open.
        using var presentation = HtmlToSlides.CreatePresentation("<!-- kosong -->");

        Assert.Equal(1, presentation.SlideCount);
        PptxValidator.AssertValid(presentation.ToArray());
    }

    [Fact]
    public void TableToSlidesPaginatesAndRepeatsTheHeader()
    {
        var rows = string.Concat(Enumerable.Range(1, 25)
            .Select(i => $"<tr><td>{i}</td><td>Nama {i}</td></tr>"));

        using var presentation = Presentation.Create();

        var slides = HtmlToSlides.TableToSlides(presentation,
            $"<table><tr><th>No</th><th>Nama</th></tr>{rows}</table>", "Peserta");

        Assert.True(slides.Count >= 3, $"25 rows should span several slides, got {slides.Count}");

        // The header repeats on every page, or the continuation pages are unreadable.
        foreach (var slide in slides)
        {
            var table = slide.Tables.First();
            Assert.Equal("No", table[0, 0].Text);
            Assert.True(table.HasHeaderRow);
        }

        // Only the first page carries the plain title; the rest say which page they are.
        Assert.Equal("Peserta", slides[0].Title!.Text);
        Assert.Equal("Peserta (2)", slides[1].Title!.Text);

        PptxValidator.AssertValid(presentation.ToArray());
    }

    [Fact]
    public void SourceNotesRecordTheHeadingPath()
    {
        using var presentation = HtmlToSlides.CreatePresentation(Sample,
            new HtmlSlideOptions { AddSourceNotes = true });

        var slide = presentation.Slides.First(s => s.Title?.Text == "Daftar");

        Assert.Contains("Judul Dokumen", slide.Notes, StringComparison.Ordinal);
        Assert.Contains("Daftar", slide.Notes, StringComparison.Ordinal);
    }
}

public class ChartTests
{
    private static readonly XNamespace C = Ns.C;

    [Fact]
    public void AColumnChartRoundTrips()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(2);
        slide.SetTitle("Penjualan");

        slide.AddChart(new ChartData
        {
            Type = ChartType.Column,
            Categories = ["Jan", "Feb", "Mar"],
            Series =
            [
                new ChartSeries("2025", [10, 20, 30]),
                new ChartSeries("2026", [15, 25, 35]),
            ],
            Title = "Per bulan",
        });

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var chart = Assert.Single(reopened[0].Charts);

        var series = chart.ChartSpace.Descendants(C + "ser").ToList();
        Assert.Equal(2, series.Count);

        // The cache is what renders; a chart with only the formula shows nothing.
        var values = series[0].Element(C + "val")!.Element(C + "numRef")!
            .Element(C + "numCache")!.Elements(C + "pt")
            .Select(p => double.Parse(p.Element(C + "v")!.Value))
            .ToList();

        Assert.Equal([10, 20, 30], values);
    }

    [Fact]
    public void AxisIdsMatchBetweenTheGroupAndTheAxes()
    {
        // A group naming an axis id that no axis declares makes PowerPoint drop the plot.
        using var presentation = Presentation.Create();

        presentation.AddSlide(2).AddChart(
            ChartData.Simple(ChartType.Line, "S", ["a", "b"], [1, 2]));

        var chart = presentation[0].Charts.First();
        var plot = chart.ChartSpace.Descendants(C + "plotArea").Single();

        var declared = plot.Elements()
            .Where(e => e.Name == C + "catAx" || e.Name == C + "valAx")
            .Select(e => e.Element(C + "axId")!.Attribute("val")!.Value)
            .ToHashSet();

        var referenced = plot.Elements()
            .Where(e => e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal))
            .SelectMany(e => e.Elements(C + "axId"))
            .Select(e => e.Attribute("val")!.Value);

        Assert.All(referenced, id => Assert.Contains(id, declared));
    }

    [Fact]
    public void APieChartHasNoAxes()
    {
        // Empty axes on a pie make PowerPoint repair the part.
        using var presentation = Presentation.Create();

        presentation.AddSlide(2).AddChart(
            ChartData.FromMap(ChartType.Pie, "Bagian", new Dictionary<string, double>
            {
                ["A"] = 1, ["B"] = 2,
            }));

        var plot = presentation[0].Charts.First().ChartSpace.Descendants(C + "plotArea").Single();

        Assert.Empty(plot.Elements(C + "catAx"));
        Assert.Empty(plot.Elements(C + "valAx"));
        Assert.Single(plot.Elements(C + "pieChart"));
    }

    [Fact]
    public void EachPieSliceGetsItsOwnColour()
    {
        // A pie is one series; without per-point colours the whole pie is one hue.
        using var presentation = Presentation.Create();

        presentation.AddSlide(2).AddChart(
            ChartData.FromMap(ChartType.Pie, "Bagian", new Dictionary<string, double>
            {
                ["A"] = 1, ["B"] = 2, ["C"] = 3,
            }));

        var series = presentation[0].Charts.First().ChartSpace.Descendants(C + "ser").Single();

        Assert.Equal(3, series.Elements(C + "dPt").Count());
    }

    [Fact]
    public void AStackedBarChartSetsOverlap()
    {
        // Without overlap 100 a stacked chart renders its segments side by side.
        using var presentation = Presentation.Create();

        presentation.AddSlide(2).AddChart(new ChartData
        {
            Type = ChartType.ColumnStacked,
            Categories = ["a", "b"],
            Series = [new ChartSeries("s1", [1, 2]), new ChartSeries("s2", [3, 4])],
        });

        var group = presentation[0].Charts.First().ChartSpace
            .Descendants(C + "barChart").Single();

        Assert.Equal("100", group.Element(C + "overlap")!.Attribute("val")!.Value);
        Assert.Equal("stacked", group.Element(C + "grouping")!.Attribute("val")!.Value);
    }

    [Fact]
    public void AGapInTheDataIsOmittedRatherThanWrittenAsNaN()
    {
        // Writing the literal "NaN" makes the whole chart fail to load.
        using var presentation = Presentation.Create();

        presentation.AddSlide(2).AddChart(
            ChartData.Simple(ChartType.Line, "S", ["a", "b", "c"], [1, double.NaN, 3]));

        var cache = presentation[0].Charts.First().ChartSpace
            .Descendants(C + "numCache").First();

        var points = cache.Elements(C + "pt").ToList();

        Assert.Equal(2, points.Count);
        Assert.Equal(["0", "2"], points.Select(p => p.Attribute("idx")!.Value));
        Assert.Equal("3", cache.Element(C + "ptCount")!.Attribute("val")!.Value);
    }

    [Fact]
    public void MismatchedSeriesLengthsAreRefusedUpFront()
    {
        using var presentation = Presentation.Create();
        var slide = presentation.AddSlide(2);

        var ex = Assert.Throws<OfficeNetException>(() => slide.AddChart(new ChartData
        {
            Categories = ["a", "b", "c"],
            Series = [new ChartSeries("s", [1, 2])],
        }));

        Assert.Contains("every series must cover every category", ex.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void APieWithSeveralSeriesIsRefused()
    {
        using var presentation = Presentation.Create();
        var slide = presentation.AddSlide(2);

        Assert.Throws<OfficeNetException>(() => slide.AddChart(new ChartData
        {
            Type = ChartType.Pie,
            Categories = ["a"],
            Series = [new ChartSeries("s1", [1]), new ChartSeries("s2", [2])],
        }));
    }

    [Fact]
    public void ScatterSeriesNeedMatchingXValues()
    {
        using var presentation = Presentation.Create();
        var slide = presentation.AddSlide(2);

        Assert.Throws<OfficeNetException>(() => slide.AddChart(new ChartData
        {
            Type = ChartType.Scatter,
            Series = [new ChartSeries("s", [1, 2, 3])],
        }));

        var chart = slide.AddChart(new ChartData
        {
            Type = ChartType.Scatter,
            Series = [new ChartSeries("s", [1, 2, 3]) { XValues = [10, 20, 30] }],
        });

        Assert.Single(chart.ChartSpace.Descendants(C + "scatterChart"));
    }

    [Fact]
    public void SetDataReplacesTheChartInPlace()
    {
        using var presentation = Presentation.Create();

        var chart = presentation.AddSlide(2).AddChart(
            ChartData.Simple(ChartType.Column, "S", ["a"], [1]));

        chart.SetData(ChartData.Simple(ChartType.Bar, "T", ["x", "y"], [5, 6]));

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var reread = reopened[0].Charts.First();

        Assert.Equal("bar",
            reread.ChartSpace.Descendants(C + "barDir").Single().Attribute("val")!.Value);
    }
}

public class MediaTests
{
    // A tiny MP4 header. Enough to exercise packaging; not a playable clip.
    private static readonly byte[] FakeVideo =
        [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2'];

    [Fact]
    public void AnEmbeddedVideoGetsBothRelationshipsItNeeds()
    {
        // a:videoFile is what PowerPoint 2007 follows and p14:media what 2010+ follows; a file
        // with only one plays in some versions and shows a black rectangle in others.
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        slide.AddVideo(FakeVideo, "mp4", Units.Inches(1), Units.Inches(1),
            Units.Inches(6), Units.Inches(3.4));

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var media = Assert.Single(reopened[0].MediaClips);

        Assert.Equal(MediaKind.Video, media.MediaType);

        var video = reopened[0].Part.RelationshipsByType(RelationshipTypes.Video).Single();
        var extension = reopened[0].Part.RelationshipsByType(RelationshipTypes.Media).Single();

        Assert.Equal(video.TargetPartName, extension.TargetPartName);
    }

    [Fact]
    public void AVideoCarriesAPosterFrameSoItIsNotABlackRectangle()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        slide.AddVideo(FakeVideo, "mp4", Units.Inches(1), Units.Inches(1),
            Units.Inches(6), Units.Inches(3.4));

        var blip = slide.Shapes.OfType<SlideMedia>().Single()
            .Element.Descendants(Ns.A + "blip").Single();

        var posterId = blip.Attribute(Ns.R + "embed")!.Value;
        Assert.NotNull(slide.Part.RelatedPart(posterId));
    }

    [Fact]
    public void AutoPlayIsWrittenIntoTheSlidesTimingTree()
    {
        // Playback is a timing command, not a shape property; a flag on the picture does nothing.
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        slide.AddVideo(FakeVideo, "mp4", Units.Inches(1), Units.Inches(1),
            Units.Inches(6), Units.Inches(3.4), autoPlay: true);

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);

        Assert.NotNull(reopened[0].Root.Element(Ns.P + "timing"));
        Assert.True(reopened[0].MediaClips.Single().AutoPlay);
    }

    [Fact]
    public void ClickToPlayIsTheDefault()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        slide.AddVideo(FakeVideo, "mp4", Units.Inches(1), Units.Inches(1),
            Units.Inches(6), Units.Inches(3.4));

        Assert.False(slide.MediaClips.Single().AutoPlay);
    }

    [Fact]
    public void AnOnlineVideoIsLinkedNotEmbedded()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        slide.AddOnlineVideo("https://www.youtube.com/embed/abc123",
            Units.Inches(1), Units.Inches(1), Units.Inches(6), Units.Inches(3.4));

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var media = Assert.Single(reopened[0].MediaClips);

        Assert.Equal(MediaKind.OnlineVideo, media.MediaType);

        // Nothing was copied into the package.
        Assert.DoesNotContain(reopened.Package.Parts,
            p => p.Name.Value.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AudioUsesTheAudioRelationshipNotTheVideoOne()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        slide.AddAudio("ID3"u8.ToArray(), "mp3", Units.Inches(1), Units.Inches(1));

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);

        Assert.Equal(MediaKind.Audio, reopened[0].MediaClips.Single().MediaType);
        Assert.Single(reopened[0].Part.RelationshipsByType(RelationshipTypes.Audio));
        Assert.Empty(reopened[0].Part.RelationshipsByType(RelationshipTypes.Video));
    }

    [Fact]
    public void AnUnknownMediaExtensionIsRefusedWithAUsefulMessage()
    {
        using var presentation = Presentation.Create();
        var slide = presentation.AddSlide(3);

        var ex = Assert.Throws<OfficeNetNotSupportedException>(() =>
            slide.AddVideo(FakeVideo, "xyz", Units.Inches(1), Units.Inches(1),
                Units.Inches(4), Units.Inches(3)));

        Assert.Contains("mp4", ex.Message, StringComparison.Ordinal);
    }
}

public class ShapeEffectTests
{
    private static Shape NewShape(Presentation presentation) =>
        presentation.AddSlide(3).AddShape(ShapeGeometry.RoundedRectangle,
            Units.Inches(1), Units.Inches(1), Units.Inches(3), Units.Inches(1));

    [Fact]
    public void AGradientReplacesTheSolidFillRatherThanJoiningIt()
    {
        // Two fills leave PowerPoint picking whichever comes first, which is rarely the new one.
        using var presentation = Presentation.Create();

        var shape = NewShape(presentation)
            .WithGradientFill(OfficeColor.Red, OfficeColor.Blue);

        var properties = shape.Element.Element(Ns.P + "spPr")!;

        Assert.Empty(properties.Elements(Ns.A + "solidFill"));
        Assert.Single(properties.Elements(Ns.A + "gradFill"));
        Assert.Equal(2, properties.Descendants(Ns.A + "gs").Count());

        PptxValidator.AssertValid(presentation.ToArray());
    }

    [Fact]
    public void TransparencyIsWrittenInsideTheColour()
    {
        // An a:alpha anywhere but inside the colour is silently ignored.
        using var presentation = Presentation.Create();

        var shape = NewShape(presentation).WithFill(OfficeColor.Red, 0.5);

        var alpha = shape.Element.Element(Ns.P + "spPr")!
            .Element(Ns.A + "solidFill")!.Element(Ns.A + "srgbClr")!
            .Element(Ns.A + "alpha");

        Assert.NotNull(alpha);
        Assert.Equal("50000", alpha!.Attribute("val")!.Value);
    }

    [Fact]
    public void ShadowsAndGlowsRoundTrip()
    {
        using var presentation = Presentation.Create();

        NewShape(presentation).WithShadow(OfficeColor.Black, Units.Pt(6), Units.Pt(4));
        NewShape(presentation).WithGlow(OfficeColor.FromRgb(0x2E, 0x54, 0x96));

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);

        Assert.Single(reopened[0].Shapes[0].Element.Descendants(Ns.A + "outerShdw"));
        Assert.Single(reopened[1].Shapes[0].Element.Descendants(Ns.A + "glow"));
    }

    [Fact]
    public void ShapePropertiesStayInSchemaOrder()
    {
        using var presentation = Presentation.Create();

        // Applied deliberately out of schema order.
        var shape = NewShape(presentation);
        shape.WithShadow();
        shape.WithOutline(OfficeColor.Black, Units.Pt(2), LineDash.Dash);
        shape.WithGradientFill(OfficeColor.Red, OfficeColor.Blue);

        var properties = shape.Element.Element(Ns.P + "spPr")!;

        Assert.Null(OpcValidator.CheckChildOrder(properties,
            "xfrm", "prstGeom", "gradFill", "ln", "effectLst"));
    }

    [Fact]
    public void AShapeHyperlinkLivesInItsNonVisualProperties()
    {
        // The link has to cover the whole shape, not just its text, for a button to work.
        using var presentation = Presentation.Create();

        var shape = NewShape(presentation).WithHyperlink("https://gravicode.com");

        var link = shape.Element.Element(Ns.P + "nvSpPr")!.Element(Ns.P + "cNvPr")!
            .Element(Ns.A + "hlinkClick");

        Assert.NotNull(link);
        PptxValidator.AssertValid(presentation.ToArray());
    }

    [Fact]
    public void CoverCropsRatherThanDistorting()
    {
        // Stretching a photo to a frame of a different ratio is the most visible way a generated
        // deck looks wrong.
        var wide = TestImages.SolidPng(400, 100, 1, 2, 3);

        using var presentation = Presentation.Create();

        var picture = presentation.AddSlide(3).AddPicture(wide,
            Units.Inches(1), Units.Inches(1), Units.Inches(2), Units.Inches(2));

        picture.Cover(wide);

        var crop = picture.Element.Descendants(Ns.A + "srcRect").Single();

        // A 4:1 image in a 1:1 frame keeps the middle quarter: 37.5% trimmed from each side.
        Assert.Equal(37500, int.Parse(crop.Attribute("l")!.Value));
        Assert.Equal(37500, int.Parse(crop.Attribute("r")!.Value));
        Assert.Null(crop.Attribute("t"));
    }

    [Fact]
    public void APictureCanBeMadeRound()
    {
        var png = TestImages.SolidPng(64, 64, 1, 2, 3);

        using var presentation = Presentation.Create();

        var picture = presentation.AddSlide(3)
            .AddPicture(png, Units.Inches(1), Units.Inches(1))
            .AsEllipse();

        Assert.Equal("ellipse", picture.Element.Element(Ns.P + "spPr")!
            .Element(Ns.A + "prstGeom")!.Attribute("prst")!.Value);

        PptxValidator.AssertValid(presentation.ToArray());
    }
}

public class ChartRoundTripTests
{
    private static ChartData ReadBack(ChartData data)
    {
        // Through a saved file rather than the in-memory object: reading the part back is the whole
        // point, and an in-memory assertion would pass even if nothing were ever serialised.
        using var stream = new MemoryStream();

        using (var presentation = Presentation.Create())
        {
            var slide = presentation.AddSlide(6);
            slide.AddChart(data);
            presentation.Save(stream);
        }

        stream.Position = 0;

        using var reopened = Presentation.Open(stream);
        var chart = reopened.Slides[0].Shapes.OfType<SlideChart>().Single();

        return chart.GetData();
    }

    [Fact]
    public void ColumnChartKeepsItsCategoriesAndSeries()
    {
        var read = ReadBack(new ChartData
        {
            Type = ChartType.Column,
            Categories = ["Jakarta", "Bandung", "Surabaya"],
            Series =
            [
                new ChartSeries("2025", [1120, 860, 740]),
                new ChartSeries("2026", [1480, 1150, 905]),
            ],
        });

        Assert.Equal(ChartType.Column, read.Type);
        Assert.Equal(["Jakarta", "Bandung", "Surabaya"], read.Categories);
        Assert.Equal(2, read.Series.Count);
        Assert.Equal("2026", read.Series[1].Name);
        Assert.Equal([1480, 1150, 905], read.Series[1].Values);
    }

    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.ColumnStacked)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.BarStacked)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.AreaStacked)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Doughnut)]
    [InlineData(ChartType.Radar)]
    public void EveryChartTypeSurvivesTheRoundTrip(ChartType type)
    {
        // Bar and column are the same element distinguished only by c:barDir, and stacked variants
        // only by c:grouping — the pairs are exactly where a reader collapses two types into one.
        var read = ReadBack(new ChartData
        {
            Type = type,
            Categories = ["a", "b"],
            Series = [new ChartSeries("s", [1, 2])],
        });

        Assert.Equal(type, read.Type);
    }

    [Fact]
    public void LabelFlagsAndNumberFormatSurvive()
    {
        // These live on the chart group and the value cache rather than on the series, which is why
        // a reader that only walks c:ser loses them and every exported chart comes out unlabelled.
        var read = ReadBack(new ChartData
        {
            Type = ChartType.Column,
            Categories = ["a", "b"],
            Series = [new ChartSeries("s", [1000, 2000])],
            ShowDataLabels = true,
            ValueFormat = "#,##0",
            ValueAxisTitle = "Juta Rupiah",
            Legend = LegendPosition.Right,
        });

        Assert.True(read.ShowDataLabels);
        Assert.Equal("#,##0", read.ValueFormat);
        Assert.Equal("Juta Rupiah", read.ValueAxisTitle);
        Assert.Equal(LegendPosition.Right, read.Legend);
    }

    [Fact]
    public void SparsePointsAreReadByIndexNotByOrder()
    {
        // c:pt carries an idx and may be sparse. Reading in document order shifts every value after
        // a gap, which turns a missing measurement into wrong data rather than a gap.
        using var presentation = Presentation.Create();
        var slide = presentation.AddSlide(6);

        var chart = slide.AddChart(new ChartData
        {
            Type = ChartType.Line,
            Categories = ["a", "b", "c"],
            Series = [new ChartSeries("s", [1, 2, 3])],
        });

        var points = chart.ChartSpace.Descendants(Ns.C + "numCache").First()
            .Elements(Ns.C + "pt").ToList();

        points[1].Remove();

        var read = chart.GetData();

        Assert.Equal(3, read.Series[0].Values.Count);
        Assert.Equal(1, read.Series[0].Values[0]);
        Assert.True(double.IsNaN(read.Series[0].Values[1]));
        Assert.Equal(3, read.Series[0].Values[2]);
    }
}
