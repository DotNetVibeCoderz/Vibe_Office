using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace Cuan.Services;

/// <summary>
/// Export data ke CSV dan Excel untuk semua modul
/// </summary>
public class ExportService
{
    /// <summary>Export list of T to CSV bytes</summary>
    public static byte[] ExportToCsv<T>(IEnumerable<T> records, string? delimiter = null)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.UTF8);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter ?? ",",
            Encoding = Encoding.UTF8,
            HasHeaderRecord = true
        });

        csv.WriteRecords(records);
        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>Export data ke Excel (.xlsx)</summary>
    public static byte[] ExportToExcel<T>(IEnumerable<T> records, string sheetName = "Data")
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);

        // Style header
        var headerRow = ws.FirstRow();
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#6366f1");
        headerRow.Style.Font.FontColor = XLColor.White;

        ws.Cell(2, 1).InsertData(records);

        // Auto-fit columns
        ws.Columns().AdjustToContents(1, ws.LastColumnUsed()?.ColumnNumber() ?? 10);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Helper: stream ke file result</summary>
    public static IResult CsvResult(byte[] data, string fileName) =>
        Results.File(data, "text/csv", fileName);

    public static IResult ExcelResult(byte[] data, string fileName) =>
        Results.File(data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
}
