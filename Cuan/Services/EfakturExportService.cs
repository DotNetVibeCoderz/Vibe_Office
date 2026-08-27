using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Cuan.Data;
using Cuan.Models;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Services;

/// <summary>
/// Menyiapkan berkas pelaporan faktur pajak.
///
/// Dua bentuk keluaran:
///  - CSV mengikuti tata letak impor aplikasi e-Faktur desktop (baris FK / LT / OF).
///  - XML mengikuti bentuk unggahan massal Coretax.
///
/// Tata letak berkas DJP berubah dari waktu ke waktu. Cocokkan keluaran ini
/// dengan spesifikasi terbaru yang berlaku untuk perusahaan Anda sebelum
/// dipakai melapor. Bentuk berkas juga bisa diganti tanpa menyentuh kode:
/// Pengaturan → Integrasi DJP → Format ekspor faktur pajak.
/// </summary>
public class EfakturExportService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;

    public EfakturExportService(AppDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    private static string Clean(string? value)
        => (value ?? string.Empty).Replace(",", " ").Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Digits(string? npwp)
        => new string((npwp ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string Num(decimal value)
        => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    // =====================================================================
    // CSV e-Faktur (FK / LT / OF)
    // =====================================================================

    public async Task<byte[]> ExportCsvAsync(int month, int year)
    {
        var fakturs = await LoadAsync(month, year);
        var sb = new StringBuilder();

        // Baris definisi kolom, sebagaimana diminta pengimpor e-Faktur.
        sb.AppendLine("FK,KD_JENIS_TRANSAKSI,FG_PENGGANTI,NOMOR_FAKTUR,MASA_PAJAK,TAHUN_PAJAK,TANGGAL_FAKTUR,NPWP,NAMA,ALAMAT_LENGKAP,JUMLAH_DPP,JUMLAH_PPN,JUMLAH_PPNBM,ID_KETERANGAN_TAMBAHAN,FG_UANG_MUKA,UANG_MUKA_DPP,UANG_MUKA_PPN,UANG_MUKA_PPNBM,REFERENSI,KODE_DOKUMEN_PENDUKUNG");
        sb.AppendLine("LT,NPWP,NAMA,JALAN,BLOK,NOMOR,RT,RW,KECAMATAN,KELURAHAN,KABUPATEN,PROPINSI,KODE_POS,NOMOR_TELEPON");
        sb.AppendLine("OF,KODE_OBJEK,NAMA,HARGA_SATUAN,JUMLAH_BARANG,HARGA_TOTAL,DISKON,DPP,PPN,TARIF_PPNBM,PPNBM");

        foreach (var faktur in fakturs)
        {
            // Nomor faktur di berkas ditulis tanpa titik dan strip.
            var serial = new string(faktur.FakturNumber.Where(char.IsDigit).ToArray());

            sb.Append("FK,")
              .Append(faktur.TransactionCode).Append(',')
              .Append(faktur.StatusCode).Append(',')
              .Append(serial).Append(',')
              .Append(faktur.TaxPeriodMonth).Append(',')
              .Append(faktur.TaxPeriodYear).Append(',')
              .Append(faktur.FakturDate.ToString("dd/MM/yyyy")).Append(',')
              .Append(Digits(faktur.BuyerNpwp)).Append(',')
              .Append(Clean(faktur.BuyerName)).Append(',')
              .Append(Clean(faktur.BuyerAddress)).Append(',')
              .Append(Num(faktur.Dpp)).Append(',')
              .Append(Num(faktur.PpnAmount)).Append(',')
              .Append(Num(faktur.PpnbmAmount)).Append(',')
              .Append(",0,0,0,0,")
              .Append(Clean(faktur.SalesInvoice?.InvoiceNumber)).Append(',')
              .AppendLine();

            var customer = faktur.Customer;
            sb.Append("LT,")
              .Append(Digits(faktur.BuyerNpwp)).Append(',')
              .Append(Clean(faktur.BuyerName)).Append(',')
              .Append(Clean(faktur.BuyerAddress)).Append(',')
              .Append(",,,,,,")
              .Append(Clean(customer?.City)).Append(',')
              .Append(",,")
              .Append(Clean(customer?.Phone))
              .AppendLine();

            var lines = faktur.SalesInvoice?.Details;
            if (lines is null || lines.Count == 0)
            {
                sb.Append("OF,,")
                  .Append(Clean(faktur.SalesInvoice?.Description ?? "Penyerahan barang/jasa")).Append(',')
                  .Append(Num(faktur.Dpp)).Append(",1,")
                  .Append(Num(faktur.Dpp)).Append(",0,")
                  .Append(Num(faktur.Dpp)).Append(',')
                  .Append(Num(faktur.PpnAmount)).Append(",0,0")
                  .AppendLine();
                continue;
            }

            foreach (var line in lines)
            {
                var gross = line.Quantity * line.UnitPrice;
                sb.Append("OF,")
                  .Append(Clean(line.Item?.ItemCode)).Append(',')
                  .Append(Clean(line.Item?.ItemName ?? line.Description)).Append(',')
                  .Append(Num(line.UnitPrice)).Append(',')
                  .Append(Num(line.Quantity)).Append(',')
                  .Append(Num(gross)).Append(',')
                  .Append(Num(line.DiscountAmount)).Append(',')
                  .Append(Num(line.LineTotal)).Append(',')
                  .Append(Num(line.TaxAmount)).Append(",0,0")
                  .AppendLine();
            }
        }

        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    // =====================================================================
    // XML Coretax
    // =====================================================================

    public async Task<byte[]> ExportXmlAsync(int month, int year)
    {
        var fakturs = await LoadAsync(month, year);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("TaxInvoiceBulk",
                new XElement("TIN", Digits(_settings.CompanyNpwp)),
                new XElement("NITKU", _settings.Get(SettingsCatalog.CompanyNitku)),
                new XElement("TaxPeriodMonth", month),
                new XElement("TaxPeriodYear", year),
                new XElement("GeneratedAt", DateTime.UtcNow.ToString("o")),
                new XElement("ListOfTaxInvoice",
                    fakturs.Select(faktur => new XElement("TaxInvoice",
                        new XElement("TaxInvoiceDate", faktur.FakturDate.ToString("yyyy-MM-dd")),
                        new XElement("TaxInvoiceOpt", faktur.TransactionCode),
                        new XElement("TrxCode", faktur.TransactionCode),
                        new XElement("AddInfo", string.Empty),
                        new XElement("CustomDoc", string.Empty),
                        new XElement("RefDesc", faktur.SalesInvoice?.InvoiceNumber ?? string.Empty),
                        new XElement("FacilityStamp", string.Empty),
                        new XElement("SellerIDTKU", _settings.Get(SettingsCatalog.CompanyNitku)),
                        new XElement("BuyerTin", Digits(faktur.BuyerNpwp)),
                        new XElement("BuyerDocument", "TIN"),
                        new XElement("BuyerName", faktur.BuyerName ?? string.Empty),
                        new XElement("BuyerAdress", faktur.BuyerAddress ?? string.Empty),
                        new XElement("BuyerEmail", faktur.Customer?.Email ?? string.Empty),
                        new XElement("SerialNumber", faktur.FakturNumber),
                        new XElement("ListOfGoodService", BuildGoods(faktur))
                    ))
                )
            )
        );

        using var ms = new MemoryStream();
        doc.Save(ms, SaveOptions.None);
        return ms.ToArray();
    }

    private IEnumerable<XElement> BuildGoods(TaxInvoice faktur)
    {
        var lines = faktur.SalesInvoice?.Details;

        if (lines is null || lines.Count == 0)
        {
            yield return new XElement("GoodService",
                new XElement("Opt", "B"),
                new XElement("Code", "000000"),
                new XElement("Name", faktur.SalesInvoice?.Description ?? "Penyerahan barang/jasa"),
                new XElement("Unit", "UM.0018"),
                new XElement("Price", Num(faktur.Dpp)),
                new XElement("Qty", "1"),
                new XElement("TotalDiscount", "0"),
                new XElement("TaxBase", Num(faktur.Dpp)),
                new XElement("OtherTaxBase", Num(faktur.Dpp)),
                new XElement("VATRate", Num(faktur.PpnRate)),
                new XElement("VAT", Num(faktur.PpnAmount)),
                new XElement("STLGRate", "0"),
                new XElement("STLG", "0"));
            yield break;
        }

        foreach (var line in lines)
        {
            yield return new XElement("GoodService",
                new XElement("Opt", line.Item?.IsService == true ? "A" : "B"),
                new XElement("Code", line.Item?.ItemCode ?? "000000"),
                new XElement("Name", line.Item?.ItemName ?? line.Description ?? string.Empty),
                new XElement("Unit", "UM.0018"),
                new XElement("Price", Num(line.UnitPrice)),
                new XElement("Qty", Num(line.Quantity)),
                new XElement("TotalDiscount", Num(line.DiscountAmount)),
                new XElement("TaxBase", Num(line.LineTotal)),
                new XElement("OtherTaxBase", Num(line.LineTotal)),
                new XElement("VATRate", Num(line.TaxPercent)),
                new XElement("VAT", Num(line.TaxAmount)),
                new XElement("STLGRate", "0"),
                new XElement("STLG", "0"));
        }
    }

    // =====================================================================
    // Bukti potong PPh — rekap CSV untuk unggahan e-Bupot
    // =====================================================================

    public async Task<byte[]> ExportWithholdingCsvAsync(int month, int year)
    {
        var slips = await _db.WithholdingTaxes
            .Where(w => w.TaxPeriodMonth == month && w.TaxPeriodYear == year && w.IsWithholder)
            .OrderBy(w => w.SlipDate)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("NOMOR_BUKTI_POTONG,JENIS_PAJAK,MASA_PAJAK,TAHUN_PAJAK,TANGGAL,NPWP_DIPOTONG,NAMA_DIPOTONG,DPP,TARIF,PPH,KETERANGAN");

        foreach (var slip in slips)
        {
            sb.Append(Clean(slip.SlipNumber)).Append(',')
              .Append(Clean(slip.TaxType)).Append(',')
              .Append(slip.TaxPeriodMonth).Append(',')
              .Append(slip.TaxPeriodYear).Append(',')
              .Append(slip.SlipDate.ToString("dd/MM/yyyy")).Append(',')
              .Append(Digits(slip.PartyNpwp)).Append(',')
              .Append(Clean(slip.PartyName)).Append(',')
              .Append(Num(slip.Dpp)).Append(',')
              .Append(Num(slip.Rate)).Append(',')
              .Append(Num(slip.TaxAmount)).Append(',')
              .Append(Clean(slip.Description))
              .AppendLine();
        }

        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private async Task<List<TaxInvoice>> LoadAsync(int month, int year)
        => await _db.TaxInvoices
            .Include(t => t.Customer)
            .Include(t => t.SalesInvoice)!.ThenInclude(s => s!.Details)!.ThenInclude(d => d.Item)
            .Where(t => t.TaxPeriodMonth == month && t.TaxPeriodYear == year
                        && t.Status != TaxInvoiceStatus.Canceled)
            .OrderBy(t => t.FakturNumber)
            .ToListAsync();

    public string FileNameFor(int month, int year, string extension)
        => $"efaktur-{year}-{month:00}.{extension}";
}
