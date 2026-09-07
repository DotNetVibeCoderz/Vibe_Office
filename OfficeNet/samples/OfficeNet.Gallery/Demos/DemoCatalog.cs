// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.Styles;
using ExcelNet;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core;
using OfficeNet.Rendering;
using PdfNet.Annotations;
using PdfNet.Content;
using PdfNet.Document;
using PowerPointNet.Charts;
using PowerPointNet.Html;
using PowerPointNet.Shapes;
using PowerPointNet;
using WordNet.Sections;
using WordNet;
using PdfAlignment = PdfNet.Content.TextAlignment;

namespace OfficeNet.Gallery.Demos;

/// <summary>
/// Every demo the gallery offers.
/// </summary>
/// <remarks>
/// Each one is bracketed by <c>// &gt;&gt; key</c> / <c>// &lt;&lt;</c> markers. The gallery reads
/// those regions back out of this file at runtime and shows them beside the output, so the code on
/// screen is the code that ran. See <see cref="DemoSource"/>.
/// </remarks>
internal static class DemoCatalog
{
    public static IReadOnlyList<Demo> All { get; } =
    [
        new("WordNet", "Report with a styled table",
            "Headings, a justified paragraph, a shaded table header, and a page-number field.",
            "word-report", WordReport),

        new("WordNet", "Mail merge",
            "Fill {{tokens}} from a dictionary, then replace text that spans runs.",
            "word-merge", WordMailMerge),

        new("ExcelNet", "Formulas that are actually evaluated",
            "SUM and AVERAGE computed by the engine, so non-Excel consumers see numbers.",
            "excel-formulas", ExcelFormulas),

        new("ExcelNet", "Conditional formatting",
            "A colour scale and a data bar across a range.",
            "excel-conditional", ExcelConditional),

        new("PowerPointNet", "Native chart",
            "A column chart whose numbers travel inside the file, drawn by the PDF exporter.",
            "ppt-chart", PowerPointChart),

        new("PowerPointNet", "HTML to slides",
            "Headings, lists and a table become a deck in one call.",
            "ppt-html", PowerPointFromHtml),

        new("PowerPointNet", "Shapes, gradients and effects",
            "Autoshapes with gradient fills, outlines, shadow and glow.",
            "ppt-shapes", PowerPointShapes),

        new("PdfNet", "Draw a page",
            "Text, alignment, lines, rectangles and measured text on a canvas.",
            "pdf-draw", PdfDraw),

        new("PdfNet", "Annotate and watermark",
            "A watermark, a note, a highlight and a stamp.",
            "pdf-annotate", PdfAnnotate),

        new("PdfNet", "Merge and split",
            "Two documents joined, then split back into single pages.",
            "pdf-merge", PdfMerge),
    ];

    // ---- WordNet -------------------------------------------------------------------------------

    private static DemoResult WordReport()
    {
        // >> word-report
        using var document = WordDocument.Create();

        document.Section.SetPageSize("A4");
        document.Section.SetMargins(Units.Cm(2.2));

        var footer = document.Section.GetFooter().AddParagraph();
        footer.Alignment = ParagraphAlignment.Center;
        footer.AddRun("Halaman ");
        footer.AddPageNumber();
        footer.AddRun(" dari ");
        footer.AddPageCount();

        document.AddHeading("Laporan Tahunan", 0);
        document.AddParagraph("Dibuat oleh Gravicode Studios.", "Subtitle");

        document.AddHeading("Ringkasan", 1);

        var summary = document.AddParagraph();
        summary.Alignment = ParagraphAlignment.Justify;
        summary.AddRun("Pendapatan tumbuh ");
        summary.AddRun("32%", bold: true);
        summary.AddRun(" dibanding tahun sebelumnya, ditopang permintaan yang kuat di Jakarta.");

        var table = document.AddTable(new[]
        {
            new[] { "Wilayah", "2025", "2026", "Pertumbuhan" },
            new[] { "Jakarta", "1.120", "1.480", "+32%" },
            new[] { "Bandung", "860", "1.150", "+34%" },
        });

        table.Rows[0].SetShading(OfficeColor.FromRgb(0x1F, 0x38, 0x64));

        foreach (var cell in table.Rows[0].Cells)
        {
            foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
            {
                run.Format.Color = OfficeColor.White;
            }
        }
        // <<

        return Render(document, "laporan.docx",
            $"{document.Paragraphs.Count} paragraphs, {document.WordCount} words");
    }

