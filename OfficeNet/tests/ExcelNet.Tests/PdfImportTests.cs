// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.Io;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Text;
using Xunit;

namespace ExcelNet.Tests;

public class PdfImportTests
{
    /// <summary>
    /// Draws a table onto a PDF page, one cell at a time.
    /// </summary>
    /// <remarks>
    /// Positioning each cell with its own text operator is how a great many real producers emit a
    /// table — there is no table operator in PDF to use instead — so a page built this way is the
    /// shape the extractor actually meets. The Word round trip is covered on the WordNet side.
    /// </remarks>
    private static PdfDocument TablePdf(string[][] rows, double[]? columnX = null)
    {
        var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            canvas.SetFont(StandardFont.Helvetica, 11);

            var xs = columnX ?? [.. Enumerable.Range(0, rows[0].Length).Select(i => 72.0 + (i * 140))];
            var y = 700.0;

            foreach (var row in rows)
            {
                for (var column = 0; column < row.Length; column++)
                {
                    canvas.DrawText(row[column], xs[column], y);
                }

                y -= 20;
            }
        }

        return pdf;
    }

    private static PdfDocument Paragraphs(params string[] lines)
    {
        var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            canvas.SetFont(StandardFont.Helvetica, 11);

            var y = 700.0;

            foreach (var line in lines)
            {
                canvas.DrawText(line, 72, y);
                y -= 16;
            }
        }

        return pdf;
    }

    [Fact]
    public void ATableBecomesASheet()
    {
        using var pdf = TablePdf(
        [
            ["Kode", "Nama", "Harga"],
            ["A100", "Kabel", "15000"],
            ["B200", "Adaptor", "45000"],
            ["C300", "Baterai", "27500"],
        ]);

        using var workbook = PdfToExcel.Convert(pdf);
        var sheet = workbook.Worksheets[0];

        Assert.Equal("Kode", sheet["A1"].Text);
        Assert.Equal("Adaptor", sheet["B3"].Text);
        Assert.Equal(27500d, sheet["C4"].Number);
    }

    [Fact]
    public void NumbersBecomeNumbersAndTextStaysText()
    {
        // A column of numbers stored as text is the most annoying thing to receive in a
        // spreadsheet: it will not sum, and the green triangles are the only clue why.
        using var pdf = TablePdf(
        [
            ["Wilayah", "Jumlah"],
            ["Jakarta", "1480"],
            ["Bandung", "1150"],
            ["Medan", "905"],
        ]);

        using var workbook = PdfToExcel.Convert(pdf);
        var sheet = workbook.Worksheets[0];

        Assert.Equal(CellValueType.Text, sheet["A2"].Value.ValueType);
        Assert.Equal(CellValueType.Number, sheet["B2"].Value.ValueType);
        Assert.Equal(1480d, sheet["B2"].Number);
    }

    [Fact]
    public void ParsingNumbersCanBeTurnedOff()
    {
        using var pdf = TablePdf(
        [
            ["Kode", "Nilai"],
            ["A", "001"],
            ["B", "002"],
            ["C", "003"],
        ]);

        using var workbook = PdfToExcel.Convert(pdf, new PdfTableOptions { ParseNumbers = false });

        // "001" as a number is 1, and the leading zeros were the point.
        Assert.Equal("001", workbook.Worksheets[0]["B2"].Text);
    }

    [Theory]
    // No separator at all.
    [InlineData("1500", 1500)]
    [InlineData("-42", -42)]
    [InlineData("0", 0)]
    // One separator, once, with something other than three digits after it: a decimal point.
    [InlineData("15,5", 15.5)]
    [InlineData("15.5", 15.5)]
    [InlineData("1.2345", 1.2345)]
    // One separator, more than once: it groups.
    [InlineData("1.234.567", 1234567)]
    [InlineData("1,234,567", 1234567)]
    // Both separators: the rightmost is the decimal point, whichever it is.
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    // An accountant's minus sign.
    [InlineData("(250)", -250)]
    // Currency and percent are formatting, not value.
    [InlineData("Rp 15000", 15000)]
    [InlineData("$1500", 1500)]
    [InlineData("USD 1500", 1500)]
    [InlineData("12%", 0.12)]
    [InlineData("15000 IDR", 15000)]
    public void ANumberIsReadWhenItCanOnlyMeanOneThing(string text, double expected)
    {
        Assert.True(PdfToExcel.TryParseNumber(text, null, out var value));
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData("1.234")]
    [InlineData("1,234")]
    public void AnAmbiguousNumberIsLeftAsText(string text)
    {
        // "1.234" is one thousand two hundred and thirty-four in Indonesia and one-point-something
        // elsewhere, and nothing in a PDF says which. Guessing wrong changes the value by a factor
        // of a thousand while looking perfectly reasonable.
        Assert.False(PdfToExcel.TryParseNumber(text, null, out _));
    }

    [Fact]
    public void NamingTheSeparatorRemovesTheGuess()
    {
        Assert.True(PdfToExcel.TryParseNumber("1.234", '.', out var asDecimal));
        Assert.Equal(1.234, asDecimal, 6);

        Assert.True(PdfToExcel.TryParseNumber("1.234", ',', out var asThousands));
        Assert.Equal(1234, asThousands, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Jakarta")]
    [InlineData("Rp")]
    [InlineData("-")]
    public void SomethingThatIsNotANumberIsNotReadAsOne(string text)
    {
        Assert.False(PdfToExcel.TryParseNumber(text, null, out _));
    }

    [Fact]
    public void SeveralTablesBecomeSeveralSheets()
    {
        var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            canvas.SetFont(StandardFont.Helvetica, 11);

            var y = 700.0;

            for (var t = 0; t < 2; t++)
            {
                for (var row = 0; row < 3; row++)
                {
                    canvas.DrawText($"T{t}R{row}", 72, y);
                    canvas.DrawText($"{(t * 100) + row}", 260, y);
                    y -= 20;
                }

                // A wide gap, so the two tables do not run together into one.
                canvas.DrawText("Pemisah antara dua tabel.", 72, y - 40);
                y -= 100;
            }
        }

        using (pdf)
        {
            using var workbook = PdfToExcel.Convert(pdf);

            Assert.Equal(2, workbook.Worksheets.Count);
            Assert.Equal("Tabel 1", workbook.Worksheets[0].Name);
            Assert.Equal("T0R0", workbook.Worksheets[0]["A1"].Text);
            Assert.Equal("T1R0", workbook.Worksheets[1]["A1"].Text);
        }
    }

    [Fact]
    public void TablesCanShareOneSheet()
    {
        var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            canvas.SetFont(StandardFont.Helvetica, 11);

            var y = 700.0;

            for (var t = 0; t < 2; t++)
            {
                for (var row = 0; row < 3; row++)
                {
                    canvas.DrawText($"T{t}R{row}", 72, y);
                    canvas.DrawText($"{row}", 260, y);
                    y -= 20;
                }

                canvas.DrawText("Pemisah.", 72, y - 40);
                y -= 100;
            }
        }

        using (pdf)
        {
            using var workbook = PdfToExcel.Convert(pdf,
                new PdfTableOptions { SheetPerTable = false });

            var sheet = Assert.Single(workbook.Worksheets);

            Assert.Equal("T0R0", sheet["A1"].Text);

            // One blank row between them, or the second table's header reads as a row of the first.
            Assert.True(sheet["A4"].IsEmpty);
            Assert.Equal("T1R0", sheet["A5"].Text);
        }
    }

    [Fact]
    public void APdfWithNoTablesGivesAnEmptyWorkbookRatherThanThrowing()
    {
        using var pdf = Paragraphs("Hanya paragraf, tidak ada tabel di sini.");
        using var workbook = PdfToExcel.Convert(pdf);

        var sheet = Assert.Single(workbook.Worksheets);

        Assert.Equal("Kosong", sheet.Name);
        Assert.True(sheet["A1"].IsEmpty);
    }

    [Fact]
    public void TheGridCanBeReadWithoutBuildingAWorkbook()
    {
        // A caller often wants the rows — to load into a database, or to see what was found before
        // deciding to keep it.
        using var pdf = TablePdf(
        [
            ["Kode", "Nama"],
            ["A100", "Kabel"],
            ["B200", "Adaptor"],
        ]);

        var tables = PdfToExcel.FindTables(pdf);
        var table = Assert.Single(tables);

        Assert.Equal(3, table.Count);
        Assert.Equal(["Kode", "Nama"], table[0]);
    }

    [Fact]
    public void ARangeReadsOnlyThosePages()
    {
        var pdf = PdfDocument.Create();

        for (var index = 0; index < 2; index++)
        {
            var page = pdf.Pages.Add(PageSize.A4);

            using var canvas = page.OpenCanvas();
            canvas.SetFont(StandardFont.Helvetica, 11);

            var y = 700.0;

            for (var row = 0; row < 3; row++)
            {
                canvas.DrawText($"P{index}R{row}", 72, y);
                canvas.DrawText($"{row}", 260, y);
                y -= 20;
            }
        }

        using (pdf)
        {
            var second = PdfToExcel.FindTables(pdf, new PdfTableOptions { Pages = 1..2 });

            Assert.Equal("P1R0", Assert.Single(second)[0][0]);
        }
    }

    [Fact]
    public void TheWorkbookItProducesIsValid()
    {
        using var pdf = TablePdf(
        [
            ["Kode", "Nama", "Harga"],
            ["A100", "Kabel", "15000"],
            ["B200", "Adaptor", "45000"],
        ]);

        using var workbook = PdfToExcel.Convert(pdf);
        using var stream = new MemoryStream();

        workbook.Save(stream);

        XlsxValidator.AssertValid(stream.ToArray());
    }
}
