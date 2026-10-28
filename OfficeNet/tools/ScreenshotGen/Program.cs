// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;
using ExcelNet.Styles;
using ExcelNet;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core;
using OfficeNet.Rendering;
using PowerPointNet.Charts;
using PowerPointNet.Html;
using PowerPointNet;
using ModelContextProtocol.Client;
using SkiaSharp;
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
await BuildMcp();

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

// ---- MCP ---------------------------------------------------------------------------------------
//
// These documents are made by the MCP server, not by calling the libraries here: the server runs as
// a separate process and every file comes from a tool call over stdio, the same path an assistant
// takes. Even the PNGs are written by the server's own render_page tool.

async Task BuildMcp()
{
    var workspace = Path.Combine(scratch, "mcp");

    if (Directory.Exists(workspace))
    {
        Directory.Delete(workspace, recursive: true);
    }

    Directory.CreateDirectory(workspace);

    var server = Path.Combine(repository, "src", "OfficeNet.Mcp", "bin", "Release", "net10.0",
        "Gravicode.OfficeNet.Mcp.dll");

    await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
    {
        Name = "officenet",
        Command = "dotnet",
        Arguments = [server, "--root", workspace],
    }));

    var tools = await client.ListToolsAsync();
    Console.WriteLine($"  MCP server answered with {tools.Count} tools");

    await Call(client, "word_create", new
    {
        path = "laporan.docx",
        title = "Laporan Kuartal III",
        author = "Gravicode Studios",
        blocks = new object[]
        {
            new { type = "heading", level = 0, text = "Laporan Kuartal III 2026" },
            new { type = "paragraph", text = "Disusun oleh asisten AI lewat **OfficeNet MCP**, dipimpin oleh *Kang Fadhil*." },
            new { type = "heading", level = 1, text = "Ringkasan" },
            new
            {
                type = "paragraph",
                align = "justify",
                text = "Pendapatan tumbuh **32%** dibanding kuartal sebelumnya, ditopang permintaan di " +
                       "Jakarta dan Bandung. Biaya operasional turun tipis berkat otomasi pelaporan.",
            },
            new { type = "heading", level = 1, text = "Sorotan" },
            new { type = "bullets", items = new[] { "Jakarta memimpin pertumbuhan", "Empat rilis library selesai", "Biaya operasional turun 4%" } },
            new { type = "heading", level = 1, text = "Pendapatan per Wilayah" },
            new
            {
                type = "table",
                rows = new[]
                {
                    new[] { "Wilayah", "Q2", "Q3", "Pertumbuhan" },
                    new[] { "Jakarta", "1.120", "1.480", "+32%" },
                    new[] { "Bandung", "860", "1.150", "+34%" },
                    new[] { "Surabaya", "740", "905", "+22%" },
                },
            },
            new { type = "quote", text = "Dokumen ini dibuat sepenuhnya lewat panggilan tool MCP." },
        },
    });

    await Call(client, "excel_create", new
    {
        path = "penjualan.xlsx",
        sheets = new[]
        {
            new
            {
                name = "Penjualan",
                rows = new object[][]
                {
                    ["Tanggal", "Produk", "Qty", "Harga", "Total"],
                    ["2026-10-01", "WordNet", 12, 450000, "=C2*D2"],
                    ["2026-10-02", "ExcelNet", 9, 520000, "=C3*D3"],
                    ["2026-10-03", "PowerPointNet", 7, 480000, "=C4*D4"],
                    ["2026-10-04", "PdfNet", 15, 300000, "=C5*D5"],
                    ["", "", "", "TOTAL", "=SUM(E2:E5)"],
                },
                numberFormats = new Dictionary<string, string> { ["A"] = "dd mmm yyyy", ["D"] = "#,##0", ["E"] = "#,##0" },
                chart = new
                {
                    type = "column",
                    title = "Total per produk",
                    categories = new[] { "WordNet", "ExcelNet", "PowerPointNet", "PdfNet" },
                    series = new[] { new { name = "Total", values = new[] { 5400000.0, 4680000, 3360000, 4500000 } } },
                    valueFormat = "#,##0",
                },
            },
        },
    });

    await Call(client, "pptx_create", new
    {
        path = "tinjauan.pptx",
        slides = new object[]
        {
            new { type = "title", title = "Tinjauan Kuartal III", subtitle = "Dibuat lewat OfficeNet MCP" },
            new { type = "bullets", title = "Sorotan", bullets = new[] { "Pendapatan naik 32%", "Jakarta memimpin", "Biaya turun 4%" } },
            new
            {
                type = "chart",
                title = "Pendapatan per Wilayah",
                chart = new
                {
                    type = "column",
                    categories = new[] { "Jakarta", "Bandung", "Surabaya", "Medan" },
                    series = new[]
                    {
                        new { name = "Q2", values = new[] { 1120.0, 860, 740, 410 } },
                        new { name = "Q3", values = new[] { 1480.0, 1150, 905, 520 } },
                    },
                    showDataLabels = true,
                    valueFormat = "#,##0",
                },
            },
            new { type = "table", title = "Status", rows = new[] { new[] { "Komponen", "Status" }, new[] { "WordNet", "Selesai" }, new[] { "ExcelNet", "Selesai" } } },
        },
    });

    // The template is itself written through the server, tokens and all, then filled from JSON.
    await Call(client, "word_create", new
    {
        path = "template-invoice.docx",
        blocks = new object[]
        {
            new { type = "heading", level = 0, text = "INVOICE {{nomor}}" },
            new { type = "paragraph", text = "Kepada: **{{pelanggan.nama}}**, {{pelanggan.kota}}" },
            new { type = "paragraph", text = "Tanggal: {{tanggal:dd MMMM yyyy}}" },
            new
            {
                type = "table",
                rows = new[]
                {
                    new[] { "Barang", "Qty", "Harga", "Jumlah" },
                    new[] { "{{#baris}}{{barang}}", "{{qty}}", "{{harga:N0}}", "{{jumlah:N0}}{{/baris}}" },
                    new[] { "", "", "Total", "{{total:N0}}" },
                },
            },
            new { type = "paragraph", text = "{{#lunas}}Status: **LUNAS**{{/lunas}}{{^lunas}}Mohon dibayar sebelum {{jatuhTempo:dd MMMM yyyy}}.{{/lunas}}" },
        },
    });

    await Call(client, "word_fill_template", new
    {
        templatePath = "template-invoice.docx",
        outputPath = "invoice-0017.docx",
        culture = "id-ID",
        data = new
        {
            nomor = "INV-2026-0017",
            tanggal = "2026-10-28",
            jatuhTempo = "2026-11-11",
            pelanggan = new { nama = "PT Maju Bersama", kota = "Bandung" },
            baris = new[]
            {
                new { barang = "Lisensi OfficeNet", qty = 3, harga = 1500000, jumlah = 4500000 },
                new { barang = "Pelatihan", qty = 1, harga = 2500000, jumlah = 2500000 },
                new { barang = "Dukungan 12 bulan", qty = 1, harga = 3000000, jumlah = 3000000 },
            },
            total = 10000000,
            lunas = false,
        },
    });

    await Call(client, "convert", new { input = "laporan.docx", output = "laporan.pdf" });

    // Preview PNGs, written by the server's render_page tool and copied into the docs.
    (string Document, int Page, string Name)[] previews =
    [
        ("laporan.pdf", 1, "mcp-word-report"),
        ("penjualan.xlsx", 1, "mcp-excel-workbook"),
        ("tinjauan.pptx", 3, "mcp-deck-03"),
        ("invoice-0017.docx", 1, "mcp-invoice"),
    ];

    foreach (var (document, page, name) in previews)
    {
        await Call(client, "render_page", new { path = document, page, dpi = 100, savePath = $"png/{name}.png" });
        var target = Path.Combine(screenshots, name + ".png");
        File.Copy(Path.Combine(workspace, "png", name + ".png"), target, overwrite: true);
        Console.WriteLine($"  {name + ".png",-32} {new FileInfo(target).Length / 1024.0,7:0.0} KB");
    }

    Showcase(
        Path.Combine(screenshots, "mcp-showcase.png"),
        [
            ("word_create", Path.Combine(screenshots, "mcp-word-report.png")),
            ("excel_create", Path.Combine(screenshots, "mcp-excel-workbook.png")),
            ("pptx_create", Path.Combine(screenshots, "mcp-deck-03.png")),
            ("word_fill_template", Path.Combine(screenshots, "mcp-invoice.png")),
        ]);
}

