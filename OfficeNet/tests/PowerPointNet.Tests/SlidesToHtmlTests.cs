// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.Core.Charts;
using OfficeNet.TestKit;
using PowerPointNet.Diagrams;
using PowerPointNet.Html;
using Xunit;

namespace PowerPointNet.Tests;

public class SlidesToHtmlTests
{
    private static Presentation Deck()
    {
        var deck = Presentation.Create();
        deck.AddTitleSlide("Laporan Kuartal", "Tim keuangan");

        var bullets = deck.AddBulletSlide("Ringkasan", ["Pendapatan naik", "Biaya turun"]);
        var detail = bullets.Body!.TextFrame!.AddParagraph("rinciannya");
        detail.Level = 1;
        detail.Runs[0].SetHyperlink("https://gravicode.com");
        bullets.Notes = "Sebut angka tahun lalu.";

        var table = deck.AddSlide(2);
        table.SetTitle("Per wilayah");
        var grid = table.AddTable(2, 2, Length.FromCentimeters(2), Length.FromCentimeters(5), Length.FromCentimeters(12), Length.FromCentimeters(3));
        grid[0, 0].Text = "Wilayah";
        grid[0, 1].Text = "Nilai";
        grid[1, 0].Text = "Jakarta";
        grid[1, 1].Text = "1.480";

        var chart = deck.AddSlide(2);
        chart.SetTitle("Tren");
        chart.AddChart(new ChartData
        {
            Type = ChartType.Column,
            Title = "Per kuartal",
            Categories = ["Q2", "Q3"],
            Series = [new ChartSeries("2026", [1120, 1480])],
        });

        var picture = deck.AddSlide(2);
        picture.SetTitle("Foto");
        picture.AddPicture(TestImages.SolidPng(4, 4, 200, 30, 30), Length.FromCentimeters(2), Length.FromCentimeters(5),
            Length.FromCentimeters(4), Length.FromCentimeters(4)).AltText = "Titik merah";

        var diagram = deck.AddSlide(2);
        diagram.SetTitle("Proses");
        diagram.AddSmartArt(DiagramKind.Process, "Rencana", "Kerjakan", "Periksa");

        var hidden = deck.AddBulletSlide("Rahasia", ["jangan tampil"]);
        hidden.IsHidden = true;

        return deck;
    }

    [Fact]
    public void EverySlideBecomesAHeadingAndItsContentInReadingOrder()
    {
        using var deck = Deck();
        var html = SlidesToHtml.Convert(deck, new SlideHtmlOptions { FullDocument = false });

        var headings = System.Text.RegularExpressions.Regex.Matches(html, "<h2>([^<]*)</h2>").Select(m => m.Groups[1].Value);
        Assert.Equal(["Laporan Kuartal", "Ringkasan", "Per wilayah", "Tren", "Foto", "Proses"], headings);
        Assert.Contains("<p>Tim keuangan</p>", html, StringComparison.Ordinal);

        // The title is the heading, not a paragraph as well.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "Per wilayah"));

        Assert.Contains("<h2>Ringkasan</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<li>Pendapatan naik</li>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://gravicode.com\">rinciannya</a>", html, StringComparison.Ordinal);
        Assert.Contains("<th>Wilayah</th>", html, StringComparison.Ordinal);
        Assert.Contains("<td>1.480</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>1480</td>", html, StringComparison.Ordinal);   // the chart's numbers
        Assert.Contains("alt=\"Titik merah\"", html, StringComparison.Ordinal);
        Assert.Contains("<li>Kerjakan</li>", html, StringComparison.Ordinal);
        Assert.Contains("<hr>", html, StringComparison.Ordinal);

        // Hidden slides and notes are left out unless asked for.
        Assert.DoesNotContain("Rahasia", html, StringComparison.Ordinal);
        Assert.DoesNotContain("tahun lalu", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedBulletIsNestedInsideItsParent()
    {
        using var deck = Deck();
        var html = SlidesToHtml.Convert(deck, new SlideHtmlOptions { FullDocument = false });

        var start = html.IndexOf("<li>Biaya turun", StringComparison.Ordinal);
        var nested = html.IndexOf("<ul>", start, StringComparison.Ordinal);
        var close = html.IndexOf("</li>", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && nested > start && nested < close, html);
    }

    [Fact]
    public void NotesAndHiddenSlidesComeWhenAskedFor()
    {
        using var deck = Deck();
        var html = SlidesToHtml.Convert(deck, new SlideHtmlOptions
        {
            FullDocument = false,
            IncludeNotes = true,
            IncludeHiddenSlides = true,
        });

        Assert.Contains("<blockquote>\n<p>Sebut angka tahun lalu.</p>\n</blockquote>", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Rahasia</h2>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlTakenIntoSlidesAndBackKeepsWhatItSaid()
    {
        using var deck = HtmlToSlides.CreatePresentation("""
            <h1>Rencana</h1><ul><li>satu</li><li>dua</li></ul>
            <h1>Angka</h1><table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
            """);

        var html = SlidesToHtml.Convert(deck, new SlideHtmlOptions { FullDocument = false });

        Assert.Contains("<h2>Rencana</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<li>dua</li>", html, StringComparison.Ordinal);
        Assert.Contains("<th>A</th>", html, StringComparison.Ordinal);
        Assert.Contains("<td>2</td>", html, StringComparison.Ordinal);
    }
}
