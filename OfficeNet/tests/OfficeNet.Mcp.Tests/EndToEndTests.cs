// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;
using ExcelNet;
using ModelContextProtocol.Protocol;
using OfficeNet.TestKit;
using PdfNet.Document;
using PowerPointNet;
using WordNet;
using Xunit;

namespace OfficeNet.Mcp.Tests;

/// <summary>
/// Every kind of document, created and changed through the protocol, then opened again by the
/// libraries — and the packages checked by the independent OPC validator.
/// </summary>
public class EndToEndTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private string Full(string relative) => Path.Combine(server.Root.Path, relative);

    private Task<CallToolResult> Call(string tool, object arguments) =>
        server.Client.CallToolAsync(tool, ToDictionary(arguments),
            cancellationToken: new CancellationTokenSource(ServerFixture.Deadline).Token).AsTask();

    /// <summary>Arguments as a client sends them: JSON, not CLR objects.</summary>
    private static Dictionary<string, object?> ToDictionary(object arguments) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments))!
            .ToDictionary(p => p.Key, p => (object?)p.Value);

    private static void AssertValidPackage(string path)
    {
        var report = OpcValidator.Validate(path);
        Assert.True(report.IsValid, report.Describe());
    }

    // ---- The catalogue -------------------------------------------------------------------------

    [Fact]
    public async Task EveryToolIsListedWithADescriptionAndHints()
    {
        var tools = await server.Client.ListToolsAsync(cancellationToken: new CancellationTokenSource(ServerFixture.Deadline).Token);
        var names = tools.Select(t => t.Name).ToHashSet();

        foreach (var expected in (string[])
                 [
                     "word_create", "word_append", "word_fill_template", "word_replace_text",
                     "word_track_changes", "word_protect", "word_watermark", "word_merge", "word_split",
                     "excel_create", "excel_write_cells", "excel_read", "excel_add_chart",
                     "pptx_create", "pptx_from_html", "pdf_merge", "pdf_split", "pdf_protect",
                     "convert", "extract_text", "document_info", "render_page", "list_files",
                 ])
        {
            Assert.Contains(expected, names);
        }

        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description), $"{t.Name} has no description"));

        // A client uses these to decide what to ask the user before running.
        var read = tools.Single(t => t.Name == "extract_text").ProtocolTool.Annotations;
        Assert.True(read?.ReadOnlyHint);
        var write = tools.Single(t => t.Name == "word_create").ProtocolTool.Annotations;
        Assert.True(write?.DestructiveHint);
    }

    // ---- Word ----------------------------------------------------------------------------------

    [Fact]
    public async Task AWordDocumentIsCreatedFromBlocks()
    {
        (await Call("word_create", new
        {
            path = "word/laporan.docx",
            title = "Laporan Kuartal",
            blocks = new object[]
            {
                new { type = "heading", text = "Laporan Kuartal III", level = 0 },
                new { type = "paragraph", text = "Pendapatan naik **32%** dibanding *kuartal lalu*." },
                new { type = "heading", text = "Sorotan", level = 1 },
                new { type = "bullets", items = new[] { "Jakarta memimpin", "Biaya turun 4%" } },
                new { type = "table", rows = new[] { new[] { "Wilayah", "Nilai" }, new[] { "Jakarta", "1.480" } } },
                new { type = "quote", text = "Angka bersifat ilustratif." },
            },
        })).Ok();

        var path = Full("word/laporan.docx");
        AssertValidPackage(path);

        using var document = WordDocument.Open(path);
        Assert.Equal("Laporan Kuartal", document.Properties.Title);
        Assert.Contains("Pendapatan naik 32% dibanding kuartal lalu.", document.ExtractText());
        Assert.True(document.Paragraphs[1].Runs.Single(r => r.Text == "32%").Format.Bold);
        Assert.Single(document.Tables);
    }

    [Fact]
    public async Task ATemplateIsFilledWithJsonData()
    {
        using (var template = WordDocument.Create())
        {
            template.AddParagraph("Yth. {{pelanggan.nama}}");
            template.AddParagraph("Tanggal: {{tanggal:dd MMMM yyyy}}");
            var table = template.AddTable(1, 2);
            table[0, 0].Text = "{{#baris}}{{barang}}";
            table[0, 1].Text = "{{harga:N0}}{{/baris}}";
            template.Save(Full("word/template.docx"));
        }

        (await Call("word_fill_template", new
        {
            templatePath = "word/template.docx",
            outputPath = "word/terisi.docx",
            culture = "id-ID",
            data = new
            {
                pelanggan = new { nama = "Kang Fadhil" },
                tanggal = "2026-10-28",
                baris = new[] { new { barang = "Kertas", harga = 50000 }, new { barang = "Tinta", harga = 125000 } },
            },
        })).Ok();

        using var filled = WordDocument.Open(Full("word/terisi.docx"));
        Assert.Contains("Yth. Kang Fadhil", filled.ExtractText());

        // JSON has no date type; an ISO string must still format as a date.
        Assert.Contains("Tanggal: 28 Oktober 2026", filled.ExtractText());
        Assert.Equal(2, filled.Tables[0].Rows.Count);
        Assert.Equal("125.000", filled.Tables[0].Rows[1].Cells[1].Text);
    }

    [Fact]
    public async Task WordEditsMergeSplitWatermarkAndProtect()
    {
        foreach (var name in (string[])["a", "b"])
        {
            (await Call("word_create", new
            {
                path = $"merge/{name}.docx",
                blocks = new[] { new { type = "heading", text = $"Bab {name}" }, new { type = "paragraph", text = "isi" } },
            })).Ok();
        }

        (await Call("word_append", new { path = "merge/a.docx", blocks = new[] { new { type = "paragraph", text = "tambahan" } } })).Ok();
        (await Call("word_replace_text", new { path = "merge/a.docx", find = "tambahan", replace = "ditambahkan" })).Ok();
        (await Call("word_merge", new { outputPath = "merge/gabung.docx", inputs = new[] { "merge/a.docx", "merge/b.docx" } })).Ok();
        (await Call("word_watermark", new { path = "merge/gabung.docx", text = "DRAF" })).Ok();
        (await Call("word_protect", new { path = "merge/gabung.docx", restriction = "readOnly", password = "rahasia" })).Ok();

        using (var merged = WordDocument.Open(Full("merge/gabung.docx")))
        {
            Assert.Contains("ditambahkan", merged.ExtractText());
            Assert.Contains("Bab b", merged.ExtractText());
            Assert.Equal("DRAF", merged.Watermark);
            Assert.True(merged.Protection!.VerifyPassword("rahasia"));
        }

        var split = (await Call("word_split", new { path = "merge/gabung.docx", outputDirectory = "merge/bab", by = "heading" })).Ok();
        Assert.Contains("gabung-02.docx", split.Text());
        Assert.True(File.Exists(Full("merge/bab/gabung-02.docx")));
    }

    [Fact]
    public async Task TrackedChangesCanBeListedAndAccepted()
    {
        using (var document = WordDocument.Create())
        {
            document.AddParagraph("Kontrak ").AddTrackedRun("baru", "Ani");
            document.Save(Full("word/revisi.docx"));
        }

        var listed = (await Call("word_track_changes", new { path = "word/revisi.docx" })).Ok();
        Assert.Contains("\"author\": \"Ani\"", listed.Text());

        (await Call("word_track_changes", new { path = "word/revisi.docx", action = "accept_all" })).Ok();

        using var accepted = WordDocument.Open(Full("word/revisi.docx"));
        Assert.Empty(accepted.Revisions);
    }

    // ---- Excel ---------------------------------------------------------------------------------

    [Fact]
    public async Task AWorkbookIsCreatedWithFormulasFormatsAndAChart()
    {
        (await Call("excel_create", new
        {
            path = "excel/penjualan.xlsx",
            sheets = new[]
            {
                new
                {
                    name = "Penjualan",
                    rows = new object[][]
                    {
                        ["Tanggal", "Produk", "Qty", "Harga", "Total"],
                        ["2026-10-01", "Kertas", 2, 50000, "=C2*D2"],
                        ["2026-10-02", "Tinta", 3, 125000, "=C3*D3"],
                        ["", "", "", "TOTAL", "=SUM(E2:E3)"],
                    },
                    numberFormats = new Dictionary<string, string> { ["D"] = "#,##0", ["E"] = "#,##0" },
                    chart = new
                    {
                        type = "column",
                        title = "Total per produk",
                        categories = new[] { "Kertas", "Tinta" },
                        series = new[] { new { name = "Total", values = new[] { 100000.0, 375000.0 } } },
                    },
                },
            },
        })).Ok();

        var path = Full("excel/penjualan.xlsx");
        AssertValidPackage(path);

        using (var workbook = Workbook.Open(path))
        {
            var sheet = workbook["Penjualan"];
            Assert.Equal(475000, sheet["E4"].Number);
            Assert.Equal(new DateTime(2026, 10, 1), sheet["A2"].DateTime);
            Assert.Single(sheet.Charts);
        }

        // Reading back through the protocol gives the calculated values, not the formulas.
        var read = (await Call("excel_read", new { path = "excel/penjualan.xlsx" })).Ok().Text();
        Assert.Contains("475000", read);
        Assert.Contains("2026-10-01T00:00:00", read);

        (await Call("excel_write_cells", new
        {
            path = "excel/penjualan.xlsx",
            sheet = "Penjualan",
            cells = new Dictionary<string, object> { ["C2"] = 10 },
        })).Ok();

        using var changed = Workbook.Open(path);
        Assert.Equal(875000, changed["Penjualan"]["E4"].Number);
    }

    // ---- PowerPoint ----------------------------------------------------------------------------

    [Fact]
    public async Task ADeckIsCreatedWithEverySlideType()
    {
        (await Call("pptx_create", new
        {
            path = "deck/tinjauan.pptx",
            slides = new object[]
            {
                new { type = "title", title = "Tinjauan Kuartal", subtitle = "Gravicode Studios" },
                new { type = "bullets", title = "Sorotan", bullets = new[] { "Pendapatan naik 32%", "Biaya turun 4%" }, notes = "Tekankan Jakarta." },
                new { type = "section", title = "Angka" },
                new { type = "table", title = "Per wilayah", rows = new[] { new[] { "Wilayah", "2026" }, new[] { "Jakarta", "1.480" } } },
                new
                {
                    type = "chart",
                    title = "Pendapatan",
                    chart = new
                    {
                        type = "line",
                        categories = new[] { "Q1", "Q2", "Q3" },
                        series = new[] { new { name = "2026", values = new[] { 10.0, 12, 15 } } },
                    },
                },
            },
        })).Ok();

        var path = Full("deck/tinjauan.pptx");
        AssertValidPackage(path);

        using var deck = Presentation.Open(path);
        Assert.Equal(5, deck.Slides.Count);
        Assert.Equal("Tekankan Jakarta.", deck.Slides[1].Notes);
        Assert.Single(deck.Slides[4].Charts);
        Assert.Single(deck.Slides[3].Tables);
    }

    [Fact]
    public async Task HtmlBecomesSlides()
    {
        (await Call("pptx_from_html", new
        {
            path = "deck/html.pptx",
            html = "<h1>Ringkasan</h1><ul><li>Satu</li><li>Dua</li></ul><h2>Rincian</h2><p>Teks.</p>",
            title = "Dari HTML",
        })).Ok();

        using var deck = Presentation.Open(Full("deck/html.pptx"));
        Assert.True(deck.Slides.Count >= 3);
    }

    // ---- PDF and conversion --------------------------------------------------------------------

    [Fact]
    public async Task ConversionsInEveryDirection()
    {
        (await Call("word_create", new
        {
            path = "convert/surat.docx",
            blocks = new object[]
            {
                new { type = "heading", text = "Surat Pemberitahuan" },
                new { type = "paragraph", text = "Dengan hormat, kami sampaikan jadwal baru." },
                new { type = "table", rows = new[] { new[] { "Hari", "Jam" }, new[] { "Senin", "09.00" } } },
            },
        })).Ok();

        (await Call("convert", new { input = "convert/surat.docx", output = "convert/surat.pdf" })).Ok();
        (await Call("convert", new { input = "convert/surat.docx", output = "convert/surat.html" })).Ok();
        (await Call("convert", new { input = "convert/surat.html", output = "convert/dari-html.docx" })).Ok();
        (await Call("convert", new { input = "convert/surat.pdf", output = "convert/dari-pdf.docx" })).Ok();
        (await Call("convert", new { input = "convert/surat.pdf", output = "convert/dari-pdf.xlsx" })).Ok();

        using (var pdf = PdfDocument.Open(Full("convert/surat.pdf")))
        {
            Assert.Contains("Surat Pemberitahuan", pdf.ExtractText());
        }

        Assert.Contains("<h1", File.ReadAllText(Full("convert/surat.html")));

        using (var back = WordDocument.Open(Full("convert/dari-pdf.docx")))
        {
            Assert.Contains("Dengan hormat", back.ExtractText());
        }

        var unsupported = await Call("convert", new { input = "convert/surat.pdf", output = "convert/surat.pptx" });
        Assert.True(unsupported.IsError);
        Assert.Contains("not supported", unsupported.Text());
    }

    [Fact]
    public async Task PdfsAreMergedSplitAndEncrypted()
    {
        foreach (var name in (string[])["satu", "dua"])
        {
            (await Call("word_create", new { path = $"pdf/{name}.docx", blocks = new[] { new { type = "paragraph", text = name } } })).Ok();
            (await Call("convert", new { input = $"pdf/{name}.docx", output = $"pdf/{name}.pdf" })).Ok();
        }

        (await Call("pdf_merge", new { outputPath = "pdf/gabung.pdf", inputs = new[] { "pdf/satu.pdf", "pdf/dua.pdf" } })).Ok();
        (await Call("pdf_split", new { path = "pdf/gabung.pdf", outputDirectory = "pdf/bagian" })).Ok();
        (await Call("pdf_protect", new { path = "pdf/gabung.pdf", outputPath = "pdf/terkunci.pdf", userPassword = "buka" })).Ok();

        using (var merged = PdfDocument.Open(Full("pdf/gabung.pdf")))
        {
            Assert.Equal(2, merged.Pages.Count);
        }

        Assert.True(File.Exists(Full("pdf/bagian/gabung-002.pdf")));

        using var locked = PdfDocument.Open(Full("pdf/terkunci.pdf"), "buka");
        Assert.True(locked.WasEncrypted);
        Assert.Contains("dua", locked.ExtractText());
    }

    // ---- Reading and previews ------------------------------------------------------------------

    [Fact]
    public async Task APageIsRenderedAndReturnedAsAnImage()
    {
        (await Call("word_create", new { path = "preview/a.docx", blocks = new[] { new { type = "heading", text = "Pratinjau" } } })).Ok();

        var result = (await Call("render_page", new { path = "preview/a.docx", savePath = "preview/a.png" })).Ok();
        var image = result.Content.OfType<ImageContentBlock>().Single();

        Assert.Equal("image/png", image.MimeType);
        var png = image.DecodedData.ToArray();
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        Assert.Equal(png, File.ReadAllBytes(Full("preview/a.png")));

        var missing = await Call("render_page", new { path = "preview/a.docx", page = 9 });
        Assert.True(missing.IsError);
    }

    [Fact]
    public async Task InfoTextAndListing()
    {
        (await Call("word_create", new
        {
            path = "info/doc.docx",
            title = "Judul",
            blocks = new[] { new { type = "heading", text = "Bab Satu" }, new { type = "paragraph", text = "isi teks" } },
        })).Ok();

        var info = (await Call("document_info", new { path = "info/doc.docx" })).Ok().Text();
        Assert.Contains("\"format\": \"Word\"", info);
        Assert.Contains("Bab Satu", info);

        Assert.Contains("isi teks", (await Call("extract_text", new { path = "info/doc.docx" })).Ok().Text());
        Assert.Contains("info/doc.docx", (await Call("list_files", new { directory = "info" })).Ok().Text());
    }

    // ---- The boundary --------------------------------------------------------------------------

    [Fact]
    public async Task APathOutsideTheWorkspaceIsAnErrorTheModelCanRead()
    {
        // A name no other run can have left behind: while mutation-testing the boundary, the
        // broken server really did write ../lolos.docx into the temp directory, and that leftover
        // then failed this test on every run after.
        var name = $"lolos-{Guid.NewGuid():N}.docx";

        var result = await Call("word_create", new
        {
            path = "../" + name,
            blocks = new[] { new { type = "paragraph", text = "x" } },
        });

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace", result.Text());
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(server.Root.Path)!, name)));
    }

    [Fact]
    public async Task AMistakeIsExplainedSoTheNextCallCanFixIt()
    {
        var result = await Call("word_create", new { path = "x.docx", blocks = new[] { new { type = "tabel" } } });

        Assert.True(result.IsError);
        Assert.Contains("Unknown block type 'tabel'", result.Text());
    }

    [Fact]
    public async Task AReadOnlyServerReadsButDoesNotWrite()
    {
        File.Copy(CreateSample(), Full("ro.docx"), overwrite: true);

        await using var client = await ServerFixture.Start(server.Root.Path, "--read-only");

        var write = await client.CallToolAsync("word_create", ToDictionary(new
        {
            path = "ro-new.docx",
            blocks = new[] { new { type = "paragraph", text = "x" } },
        }), cancellationToken: new CancellationTokenSource(ServerFixture.Deadline).Token);
        Assert.True(write.IsError);
        Assert.Contains("read-only", write.Text());
        Assert.False(File.Exists(Full("ro-new.docx")));

        var read = await client.CallToolAsync("extract_text", ToDictionary(new { path = "ro.docx" }),
            cancellationToken: new CancellationTokenSource(ServerFixture.Deadline).Token);
        Assert.False(read.IsError ?? false);
    }

    /// <summary>The C# client sample in docs/Mcp.md, compiled and run here.</summary>
    [Fact]
    public async Task TheDocumentationsClientSampleWorks()
    {
        await using var client = await ModelContextProtocol.Client.McpClient.CreateAsync(
            new ModelContextProtocol.Client.StdioClientTransport(new()
            {
                Name = "officenet",
                Command = "dotnet",
                Arguments = [ServerFixture.ServerPath(), "--root", server.Root.Path],
            }), cancellationToken: TestContext.Current.CancellationToken);

        // The docs omit the token for brevity; a test passes xUnit's so cancellation reaches it.
        var result = await client.CallToolAsync("word_create", new Dictionary<string, object?>
        {
            ["path"] = "surat.docx",
            ["blocks"] = new object[]
            {
                new { type = "heading", text = "Surat Pemberitahuan" },
                new { type = "paragraph", text = "Dengan hormat, kami sampaikan **jadwal baru**." },
            },
        }, cancellationToken: TestContext.Current.CancellationToken);

        result.Ok();
        using var document = WordDocument.Open(Full("surat.docx"));
        Assert.Contains("jadwal baru", document.ExtractText());
    }

    private string CreateSample()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("contoh");
        var path = Full("sample.docx");
        document.Save(path);
        return path;
    }
}
