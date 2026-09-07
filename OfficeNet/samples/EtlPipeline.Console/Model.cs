// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using ExcelNet;

namespace OfficeNet.Samples.Etl;

/// <summary>One row of the source data.</summary>
internal sealed record Sales(DateTime Date, string Product, string Region, int Quantity, double Price)
{
    public double Total => Quantity * Price;

    /// <summary>Reads the imported sheet back into typed records.</summary>
    /// <remarks>
    /// CSV import gives strings and numbers; everything downstream wants a shape it can group by.
    /// Doing the conversion once here rather than reaching into cells all over the pipeline is what
    /// keeps the aggregation readable.
    /// </remarks>
    public static IEnumerable<Sales> ReadFrom(Worksheet sheet)
    {
        // Row 0 is the header.
        for (var row = 1; row < sheet.RowCount; row++)
        {
            var product = sheet[row, 1].Text;

            if (string.IsNullOrWhiteSpace(product))
            {
                continue;
            }

            yield return new Sales(
                ParseDate(sheet[row, 0].Text),
                product,
                sheet[row, 2].Text,
                (int)sheet[row, 3].Number,
                sheet[row, 4].Number);
        }
    }

    private static DateTime ParseDate(string text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value)
            ? value
            : DateTime.MinValue;
}

/// <summary>Aggregated figures for one region.</summary>
internal sealed record RegionSummary(
    string Region,
    double Revenue,
    int Quantity,
    double Average,
    int Transactions);

/// <summary>Generates the source CSV, so the sample needs no fixture file.</summary>
internal static class SampleData
{
    private static readonly string[] Products =
        ["WordNet", "ExcelNet", "PowerPointNet", "PdfNet", "OfficeNet.Rendering"];

    private static readonly string[] Regions =
        ["Jakarta", "Bandung", "Surabaya", "Medan", "Makassar"];

    /// <summary>
    /// Writes Indonesian-format CSV: <c>;</c> as the field separator, <c>,</c> as the decimal mark.
    /// </summary>
    /// <remarks>
    /// The two go together and cannot be chosen independently: a locale that writes 1.250,50 must
    /// use a field separator that is not the comma, or every number becomes two fields.
    /// </remarks>
    public static string GenerateCsv(int rows, int seed)
    {
        var random = new Random(seed);
        var builder = new StringBuilder();

        builder.Append("Tanggal;Produk;Wilayah;Qty;Harga\n");

        var start = new DateTime(2026, 1, 1);

        for (var i = 0; i < rows; i++)
        {
            var date = start.AddDays(random.Next(0, 240));
            var product = Products[random.Next(Products.Length)];
            var region = Regions[random.Next(Regions.Length)];
            var quantity = random.Next(1, 45);

            // Prices land on 500-rupiah steps, which is what real price lists look like and makes
            // the totals in the report readable.
            var price = random.Next(120, 560) * 500.0;

            builder.Append(CultureInfo.InvariantCulture,
                $"{date:yyyy-MM-dd};{product};{region};{quantity};");

            builder.Append(price.ToString("0.00", new CultureInfo("id-ID")));
            builder.Append('\n');
        }

        return builder.ToString();
    }
}
