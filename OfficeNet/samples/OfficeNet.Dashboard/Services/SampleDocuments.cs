// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.Styles;
using ExcelNet;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core;
using PowerPointNet.Charts;
using PowerPointNet;
using WordNet;

namespace OfficeNet.Dashboard.Services;

/// <summary>
/// Builds one document per format, so the dashboard is usable with nothing to hand.
/// </summary>
/// <remarks>
/// Generated with the libraries rather than shipped as fixture files: a sample app that
/// demonstrates a document library should not need documents committed alongside it, and this way
/// the demo content cannot drift out of step with the API.
/// </remarks>
internal static class SampleDocuments
{
    public static IEnumerable<(string FileName, byte[] Bytes)> Build()
    {
        yield return ("laporan-tahunan.docx", Word());
        yield return ("penjualan.xlsx", Excel());
        yield return ("ringkasan.pptx", PowerPoint());
    }

    private static byte[] Word()
    {
        using var document = WordDocument.Create();

        document.Properties.Title = "Laporan Tahunan";
        document.Properties.Creator = "Gravicode Studios";

        document.Section.SetPageSize("A4");
        document.Section.SetMargins(Units.Cm(2.2));

        document.Section.GetHeader().AddParagraph("Gravicode Studios").Alignment =
            ParagraphAlignment.Right;

        var footer = document.Section.GetFooter().AddParagraph();
        footer.Alignment = ParagraphAlignment.Center;
        footer.AddRun("Halaman ");
        footer.AddPageNumber();
        footer.AddRun(" dari ");
        footer.AddPageCount();

        document.AddHeading("Laporan Tahunan", 0);
        document.AddParagraph("Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.", "Subtitle");

        document.AddHeading("Ringkasan", 1);

        var summary = document.AddParagraph();
        summary.Alignment = ParagraphAlignment.Justify;
        summary.AddRun("Pendapatan tumbuh ");
        summary.AddRun("32%", bold: true);
        summary.AddRun(" dibanding tahun sebelumnya, ditopang permintaan yang kuat di Jakarta " +
                       "dan Bandung. Seluruh wilayah menutup tahun di atas target.");

        document.AddHeading("Pendapatan per Wilayah", 1);

        var table = document.AddTable(new[]
        {
            new[] { "Wilayah", "2025", "2026", "Pertumbuhan" },
            new[] { "Jakarta", "1.120", "1.480", "+32%" },
            new[] { "Bandung", "860", "1.150", "+34%" },
            new[] { "Surabaya", "740", "905", "+22%" },
        });

        table.Rows[0].SetShading(OfficeColor.FromRgb(0x1F, 0x38, 0x64));

        foreach (var cell in table.Rows[0].Cells)
        {
            foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
            {
                run.Format.Color = OfficeColor.White;
            }
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static byte[] Excel()
    {
        using var workbook = Workbook.Create("Penjualan");
        var sheet = workbook["Penjualan"];

        sheet.WriteHeader("A1", ["Tanggal", "Produk", "Wilayah", "Qty", "Harga", "Total"]);

        string[] products = ["WordNet", "ExcelNet", "PowerPointNet", "PdfNet"];
        string[] regions = ["Jakarta", "Bandung", "Surabaya"];
        var random = new Random(7);

        for (var i = 0; i < 14; i++)
        {
            var row = i + 1;
            sheet[row, 0].Set(new DateTime(2026, 1, 5).AddDays(i * 4));
            sheet[row, 1].Set(products[i % products.Length]);
            sheet[row, 2].Set(regions[i % regions.Length]);
            sheet[row, 3].Set(random.Next(4, 40));
            sheet[row, 4].Set(random.Next(60, 280) * 1000.0).WithNumberFormat(NumberFormats.Rupiah);
            sheet[row, 5].SetFormula($"D{row + 1}*E{row + 1}").WithNumberFormat(NumberFormats.Rupiah);
        }

        sheet["E16"].Set("TOTAL").Bold();
        sheet["F16"].SetFormula("SUM(F2:F15)").WithStyle(
            CellStyle.Default.Bold()
                .WithNumberFormat(NumberFormats.Rupiah)
                .WithBackground(OfficeColor.FromRgb(0xFF, 0xF2, 0xCC)));

        sheet.AddColorScale("D2:D15",
            OfficeColor.FromRgb(0xF8, 0x69, 0x6B),
            OfficeColor.FromRgb(0x63, 0xBE, 0x7B));

        sheet.AutoFitColumns();

        // Without this every consumer other than Excel reads the cached results as zero.
        workbook.Recalculate();

        using var stream = new MemoryStream();
        workbook.Save(stream);
        return stream.ToArray();
    }

    private static byte[] PowerPoint()
    {
        using var deck = Presentation.Create();

        deck.Master!.SetThemeColor("accent1", OfficeColor.FromRgb(0x1F, 0x38, 0x64));

        deck.AddTitleSlide("OfficeNet", "Word, Excel, PowerPoint dan PDF untuk .NET 10");

        deck.AddBulletSlide("Komponen", [
            "WordNet — python-docx",
            "ExcelNet — openpyxl + pandas",
            "PowerPointNet — python-pptx + PptxGenJS",
            "PdfNet — PyPDF2",
        ]);

        var chart = deck.AddSlide(2);
        chart.SetTitle("Pendapatan per Wilayah");

        chart.AddChart(new ChartData
        {
            Type = ChartType.Column,
            Categories = ["Jakarta", "Bandung", "Surabaya", "Medan"],
            Series =
            [
                new ChartSeries("2025", [1120, 860, 740, 410]),
                new ChartSeries("2026", [1480, 1150, 905, 520]),
            ],
            ValueAxisTitle = "Juta Rupiah",
            ValueFormat = "#,##0",
            ShowDataLabels = true,
        });

        using var stream = new MemoryStream();
        deck.Save(stream);
        return stream.ToArray();
    }
}
