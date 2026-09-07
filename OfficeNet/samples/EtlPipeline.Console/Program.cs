// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.Io;
using ExcelNet.Styles;
using ExcelNet;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core;
using OfficeNet.Samples.Etl;
using PowerPointNet.Charts;
using PowerPointNet;
using System.Globalization;
using WordNet;

// An ETL pipeline: raw CSV in, a formatted workbook, a Word report and a slide deck out.
//
// This is the shape most document automation actually takes — the interesting work is aggregation
// and presentation, not any single library call. It deliberately uses four of them together so the
// seams show: ExcelNet for the numbers, WordNet for the narrative, PowerPointNet for the deck, and
// PdfNet underneath all three for the PDF versions.

var output = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "keluaran");
Directory.CreateDirectory(output);

Console.WriteLine("OfficeNet ETL — laporan penjualan");
Console.WriteLine($"Keluaran: {Path.GetFullPath(output)}");
Console.WriteLine();

// ---- Extract -----------------------------------------------------------------------------------
//
// Generated rather than read from a fixture so the sample runs anywhere with no setup. Swap this
// for CsvIo.Import or SqlIo.Import and the rest of the pipeline is unchanged.

var csvPath = Path.Combine(output, "01-mentah.csv");
File.WriteAllText(csvPath, SampleData.GenerateCsv(rows: 500, seed: 20260907));

Console.WriteLine($"[1/5] Ekstrak   — {csvPath} ({new FileInfo(csvPath).Length / 1024.0:0.0} KB)");

using var workbook = Workbook.Create("Mentah");

// CsvOptions.Indonesian: ';' separator with ',' as the decimal mark. Reading Indonesian CSV with
// the invariant options turns every "1.250,50" into two columns of nonsense.
CsvIo.Import(workbook, csvPath, "Mentah", CsvOptions.Indonesian);

var raw = workbook["Mentah"];
Console.WriteLine($"              {raw.RowCount - 1} baris dimuat");

// ---- Transform ---------------------------------------------------------------------------------

var records = Sales.ReadFrom(raw).ToList();

var byRegion = records
    .GroupBy(r => r.Region)
    .Select(g => new RegionSummary(
        g.Key,
        g.Sum(r => r.Total),
        g.Sum(r => r.Quantity),
        g.Average(r => r.Total),
        g.Count()))
    .OrderByDescending(s => s.Revenue)
    .ToList();

var byProduct = records
    .GroupBy(r => r.Product)
    .Select(g => new { Product = g.Key, Revenue = g.Sum(r => r.Total) })
    .OrderByDescending(x => x.Revenue)
    .ToList();

var byMonth = records
    .GroupBy(r => new DateTime(r.Date.Year, r.Date.Month, 1))
    .Select(g => new { Month = g.Key, Revenue = g.Sum(r => r.Total) })
    .OrderBy(x => x.Month)
    .ToList();

var grandTotal = records.Sum(r => r.Total);

Console.WriteLine($"[2/5] Transform — {byRegion.Count} wilayah, {byProduct.Count} produk, " +
                  $"total {grandTotal:N0}");

// ---- Load: workbook ------------------------------------------------------------------------

var summary = workbook.AddSheet("Ringkasan");

var header = CellStyle.Default
    .Bold()
    .WithColor(OfficeColor.White)
    .WithBackground(OfficeColor.FromRgb(0x1F, 0x38, 0x64))
    .WithAlignment(HorizontalAlignment.Center);

summary.WriteHeader("A1", ["Wilayah", "Pendapatan", "Qty", "Rata-rata", "Transaksi"]);
summary.Range("A1:E1").ApplyStyle(header);

for (var i = 0; i < byRegion.Count; i++)
{
    var row = i + 1;
    var region = byRegion[i];

    summary[row, 0].Set(region.Region);
    summary[row, 1].Set(region.Revenue).WithNumberFormat(NumberFormats.Rupiah);
    summary[row, 2].Set(region.Quantity);
    summary[row, 3].Set(region.Average).WithNumberFormat(NumberFormats.Rupiah);
    summary[row, 4].Set(region.Transactions);
}