    private static DemoResult WordMailMerge()
    {
        // >> word-merge
        using var document = WordDocument.Create();

        document.AddHeading("Surat Penawaran", 1);
        document.AddParagraph("Kepada {{nama}} di {{kota}},");
        document.AddParagraph("Terima kasih atas pesanan Anda pada tahun 2025.");

        var filled = document.MailMerge(new Dictionary<string, string>
        {
            ["nama"] = "Budi Santoso",
            ["kota"] = "Bandung",
        });

        // Word splits text across runs at arbitrary points, so a phrase you can see is often in no
        // single run. ReplaceTextAcrossRuns joins the paragraph, replaces, and redistributes.
        var replaced = document.ReplaceTextAcrossRuns("2025", "2026");
        // <<

        return Render(document, "surat.docx",
            $"{filled} tokens filled, {replaced} replacements across runs");
    }

    // ---- ExcelNet ------------------------------------------------------------------------------

    private static DemoResult ExcelFormulas()
    {
        // >> excel-formulas
        using var workbook = Workbook.Create("Penjualan");
        var sheet = workbook["Penjualan"];

        sheet.WriteHeader("A1", ["Produk", "Qty", "Harga", "Total"]);

        string[] products = ["WordNet", "ExcelNet", "PowerPointNet", "PdfNet"];

        for (var i = 0; i < products.Length; i++)
        {
            var row = i + 1;
            sheet[row, 0].Set(products[i]);
            sheet[row, 1].Set((i + 2) * 7);
            sheet[row, 2].Set(150_000.0).WithNumberFormat(NumberFormats.Rupiah);
            sheet[row, 3].SetFormula($"B{row + 1}*C{row + 1}")
                .WithNumberFormat(NumberFormats.Rupiah);
        }

        sheet["C6"].Set("TOTAL").Bold();
        sheet["C7"].Set("RATA-RATA").Bold();
        sheet["D6"].SetFormula("SUM(D2:D5)").WithNumberFormat(NumberFormats.Rupiah);
        sheet["D7"].SetFormula("AVERAGE(D2:D5)").WithNumberFormat(NumberFormats.Rupiah);

        sheet.AutoFitColumns();

        // Without this the cached results stay zero for every consumer except Excel itself.
        workbook.Recalculate();
        // <<

        return Render(workbook, "penjualan.xlsx",
            $"Total = {sheet["D6"].Number:N0}   Average = {sheet["D7"].Number:N0}");
    }

    private static DemoResult ExcelConditional()
    {
        // >> excel-conditional
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["Wilayah", "Kuartal 1", "Kuartal 2"]);

        var regions = new[] { "Jakarta", "Bandung", "Surabaya", "Medan", "Makassar" };
        var random = new Random(11);

        for (var i = 0; i < regions.Length; i++)
        {
            sheet[i + 1, 0].Set(regions[i]);
            sheet[i + 1, 1].Set(random.Next(200, 900));
            sheet[i + 1, 2].Set(random.Next(200, 900));
        }

        sheet.AddColorScale("B2:B6",
            OfficeColor.FromRgb(0xF8, 0x69, 0x6B),
            OfficeColor.FromRgb(0x63, 0xBE, 0x7B));

        sheet.AddDataBar("C2:C6", OfficeColor.FromRgb(0x63, 0x8E, 0xC6));

        sheet.Frozen = new FreezePanes(Rows: 1, Columns: 0);
        sheet.AutoFitColumns();
        // <<

