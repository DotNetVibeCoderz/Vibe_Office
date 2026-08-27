using Cuan.Data;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Services;

public enum DocumentKind
{
    Journal,
    SalesInvoice,
    PurchaseInvoice,
    CashIn,
    CashOut,
    BankIn,
    BankOut,
    GiroIn,
    GiroOut,
    StockAdjustment,
    BankTransfer
}

/// <summary>
/// Pembuat nomor dokumen berurut.
///
/// Nomor diambil dari nomor terbesar yang sudah ada di basis data — bukan dari
/// hitungan baris di memori — lalu seluruh pembuatan nomor diserialkan dengan
/// satu kunci proses supaya dua circuit tidak pernah memperoleh nomor kembar.
/// Pola, awalan, panjang digit, dan periode pengulangan diatur dari halaman
/// Pengaturan.
/// </summary>
public class DocumentNumberService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly SettingsService _settings;

    public DocumentNumberService(SettingsService settings) => _settings = settings;

    private string PrefixKeyOf(DocumentKind kind) => kind switch
    {
        DocumentKind.Journal => SettingsCatalog.PrefixJournal,
        DocumentKind.SalesInvoice => SettingsCatalog.PrefixSalesInvoice,
        DocumentKind.PurchaseInvoice => SettingsCatalog.PrefixPurchaseInvoice,
        DocumentKind.CashIn => SettingsCatalog.PrefixCashIn,
        DocumentKind.CashOut => SettingsCatalog.PrefixCashOut,
        DocumentKind.BankIn => SettingsCatalog.PrefixBankIn,
        DocumentKind.BankOut => SettingsCatalog.PrefixBankOut,
        DocumentKind.GiroIn => SettingsCatalog.PrefixGiroIn,
        DocumentKind.GiroOut => SettingsCatalog.PrefixGiroOut,
        DocumentKind.StockAdjustment => SettingsCatalog.PrefixStock,
        DocumentKind.BankTransfer => SettingsCatalog.PrefixTransfer,
        _ => SettingsCatalog.PrefixJournal
    };

    /// <summary>Bagian nomor sebelum urutan, mis. "INV-2026-".</summary>
    public string StemFor(DocumentKind kind, DateTime date)
    {
        var format = _settings.Get(SettingsCatalog.DocumentNumberFormat, "{PREFIX}-{YYYY}-{SEQ}");
        var reset = _settings.Get(SettingsCatalog.DocumentNumberReset, "Tahun");

        var stem = format
            .Replace("{PREFIX}", _settings.Get(PrefixKeyOf(kind)))
            .Replace("{YYYY}", reset == "Tidak pernah" ? "" : date.Year.ToString("0000"))
            .Replace("{MM}", date.Month.ToString("00"));

        var seqAt = stem.IndexOf("{SEQ}", StringComparison.Ordinal);
        return seqAt < 0 ? stem : stem[..seqAt];
    }

    public async Task<string> NextAsync(AppDbContext db, DocumentKind kind, DateTime date)
    {
        await Gate.WaitAsync();
        try
        {
            var stem = StemFor(kind, date);
            var existing = await ExistingNumbersAsync(db, kind, stem);

            var next = 1;
            foreach (var number in existing)
            {
                var tail = number.Length > stem.Length ? number[stem.Length..] : string.Empty;
                var digits = new string(tail.TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n) && n >= next) next = n + 1;
            }

            var padding = _settings.GetInt(SettingsCatalog.DocumentNumberPadding, 4);
            return stem + next.ToString(new string('0', Math.Clamp(padding, 1, 10)));
        }
        finally { Gate.Release(); }
    }

    private static async Task<List<string>> ExistingNumbersAsync(AppDbContext db, DocumentKind kind, string stem) => kind switch
    {
        DocumentKind.Journal => await db.JournalEntries
            .Where(x => x.JournalNumber.StartsWith(stem)).Select(x => x.JournalNumber).ToListAsync(),
        DocumentKind.SalesInvoice => await db.SalesInvoices
            .Where(x => x.InvoiceNumber.StartsWith(stem)).Select(x => x.InvoiceNumber).ToListAsync(),
        DocumentKind.PurchaseInvoice => await db.PurchaseInvoices
            .Where(x => x.InvoiceNumber.StartsWith(stem)).Select(x => x.InvoiceNumber).ToListAsync(),
        DocumentKind.CashIn or DocumentKind.CashOut => await db.CashTransactions
            .Where(x => x.TransactionNumber.StartsWith(stem)).Select(x => x.TransactionNumber).ToListAsync(),
        DocumentKind.BankIn or DocumentKind.BankOut => await db.BankTransactions
            .Where(x => x.TransactionNumber.StartsWith(stem)).Select(x => x.TransactionNumber).ToListAsync(),
        DocumentKind.GiroIn or DocumentKind.GiroOut => await db.GiroTransactions
            .Where(x => x.TransactionNumber.StartsWith(stem)).Select(x => x.TransactionNumber).ToListAsync(),
        DocumentKind.BankTransfer => await db.BankTransfers
            .Where(x => x.TransferNumber.StartsWith(stem)).Select(x => x.TransferNumber).ToListAsync(),
        _ => new List<string>()
    };

    /// <summary>
    /// Nomor Seri Faktur Pajak berikutnya, format DJP: 000.000-00.00000000.
    /// Jatah NSFP dan nomor berjalan diatur di Pengaturan → Pajak.
    /// </summary>
    public async Task<string> NextFakturPajakAsync(DateTime date)
    {
        await Gate.WaitAsync();
        try
        {
            var branch = _settings.Get(SettingsCatalog.NsfpPrefix, "000").PadLeft(3, '0');
            var code = _settings.Get(SettingsCatalog.FakturTransactionCode, "01");
            var status = _settings.Get(SettingsCatalog.FakturStatusCode, "0");
            var current = _settings.Get(SettingsCatalog.NsfpNext, "00000001");

            if (!long.TryParse(current, out var serial)) serial = 1;
            var year = (date.Year % 100).ToString("00");
            var number = $"{code}{status}.{branch}-{year}.{serial:00000000}";

            await _settings.SaveOneAsync(SettingsCatalog.NsfpNext, (serial + 1).ToString("00000000"));
            return number;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Sisa jatah NSFP yang belum dipakai.</summary>
    public int RemainingNsfp()
    {
        long.TryParse(_settings.Get(SettingsCatalog.NsfpRangeEnd, "0"), out var end);
        long.TryParse(_settings.Get(SettingsCatalog.NsfpNext, "0"), out var next);
        return (int)Math.Max(0, end - next + 1);
    }
}
