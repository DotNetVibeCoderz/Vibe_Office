// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.DataFrames;
using ExcelNet.Io;
using ExcelNet.Styles;
using ExcelNet;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core;
using OfficeNet.Rendering;
using OfficeNet;
using PdfNet.Annotations;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Forms;
using PowerPointNet.Charts;
using PowerPointNet.Html;
using PowerPointNet.Shapes;
using PowerPointNet;
using WordNet.Notes;
using WordNet.Sections;
using WordNet.Styles;
using WordNet;
using Xunit;

namespace OfficeNet.Docs.Tests;

/// <summary>
/// Every code sample in <c>docs/</c>, compiled and run.
/// </summary>
/// <remarks>
/// <para>
/// Documentation that does not compile is worse than no documentation: it looks authoritative and
/// wastes the reader's afternoon. These tests are the samples themselves — when an API is renamed,
/// this project stops building and the docs get fixed with the code rather than months later.
/// </para>
/// <para>
/// The assertions are deliberately light. The point is that the sample compiles and does not throw;
/// the behaviour it demonstrates is covered by the per-library test projects.
/// </para>
/// </remarks>
public class DocSamples : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "officenet-docs-" + Guid.NewGuid().ToString("N")[..8]);

    public DocSamples() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A handle left open by a failing test must not replace its failure with this one.
        }
    }

    private string Path_(string name) => System.IO.Path.Combine(_directory, name);

    // ---- docs/WordNet.md -----------------------------------------------------------------------

    [Fact]
    public void WordNet_ParagraphsRunsHeadingsListsAndStyles()
    {
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph();
        paragraph.Alignment = ParagraphAlignment.Justify;
        paragraph.AddRun("Pendapatan tumbuh ");
        paragraph.AddRun("32%", bold: true);
        paragraph.AddRun(" dibanding tahun sebelumnya.");

        var run = paragraph.Runs[0];
        run.Format.Bold = true;
        run.Format.Italic = true;
        run.Format.Underline = UnderlineStyle.Single;
        run.Format.FontSize = Units.Pt(14);
        run.Format.Color = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
        run.Format.FontName = "Calibri";
        run.Format.Highlight = "yellow";

        document.AddHeading("Laporan Tahunan", 0);
        document.AddHeading("Ringkasan", 1);

        document.AddList(["WordNet — penulisan ulang python-docx", "ExcelNet"]);
        document.AddList(["Pertama", "Kedua"], numbered: true);
        document.AddList(["Sub-butir"], level: 1);

        document.AddPageBreak();
        document.AddTableOfContents(levels: 3);

        document.AddParagraph("Kutipan.", "Quote");

        var style = document.Styles.GetOrAdd("Catatan", "Catatan", StyleType.Paragraph, basedOn: "Normal");
        style.RunFormat.FontSize = Units.Pt(9);
        style.RunFormat.Italic = true;
        document.AddParagraph("Catatan kaki.", "Catatan");

        Assert.NotNull(document.Styles["Catatan"]);
    }

    [Fact]
    public void WordNet_Tables()
    {
        using var document = WordDocument.Create();

        var table = document.AddTable(new[]
        {
            new[] { "Wilayah", "2025", "2026", "Pertumbuhan" },
            new[] { "Jakarta", "1.120", "1.480", "+32%" },
        });

        table.Rows[0].SetShading(OfficeColor.FromRgb(0x1F, 0x38, 0x64));

        foreach (var cell in table.Rows[0].Cells)
        {
            foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
            {
                run.Format.Color = OfficeColor.White;
            }
        }

        var grid = document.AddTable(rows: 3, columns: 2);
        grid[0, 0].Text = "Nama";
        grid[0, 1].Text = "Nilai";
        grid.SetColumnWidth(0, Units.Cm(4));
        grid.MergeCells(firstRow: 1, firstColumn: 0, lastRow: 1, lastColumn: 1);

        Assert.Equal("Nama", grid[0, 0].Text);
    }

    [Fact]
    public void WordNet_SectionsHeadersFootersAndFields()
    {
        using var document = WordDocument.Create();

        var section = document.Section;
        section.SetPageSize("A4");
        section.SetMargins(Units.Cm(2.2));
        section.Orientation = PageOrientation.Landscape;

        section.GetHeader().AddParagraph("Gravicode Studios").Alignment = ParagraphAlignment.Right;

        var footer = section.GetFooter().AddParagraph();
        footer.Alignment = ParagraphAlignment.Center;
        footer.AddRun("Halaman ");
        footer.AddPageNumber();
        footer.AddRun(" dari ");
        footer.AddPageCount();

        document.AddSection(SectionStart.NextPage);

        Assert.Equal(2, document.Sections.Count);
    }

    [Fact]
    public void WordNet_NotesAndComments()
    {
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph("Pendapatan tumbuh 32% pada 2026.");

        var note = paragraph.AddFootnote("Sumber: laporan internal, Januari 2026.");
        note.AddParagraph("Angka telah diaudit.");

        paragraph.AddEndnote("Lihat lampiran B.");
        paragraph.AddComment("Tolong konfirmasi angkanya.", "Kang Fadhil");

        var run = paragraph.AddRun("angka ini");
        run.AddComment("Dari mana asalnya?", "Kang Fadhil");

        foreach (var footnote in document.Footnotes.All)
        {
            _ = $"{footnote.Id}: {footnote.Text}";
        }

        _ = document.Comments.ByAuthor("Kang Fadhil").Count();

        Assert.Single(document.Footnotes.All);
        Assert.Single(document.Endnotes.All);
        Assert.Equal(2, document.Comments.Count);

        Assert.True(document.Footnotes.Remove(note.Id));
        Assert.Empty(document.Footnotes.All);
    }

    [Fact]
    public void WordNet_ReplaceMailMergeAndRead()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Tahun 2025 untuk {{nama}} di {{kota}}.");

        document.ReplaceText("2025", "2026");
        document.ReplaceTextAcrossRuns("Tahun", "Periode");

        document.MailMerge(new Dictionary<string, string>
        {
            ["nama"] = "Budi",
            ["kota"] = "Bandung",
        });

        Assert.Contains("Budi", document.ExtractText(), StringComparison.Ordinal);
        Assert.Contains("2026", document.ExtractText(), StringComparison.Ordinal);
        Assert.True(document.WordCount > 0);

        foreach (var paragraph in document.Paragraphs)
        {
            _ = $"[{paragraph.StyleId}] {paragraph.Text}";
        }

        foreach (var table in document.Tables)
        {
            foreach (var row in table.Rows)
            {
                _ = string.Join(" | ", row.Cells.Select(c => c.Text));
            }
        }

        _ = document.AllParagraphs.Count();
    }

    [Fact]
    public void WordNet_MetadataAndPdfExport()
    {
        var path = Path_("laporan.pdf");

        using var document = WordDocument.Create();
        document.Properties.Title = "Laporan Tahunan";
        document.Properties.Creator = "Gravicode Studios";
        document.Properties.Keywords = "laporan; 2026";
        document.Properties.Category = "Internal";

        document.AddHeading("Laporan", 0);
        document.AddParagraph("Isi.");

        document.SaveAsPdf(path, new WordNet.Export.PdfExportOptions
        {
            Watermark = "DRAF",
            WatermarkOpacity = 0.12,
            IncludeHeadersAndFooters = true,
        });

        using var pdf = document.ToPdf();

        Assert.True(File.Exists(path));
        Assert.True(pdf.Pages.Count > 0);
    }

    // ---- docs/ExcelNet.md ----------------------------------------------------------------------

    [Fact]
    public void ExcelNet_SheetsCellsAndBulkWrites()
    {
        using var workbook = Workbook.Create("Penjualan");

        var sheet = workbook["Penjualan"];
        _ = workbook[0];
        _ = workbook.Find("Arsip");

        workbook.AddSheet("Ringkasan");
        workbook.AddSheetUnique("Data");
        workbook.CopySheet("Penjualan", "Penjualan (salinan)");
        workbook.MoveSheet("Ringkasan", 0);
        workbook.RemoveSheet("Data");

        sheet["A1"].Set("Tanggal");
        sheet[0, 1].Set("Produk");
        sheet["B2"].Set(42);
        sheet["B3"].Set(3.14);
        sheet["B4"].Set(DateTime.Now);
        sheet["B5"].Set(true);
        sheet["B6"].Set("teks");
        sheet["B7"].Set(null);

        _ = sheet["B6"].Text;
        _ = sheet["B2"].Number;
        _ = sheet["B4"].DateTime;
        Assert.True(sheet["Z99"].IsEmpty);

        sheet.WriteHeader("A10", ["Tanggal", "Produk", "Qty", "Total"]);
        sheet.WriteRow("A11", DateTime.Today, "WordNet", 12, 480_000);
        sheet.WriteColumn("F1", "Jan", "Feb", "Mar");
        sheet.WriteRange("A20", new object?[][] { ["a", 1], ["b", 2] });
    }

    [Fact]
    public void ExcelNet_FormulasAreEvaluated()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        for (var row = 1; row <= 3; row++)
        {
            sheet[row, 3].Set(2);
            sheet[row, 4].Set(1000);
            sheet[row, 5].SetFormula($"D{row + 1}*E{row + 1}");
        }

        sheet["F16"].SetFormula("SUM(F2:F15)");
        sheet["F17"].SetFormula("AVERAGE(F2:F15)");
        sheet["G2"].SetFormula("IF(F2>1000000,\"Besar\",\"Kecil\")");
        sheet["A1"].SetFormula("1/0");
        sheet["A2"].SetFormula("IFERROR(A1,\"n/a\")");

        workbook.Recalculate();

        Assert.Equal(6000, sheet["F16"].Number);
        Assert.Equal("n/a", sheet["A2"].Text);
    }

    [Fact]
    public void ExcelNet_StylesRangesAndLayout()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet["A1"].Set("Judul").Bold();
        sheet["A1"].WithBackground(OfficeColor.FromRgb(0xFF, 0xF2, 0xCC));
        sheet["B2"].Set(1000).WithNumberFormat(NumberFormats.Rupiah);

        var header = CellStyle.Default
            .Bold()
            .WithColor(OfficeColor.White)
            .WithBackground(OfficeColor.FromRgb(0x1F, 0x38, 0x64))
            .WithAlignment(HorizontalAlignment.Center)
            .WithBorder(CellBorder.All(BorderLineStyle.Thin));

        var range = sheet.Range("A1:D10");
        range.Fill(0);
        range.ApplyStyle(header);
        range.ModifyStyle(s => s.Bold());
        range.SetOutlineBorder(BorderLineStyle.Medium);

        _ = range.ToArray();

        foreach (var cell in range)
        {
            _ = $"{cell.Address}: {cell.Text}";
        }

        sheet.SetColumnWidth("A", 18);
        sheet.AutoFitColumns();
        sheet.SetRowHeight(0, 22);
        sheet.HideColumn(3);

        sheet.Frozen = new FreezePanes(1, 0);
        sheet.TabColor = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
        sheet.ShowGridLines = false;

        sheet.AddColorScale("D2:D15",
            OfficeColor.FromRgb(0xF8, 0x69, 0x6B),
            OfficeColor.FromRgb(0x63, 0xBE, 0x7B));

        sheet.AddDataBar("E2:E15", OfficeColor.FromRgb(0x63, 0x8E, 0xC6));
    }

    [Fact]
    public void ExcelNet_Charts()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["Wilayah", "2026"]);
        sheet.WriteRow("A2", "Jakarta", 1480);
        sheet.WriteRow("A3", "Bandung", 1150);

        sheet.AddChart(new ChartData
        {
            Type = ChartType.Column,
            Title = "Pendapatan per Wilayah",
            Categories = ["Jakarta", "Bandung", "Surabaya", "Medan"],
            Series =
            [
                new ChartSeries("2025", [1120, 860, 740, 410]),
                new ChartSeries("2026", [1480, 1150, 905, 520]),
            ],
            ValueFormat = "#,##0",
            ShowDataLabels = true,
        }, "E2:M20");

        foreach (var chart in sheet.Charts)
        {
            var data = chart.GetData();
            _ = $"{data.Type} at {chart.Anchor.A1}, {data.Series.Count} series";

            chart.SetData(data with { Type = ChartType.Bar });
        }

        Assert.Single(sheet.Charts);
        Assert.Equal(ChartType.Bar, sheet.Charts[0].GetData().Type);
    }

    [Fact]
    public void ExcelNet_CsvJsonAndDataFrames()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["Nama", "Nilai"]);
        sheet.WriteRow("A2", "Budi", 90);
        sheet.WriteRow("A3", "Siti", 95);

        var csv = Path_("data.csv");
        CsvIo.Export(sheet, csv);
        CsvIo.Export(sheet, Path_("id.csv"), CsvOptions.Indonesian);
        CsvIo.Import(workbook, csv, "Impor");

        var json = JsonIo.ExportText(sheet);
        JsonIo.Import(workbook, json, "ImporJson");

        var frame = sheet.ToDataFrame();
        _ = sheet.Describe();
        workbook.WriteDataFrame(frame, "Hasil");

        Assert.NotNull(workbook.Find("Hasil"));
    }

    [Fact]
    public void ExcelNet_PdfExportAndReading()
    {
        var path = Path_("data.xlsx");

        using (var workbook = Workbook.Create("Data"))
        {
            workbook["Data"]["A1"].Set("Halo");
            workbook.Save(path);
        }

        using var reopened = Workbook.Open(path);

        reopened.SaveAsPdf(Path_("data.pdf"), new ExcelPdfOptions
        {
            ShowGridLines = true,
            RepeatHeaderRow = true,
        });

        foreach (var sheet in reopened)
        {
            _ = $"{sheet.Name}: {sheet.RowCount} x {sheet.ColumnCount}";

            foreach (var cell in sheet.UsedCells)
            {
                _ = $"  {cell.Address} = {cell.Text}";
            }
        }

        Assert.True(File.Exists(Path_("data.pdf")));
    }

    // ---- docs/PowerPointNet.md -----------------------------------------------------------------

    [Fact]
    public void PowerPointNet_SlidesTextAndShapes()
    {
        using var deck = Presentation.Create();

        deck.UseWidescreen();

        deck.AddTitleSlide("OfficeNet", "Word, Excel, PowerPoint dan PDF untuk .NET 10");
        deck.AddBulletSlide("Komponen", ["WordNet", "ExcelNet"]);
        deck.AddSectionSlide("Bagian II");

        var slide = deck.AddSlide(layoutIndex: 1);
        slide.SetTitle("Judul");
        slide.SetBody(["Butir satu", "Butir dua"]);

        var box = slide.AddTextBox("Halo", Units.Inches(1), Units.Inches(2),
            Units.Inches(4), Units.Inches(1));

        box.TextFrame!.Text = "Baris pertama\nBaris kedua";

        var paragraph = box.TextFrame.AddParagraph("Butir");
        paragraph.Level = 1;
        paragraph.Alignment = PowerPointNet.Shapes.TextAlignment.Center;

        var run = paragraph.AddRun("tebal");
        run.Bold = true;
        run.FontSize = Units.Pt(24);
        run.Color = OfficeColor.FromRgb(0x1F, 0x38, 0x64);

        var shape = slide.AddShape(ShapeGeometry.RoundedRectangle,
            Units.Inches(1), Units.Inches(1), Units.Inches(3), Units.Inches(1.5));

        shape.WithFill(OfficeColor.FromRgb(0x1F, 0x38, 0x64))
             .WithOutline(OfficeColor.White, Units.Pt(2))
             .WithShadow(blur: Units.Pt(8))
             .WithHyperlink("https://github.com/DotNetVibeCoderz/Vibe_Office");

        slide.AddShape(ShapeGeometry.Ellipse, Units.Inches(5), Units.Inches(1),
                Units.Inches(2), Units.Inches(2))
            .WithGradientFill(
                OfficeColor.FromRgb(0x1F, 0x38, 0x64),
                OfficeColor.FromRgb(0x63, 0x8E, 0xC6),
                GradientDirection.DiagonalDown)
            .WithGlow(OfficeColor.White, Units.Pt(6));

        deck.DuplicateSlide(0);
        deck.MoveSlide(1, 0);
        deck.ReverseSlides();

        Assert.True(deck.SlideCount > 3);
    }

    [Fact]
    public void PowerPointNet_TablesAndCharts()
    {
        using var deck = Presentation.Create();
        var slide = deck.AddSlide(2);   // Title Only

        var table = slide.AddTable(rows: 3, columns: 3,
            Units.Inches(1), Units.Inches(1.9),
            deck.SlideWidth - Units.Inches(2), Units.Inches(2.6));

        table.SetData(new[]
        {
            new[] { "Komponen", "Analog Python", "Status" },
            new[] { "WordNet", "python-docx", "Selesai" },
        });

        table[0, 0].Text = "Komponen";

        var chartSlide = deck.AddSlide(2);

        chartSlide.AddChart(new ChartData
        {
            Type = ChartType.Column,
            Categories = ["Jakarta", "Bandung"],
            Series =
            [
                new ChartSeries("2025", [1120, 860]),
                new ChartSeries("2026", [1480, 1150]),
            ],
            ValueAxisTitle = "Juta Rupiah",
            ValueFormat = "#,##0",
            ShowDataLabels = true,
            Legend = LegendPosition.Bottom,
        });

        _ = ChartData.Simple(ChartType.Pie, "Pangsa", ["A", "B", "C"], [50, 30, 20]);

        var chart = chartSlide.Charts.First();
        var data = chart.GetData();
        chart.SetData(data with { Type = ChartType.Line });

        Assert.Equal(ChartType.Line, chart.GetData().Type);
    }

    [Fact]
    public void PowerPointNet_HtmlToSlides()
    {
        const string Html = """
            <h1>Tinjauan Kuartal</h1>
            <p>Disusun oleh <strong>Gravicode Studios</strong>.</p>
            <h2>Sorotan</h2>
            <ul><li>Pendapatan naik <strong>32%</strong></li></ul>
            <h2>Angka</h2>
            <table><tr><th>Wilayah</th><th>2026</th></tr><tr><td>Jakarta</td><td>1.480</td></tr></table>
            """;

        using var deck = HtmlToSlides.CreatePresentation(Html, new HtmlSlideOptions
        {
            TitleSlide = "Tinjauan Kuartal",
            SubtitleSlide = "Disusun otomatis",
            SplitOnHeadingLevel = 2,
            MaxLinesPerSlide = 9,
        });

        using var appended = Presentation.Create();
        HtmlToSlides.Convert(appended, Html);
        HtmlToSlides.TableToSlides(appended, Html);

        Assert.True(deck.SlideCount >= 3);
    }

    [Fact]
    public void PowerPointNet_TransitionsNotesThemesAndPdf()
    {
        using var deck = Presentation.Create();
        var slide = deck.AddTitleSlide("Judul", "Sub");

        slide.SetTransition(SlideTransition.Fade, TimeSpan.FromSeconds(0.7));
        slide.Notes = "Sebutkan angka pertumbuhan di sini.";
        slide.BackgroundColor = OfficeColor.FromRgb(0xF2, 0xF2, 0xF2);

        deck.Master!.SetThemeColor("accent1", OfficeColor.FromRgb(0x1F, 0x38, 0x64));

        foreach (var s in deck.Slides)
        {
            _ = $"#{s.SlideNumber} {s.Title?.TextFrame?.Text}";
            _ = s.Tables.Count();
            _ = s.Charts.Count();
            _ = s.Pictures.Count();
        }

        deck.SaveAsPdf(Path_("deck.pdf"), new PowerPointNet.Export.SlidePdfOptions
        {
            IncludeHiddenSlides = false,
            IncludeImages = true,
        });

        Assert.True(File.Exists(Path_("deck.pdf")));
    }

    // ---- docs/PdfNet.md ------------------------------------------------------------------------

    [Fact]
    public void PdfNet_PagesMergeSplitAndDraw()
    {
        using var document = PdfDocument.Create();

        var page = document.Pages.Add(PageSize.A4);
        document.Pages.Add(PageSize.Letter);
        document.Pages.Add(PageSize.Points(400, 600));

        var first = document.Pages[0];
        first.Rotation = 90;
        _ = first.Width;
        _ = first.MediaBox;

        using (var canvas = page.OpenCanvas())
        {
            canvas.TopDown = true;

            canvas.SetFont(StandardFont.HelveticaBold, 24);
            canvas.SetFillColor(OfficeColor.FromRgb(0x1F, 0x38, 0x64));
            canvas.DrawText("Laporan", 72, 72);

            canvas.SetFont(StandardFont.Helvetica, 11);
            canvas.SetFillColor(OfficeColor.Black);
            canvas.DrawText("Rata kiri-kanan.", 72, 110, 450, PdfNet.Content.TextAlignment.Justify);

            canvas.SetStrokeColor(OfficeColor.Gray);
            canvas.SetLineWidth(0.5);
            canvas.MoveTo(72, 130).LineTo(522, 130).Stroke();

            canvas.SetFillColor(OfficeColor.FromRgb(0xFF, 0xF2, 0xCC));
            canvas.Rectangle(72, 150, 200, 60).Fill();
            canvas.RoundedRectangle(72, 230, 200, 60, 8).Stroke();

            _ = canvas.MeasureText("Berapa lebarnya?");
        }

        document.Pages.RemoveAt(2);
        document.Pages.Move(0, 1);

        document.Info.Title = "Laporan Tahunan";
        document.Info.Author = "Gravicode Studios";

        var path = Path_("drawn.pdf");
        document.Save(path);
        _ = document.ToArray();

        using var a = PdfDocument.Open(path);
        using var b = PdfDocument.Open(path);

        a.Merge(b);
        a.MergeRange(b, 0, 1);

        var singles = a.Split();
        var chunks = a.Split(pagesPerPart: 2);

        Assert.True(singles.Count > 1);
        Assert.NotEmpty(chunks);

        foreach (var part in singles) { part.Dispose(); }
        foreach (var part in chunks) { part.Dispose(); }
    }

    [Fact]
    public void PdfNet_ExtractEncryptAndAnnotate()
    {
        var path = Path_("source.pdf");

        using (var document = WordDocument.Create())
        {
            document.AddParagraph("Teks yang bisa diekstrak.");
            document.SaveAsPdf(path);
        }

        using var pdf = PdfDocument.Open(path);

        Assert.Contains("diekstrak", pdf.ExtractText(), StringComparison.Ordinal);
        _ = pdf.Pages[0].ExtractText();
        _ = pdf.WasEncrypted;
        _ = pdf.WasRepaired;

        foreach (var fragment in pdf.Pages[0].ExtractTextFragments())
        {
            _ = $"{fragment.Text} @ ({fragment.X}, {fragment.Y}) {fragment.FontSize}pt";
        }

        foreach (var image in pdf.Pages[0].ExtractImages())
        {
            _ = $"{image.Name}: {image.Width}x{image.Height} {image.Extension}";
        }

        var page = pdf.Pages[0];
        var area = new PdfRectangle(72, 680, 200, 696);

        page.AddTextNote(72, 700, "Perlu ditinjau.", "Kang Fadhil");
        page.AddLink(area, "https://gravicode.com");
        page.AddHighlight([area], OfficeColor.FromRgb(0xFF, 0xF0, 0x00));
        page.AddUnderline([area], OfficeColor.Blue);
        page.AddStrikeOut([area], OfficeColor.Red);
        page.AddStamp(area, "DISETUJUI", OfficeColor.Green);
        page.AddWatermark("DRAF", OfficeColor.Gray, 0.12);

        _ = page.GetAnnotations();
        page.RemoveAnnotations(a => a.AnnotationType == PdfAnnotationType.Link);

        _ = AcroForm.Open(pdf);
        var form = AcroForm.OpenOrCreate(pdf);
        _ = form.Fields.Count;
        form.Flatten();

        pdf.Encrypt("rahasia");
        pdf.Save(Path_("encrypted.pdf"));

        using var locked = PdfDocument.Open(Path_("encrypted.pdf"), "rahasia");
        Assert.True(locked.WasEncrypted);
    }

    // ---- docs/Core.md --------------------------------------------------------------------------

    [Fact]
    public void Core_UnitsColourPackageAndImages()
    {
        var width = Units.Cm(2.5);
        var margin = Units.Inches(1);

        _ = Units.Pt(11);
        _ = Units.Twips(720);
        _ = Units.Px(96);
        _ = Length.FromPixels(96, 300);

        _ = width.Centimeters;
        _ = width.Points;
        _ = width.Twips;
        _ = width.Emu;

        _ = width + margin;
        _ = width / 2;

        // Quantisation: 1.5 cm is 850 twips, which is not exactly 1.5 cm coming back.
        var oneAndAHalf = Units.Cm(1.5);
        Assert.Equal(850, oneAndAHalf.Twips);

        var navy = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
        Assert.Equal("1F3864", navy.ToHex());
        Assert.Equal(navy, OfficeColor.Parse("#1F3864"));
        Assert.True(OfficeColor.TryParse("1F3864", out _));

        _ = OfficeColor.FromTheme(ThemeColor.Accent1, 0.4);
        _ = OfficeColor.Automatic.ToHex();

        var path = Path_("package.docx");

        using (var document = WordDocument.Create())
        {
            document.Properties.Title = "Laporan Tahunan";
            document.Properties.Creator = "Gravicode Studios";
            document.Custom["Departemen"] = "Riset";
            document.AddParagraph("Isi.");
            document.Save(path);
        }

        using var package = OpcPackage.Open(path);

        foreach (var part in package.Parts)
        {
            _ = $"{part.Name} — {part.ContentType} ({part.GetBytes().Length} bytes)";
        }

        var documentPart = package.MainDocumentPart;

        Assert.NotNull(documentPart);
        Assert.NotNull(documentPart.Xml.Root);

        var info = ImageInfo.Read(TinyPng());
        Assert.Equal(1, info.PixelWidth);
    }

    // ---- docs/OfficeNet.md ---------------------------------------------------------------------

    [Fact]
    public void Office_DetectOpenExtractAndConvert()
    {
        var docx = Path_("berkas.docx");

        using (var document = WordDocument.Create())
        {
            document.AddParagraph("Halo dunia.");
            document.Save(docx);
        }

        Assert.Equal(OfficeFormat.Word, Office.DetectFormat(docx));
        Assert.True(Office.IsSupportedExtension(docx));
        Assert.NotEmpty(Office.SupportedExtensions);

        using (var opened = Office.Open(docx))
        {
            _ = opened.ExtractText();
            _ = opened.Properties.Title;

            switch (opened)
            {
                case WordDocument word:
                    _ = word.WordCount;
                    break;

                case Workbook workbook:
                    _ = workbook.Count;
                    break;

                case Presentation deck:
                    _ = deck.SlideCount;
                    break;
            }
        }

        Assert.Contains("Halo", Office.ExtractText(docx), StringComparison.Ordinal);

        var pdf = Office.ConvertToPdf(docx, Path_("berkas.pdf"));
        Assert.True(File.Exists(pdf));

        // Line endings are '\n' on every platform, which is what makes extracted text hashable.
        Assert.DoesNotContain('\r', Office.ExtractText(docx));
    }

    // ---- docs/Rendering.md ---------------------------------------------------------------------

    [Fact]
    public void Rendering_AnyFormatToImages()
    {
        var docx = Path_("render.docx");

        using (var document = WordDocument.Create())
        {
            document.AddHeading("Judul", 1);
            document.AddParagraph("Isi.");
            document.Save(docx);
        }

        var pages = DocumentRenderer.Render(docx);
        var files = DocumentRenderer.RenderToFiles(docx, _directory);
        var thumb = DocumentRenderer.RenderThumbnail(docx, 400);

        Assert.NotEmpty(pages);
        Assert.NotEmpty(files);
        Assert.NotEmpty(thumb);

        using var pdf = PdfDocument.Open(Office.ConvertToPdf(docx, Path_("render.pdf")));

        _ = DocumentRenderer.RenderPage(pdf.Pages[0], new RenderOptions
        {
            Dpi = 150,
            Format = RenderFormat.Png,
            Quality = 90,
            Background = OfficeColor.White,
            MaxPixels = 4000,
            DrawPageBorder = true,
        });

        _ = DocumentRenderer.RenderPdf(pdf);

        using var document2 = WordDocument.Open(docx);
        _ = DocumentRenderer.RenderWord(document2);
    }

    // ---- Remaining documented members ----------------------------------------------------------

    [Fact]
    public void Documented_MembersThatTheOtherSamplesDoNotTouch()
    {
        // Every API named in docs/ but not exercised above. Grouped rather than split, because the
        // value here is "the name still exists", not a behavioural assertion.
        using var document = WordDocument.Create();
        document.AddPicture(TinyPng(), width: Units.Cm(4));

        var range = document.AddTable(2, 2);
        range.Rows[0].Cells[0].Text = "x";

        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["A", "B"]);
        sheet.WriteRow("A2", 1, 2);

        sheet.AutoFilter = CellRangeReference.Parse("A1:B2");
        sheet.MergeCells("D1:E1");
        sheet.Range("F1:G1").Merge();
        sheet.AddConditionalFormat(CellRangeReference.Parse("A2:B2"), "greaterThan",
            CellStyle.Default.WithBackground(OfficeColor.FromRgb(0xC6, 0xEF, 0xCE)), "1");

        using var deck = Presentation.Create();

        _ = deck.FindLayout("Blank");
        _ = deck.Layouts.Count;
        deck.Master!.SetThemeFonts("Poppins", "Inter");

        var slide = deck.AddSlide(2);
        var shape = slide.AddTextBox("a", Units.Inches(1), Units.Inches(1),
            Units.Inches(2), Units.Inches(1));

        var paragraph = shape.TextFrame!.AddParagraph("butir");
        paragraph.HasBullet = true;

        slide.AnimateOnClick(AnimationEffect.Fade, shape);
        slide.IsHidden = true;

        var table = slide.AddTable(2, 2, Units.Inches(1), Units.Inches(3),
            Units.Inches(4), Units.Inches(1));

        table.SetColumnWidth(0, Units.Inches(3));

        _ = ChartData.FromMap(ChartType.Bar, "Penjualan",
            new Dictionary<string, double> { ["A"] = 1, ["B"] = 2 });

        var html = Path_("halaman.html");
        File.WriteAllText(html, "<h1>Judul</h1><p>Isi</p>");

        using var fromFile = HtmlToSlides.CreatePresentationFromFile(html);
        Assert.True(fromFile.SlideCount > 0);
    }

    /// <summary>A one-pixel PNG, so the image tests need no fixture file.</summary>
    private static byte[] TinyPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];
}
