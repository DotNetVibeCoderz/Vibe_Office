// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.Io;
using OfficeNet.Core;
using OfficeNet.Core.Charts;
using OfficeNet.TestKit;
using PdfNet.Document;
using PdfNet.Objects;
using Xunit;

namespace ExcelNet.Tests;

public class PdfAExportTests
{
    private static readonly byte[] Font = SyntheticFont.Build(SyntheticFont.DefaultCharacters + "-%/:—");   // the footer has an em dash

    private static Workbook Sheet()
    {
        var workbook = Workbook.Create("Penjualan");
        var sheet = workbook[0];
        sheet["A1"].Set("Wilayah");
        sheet["B1"].Set("Nilai");
        sheet["A2"].Set("Jakarta");
        sheet["B2"].Set(1480.5).WithNumberFormat("#,##0.00");
        sheet["A3"].Set("Bandung");
        sheet["B3"].Set(1150);
        sheet.AddChart(new ChartData
        {
            Type = ChartType.Column,
            Title = "Per wilayah",
            Categories = ["Jakarta", "Bandung"],
            Series = [new ChartSeries("Q3", [1480, 1150])],
        }, "D2:J15");
        return workbook;
    }

    private static ExcelPdfOptions Archival() => new()
    {
        Conformance = PdfConformance.PdfA2B,
        FontResolver = (_, _, _) => Font,
    };

    [Fact]
    public void AnArchivalExportEmbedsEveryFontAndSaysSo()
    {
        using var workbook = Sheet();
        using var pdf = ExcelToPdf.Convert(workbook, Archival());

        using var reopened = PdfDocument.Open(pdf.ToArray());
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", reopened.XmpMetadata, StringComparison.Ordinal);

        foreach (var page in reopened.Pages)
        {
            var fonts = page.Dictionary.Get<PdfDictionary>(PdfName.Resources)?.Get<PdfDictionary>(PdfName.Get("Font"));
            Assert.All(fonts ?? [], f => Assert.Equal("Type0", (reopened.Follow(f.Value) as PdfDictionary)?.GetName(PdfName.Subtype)));
        }

        var text = reopened.ExtractText();
        Assert.Contains("1,480.50", text, StringComparison.Ordinal);
        Assert.Contains("Per wilayah", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAResolverAnArchivalExportExplainsWhatItNeeds()
    {
        using var workbook = Sheet();

        var error = Assert.Throws<OfficeNetException>(() =>
            ExcelToPdf.Convert(workbook, new ExcelPdfOptions { Conformance = PdfConformance.PdfA2B }));

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

        using (var workbook = Sheet())
        using (var pdf = ExcelToPdf.Convert(workbook, Archival()))
        {
            pdf.Save(file.Path);
        }

        Assert.Equal("true", VeraPdf.Validate(file.Path, "2b", TestContext.Current.CancellationToken));
    }
}
