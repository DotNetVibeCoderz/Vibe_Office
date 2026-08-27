using Cuan.Data;
using Cuan.Models;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Services;

/// <summary>Ringkasan satu masa pajak, dihitung langsung dari transaksi.</summary>
public sealed class TaxPeriodSummary
{
    public int Month { get; set; }
    public int Year { get; set; }

    public decimal OutputDpp { get; set; }
    public decimal OutputTax { get; set; }
    public int OutputCount { get; set; }

    public decimal InputDpp { get; set; }
    public decimal InputTax { get; set; }
    public int InputCount { get; set; }

    public decimal Pph21 { get; set; }
    public decimal Pph23 { get; set; }
    public decimal Pph42 { get; set; }
    public decimal Pph25 { get; set; }

    /// <summary>Positif = kurang bayar, negatif = lebih bayar.</summary>
    public decimal PpnPayable => OutputTax - InputTax;

    public decimal TotalWithholding => Pph21 + Pph23 + Pph42;

    /// <summary>Faktur penjualan di masa ini yang belum punya faktur pajak.</summary>
    public int UninvoicedSales { get; set; }

    public string PeriodLabel => $"{MonthName(Month)} {Year}";

    public static string MonthName(int month) => month switch
    {
        1 => "Januari", 2 => "Februari", 3 => "Maret", 4 => "April",
        5 => "Mei", 6 => "Juni", 7 => "Juli", 8 => "Agustus",
        9 => "September", 10 => "Oktober", 11 => "November", 12 => "Desember",
        _ => month.ToString()
    };
}

