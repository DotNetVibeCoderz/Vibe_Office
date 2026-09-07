// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.Styles;
using ExcelNet;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core;
using OfficeNet.Rendering;
using PowerPointNet.Charts;
using PowerPointNet.Html;
using PowerPointNet;
using WordNet;

// Generates the screenshots the documentation embeds.
//
// They are produced from the library's own output rather than captured from a viewer, so they are
// reproducible on any machine and cannot drift away from what the code actually does. Re-run this
// after changing anything that affects layout.

var repository = FindRepositoryRoot();
var screenshots = Path.Combine(repository, "docs", "screenshots");
var scratch = Path.Combine(Path.GetTempPath(), "officenet-screenshots");

Directory.CreateDirectory(screenshots);
Directory.CreateDirectory(scratch);

Console.WriteLine($"Writing screenshots to {screenshots}");

var render = new RenderOptions { Dpi = 110, DrawPageBorder = true, MaxPixels = 1600 };

BuildWord();
BuildExcel();
BuildPowerPoint();
BuildHtmlConversion();
BuildPdf();

Console.WriteLine("Done.");

void BuildWord()
{
    var path = Path.Combine(scratch, "word-report.docx");

    using (var document = WordDocument.Create())
    {
        document.Properties.Title = "Laporan Tahunan OfficeNet";
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
        document.AddParagraph("Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.",
            "Subtitle");

        document.AddHeading("Ringkasan", 1);

        var summary = document.AddParagraph();
        summary.Alignment = ParagraphAlignment.Justify;
        summary.AddRun("Pendapatan tumbuh ");
        summary.AddRun("32%", bold: true);
        summary.AddRun(" dibanding tahun sebelumnya, ditopang oleh permintaan yang kuat di " +
                       "Jakarta dan Bandung. Seluruh wilayah menutup tahun di atas target, " +
                       "dan biaya operasional turun tipis berkat otomasi pelaporan.");

        document.AddHeading("Komponen", 1);
        document.AddList(
        [
            "WordNet — penulisan ulang python-docx",
            "ExcelNet — penulisan ulang openpyxl dan pandas",
            "PowerPointNet — penulisan ulang python-pptx",
            "PdfNet — penulisan ulang PyPDF2",
        ]);

        document.AddHeading("Pendapatan per Wilayah", 1);

        var table = document.AddTable(new[]
        {
            new[] { "Wilayah", "2025", "2026", "Pertumbuhan" },
            new[] { "Jakarta", "1.120", "1.480", "+32%" },
            new[] { "Bandung", "860", "1.150", "+34%" },
            new[] { "Surabaya", "740", "905", "+22%" },
            new[] { "Medan", "410", "520", "+27%" },
        });

        table.Rows[0].SetShading(OfficeColor.FromRgb(0x1F, 0x38, 0x64));

        foreach (var cell in table.Rows[0].Cells)
        {
            foreach (var paragraph in cell.Paragraphs)
            {
                foreach (var run in paragraph.Runs)
                {
                    run.Format.Color = OfficeColor.White;
                }
            }
        }

        document.AddHeading("Catatan", 1);
        document.AddParagraph(
            "Angka bersifat ilustratif dan dihasilkan oleh contoh kode.", "Quote");

        document.Save(path);
    }

    Save("wordnet-document", path, render);
}

void BuildExcel()
{
    var path = Path.Combine(scratch, "excel-sales.xlsx");

    using (var workbook = Workbook.Create("Penjualan"))
    {
        var sheet = workbook["Penjualan"];

        sheet.WriteHeader("A1",
            ["Tanggal", "Produk", "Wilayah", "Qty", "Harga", "Total"]);

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
            sheet[row, 4].Set(random.Next(60, 280) * 1000.0)
                .WithNumberFormat(NumberFormats.Rupiah);
            sheet[row, 5].SetFormula($"D{row + 1}*E{row + 1}")
                .WithNumberFormat(NumberFormats.Rupiah);
        }

        sheet["E16"].Set("TOTAL").Bold();
        sheet["F16"].SetFormula("SUM(F2:F15)").WithStyle(
            CellStyle.Default.Bold()
                .WithNumberFormat(NumberFormats.Rupiah)
                .WithBackground(OfficeColor.FromRgb(0xFF, 0xF2, 0xCC)));

        sheet["E17"].Set("RATA-RATA").Bold();
        sheet["F17"].SetFormula("AVERAGE(F2:F15)")
            .WithNumberFormat(NumberFormats.Rupiah);

        sheet.AddColorScale("D2:D15",
            OfficeColor.FromRgb(0xF8, 0x69, 0x6B),
            OfficeColor.FromRgb(0x63, 0xBE, 0x7B));

        sheet.AutoFitColumns();
        workbook.Recalculate();
        workbook.Save(path);
    }

    Save("excelnet-workbook", path, new RenderOptions
    {
        Dpi = 110,
        DrawPageBorder = true,
        MaxPixels = 1600,
    });
}