var totalRow = byRegion.Count + 1;
summary[totalRow, 0].Set("TOTAL").Bold();

// Formulas rather than the computed value: the reader can widen the range and Excel recomputes,
// which a hard-coded number does not allow.
summary[totalRow, 1].SetFormula($"SUM(B2:B{totalRow})").WithStyle(
    CellStyle.Default.Bold()
        .WithNumberFormat(NumberFormats.Rupiah)
        .WithBackground(OfficeColor.FromRgb(0xFF, 0xF2, 0xCC)));

summary[totalRow, 2].SetFormula($"SUM(C2:C{totalRow})").Bold();
summary[totalRow, 4].SetFormula($"SUM(E2:E{totalRow})").Bold();

summary.AddColorScale($"B2:B{totalRow}",
    OfficeColor.FromRgb(0xFF, 0xEB, 0x84),
    OfficeColor.FromRgb(0x63, 0xBE, 0x7B));

summary.Frozen = new FreezePanes(Rows: 1, Columns: 0);
summary.AutoFitColumns();
summary.TabColor = OfficeColor.FromRgb(0x1F, 0x38, 0x64);

var monthly = workbook.AddSheet("Bulanan");
monthly.WriteHeader("A1", ["Bulan", "Pendapatan"]);
monthly.Range("A1:B1").ApplyStyle(header);

for (var i = 0; i < byMonth.Count; i++)
{
    monthly[i + 1, 0].Set(byMonth[i].Month.ToString("MMMM yyyy", new CultureInfo("id-ID")));
    monthly[i + 1, 1].Set(byMonth[i].Revenue).WithNumberFormat(NumberFormats.Rupiah);
}

monthly.AutoFitColumns();

// Without this the cached results are zero for every consumer except Excel itself — including the
// PDF export three lines below.
workbook.Recalculate();

var workbookPath = Path.Combine(output, "02-laporan.xlsx");
workbook.Save(workbookPath);
workbook.SaveAsPdf(Path.Combine(output, "02-laporan.pdf"));

Console.WriteLine($"[3/5] Workbook  — {Path.GetFileName(workbookPath)} " +
                  $"({workbook.Count} lembar)");

// ---- Load: Word report -------------------------------------------------------------------------

using (var report = WordDocument.Create())
{
    report.Properties.Title = "Laporan Penjualan";
    report.Properties.Creator = "Gravicode Studios";

    report.Section.SetPageSize("A4");
    report.Section.SetMargins(Units.Cm(2.2));

    report.Section.GetHeader().AddParagraph("Gravicode Studios").Alignment =
        ParagraphAlignment.Right;

    var footer = report.Section.GetFooter().AddParagraph();
    footer.Alignment = ParagraphAlignment.Center;
    footer.AddRun("Halaman ");
    footer.AddPageNumber();
    footer.AddRun(" dari ");
    footer.AddPageCount();

    report.AddHeading("Laporan Penjualan", 0);
    report.AddParagraph(
        $"Dibuat otomatis pada {DateTime.Now:d MMMM yyyy}, dari {records.Count} transaksi.",
        "Subtitle");

    report.AddHeading("Ringkasan", 1);

    var lead = report.AddParagraph();
    lead.Alignment = ParagraphAlignment.Justify;
    lead.AddRun("Total pendapatan mencapai ");
    lead.AddRun($"Rp{grandTotal:N0}", bold: true);
    lead.AddRun($" dari {records.Count} transaksi di {byRegion.Count} wilayah. ");
    lead.AddRun($"Wilayah dengan pendapatan tertinggi adalah ");
    lead.AddRun(byRegion[0].Region, bold: true);
    lead.AddRun($" dengan Rp{byRegion[0].Revenue:N0} " +
                $"({byRegion[0].Revenue / grandTotal:P1} dari keseluruhan).");

    report.AddHeading("Pendapatan per Wilayah", 1);

    var rows = new List<string[]> { new[] { "Wilayah", "Pendapatan", "Transaksi", "Porsi" } };

    rows.AddRange(byRegion.Select(r => new[]
    {
        r.Region,
        $"Rp{r.Revenue:N0}",
        r.Transactions.ToString(CultureInfo.InvariantCulture),
        $"{r.Revenue / grandTotal:P1}",
    }));

    var table = report.AddTable(rows);
    table.Rows[0].SetShading(OfficeColor.FromRgb(0x1F, 0x38, 0x64));

    foreach (var cell in table.Rows[0].Cells)
    {
        foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
        {
            run.Format.Color = OfficeColor.White;
        }
    }

    report.AddHeading("Produk Terlaris", 1);
    // numbered: true, not a hand-written "1." prefix — otherwise the list renders as "• 1. …"
    // with Word's numbering and the manual one fighting each other.
    report.AddList(
        byProduct.Take(5).Select(p => $"{p.Product} — Rp{p.Revenue:N0}"),
        numbered: true);

    report.AddHeading("Catatan", 1);
    report.AddParagraph(
        "Angka dihasilkan oleh contoh kode dan tidak mewakili data nyata.", "Quote");

    report.Save(Path.Combine(output, "03-laporan.docx"));
    report.SaveAsPdf(Path.Combine(output, "03-laporan.pdf"));
}

