using Cuan.Data;
using Cuan.Models;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Services;

/// <summary>
/// Mesin posting: satu-satunya tempat yang boleh mengubah saldo akun dan
/// kuantitas barang.
///
/// Sebelumnya ChartOfAccount.CurrentBalance hanya diisi DataSeeder sehingga
/// Laba Rugi, Neraca, dan Arus Kas berhenti di angka contoh. Sekarang setiap
/// jurnal yang diposting menggerakkan saldo akun, dan setiap dokumen transaksi
/// membentuk jurnalnya sendiri sesuai pemetaan akun di Pengaturan.
/// </summary>
public class PostingService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly DocumentNumberService _numbers;

    public PostingService(AppDbContext db, SettingsService settings, DocumentNumberService numbers)
    {
        _db = db;
        _settings = settings;
        _numbers = numbers;
    }

    /// <summary>
    /// Arah normal saldo: aktiva dan beban bertambah di debit, kewajiban,
    /// modal, dan pendapatan bertambah di kredit.
    /// </summary>
    public static decimal SignedAmount(int accountType, decimal debit, decimal credit)
        => accountType is 1 or 5 ? debit - credit : credit - debit;

    // =====================================================================
    // Jurnal
    // =====================================================================

    /// <summary>Menandai jurnal sebagai diposting dan menggerakkan saldo akunnya.</summary>
    public async Task PostJournalAsync(int journalId, string? userName = null)
    {
        var journal = await _db.JournalEntries
            .Include(j => j.Details)
            .FirstOrDefaultAsync(j => j.Id == journalId);
        if (journal is null || journal.IsPosted) return;

        await ApplyDetailsAsync(journal.Details, +1);

        journal.IsPosted = true;
        journal.PostedAt = DateTime.UtcNow;
        journal.PostedBy = userName;
        await _db.SaveChangesAsync();
    }

    /// <summary>Membatalkan posting: saldo dikembalikan seperti sebelum jurnal ini.</summary>
    public async Task UnpostJournalAsync(int journalId)
    {
        var journal = await _db.JournalEntries
            .Include(j => j.Details)
            .FirstOrDefaultAsync(j => j.Id == journalId);
        if (journal is null || !journal.IsPosted) return;

        await ApplyDetailsAsync(journal.Details, -1);

        journal.IsPosted = false;
        journal.PostedAt = null;
        journal.PostedBy = null;
        await _db.SaveChangesAsync();
    }

    private async Task ApplyDetailsAsync(IEnumerable<JournalEntryDetail> details, int direction)
    {
        if (!_settings.GetBool(SettingsCatalog.PostingUpdatesBalance, true)) return;

        var ids = details.Select(d => d.ChartOfAccountId).Distinct().ToList();
        var accounts = await _db.ChartOfAccounts.Where(a => ids.Contains(a.Id)).ToListAsync();

        foreach (var detail in details)
        {
            var account = accounts.FirstOrDefault(a => a.Id == detail.ChartOfAccountId);
            if (account is null) continue;
            account.CurrentBalance += direction * SignedAmount(account.AccountType, detail.Debit, detail.Credit);
            account.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Menghitung ulang seluruh saldo akun dari saldo awal ditambah semua jurnal
    /// yang sudah diposting. Dipakai setelah impor data atau saat saldo dicurigai
    /// melenceng.
    /// </summary>
    public async Task<int> RecalculateAllBalancesAsync()
    {
        var accounts = await _db.ChartOfAccounts.ToListAsync();
        var lines = await _db.JournalEntryDetails
            .Include(d => d.JournalEntry)
            .Where(d => d.JournalEntry!.IsPosted)
            .Select(d => new { d.ChartOfAccountId, d.Debit, d.Credit })
            .ToListAsync();

        var movement = lines
            .GroupBy(l => l.ChartOfAccountId)
            .ToDictionary(g => g.Key, g => (Debit: g.Sum(x => x.Debit), Credit: g.Sum(x => x.Credit)));

        foreach (var account in accounts.Where(a => !a.IsHeader))
        {
            var delta = movement.TryGetValue(account.Id, out var m)
                ? SignedAmount(account.AccountType, m.Debit, m.Credit)
                : 0m;
            account.CurrentBalance = account.OpeningBalance + delta;
        }

        // Akun header menampung jumlah seluruh keturunannya.
        var byParent = accounts.Where(a => a.ParentId.HasValue)
            .GroupBy(a => a.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        decimal Roll(ChartOfAccount node)
        {
            if (!node.IsHeader) return node.CurrentBalance;
            var total = 0m;
            if (byParent.TryGetValue(node.Id, out var children))
                total = children.Sum(Roll);
            node.CurrentBalance = total;
            return total;
        }

        foreach (var root in accounts.Where(a => a.ParentId is null)) Roll(root);

        await _db.SaveChangesAsync();
        return accounts.Count;
    }

    // =====================================================================
    // Pembentuk jurnal dari dokumen transaksi
    // =====================================================================

    private async Task<Dictionary<string, ChartOfAccount>> MapAsync(params string[] settingKeys)
    {
        var codes = settingKeys.Select(k => _settings.Get(k)).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        var accounts = await _db.ChartOfAccounts.Where(a => codes.Contains(a.AccountCode)).ToListAsync();

        var map = new Dictionary<string, ChartOfAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in settingKeys)
        {
            var account = accounts.FirstOrDefault(a => a.AccountCode == _settings.Get(key));
            if (account is not null) map[key] = account;
        }
        return map;
    }

    private static void Line(JournalEntry journal, ChartOfAccount? account, decimal debit, decimal credit, string? note)
    {
        if (account is null) return;
        if (debit == 0 && credit == 0) return;
        journal.Details.Add(new JournalEntryDetail
        {
            ChartOfAccountId = account.Id,
            Debit = debit,
            Credit = credit,
            Description = note
        });
    }

    private async Task<JournalEntry> NewJournalAsync(DateTime date, string description, string reference,
        JournalSource source, string? userName)
    {
        return new JournalEntry
        {
            JournalNumber = await _numbers.NextAsync(_db, DocumentKind.Journal, date),
            TransactionDate = date,
            Description = description,
            Reference = reference,
            SourceType = source,
            CreatedBy = userName,
            CreatedAt = DateTime.UtcNow
        };
    }

    public bool AutoPostEnabled => _settings.GetBool(SettingsCatalog.AutoPostJournal, true);

    /// <summary>Jurnal faktur penjualan, termasuk PPN keluaran dan harga pokok.</summary>
    public async Task<JournalEntry?> PostSalesInvoiceAsync(SalesInvoice invoice, string? userName = null)
    {
        if (!AutoPostEnabled || invoice.IsPosted) return null;

        var map = await MapAsync(SettingsCatalog.AccountAr, SettingsCatalog.AccountSales,
            SettingsCatalog.AccountSalesDiscount, SettingsCatalog.AccountPpnKeluaran,
            SettingsCatalog.AccountShipping, SettingsCatalog.AccountCogs, SettingsCatalog.AccountInventory);

        var journal = await NewJournalAsync(invoice.InvoiceDate,
            $"Faktur penjualan {invoice.InvoiceNumber}", invoice.InvoiceNumber,
            JournalSource.SalesInvoice, userName);

        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountAr), invoice.GrandTotal, 0, "Piutang usaha");
        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountSalesDiscount), invoice.DiscountAmount, 0, "Diskon penjualan");
        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountSales), 0, invoice.SubTotal, "Penjualan");
        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountPpnKeluaran), 0, invoice.TaxAmount, "PPN keluaran");
        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountShipping), 0, invoice.ShippingCost, "Ongkos kirim ditagihkan");

        // Harga pokok penjualan diambil dari harga pokok barang saat faktur dibuat.
        var itemIds = invoice.Details.Select(d => d.ItemId).Distinct().ToList();
        var costs = await _db.Items.Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.CostPrice }).ToDictionaryAsync(i => i.Id, i => i.CostPrice);
        var cogs = invoice.Details.Sum(d => d.Quantity * costs.GetValueOrDefault(d.ItemId));
        if (cogs > 0)
        {
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountCogs), cogs, 0, "Harga pokok penjualan");
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountInventory), 0, cogs, "Pengurangan persediaan");
        }

        return await FinishAsync(journal, userName, j =>
        {
            invoice.JournalEntryId = j.Id;
            invoice.IsPosted = true;
        });
    }

    /// <summary>Jurnal faktur pembelian, termasuk PPN masukan.</summary>
    public async Task<JournalEntry?> PostPurchaseInvoiceAsync(PurchaseInvoice invoice, string? userName = null)
    {
        if (!AutoPostEnabled || invoice.IsPosted) return null;

        var map = await MapAsync(SettingsCatalog.AccountAp, SettingsCatalog.AccountPurchase,
            SettingsCatalog.AccountPpnMasukan, SettingsCatalog.AccountShipping);

        var journal = await NewJournalAsync(invoice.InvoiceDate,
            $"Faktur pembelian {invoice.InvoiceNumber}", invoice.InvoiceNumber,
            JournalSource.PurchaseInvoice, userName);

        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountPurchase), invoice.SubTotal, 0, "Persediaan / pembelian");
        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountPpnMasukan), invoice.TaxAmount, 0, "PPN masukan");
        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountShipping), invoice.ShippingCost, 0, "Ongkos kirim");
        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountPurchase), 0, invoice.DiscountAmount, "Diskon pembelian");
        Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountAp), 0, invoice.GrandTotal, "Hutang usaha");

        return await FinishAsync(journal, userName, j =>
        {
            invoice.JournalEntryId = j.Id;
            invoice.IsPosted = true;
        });
    }

    /// <summary>Jurnal kas masuk / kas keluar.</summary>
    public async Task<JournalEntry?> PostCashAsync(CashTransaction trx, string? userName = null)
    {
        if (!AutoPostEnabled || trx.IsPosted) return null;

        var map = await MapAsync(SettingsCatalog.AccountAr, SettingsCatalog.AccountAp,
            SettingsCatalog.AccountOtherIncome, SettingsCatalog.AccountOtherExpense);
        var cash = await _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == trx.ChartOfAccountId);

        var isIn = trx.Type == CashTransactionType.CashIn;
        var counterpart = isIn
            ? (trx.CustomerId.HasValue ? map.GetValueOrDefault(SettingsCatalog.AccountAr)
                                       : map.GetValueOrDefault(SettingsCatalog.AccountOtherIncome))
            : (trx.SupplierId.HasValue ? map.GetValueOrDefault(SettingsCatalog.AccountAp)
                                       : map.GetValueOrDefault(SettingsCatalog.AccountOtherExpense));

        var journal = await NewJournalAsync(trx.TransactionDate,
            trx.Description ?? (isIn ? "Kas masuk" : "Kas keluar"), trx.TransactionNumber,
            isIn ? JournalSource.CashIn : JournalSource.CashOut, userName);

        if (isIn)
        {
            Line(journal, cash, trx.Amount, 0, "Kas masuk");
            Line(journal, counterpart, 0, trx.Amount, trx.Description);
        }
        else
        {
            Line(journal, counterpart, trx.Amount, 0, trx.Description);
            Line(journal, cash, 0, trx.Amount, "Kas keluar");
        }

        return await FinishAsync(journal, userName, j =>
        {
            trx.JournalEntryId = j.Id;
            trx.IsPosted = true;
        });
    }

    /// <summary>Jurnal bank masuk / bank keluar terhadap akun kas rekening bank.</summary>
    public async Task<JournalEntry?> PostBankAsync(BankTransaction trx, string? userName = null)
    {
        if (!AutoPostEnabled || trx.IsPosted) return null;

        var map = await MapAsync(SettingsCatalog.AccountAr, SettingsCatalog.AccountAp,
            SettingsCatalog.AccountOtherIncome, SettingsCatalog.AccountOtherExpense, SettingsCatalog.AccountCash);

        var bank = await _db.BankAccounts.Include(b => b.ChartOfAccount)
            .FirstOrDefaultAsync(b => b.Id == trx.BankAccountId);
        var bankAccount = bank?.ChartOfAccount ?? map.GetValueOrDefault(SettingsCatalog.AccountCash);

        var isIn = trx.Type == BankTransactionType.BankIn;
        var counterpart = isIn
            ? (trx.CustomerId.HasValue ? map.GetValueOrDefault(SettingsCatalog.AccountAr)
                                       : map.GetValueOrDefault(SettingsCatalog.AccountOtherIncome))
            : (trx.SupplierId.HasValue ? map.GetValueOrDefault(SettingsCatalog.AccountAp)
                                       : map.GetValueOrDefault(SettingsCatalog.AccountOtherExpense));

        var journal = await NewJournalAsync(trx.TransactionDate,
            trx.Description ?? (isIn ? "Bank masuk" : "Bank keluar"), trx.TransactionNumber,
            isIn ? JournalSource.BankIn : JournalSource.BankOut, userName);

        if (isIn)
        {
            Line(journal, bankAccount, trx.Amount, 0, bank?.BankName);
            Line(journal, counterpart, 0, trx.Amount, trx.Description);
        }
        else
        {
            Line(journal, counterpart, trx.Amount, 0, trx.Description);
            Line(journal, bankAccount, 0, trx.Amount, bank?.BankName);
        }

        return await FinishAsync(journal, userName, j =>
        {
            trx.JournalEntryId = j.Id;
            trx.IsPosted = true;
        });
    }

    /// <summary>
    /// Jurnal giro. Saat diterima, nilainya menggantung di akun giro belum cair;
    /// akun kas baru bergerak ketika giro dicairkan.
    /// </summary>
    public async Task<JournalEntry?> PostGiroAsync(GiroTransaction trx, string? userName = null)
    {
        if (!AutoPostEnabled || trx.IsPosted) return null;

        var map = await MapAsync(SettingsCatalog.AccountGiroIn, SettingsCatalog.AccountGiroOut,
            SettingsCatalog.AccountAr, SettingsCatalog.AccountAp);

        var isIn = trx.Type == GiroType.GiroIn;
        var journal = await NewJournalAsync(trx.TransactionDate,
            trx.Description ?? (isIn ? "Giro masuk" : "Giro keluar"), trx.TransactionNumber,
            isIn ? JournalSource.GiroIn : JournalSource.GiroOut, userName);

        if (isIn)
        {
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountGiroIn), trx.Amount, 0, $"Giro {trx.GiroNumber}");
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountAr), 0, trx.Amount, "Pelunasan piutang");
        }
        else
        {
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountAp), trx.Amount, 0, "Pelunasan hutang");
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountGiroOut), 0, trx.Amount, $"Giro {trx.GiroNumber}");
        }

        return await FinishAsync(journal, userName, j =>
        {
            trx.JournalEntryId = j.Id;
            trx.IsPosted = true;
        });
    }

    /// <summary>Jurnal selisih penyesuaian stok terhadap akun persediaan.</summary>
    public async Task<JournalEntry?> PostStockAdjustmentAsync(StockMovement movement, string? userName = null)
    {
        if (!AutoPostEnabled) return null;

        var value = movement.Quantity * movement.UnitCost;
        if (value == 0) return null;

        var map = await MapAsync(SettingsCatalog.AccountInventory, SettingsCatalog.AccountStockAdjustment);
        var journal = await NewJournalAsync(movement.MovementDate,
            movement.Description ?? "Penyesuaian stok", $"ADJ-{movement.Id}",
            JournalSource.StockAdj, userName);

        if (value > 0)
        {
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountInventory), value, 0, "Stok bertambah");
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountStockAdjustment), 0, value, "Selisih penyesuaian");
        }
        else
        {
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountStockAdjustment), -value, 0, "Selisih penyesuaian");
            Line(journal, map.GetValueOrDefault(SettingsCatalog.AccountInventory), 0, -value, "Stok berkurang");
        }

        return await FinishAsync(journal, userName, _ => { });
    }

    private async Task<JournalEntry?> FinishAsync(JournalEntry journal, string? userName, Action<JournalEntry> link)
    {
        if (journal.Details.Count == 0) return null;

        var debit = journal.Details.Sum(d => d.Debit);
        var credit = journal.Details.Sum(d => d.Credit);
        if (Math.Abs(debit - credit) > 0.005m)
            throw new InvalidOperationException(
                $"Jurnal {journal.JournalNumber} tidak balance: debit {debit:N0} vs kredit {credit:N0}. " +
                "Periksa Pengaturan → Pemetaan Akun.");

        _db.JournalEntries.Add(journal);
        await _db.SaveChangesAsync();

        await ApplyDetailsAsync(journal.Details, +1);
        journal.IsPosted = true;
        journal.PostedAt = DateTime.UtcNow;
        journal.PostedBy = userName;

        link(journal);
        await _db.SaveChangesAsync();
        return journal;
    }

    /// <summary>Membatalkan jurnal yang lahir dari sebuah dokumen.</summary>
    public async Task ReverseDocumentJournalAsync(int? journalEntryId)
    {
        if (journalEntryId is null) return;
        var journal = await _db.JournalEntries.Include(j => j.Details)
            .FirstOrDefaultAsync(j => j.Id == journalEntryId);
        if (journal is null) return;

        if (journal.IsPosted) await ApplyDetailsAsync(journal.Details, -1);
        _db.JournalEntries.Remove(journal);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // Stok
    // =====================================================================

    /// <summary>
    /// Menerapkan mutasi stok ke kuantitas barang dan stok per gudang.
    /// Sebelumnya StockMovement hanya dicatat tanpa pernah mengubah stok.
    /// </summary>
    public async Task ApplyStockMovementAsync(StockMovement movement, int direction = +1)
    {
        if (!_settings.GetBool(SettingsCatalog.StockMovementUpdatesQty, true)) return;

        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == movement.ItemId);
        if (item is null || item.IsService) return;

        var delta = direction * movement.Quantity;
        item.StockQuantity += delta;
        item.UpdatedAt = DateTime.UtcNow;

        if (movement.WarehouseId > 0)
        {
            var stock = await _db.ItemStocks
                .FirstOrDefaultAsync(s => s.ItemId == movement.ItemId && s.WarehouseId == movement.WarehouseId);
            if (stock is null)
            {
                stock = new ItemStock { ItemId = movement.ItemId, WarehouseId = movement.WarehouseId };
                _db.ItemStocks.Add(stock);
            }
            stock.Quantity += delta;
            stock.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>Mencatat keluarnya barang akibat faktur penjualan.</summary>
    public async Task<string?> ApplySalesStockAsync(SalesInvoice invoice, string? userName = null)
    {
        var allowNegative = _settings.GetBool(SettingsCatalog.AllowNegativeStock, false);

        foreach (var line in invoice.Details)
        {
            var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == line.ItemId);
            if (item is null || item.IsService) continue;

            if (!allowNegative && item.StockQuantity < line.Quantity)
                return $"Stok {item.ItemName} tinggal {item.StockQuantity:N0}, diminta {line.Quantity:N0}. " +
                       "Aktifkan \"Izinkan stok minus\" di Pengaturan bila memang dikehendaki.";
        }

        foreach (var line in invoice.Details)
        {
            var movement = new StockMovement
            {
                MovementDate = invoice.InvoiceDate,
                Type = StockMovementType.Sales,
                ItemId = line.ItemId,
                WarehouseId = invoice.WarehouseId ?? 0,
                Quantity = -line.Quantity,
                UnitCost = line.UnitPrice,
                Description = $"Penjualan {invoice.InvoiceNumber}",
                ReferenceType = nameof(SalesInvoice),
                ReferenceId = invoice.Id,
                CreatedBy = userName
            };
            _db.StockMovements.Add(movement);
            await ApplyStockMovementAsync(movement);
        }

        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>Mencatat masuknya barang akibat faktur pembelian.</summary>
    public async Task ApplyPurchaseStockAsync(PurchaseInvoice invoice, string? userName = null)
    {
        foreach (var line in invoice.Details)
        {
            var movement = new StockMovement
            {
                MovementDate = invoice.InvoiceDate,
                Type = StockMovementType.Purchase,
                ItemId = line.ItemId,
                WarehouseId = invoice.WarehouseId ?? 0,
                Quantity = line.Quantity,
                UnitCost = line.UnitPrice,
                Description = $"Pembelian {invoice.InvoiceNumber}",
                ReferenceType = nameof(PurchaseInvoice),
                ReferenceId = invoice.Id,
                CreatedBy = userName
            };
            _db.StockMovements.Add(movement);
            await ApplyStockMovementAsync(movement);

            // Harga pokok rata-rata bergerak.
            var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == line.ItemId);
            if (item is not null && item.StockQuantity > 0)
            {
                var before = item.StockQuantity - line.Quantity;
                var valueBefore = before * item.CostPrice;
                var valueIn = line.Quantity * line.UnitPrice;
                item.CostPrice = Math.Round((valueBefore + valueIn) / item.StockQuantity, 2);
            }
        }

        await _db.SaveChangesAsync();
    }
}