void BuildPowerPoint()
{
    var path = Path.Combine(scratch, "deck.pptx");

    using (var presentation = Presentation.Create())
    {
        presentation.Master!.SetThemeColor("accent1", OfficeColor.FromRgb(0x1F, 0x38, 0x64));

        presentation.AddTitleSlide("OfficeNet",
            "Word, Excel, PowerPoint, dan PDF untuk .NET 10");

        presentation.AddBulletSlide("Komponen",
        [
            "WordNet — python-docx",
            "ExcelNet — openpyxl + pandas",
            "PowerPointNet — python-pptx + PptxGenJS",
            "PdfNet — PyPDF2",
        ]);

        var chartSlide = presentation.AddSlide(2);
        chartSlide.SetTitle("Pendapatan per Wilayah");
        chartSlide.AddChart(new ChartData
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

        var tableSlide = presentation.AddSlide(2);
        tableSlide.SetTitle("Status Pengembangan");

        tableSlide.AddTable(5, 3, Units.Inches(1), Units.Inches(1.9),
                presentation.SlideWidth - Units.Inches(2), Units.Inches(2.6))
            .SetData(new[]
            {
                new[] { "Komponen", "Analog Python", "Status" },
                new[] { "WordNet", "python-docx", "Selesai" },
                new[] { "ExcelNet", "openpyxl + pandas", "Selesai" },
                new[] { "PowerPointNet", "python-pptx", "Selesai" },
                new[] { "PdfNet", "PyPDF2", "Selesai" },
            });

        presentation.Save(path);
    }

    Save("powerpointnet-deck", path, new RenderOptions
    {
        Dpi = 110,
        DrawPageBorder = true,
        MaxPixels = 1400,
    });
}

void BuildHtmlConversion()
{
    const string Html = """
        <h1>Tinjauan Kuartal</h1>
        <p>Disusun oleh <strong>Gravicode Studios</strong>, dipimpin oleh <em>Kang Fadhil</em>.</p>
        <h2>Sorotan</h2>
        <ul>
          <li>Pendapatan naik <strong>32%</strong>
            <ul><li>Jakarta memimpin pertumbuhan</li></ul>
          </li>
          <li>Biaya operasional turun 4%</li>
          <li>Empat rilis library selesai</li>
        </ul>
        <h2>Ringkasan Angka</h2>
        <table>
          <tr><th>Wilayah</th><th>2025</th><th>2026</th></tr>
          <tr><td>Jakarta</td><td>1.120</td><td>1.480</td></tr>
          <tr><td>Bandung</td><td>860</td><td>1.150</td></tr>
          <tr><td>Surabaya</td><td>740</td><td>905</td></tr>
        </table>
        """;

    var path = Path.Combine(scratch, "html-deck.pptx");

    using (var presentation = HtmlToSlides.CreatePresentation(Html, new HtmlSlideOptions
    {
        TitleSlide = "Dari HTML ke PowerPoint",
        SubtitleSlide = "Satu panggilan, tanpa penyuntingan manual",
    }))
    {
        presentation.Save(path);
    }

    Save("html-to-slides", path, new RenderOptions
    {
        Dpi = 110,
        DrawPageBorder = true,
        MaxPixels = 1400,
    });
}

void BuildPdf()
{
    // The Word document converted to PDF: the same content through the export path, which is what
    // the documentation claims the exporter produces.
    var source = Path.Combine(scratch, "word-report.docx");
    var path = Path.Combine(scratch, "word-report.pdf");

    OfficeNet.Office.ConvertToPdf(source, path);

    Save("pdfnet-export", path, render);
}

void Save(string prefix, string documentPath, RenderOptions options)
{
    var written = DocumentRenderer.RenderToFiles(documentPath, screenshots, options, prefix);

    foreach (var file in written)
    {
        var info = new FileInfo(file);
        Console.WriteLine($"  {Path.GetFileName(file),-32} {info.Length / 1024.0,7:0.0} KB");
    }
}

static string FindRepositoryRoot()
{
    // Walk up from the binary rather than trusting the working directory: the tool is normally run
    // through `dotnet run`, whose current directory is the project, not the repository.
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "OfficeNet.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException(
        "Could not find OfficeNet.sln above the tool's output directory.");
}