Console.WriteLine("[4/5] Laporan   — 03-laporan.docx + .pdf");

// ---- Load: deck ----------------------------------------------------------------------------

using (var deck = Presentation.Create())
{
    deck.Master!.SetThemeColor("accent1", OfficeColor.FromRgb(0x1F, 0x38, 0x64));

    deck.AddTitleSlide("Laporan Penjualan",
        $"{records.Count} transaksi · Rp{grandTotal:N0}");

    // Layout 2 is "Title Only": its title sits at the top, leaving the body of the slide free.
    // Layout 5 is "Section Header", whose title is placed low down — a chart added there draws
    // straight over it.
    var chartSlide = deck.AddSlide(2);
    chartSlide.SetTitle("Pendapatan per Wilayah");

    chartSlide.AddChart(new ChartData
    {
        Type = ChartType.Column,
        Categories = [.. byRegion.Select(r => r.Region)],
        Series = [new ChartSeries("Pendapatan", [.. byRegion.Select(r => r.Revenue)])],
        ValueFormat = "#,##0",
        ShowDataLabels = true,
        Legend = LegendPosition.None,
    });

    var trend = deck.AddSlide(2);
    trend.SetTitle("Tren Bulanan");

    trend.AddChart(new ChartData
    {
        Type = ChartType.Line,
        Categories = [.. byMonth.Select(m => m.Month.ToString("MMM", new CultureInfo("id-ID")))],
        Series = [new ChartSeries("Pendapatan", [.. byMonth.Select(m => m.Revenue)])],
        ValueFormat = "#,##0",
        Legend = LegendPosition.None,
    });

    var share = deck.AddSlide(2);
    share.SetTitle("Porsi per Produk");

    share.AddChart(new ChartData
    {
        Type = ChartType.Pie,
        Categories = [.. byProduct.Take(5).Select(p => p.Product)],
        Series = [new ChartSeries("Pendapatan", [.. byProduct.Take(5).Select(p => p.Revenue)])],
        ShowPercentages = true,
        Legend = LegendPosition.Right,
    });

    deck.Save(Path.Combine(output, "04-deck.pptx"));
    deck.SaveAsPdf(Path.Combine(output, "04-deck.pdf"));
}

Console.WriteLine("[5/5] Deck      — 04-deck.pptx + .pdf");
Console.WriteLine();
Console.WriteLine("Berkas yang dihasilkan:");

foreach (var file in Directory.EnumerateFiles(output).OrderBy(f => f))
{
    Console.WriteLine($"  {Path.GetFileName(file),-24} {new FileInfo(file).Length / 1024.0,8:0.0} KB");
}

return 0;
