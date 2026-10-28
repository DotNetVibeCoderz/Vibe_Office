// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using ExcelNet.Io;
using ExcelNet.Styles;
using OfficeNet.Core.Drawing;
using Xunit;

namespace ExcelNet.Tests;

public class HtmlIoTests
{
    private static byte[] Save(Workbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream);
        return stream.ToArray();
    }

    private static Workbook Report()
    {
        var workbook = Workbook.Create("Penjualan");
        var sheet = workbook[0];

        sheet["A1"].Set("Laporan Q3").WithStyle(CellStyle.Default.Bold().WithColor(OfficeColor.FromRgb(0x1F, 0x38, 0x64)));
        sheet.MergeCells("A1:C1");
        sheet["A2"].Set("Wilayah");
        sheet["B2"].Set("Pendapatan");
        sheet["C2"].Set("Tumbuh");
        sheet.Range("A2:C2").ApplyStyle(CellStyle.Default.Bold() with { BackgroundColor = OfficeColor.FromRgb(0xDD, 0xEB, 0xF7) });
        sheet["A3"].Set("Jakarta");
        sheet["B3"].Set(1480000.5).WithNumberFormat("#,##0.00");
        sheet["C3"].Set(0.125).WithNumberFormat("0.0%");
        sheet["A4"].Set("Bandung <&>");
        sheet["B4"].Set(true);
        // A line break in a cell comes with wrapping, as Alt+Enter turns it on in Excel.
        sheet["C4"].Set("baris1\nbaris2").WithStyle(CellStyle.Default with { WrapText = true });
        sheet["A5"].Set("situs").WithHyperlink("https://gravicode.com");
        sheet["B5"].Set("jahat").WithHyperlink("javascript:alert(1)");
        sheet["C5"].Set("tinggi");
        sheet.MergeCells("C5:C6");

        return workbook;
    }

    [Fact]
    public void ASheetIsWrittenAsItsDisplayedValuesWithMergesAndFormatting()
    {
        using var workbook = Report();

        var html = HtmlIo.ExportText(workbook[0], new ExcelHtmlOptions { FullDocument = false });

        Assert.Contains("<caption>Penjualan</caption>", html, StringComparison.Ordinal);
        Assert.Contains("<td colspan=\"3\" style=\"font-weight:bold;color:#1F3864;\">Laporan Q3</td>", html, StringComparison.Ordinal);
        Assert.Contains(">1,480,000.50</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td style=\"text-align:right;\">12.5%</td>", html, StringComparison.Ordinal);
        Assert.Contains("background-color:#DDEBF7;", html, StringComparison.Ordinal);
        Assert.Contains(">Bandung &lt;&amp;&gt;</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td style=\"text-align:center;\">TRUE</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td style=\"white-space:pre-wrap;\">baris1<br>baris2</td>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://gravicode.com\">situs</a>", html, StringComparison.Ordinal);
        Assert.Contains("<td>jahat</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td rowspan=\"2\">tinggi</td>", html, StringComparison.Ordinal);

        // The row under the rowspan has two cells, not three.
        Assert.Contains("<tr><td></td><td></td></tr>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CellsArePlacedByTheTableAlgorithmAndTyped()
    {
        using var workbook = Workbook.Create();

        var sheets = HtmlIo.ImportText(workbook, """
            <table>
              <caption>Data: Q3/2026</caption>
              <colgroup><col style="width:120px"><col width="80"></colgroup>
              <thead><tr><th>Kode</th><th>Nilai</th><th>2026</th></tr></thead>
              <tbody>
                <tr><td>007</td><td>1,234.5</td><td>12.5%</td></tr>
                <tr><td rowspan="2" style="background-color:#ffeeaa">Gabung</td><td><b>99</b></td><td>2026-10-29</td></tr>
                <tr><td colspan="2" style="text-align:center;color:red">lebar <i>sekali</i></td></tr>
                <tr><td><a href="https://example.org">tautan</a></td><td>true</td><td>baris<br>dua</td><td><b>Total</b> sementara</td></tr>
              </tbody>
            </table>
            <table><tr><td>lain</td><td><table><tr><td>bersarang</td></tr></table></td></tr></table>
            """);

        Assert.Equal(["Data Q3 2026", "Sheet2"], sheets.Select(s => s.Name));
        var sheet = sheets[0];

        // A header is a label, even one that looks like a number.
        Assert.Equal(CellValueType.Text, sheet["C1"].Value.ValueType);
        Assert.Equal("2026", sheet["C1"].Value.AsText());

        // A cell has one format: partly bold is not bold.
        Assert.Equal("Total sementara", sheet["D5"].Value.AsText());
        Assert.False(sheet["D5"].Style.Font.Bold);

        Assert.Equal("007", sheet["A2"].Value.AsText());
        Assert.Equal(CellValueType.Text, sheet["A2"].Value.ValueType);
        Assert.Equal(1234.5, sheet["B2"].Value.AsNumber());
        Assert.Equal("#,##0.0", sheet["B2"].Style.NumberFormat);
        Assert.Equal(0.125, sheet["C2"].Value.AsNumber(), 10);
        Assert.Equal("0.0%", sheet["C2"].Style.NumberFormat);
        Assert.Equal(new DateTime(2026, 10, 29), sheet["C3"].Value.AsDateTime());
        Assert.True(sheet["B5"].Value.AsBoolean());

        // The rowspan in A3 pushes the colspan of the next row to B4.
        Assert.Equal("lebar sekali", sheet["B4"].Value.AsText());
        Assert.Equal(["A3:A4", "B4:C4"], sheet.MergedRanges.Select(r => r.ToString()).Order());

        Assert.True(sheet["A1"].Style.Font.Bold);           // th
        Assert.True(sheet["B3"].Style.Font.Bold);           // all of it in <b>
        Assert.False(sheet["B4"].Style.Font.Italic);        // only part in <i>
        Assert.Equal(OfficeColor.FromRgb(0xFF, 0x00, 0x00), sheet["B4"].Style.Font.Color);
        Assert.Equal(HorizontalAlignment.Center, sheet["B4"].Style.Horizontal);
        Assert.Equal(OfficeColor.FromRgb(0xFF, 0xEE, 0xAA), sheet["A3"].Style.BackgroundColor);
        Assert.Equal("https://example.org", sheet["A5"].Hyperlink);
        Assert.Equal("baris\ndua", sheet["C5"].Value.AsText());
        Assert.True(sheet["C5"].Style.WrapText);

        Assert.Equal(16.43, sheet.GetColumnWidth(0), 2);
        Assert.Equal(10.71, sheet.GetColumnWidth(1), 2);

        // A table in a cell is that cell's text, not a sheet of its own.
        Assert.Equal("bersarang", sheets[1]["B1"].Value.AsText());

        XlsxValidator.AssertValid(Save(workbook));
    }

    [Fact]
    public void AWorkbookSurvivesARoundTripThroughHtml()
    {
        using var original = Report();
        var html = HtmlIo.ExportText(original);

        using var copy = Workbook.Create();
        HtmlIo.ImportText(copy, html);

        XlsxValidator.AssertValid(Save(copy));

        // Workbook.Create's empty Sheet1 is filled, not shadowed by "Penjualan (2)".
        Assert.Equal(["Sheet1", "Penjualan"], copy.Select(s => s.Name));
        var sheet = copy["Penjualan"];

        Assert.Equal(["A1:C1", "C5:C6"], sheet.MergedRanges.Select(r => r.ToString()).Order());
        Assert.Equal(1480000.5, sheet["B3"].Value.AsNumber());
        Assert.Equal("#,##0.00", sheet["B3"].Style.NumberFormat);
        Assert.Equal("0.0%", sheet["C3"].Style.NumberFormat);
        Assert.Equal("Bandung <&>", sheet["A4"].Value.AsText());
        Assert.True(sheet["B4"].Value.AsBoolean());
        Assert.Equal("https://gravicode.com", sheet["A5"].Hyperlink);
        Assert.Null(sheet["B5"].Hyperlink);
        Assert.True(sheet["A2"].Style.Font.Bold);
        Assert.Equal(OfficeColor.FromRgb(0xDD, 0xEB, 0xF7), sheet["A2"].Style.BackgroundColor);
        Assert.Equal(OfficeColor.FromRgb(0x1F, 0x38, 0x64), sheet["A1"].Style.Font.Color);

        // And it reads back as the same page.
        Assert.Equal(HtmlIo.ExportText(original["Penjualan"]), HtmlIo.ExportText(sheet));
    }

    [Fact]
    public void ASheetWithContentIsNeverOverwritten()
    {
        using var workbook = Workbook.Create("Data");
        workbook[0]["A1"].Set("milik saya");

        var sheets = HtmlIo.ImportText(workbook, "<table><caption>Data</caption><tr><td>baru</td></tr></table>");

        Assert.Equal("Data (2)", sheets[0].Name);
        Assert.Equal("milik saya", workbook["Data"]["A1"].Value.AsText());
    }

    [Fact]
    public void AnEmptySheetOfTheSameNameIsFilled()
    {
        using var workbook = Workbook.Create();

        var sheets = HtmlIo.ImportText(workbook, "<table><caption>Sheet1</caption><tr><td>isi</td></tr></table>");

        Assert.Same(workbook["Sheet1"], sheets[0]);
        Assert.Single(workbook);
        Assert.Equal("isi", workbook["Sheet1"]["A1"].Value.AsText());
    }

    [Fact]
    public void NumbersAreReadInTheGivenCulture()
    {
        using var workbook = Workbook.Create();

        var sheet = HtmlIo.ImportText(workbook, "<table><tr><td>1.234,5</td><td>12,5%</td></tr></table>",
            new ExcelHtmlImportOptions { Culture = CultureInfo.GetCultureInfo("id-ID") })[0];

        Assert.Equal(1234.5, sheet["A1"].Value.AsNumber());
        Assert.Equal("#,##0.0", sheet["A1"].Style.NumberFormat);
        Assert.Equal(0.125, sheet["B1"].Value.AsNumber(), 10);
        Assert.Equal("0.0%", sheet["B1"].Style.NumberFormat);
    }
}