        return Render(workbook, "kondisional.xlsx",
            $"{sheet.ConditionalRules.Count} conditional rules — " +
            "note the PDF export does not draw them, only Excel does");
    }

    // ---- PowerPointNet -------------------------------------------------------------------------

    private static DemoResult PowerPointChart()
    {
        // >> ppt-chart
        using var deck = Presentation.Create();

        deck.Master!.SetThemeColor("accent1", OfficeColor.FromRgb(0x1F, 0x38, 0x64));
        deck.AddTitleSlide("Laporan", "Gravicode Studios");

        // Layout 2 is "Title Only": the title sits at the top and the body is free.
        var slide = deck.AddSlide(2);
        slide.SetTitle("Pendapatan per Wilayah");

        slide.AddChart(new ChartData
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
            Legend = LegendPosition.Bottom,
        });
        // <<

        var data = slide.Charts.First().GetData();

        return Render(deck, "chart.pptx",
            $"Read back: {data.Type}, {data.Categories.Count} categories, " +
            $"{data.Series.Count} series, labels {data.ShowDataLabels}");
    }

    private static DemoResult PowerPointFromHtml()
    {
        // >> ppt-html
        const string Html = """
            <h1>Tinjauan Kuartal</h1>
            <p>Disusun oleh <strong>Gravicode Studios</strong>.</p>
            <h2>Sorotan</h2>
            <ul>
              <li>Pendapatan naik <strong>32%</strong>
                <ul><li>Jakarta memimpin pertumbuhan</li></ul>
              </li>
              <li>Biaya operasional turun 4%</li>
            </ul>
            <h2>Ringkasan Angka</h2>
            <table>
              <tr><th>Wilayah</th><th>2025</th><th>2026</th></tr>
              <tr><td>Jakarta</td><td>1.120</td><td>1.480</td></tr>
              <tr><td>Bandung</td><td>860</td><td>1.150</td></tr>
            </table>
            """;

        using var deck = HtmlToSlides.CreatePresentation(Html, new HtmlSlideOptions
        {
            TitleSlide = "Dari HTML ke PowerPoint",
            SubtitleSlide = "Satu panggilan, tanpa penyuntingan manual",
            SplitOnHeadingLevel = 2,
        });
        // <<

        return Render(deck, "dari-html.pptx", $"{deck.SlideCount} slides generated from HTML");
    }

    private static DemoResult PowerPointShapes()
    {
        // >> ppt-shapes
        using var deck = Presentation.Create();

        // Layout 3 is "Blank".
        var slide = deck.AddSlide(3);

        slide.AddShape(ShapeGeometry.RoundedRectangle,
                Units.Inches(0.8), Units.Inches(1.2), Units.Inches(3.6), Units.Inches(1.8))
            .WithGradientFill(
                OfficeColor.FromRgb(0x1F, 0x38, 0x64),
                OfficeColor.FromRgb(0x63, 0x8E, 0xC6),
                GradientDirection.DiagonalDown)
            .WithShadow(blur: Units.Pt(12));

        slide.AddShape(ShapeGeometry.Ellipse,
                Units.Inches(5), Units.Inches(1.2), Units.Inches(2.2), Units.Inches(2.2))
            .WithFill(OfficeColor.FromRgb(0xE8, 0x71, 0x22))
            .WithOutline(OfficeColor.White, Units.Pt(3))
            .WithGlow(OfficeColor.FromRgb(0xFF, 0xC0, 0x66), Units.Pt(8));

        var caption = slide.AddTextBox("Bentuk, gradien, dan efek",
            Units.Inches(0.8), Units.Inches(3.4), Units.Inches(6.4), Units.Inches(0.8));

        var run = caption.TextFrame!.Paragraphs[0].Runs[0];
        run.Bold = true;
        run.FontSize = Units.Pt(20);
        run.Color = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
        // <<

        return Render(deck, "bentuk.pptx", $"{slide.Shapes.Count} shapes on the slide");
    }

    // ---- PdfNet --------------------------------------------------------------------------------

    private static DemoResult PdfDraw()
    {
        // >> pdf-draw
        using var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            // PDF measures from the bottom-left; every Office format measures from the top.
            // TopDown converts once here rather than at each call site.
            canvas.TopDown = true;

            canvas.SetFillColor(OfficeColor.FromRgb(0x1F, 0x38, 0x64));
            canvas.Rectangle(0, 0, page.Width, 92).Fill();

            canvas.SetFont(StandardFont.HelveticaBold, 26);
            canvas.SetFillColor(OfficeColor.White);
            canvas.DrawText("Laporan Tahunan", 56, 58);

            canvas.SetFont(StandardFont.Helvetica, 11);
            canvas.SetFillColor(OfficeColor.Black);
            canvas.DrawText(
                "Pendapatan tumbuh 32% dibanding tahun sebelumnya, ditopang permintaan yang " +
                "kuat di Jakarta dan Bandung. Seluruh wilayah menutup tahun di atas target.",
                56, 140, page.Width - 112, PdfAlignment.Justify);

            var y = 210.0;

            foreach (var (label, value) in new[]
                     {
                         ("Jakarta", 1480.0), ("Bandung", 1150.0),
                         ("Surabaya", 905.0), ("Medan", 520.0),
                     })
            {
                canvas.SetFillColor(OfficeColor.FromRgb(0x63, 0x8E, 0xC6));
                canvas.Rectangle(150, y, value / 4, 18).Fill();

                canvas.SetFillColor(OfficeColor.Black);
                canvas.SetFont(StandardFont.Helvetica, 10);
                canvas.DrawText(label, 56, y + 13);
                canvas.DrawText($"{value:N0}", 158 + value / 4, y + 13);

                y += 28;
            }
        }

        document.Info.Title = "Laporan Tahunan";
        // <<

        return Render(document, "gambar.pdf",
            $"{document.Pages.Count} page, {document.ExtractText().Length} characters extractable");
    }

    private static DemoResult PdfAnnotate()
    {
        // >> pdf-annotate
        using var source = WordDocument.Create();
        source.AddHeading("Dokumen untuk Ditinjau", 1);
        source.AddParagraph("Isi yang perlu dibaca dan dikomentari sebelum rilis.");

        using var document = source.ToPdf();
        var page = document.Pages[0];

        page.AddWatermark("DRAF", OfficeColor.Gray, opacity: 0.12);
        page.AddTextNote(430, 120, "Perlu ditinjau sebelum rilis.", author: "Kang Fadhil");

        // Quad points go upper-left, upper-right, lower-left, lower-right. Clockwise — the order
        // that feels right — draws a bowtie.
        page.AddHighlight([new PdfRectangle(56, 690, 300, 712)],
            OfficeColor.FromRgb(0xFF, 0xF0, 0x00));

        page.AddStamp(new PdfRectangle(400, 640, 540, 690), "DISETUJUI", OfficeColor.Green);
        // <<

        return Render(document, "anotasi.pdf",
            $"{page.GetAnnotations().Count} annotations on page 1");
    }

    private static DemoResult PdfMerge()
    {
        // >> pdf-merge
        using var first = WordDocument.Create();
        first.AddHeading("Bagian Satu", 1);
        first.AddParagraph("Halaman pertama, dari WordNet.");

        using var second = WordDocument.Create();
        second.AddHeading("Bagian Dua", 1);
        second.AddParagraph("Halaman kedua, juga dari WordNet.");

        using var merged = first.ToPdf();
        using var addition = second.ToPdf();

        // Merging rewrites every object number in the imported document, so two files that both
        // call their font object "7 0 R" do not collide.
        merged.Merge(addition);

        var parts = merged.Split();
        // <<

        var log = $"Merged to {merged.Pages.Count} pages, split back into {parts.Count} documents";

        foreach (var part in parts)
        {
            part.Dispose();
        }

        return Render(merged, "gabungan.pdf", log);
    }

    // ---- Rendering ---------------------------------------------------------------------------

    private static DemoResult Render(WordDocument document, string fileName, string log)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        var bytes = stream.ToArray();

        return new DemoResult(DocumentRenderer.RenderWord(document, Preview), log, fileName, bytes);
    }

    private static DemoResult Render(Workbook workbook, string fileName, string log)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream);
        var bytes = stream.ToArray();

        return new DemoResult(DocumentRenderer.RenderExcel(workbook, Preview), log, fileName, bytes);
    }

    private static DemoResult Render(Presentation deck, string fileName, string log)
    {
        using var stream = new MemoryStream();
        deck.Save(stream);
        var bytes = stream.ToArray();

        return new DemoResult(
            DocumentRenderer.RenderPowerPoint(deck, Preview), log, fileName, bytes);
    }

    private static DemoResult Render(PdfDocument document, string fileName, string log)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        var bytes = stream.ToArray();

        return new DemoResult(DocumentRenderer.RenderPdf(document, Preview), log, fileName, bytes);
    }

    /// <summary>Preview quality: sharp enough to read, small enough to render while you watch.</summary>
    private static RenderOptions Preview => new()
    {
        Dpi = 110,
        MaxPixels = 1000,
        DrawPageBorder = false,
    };
}