/// <summary>
/// Perhitungan pajak dan penerbitan faktur pajak.
///
/// Semua tarif diambil dari Pengaturan → Pajak, tidak ada angka yang
/// ditanam di dalam kode.
/// </summary>
public class TaxService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly DocumentNumberService _numbers;

    public TaxService(AppDbContext db, SettingsService settings, DocumentNumberService numbers)
    {
        _db = db;
        _settings = settings;
        _numbers = numbers;
    }

    public decimal PpnRate => _settings.GetRate(SettingsCatalog.PpnRate, 11);
    public decimal PpnPercent => _settings.GetDecimal(SettingsCatalog.PpnRate, 11);
    public bool PpnEnabled => _settings.PpnEnabled;

    /// <summary>
    /// Menghitung DPP dan PPN dari nilai sebelum pajak, memperhatikan pilihan
    /// "harga sudah termasuk PPN" dan faktor DPP nilai lain.
    /// </summary>
    public (decimal Dpp, decimal Ppn) Compute(decimal amount)
    {
        if (!PpnEnabled) return (amount, 0m);

        var rate = PpnRate;
        var factor = _settings.GetDecimal(SettingsCatalog.PpnDppFactor, 1m);
        if (factor <= 0) factor = 1m;

        if (_settings.GetBool(SettingsCatalog.PpnPriceInclusive, false))
        {
            var dpp = amount / (1 + rate * factor);
            return (Round(dpp), Round(dpp * factor * rate));
        }

        return (Round(amount), Round(amount * factor * rate));
    }

    private decimal Round(decimal value)
        => Math.Round(value, _settings.GetInt(SettingsCatalog.RoundingDecimals, 0), MidpointRounding.AwayFromZero);

    // =====================================================================
    // Ringkasan masa pajak
    // =====================================================================

    public async Task<TaxPeriodSummary> SummarizeAsync(int month, int year)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var sales = await _db.SalesInvoices
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end && s.Status != InvoiceStatus.Canceled)
            .Select(s => new { s.Id, s.SubTotal, s.DiscountAmount, s.TaxAmount })
            .ToListAsync();

        var purchases = await _db.PurchaseInvoices
            .Where(p => p.InvoiceDate >= start && p.InvoiceDate < end && p.Status != InvoiceStatus.Canceled)
            .Select(p => new { p.SubTotal, p.DiscountAmount, p.TaxAmount })
            .ToListAsync();

        var withholdings = await _db.WithholdingTaxes
            .Where(w => w.TaxPeriodMonth == month && w.TaxPeriodYear == year && w.IsWithholder)
            .Select(w => new { w.TaxType, w.TaxAmount })
            .ToListAsync();

        var invoicedIds = await _db.TaxInvoices
            .Where(t => t.TaxPeriodMonth == month && t.TaxPeriodYear == year
                        && t.Status != TaxInvoiceStatus.Canceled && t.SalesInvoiceId != null)
            .Select(t => t.SalesInvoiceId!.Value)
            .ToListAsync();

        return new TaxPeriodSummary
        {
            Month = month,
            Year = year,
            OutputDpp = sales.Sum(s => s.SubTotal - s.DiscountAmount),
            OutputTax = sales.Sum(s => s.TaxAmount),
            OutputCount = sales.Count,
            InputDpp = purchases.Sum(p => p.SubTotal - p.DiscountAmount),
            InputTax = purchases.Sum(p => p.TaxAmount),
            InputCount = purchases.Count,
            Pph21 = withholdings.Where(w => w.TaxType == "PPh21").Sum(w => w.TaxAmount),
            Pph23 = withholdings.Where(w => w.TaxType == "PPh23").Sum(w => w.TaxAmount),
            Pph42 = withholdings.Where(w => w.TaxType == "PPh4-2").Sum(w => w.TaxAmount),
            Pph25 = _settings.GetDecimal(SettingsCatalog.Pph25Monthly, 0),
            UninvoicedSales = sales.Count(s => !invoicedIds.Contains(s.Id))
        };
    }

    public async Task<List<TaxPeriodSummary>> SummarizeYearAsync(int year)
    {
        var result = new List<TaxPeriodSummary>();
        for (var month = 1; month <= 12; month++)
            result.Add(await SummarizeAsync(month, year));
        return result;
    }

    // =====================================================================
    // Faktur pajak
    // =====================================================================

    /// <summary>Faktur penjualan bermasa tertentu yang belum diterbitkan faktur pajaknya.</summary>
    public async Task<List<SalesInvoice>> PendingSalesAsync(int month, int year)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var already = await _db.TaxInvoices
            .Where(t => t.SalesInvoiceId != null && t.Status != TaxInvoiceStatus.Canceled)
            .Select(t => t.SalesInvoiceId!.Value)
            .ToListAsync();

        return await _db.SalesInvoices
            .Include(s => s.Customer)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end
                        && s.Status != InvoiceStatus.Canceled
                        && s.TaxAmount > 0
                        && !already.Contains(s.Id))
            .OrderBy(s => s.InvoiceDate)
            .ToListAsync();
    }

    /// <summary>
    /// Menerbitkan faktur pajak untuk sebuah faktur penjualan, mengambil satu
    /// nomor dari jatah NSFP.
    /// </summary>
    public async Task<TaxInvoice> IssueAsync(SalesInvoice invoice, string? userName = null)
    {
        if (!_settings.IsPkp)
            throw new InvalidOperationException(
                "Perusahaan belum berstatus PKP. Ubah di Pengaturan → Perusahaan sebelum menerbitkan faktur pajak.");

        if (_numbers.RemainingNsfp() <= 0)
            throw new InvalidOperationException(
                "Jatah NSFP habis. Perbarui rentang nomor seri faktur pajak di Pengaturan → Pajak.");

        var customer = invoice.Customer
            ?? await _db.Customers.FirstOrDefaultAsync(c => c.Id == invoice.CustomerId);

        var dpp = invoice.SubTotal - invoice.DiscountAmount;
        var faktur = new TaxInvoice
        {
            FakturNumber = await _numbers.NextFakturPajakAsync(invoice.InvoiceDate),
            TransactionCode = _settings.Get(SettingsCatalog.FakturTransactionCode, "01"),
            StatusCode = _settings.Get(SettingsCatalog.FakturStatusCode, "0"),
            FakturDate = invoice.InvoiceDate,
            TaxPeriodMonth = invoice.InvoiceDate.Month,
            TaxPeriodYear = invoice.InvoiceDate.Year,
            SalesInvoiceId = invoice.Id,
            CustomerId = invoice.CustomerId,
            BuyerNpwp = customer?.TaxNumber,
            BuyerName = customer?.Name,
            BuyerAddress = customer?.Address,
            Dpp = dpp,
            PpnAmount = invoice.TaxAmount,
            PpnRate = PpnPercent,
            Status = TaxInvoiceStatus.Issued,
            CreatedBy = userName
        };

        _db.TaxInvoices.Add(faktur);
        await _db.SaveChangesAsync();
        return faktur;
    }

    /// <summary>Menerbitkan faktur pajak untuk seluruh penjualan yang tertunda di satu masa.</summary>
    public async Task<(int Issued, string? Error)> IssueBatchAsync(int month, int year, string? userName = null)
    {
        var pending = await PendingSalesAsync(month, year);
        var issued = 0;

        foreach (var invoice in pending)
        {
            try
            {
                await IssueAsync(invoice, userName);
                issued++;
            }
            catch (InvalidOperationException ex)
            {
                return (issued, ex.Message);
            }
        }

        return (issued, null);
    }

    // =====================================================================
    // SPT Masa
    // =====================================================================

    /// <summary>Menyusun (atau memperbarui) draf SPT Masa PPN untuk satu masa.</summary>
    public async Task<TaxReturn> BuildPpnReturnAsync(int month, int year, string? userName = null)
    {
        var summary = await SummarizeAsync(month, year);

        var previous = await _db.TaxReturns
            .Where(r => r.ReturnType == "PPN" && r.Status != TaxReturnStatus.Rejected)
            .Where(r => (r.PeriodYear * 100 + r.PeriodMonth) < (year * 100 + month))
            .OrderByDescending(r => r.PeriodYear * 100 + r.PeriodMonth)
            .FirstOrDefaultAsync();

        var carry = previous is not null && previous.PayableAmount < 0 ? -previous.PayableAmount : 0m;

        var existing = await _db.TaxReturns
            .FirstOrDefaultAsync(r => r.ReturnType == "PPN" && r.PeriodMonth == month && r.PeriodYear == year);

        var ret = existing ?? new TaxReturn
        {
            PeriodMonth = month,
            PeriodYear = year,
            ReturnType = "PPN",
            CreatedBy = userName
        };

        if (ret.Status is TaxReturnStatus.Accepted or TaxReturnStatus.Paid)
            return ret; // SPT yang sudah diterima tidak ditimpa; buat pembetulan bila perlu.

        ret.OutputDpp = summary.OutputDpp;
        ret.OutputTax = summary.OutputTax;
        ret.InputDpp = summary.InputDpp;
        ret.InputTax = summary.InputTax;
        ret.CarryForward = carry;
        ret.PayableAmount = summary.OutputTax - summary.InputTax - carry;

        if (existing is null) _db.TaxReturns.Add(ret);
        await _db.SaveChangesAsync();
        return ret;
    }

    /// <summary>Membuat pembetulan SPT untuk masa yang sudah dilaporkan.</summary>
    public async Task<TaxReturn> ReviseAsync(TaxReturn original, string? userName = null)
    {
        var summary = await SummarizeAsync(original.PeriodMonth, original.PeriodYear);
        var revision = new TaxReturn
        {
            PeriodMonth = original.PeriodMonth,
            PeriodYear = original.PeriodYear,
            ReturnType = original.ReturnType,
            Revision = original.Revision + 1,
            OutputDpp = summary.OutputDpp,
            OutputTax = summary.OutputTax,
            InputDpp = summary.InputDpp,
            InputTax = summary.InputTax,
            CarryForward = original.CarryForward,
            PayableAmount = summary.OutputTax - summary.InputTax - original.CarryForward,
            Notes = $"Pembetulan ke-{original.Revision + 1} atas SPT {original.PeriodMonth}/{original.PeriodYear}",
            CreatedBy = userName
        };

        _db.TaxReturns.Add(revision);
        await _db.SaveChangesAsync();
        return revision;
    }

    /// <summary>Batas akhir lapor SPT Masa PPN: akhir bulan berikutnya.</summary>
    public static DateTime PpnDueDate(int month, int year)
        => new DateTime(year, month, 1).AddMonths(2).AddDays(-1);

    /// <summary>Batas akhir setor PPh masa: tanggal 10 bulan berikutnya.</summary>
    public static DateTime PphDueDate(int month, int year)
        => new DateTime(year, month, 1).AddMonths(1).AddDays(9);
}