static async Task Call(McpClient client, string tool, object arguments)
{
    // Through JSON, as a client sends them.
    var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments))!
        .ToDictionary(p => p.Key, p => (object?)p.Value);

    var result = await client.CallToolAsync(tool, json);
    var text = string.Join(" ", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text));

    if (result.IsError == true)
    {
        throw new InvalidOperationException($"{tool} failed: {text}");
    }

    Console.WriteLine($"  {tool,-20} {text}");
}

// Four previews side by side, each captioned with the tool that made it.
void Showcase(string path, (string Caption, string Image)[] tiles)
{
    const int tileWidth = 560;
    const int tileHeight = 420;
    const int gap = 24;
    const int caption = 34;

    // Two by two: four in a row is too wide for a README.
    const int columns = 2;
    var rows = (tiles.Length + columns - 1) / columns;
    var width = gap + columns * (tileWidth + gap);
    var height = gap + rows * (caption + tileHeight + gap);

    using var surface = SKSurface.Create(new SKImageInfo(width, height));
    var canvas = surface.Canvas;
    canvas.Clear(new SKColor(0xF3, 0xF4, 0xF6));

    using var typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold) ?? SKTypeface.Default;
    using var font = new SKFont(typeface, 20);
    using var text = new SKPaint { Color = new SKColor(0x1F, 0x38, 0x64), IsAntialias = true };
    using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 40), IsAntialias = true };

    for (var i = 0; i < tiles.Length; i++)
    {
        var x = gap + i % columns * (tileWidth + gap);
        var y = gap + i / columns * (caption + tileHeight + gap);
        canvas.DrawText(tiles[i].Caption, x, y + 22, SKTextAlign.Left, font, text);

        using var image = SKBitmap.Decode(tiles[i].Image);
        var scale = Math.Min((float)tileWidth / image.Width, (float)tileHeight / image.Height);
        var drawn = new SKRect(0, 0, image.Width * scale, image.Height * scale);
        drawn.Offset(x + (tileWidth - drawn.Width) / 2, y + caption + (tileHeight - drawn.Height) / 2);

        var shadowRect = drawn;
        shadowRect.Offset(4, 4);
        canvas.DrawRect(shadowRect, shadow);

        using var sampled = SKImage.FromBitmap(image);
        canvas.DrawImage(sampled, drawn, new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    using var snapshot = surface.Snapshot();
    using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
    Console.WriteLine($"  {Path.GetFileName(path),-32} {new FileInfo(path).Length / 1024.0,7:0.0} KB");
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
