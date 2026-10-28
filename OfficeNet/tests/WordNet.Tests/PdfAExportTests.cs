// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.Core.Charts;
using OfficeNet.TestKit;
using PdfNet.Document;
using PdfNet.Objects;
using WordNet.Export;
using Xunit;

namespace WordNet.Tests;

public class PdfAExportTests
{
    /// <summary>One synthetic font for every family and style — what a resolver hands back.</summary>
    private static readonly byte[] Font = SyntheticFont.Build(SyntheticFont.DefaultCharacters + "-%•");

    private static WordDocument Report()
    {
        var document = WordDocument.Create();
        document.Properties.Title = "Laporan arsip";
        document.AddHeading("Laporan Kuartal", 1);

        var paragraph = document.AddParagraph("Penjualan ");
        paragraph.AddRun("naik 32%").Format.Bold = true;
        paragraph.AddRun(", tercatat.").Format.Italic = true;

        document.AddList(["Jakarta", "Bandung"]);

        var table = document.AddTable(2, 2);
        table.Rows[0].Cells[0].Text = "Wilayah";
        table.Rows[0].Cells[1].Text = "Nilai";
        table.Rows[1].Cells[0].Text = "Jakarta";
        table.Rows[1].Cells[1].Text = "1480";

        document.AddChart(new ChartData
        {
            Type = ChartType.Column,
            Title = "Per wilayah",
            Categories = ["Jakarta", "Bandung"],
            Series = [new ChartSeries("Q3", [1480, 1150])],
        });

        return document;
    }

    private static PdfExportOptions Archival() => new()
    {
        Conformance = PdfConformance.PdfA2B,
        FontResolver = (_, _, _) => Font,
        Watermark = "ARSIP",
    };

    [Fact]
    public void AnArchivalExportEmbedsEveryFontItDrawsWith()
    {
        using var document = Report();
        using var pdf = document.ToPdf(Archival());

        Assert.Equal(PdfConformance.PdfA2B, pdf.Conformance);

        using var reopened = PdfDocument.Open(pdf.ToArray());
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", reopened.XmpMetadata, StringComparison.Ordinal);

        // Every font on the page is a Type0 with its program in the file; none of the standard 14.
        var resources = reopened.Pages[0].Dictionary.Get<PdfDictionary>(PdfName.Resources)!;
        var fonts = resources.Get<PdfDictionary>(PdfName.Get("Font"))!.Select(f => reopened.Follow(f.Value)).OfType<PdfDictionary>().ToList();

        Assert.NotEmpty(fonts);
        Assert.All(fonts, font => Assert.Equal("Type0", font.GetName(PdfName.Subtype)));

        // One file for every style: the same bytes are embedded once, not once per style.
        var objects = resources.Get<PdfDictionary>(PdfName.Get("Font"))!.Select(f => f.Value).OfType<PdfReference>()
            .Select(r => r.Number).Distinct();
        Assert.Single(objects);

        var text = reopened.ExtractText();
        Assert.Contains("Laporan Kuartal", text, StringComparison.Ordinal);
        Assert.Contains("naik 32%", text, StringComparison.Ordinal);
        Assert.Contains("ARSIP", text, StringComparison.Ordinal);
        Assert.Contains("Per wilayah", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAFontResolverAnArchivalExportExplainsWhatItNeeds()
    {
        using var document = Report();

        var error = Assert.Throws<OfficeNetException>(() =>
            document.ToPdf(new PdfExportOptions { Conformance = PdfConformance.PdfA2B }));

        Assert.Contains("Calibri", error.Message, StringComparison.Ordinal);
        Assert.Contains("FontResolver", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACharacterTheFontLacksIsRefusedByName()
    {
        // PDF/A forbids the empty .notdef glyph, which is what a character outside the font draws.
        using var document = WordDocument.Create();
        document.AddParagraph("Harga € 10");

        using var pdf = document.ToPdf(new PdfExportOptions
        {
            Conformance = PdfConformance.PdfA2B,
            FontResolver = (_, _, _) => Font,
        });

        var error = Assert.Throws<OfficeNetException>(() => pdf.ToArray());
        Assert.Contains("U+20AC", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFamilyTheResolverLacksFallsBackToTheDefault()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Teks").Runs[0].Format.FontName = "Tidak Ada";

        var asked = new List<string>();
        using var pdf = document.ToPdf(new PdfExportOptions
        {
            Conformance = PdfConformance.PdfA2B,
            DefaultFontFamily = "Dasar",
            FontResolver = (family, _, _) =>
            {
                asked.Add(family);
                return family == "Dasar" ? Font : null;
            },
        });

        Assert.Equal(["Tidak Ada", "Dasar"], asked.Distinct());
        Assert.Contains("Teks", pdf.ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutConformanceAResolverStillEmbedsAndAMissingFontFallsBackToStandard()
    {
        using var document = Report();

        using var embedded = document.ToPdf(new PdfExportOptions { FontResolver = (_, _, _) => Font });
        Assert.Equal(PdfConformance.None, embedded.Conformance);
        Assert.Contains("Laporan Kuartal", embedded.ExtractText(), StringComparison.Ordinal);

        // No resolver and no PDF/A: the standard fonts, as before.
        using var standard = document.ToPdf();
        using var reopened = PdfDocument.Open(standard.ToArray());
        var fonts = reopened.Pages[0].Dictionary.Get<PdfDictionary>(PdfName.Resources)!.Get<PdfDictionary>(PdfName.Get("Font"))!;
        Assert.Contains(fonts, f => (reopened.Follow(f.Value) as PdfDictionary)?.GetName(PdfName.Subtype) == "Type1");
    }

    [Fact]
    public void VeraPdfFindsTheExportConforming()
    {
        if (VeraPdf.Launcher is null)
        {
            Assert.Skip(VeraPdf.SkipReason);
        }

        using var folder = new TempDirectory();
        var path = Path.Combine(folder.Path, "laporan.pdf");

        using (var document = Report())
        using (var pdf = document.ToPdf(Archival()))
        {
            pdf.Save(path);
        }

        Assert.Equal("true", VeraPdf.Validate(path, "2b", TestContext.Current.CancellationToken));
    }
}
