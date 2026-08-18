using VibeDesk.Application.Documents;
using VibeDesk.Domain;
using VibeDesk.Office;
using Xunit;

namespace VibeDesk.Tests.Office;

/// <summary>
/// Office import and export, checked by round-tripping through real OOXML packages.
/// </summary>
/// <remarks>
/// Conversion is lossy on purpose, so these assert what must survive rather than byte equality:
/// the text, the structure that has a counterpart on both sides, and — for spreadsheets — the values
/// and formulas, which are the part users would notice losing immediately.
/// </remarks>
public class OfficeConverterTests
{
    private readonly IOfficeConverter _converter = new OfficeConverter();

    private MemoryStream Roundtrip(Action<Stream> write)
    {
        var stream = new MemoryStream();
        write(stream);

        // The SDK writes on dispose, so reading before rewinding sees a partial package.
        stream.Position = 0;
        return stream;
    }

    // ─────────────────────────────────── detection ───────────────────────────────────

    [Theory]
    [InlineData("report.docx", OfficeFormat.Word)]
    [InlineData("REPORT.DOCX", OfficeFormat.Word)]
    [InlineData("budget.xlsx", OfficeFormat.Excel)]
    [InlineData("deck.pptx", OfficeFormat.PowerPoint)]
    public void DetectsByExtension(string name, OfficeFormat expected) =>
        Assert.Equal(expected, _converter.Detect(name, "application/octet-stream"));

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("photo.png")]
    [InlineData("archive.zip")]
    public void LeavesUnrelatedFilesAlone(string name) =>
        Assert.Null(_converter.Detect(name, null));

    [Theory]
    [InlineData("legacy.doc")]
    [InlineData("legacy.xls")]
    [InlineData("legacy.ppt")]
    public void RefusesTheLegacyBinaryFormats(string name) =>
        // Not OOXML at all. Importing one would produce a document full of mojibake, so it stays an
        // attachment and says so, which is the honest outcome.
        Assert.Null(_converter.Detect(name, null));

    [Fact]
    public void TargetTypeFollowsTheFormat()
    {
        Assert.Equal(DriveItemType.Document, IOfficeConverter.TargetType(OfficeFormat.Word));
        Assert.Equal(DriveItemType.Spreadsheet, IOfficeConverter.TargetType(OfficeFormat.Excel));
        Assert.Equal(DriveItemType.Presentation, IOfficeConverter.TargetType(OfficeFormat.PowerPoint));
    }

    // ─────────────────────────────────── Word ───────────────────────────────────

    [Fact]
    public void WordRoundTripKeepsHeadingsParagraphsAndMarks()
    {
        var source = new DocumentModel
        {
            Html = "<h1>Laporan</h1><p>Baris <b>tebal</b> dan <i>miring</i>.</p><h2>Rincian</h2><p>Isi.</p>",
        };

        using var file = Roundtrip(s => _converter.WriteWord(s, source, "Laporan Q3"));
        var result = _converter.ReadWord(file);

        Assert.Contains("<h1>", result.Html);
        Assert.Contains("<h2>Rincian</h2>", result.Html);
        Assert.Contains("<b>tebal</b>", result.Html);
        Assert.Contains("<i>miring</i>", result.Html);
        Assert.Contains("Laporan Q3", result.Html);   // the title becomes the opening heading
    }

    [Fact]
    public void WordRoundTripKeepsListsAndDistinguishesTheirKind()
    {
        var source = new DocumentModel
        {
            Html = "<ul><li>Satu</li><li>Dua</li></ul><ol><li>Pertama</li></ol>",
        };

        using var file = Roundtrip(s => _converter.WriteWord(s, source, "Daftar"));
        var result = _converter.ReadWord(file);

        Assert.Contains("<ul>", result.Html);
        Assert.Contains("<li>Satu</li>", result.Html);
        Assert.Contains("<ol>", result.Html);
        Assert.Contains("<li>Pertama</li>", result.Html);
    }

    [Fact]
    public void WordRoundTripKeepsTables()
    {
        var source = new DocumentModel
        {
            Html = "<table><tr><td>Bulan</td><td>Nilai</td></tr><tr><td>Jan</td><td>4000</td></tr></table>",
        };

        using var file = Roundtrip(s => _converter.WriteWord(s, source, "Tabel"));
        var result = _converter.ReadWord(file);

        Assert.Contains("<table>", result.Html);
        Assert.Contains("<td>Bulan</td>", result.Html);
        Assert.Contains("<td>4000</td>", result.Html);
    }

    [Fact]
    public void WordEscapesMarkupInTheText()
    {
        var source = new DocumentModel { Html = "<p>a &lt; b &amp; c</p>" };

        using var file = Roundtrip(s => _converter.WriteWord(s, source, "Escape"));
        var result = _converter.ReadWord(file);

        // The characters survive as text, not as tags that would break the stored HTML.
        Assert.Contains("a &lt; b &amp; c", result.Html);
        Assert.DoesNotContain("<b ", result.Html);
    }

    [Fact]
    public void WordCountsWordsOnImport()
    {
        var source = new DocumentModel { Html = "<p>satu dua tiga empat</p>" };

        using var file = Roundtrip(s => _converter.WriteWord(s, source, ""));
        var result = _converter.ReadWord(file);

        Assert.Equal(4, result.WordCount);
    }

    // ─────────────────────────────────── Excel ───────────────────────────────────

    [Fact]
    public void ExcelRoundTripKeepsValuesTypesAndFormulas()
    {
        var source = new SpreadsheetModel
        {
            Sheets =
            [
                new SheetTab
                {
                    Name = "Data",
                    Cells = new()
                    {
                        ["A1"] = new Cell { V = "Bulan" },
                        ["B1"] = new Cell { V = "Nilai" },
                        ["A2"] = new Cell { V = "Jan" },
                        ["B2"] = new Cell { V = "4000" },
                        ["A3"] = new Cell { V = "Feb" },
                        ["B3"] = new Cell { V = "5248" },
                        ["B4"] = new Cell { F = "=SUM(B2:B3)", V = "9248" },
                    },
                },
            ],
        };

        using var file = Roundtrip(s => _converter.WriteExcel(s, source));
        var result = _converter.ReadExcel(file);

        var tab = Assert.Single(result.Sheets);
        Assert.Equal("Data", tab.Name);

        Assert.Equal("Bulan", tab.Cells["A1"].V);
        Assert.Equal("4000", tab.Cells["B2"].V);

        // The formula keeps its leading '=' on our side and loses it on Excel's; both directions
        // have to agree or every imported formula becomes text.
        Assert.Equal("=SUM(B2:B3)", tab.Cells["B4"].F);
        Assert.Equal("9248", tab.Cells["B4"].V);
    }

    [Fact]
    public void ExcelRoundTripKeepsEverySheet()
    {
        var source = new SpreadsheetModel
        {
            Sheets =
            [
                new SheetTab { Name = "Satu", Cells = new() { ["A1"] = new Cell { V = "x" } } },
                new SheetTab { Name = "Dua", Cells = new() { ["A1"] = new Cell { V = "y" } } },
            ],
        };

        using var file = Roundtrip(s => _converter.WriteExcel(s, source));
        var result = _converter.ReadExcel(file);

        Assert.Equal(2, result.Sheets.Count);
        Assert.Equal(["Satu", "Dua"], result.Sheets.Select(s => s.Name));
        Assert.Equal("y", result.Sheets[1].Cells["A1"].V);
    }

    [Fact]
    public void ExcelKeepsTextThatLooksLikeNothingElse()
    {
        var source = new SpreadsheetModel
        {
            Sheets = [new SheetTab { Cells = new() { ["A1"] = new Cell { V = "Jakarta, Indonesia" } } }],
        };

        using var file = Roundtrip(s => _converter.WriteExcel(s, source));
        var result = _converter.ReadExcel(file);

        Assert.Equal("Jakarta, Indonesia", result.Sheets[0].Cells["A1"].V);
    }

    [Fact]
    public void ExcelWritesRowsInOrderEvenWhenTheMapIsNot()
    {
        // A Dictionary has no order, and Excel rejects a sheet whose rows are not ascending.
        var source = new SpreadsheetModel
        {
            Sheets =
            [
                new SheetTab
                {
                    Cells = new()
                    {
                        ["A10"] = new Cell { V = "sepuluh" },
                        ["A2"] = new Cell { V = "dua" },
                        ["C1"] = new Cell { V = "tiga" },
                        ["A1"] = new Cell { V = "satu" },
                    },
                },
            ],
        };

        using var file = Roundtrip(s => _converter.WriteExcel(s, source));
        var result = _converter.ReadExcel(file);

        Assert.Equal("satu", result.Sheets[0].Cells["A1"].V);
        Assert.Equal("dua", result.Sheets[0].Cells["A2"].V);
        Assert.Equal("sepuluh", result.Sheets[0].Cells["A10"].V);
        Assert.Equal("tiga", result.Sheets[0].Cells["C1"].V);
    }

    [Fact]
    public void AnEmptySpreadsheetStillProducesAnOpenableWorkbook()
    {
        using var file = Roundtrip(s => _converter.WriteExcel(s, new SpreadsheetModel { Sheets = [] }));
        var result = _converter.ReadExcel(file);

        Assert.Single(result.Sheets);
    }

    // ─────────────────────────────────── PowerPoint ───────────────────────────────────

    [Fact]
    public void PowerPointRoundTripKeepsSlidesAndTheirText()
    {
        var source = new PresentationModel
        {
            Slides =
            [
                Slide("Rencana 2026"),
                Slide("Target", "Naik 30%", "Tiga kota baru"),
                Slide("Penutup"),
            ],
        };

        using var file = Roundtrip(s => _converter.WritePowerPoint(s, source, "Rencana"));
        var result = _converter.ReadPowerPoint(file);

        Assert.Equal(3, result.Slides.Count);

        var all = result.Slides
            .SelectMany(s => s.Elements)
            .Select(e => e.Text ?? string.Empty)
            .ToList();

        Assert.Contains(all, t => t.Contains("Rencana 2026"));
        Assert.Contains(all, t => t.Contains("Naik 30%"));
        Assert.Contains(all, t => t.Contains("Tiga kota baru"));
        Assert.Contains(all, t => t.Contains("Penutup"));
    }

    [Fact]
    public void AnEmptyDeckStillProducesAnOpenablePresentation()
    {
        using var file = Roundtrip(
            s => _converter.WritePowerPoint(s, new PresentationModel { Slides = [] }, "Kosong"));

        var result = _converter.ReadPowerPoint(file);

        // One slide carrying the deck name, rather than a file the editor cannot open.
        Assert.Single(result.Slides);
        Assert.Contains(result.Slides[0].Elements, e => (e.Text ?? string.Empty).Contains("Kosong"));
    }

    private static Slide Slide(string title, params string[] body)
    {
        var slide = new Slide { Layout = body.Length > 0 ? "titleContent" : "title" };

        slide.Elements.Add(new SlideElement
        {
            Type = "text",
            X = 8, Y = 12, W = 84, H = 18,
            Text = $"<h1>{title}</h1>",
        });

        if (body.Length > 0)
        {
            slide.Elements.Add(new SlideElement
            {
                Type = "text",
                X = 8, Y = 36, W = 84, H = 52,
                Text = string.Concat(body.Select(b => $"<p>{b}</p>")),
            });
        }

        return slide;
    }
}
