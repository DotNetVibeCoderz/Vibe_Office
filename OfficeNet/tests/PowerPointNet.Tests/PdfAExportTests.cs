// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.Core.Charts;
using OfficeNet.TestKit;
using PdfNet.Document;
using PdfNet.Objects;
using PowerPointNet.Export;
using Xunit;

namespace PowerPointNet.Tests;

public class PdfAExportTests
{
    private static readonly byte[] Font = SyntheticFont.Build(SyntheticFont.DefaultCharacters + "-%•");

    private static Presentation Deck()
    {
        var deck = Presentation.Create();
        deck.AddTitleSlide("Laporan", "Tim keuangan");
        deck.AddBulletSlide("Ringkasan", ["Pendapatan naik", "Biaya turun"]).Notes = "Catatan pembicara.";
        deck.AddSlide(2).AddChart(new ChartData
        {
            Type = ChartType.Pie,
            Title = "Porsi",
            Categories = ["A", "B"],
            Series = [new ChartSeries("x", [60, 40])],
        });
        return deck;
    }

    private static SlidePdfOptions Archival() => new()
    {
        Conformance = PdfConformance.PdfA2B,
        FontResolver = (_, _, _) => Font,
        ShowSlideNumbers = true,
        IncludeNotes = true,
        Watermark = "DRAF",
    };

    [Fact]
    public void AnArchivalExportEmbedsEveryFontOnEverySlide()
    {
        using var deck = Deck();
        using var pdf = deck.ToPdf(Archival());

        using var reopened = PdfDocument.Open(pdf.ToArray());
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", reopened.XmpMetadata, StringComparison.Ordinal);
        Assert.Equal(3, reopened.Pages.Count);

        foreach (var page in reopened.Pages)
        {
            var fonts = page.Dictionary.Get<PdfDictionary>(PdfName.Resources)?.Get<PdfDictionary>(PdfName.Get("Font"));
            Assert.All(fonts ?? [], f => Assert.Equal("Type0", (reopened.Follow(f.Value) as PdfDictionary)?.GetName(PdfName.Subtype)));
        }

        var text = reopened.ExtractText();
        Assert.Contains("Pendapatan naik", text, StringComparison.Ordinal);
        Assert.Contains("Catatan pembicara.", text, StringComparison.Ordinal);
        Assert.Contains("DRAF", text, StringComparison.Ordinal);
        Assert.Contains("Porsi", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAResolverAnArchivalExportExplainsWhatItNeeds()
    {
        using var deck = Deck();

        var error = Assert.Throws<OfficeNetException>(() =>
            deck.ToPdf(new SlidePdfOptions { Conformance = PdfConformance.PdfA2B }));

        Assert.Contains("FontResolver", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VeraPdfFindsTheExportConforming()
    {
        if (VeraPdf.Launcher is null)
        {
            Assert.Skip(VeraPdf.SkipReason);
        }

        using var file = new TempFile(".pdf");

        using (var deck = Deck())
        using (var pdf = deck.ToPdf(Archival()))
        {
            pdf.Save(file.Path);
        }

        Assert.Equal("true", VeraPdf.Validate(file.Path, "2b", TestContext.Current.CancellationToken));
    }
}
