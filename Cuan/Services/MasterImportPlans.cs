using Cuan.Data;
using Cuan.Models;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Services;

/// <summary>
/// Rencana impor untuk setiap data induk.
///
/// Tiap rencana dibangun sesaat sebelum dipakai karena beberapa kolom perlu
/// mencocokkan teks dengan data yang sudah ada (nama kategori, kode satuan,
/// kode akun), dan daftar itu hanya diketahui setelah membaca basis data.
/// </summary>
public class MasterImportPlans
{
    private readonly AppDbContext _db;

    public MasterImportPlans(AppDbContext db) => _db = db;

    private static ImportColumn<T> Col<T>(string header, Action<T, string> apply,
        bool required = false, string? example = null, string? hint = null)
        => new() { Header = header, Apply = apply, Required = required, Example = example, Hint = hint };

    // =====================================================================
    // Chart of Accounts
    // =====================================================================
    public async Task<ImportPlan<ChartOfAccount>> ChartOfAccountsAsync()
    {
        var existing = await _db.ChartOfAccounts.Select(a => a.AccountCode).ToListAsync();
        var byCode = await _db.ChartOfAccounts.ToDictionaryAsync(a => a.AccountCode, a => a.Id);

        var types = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Aktiva"] = 1, ["Aset"] = 1, ["Kewajiban"] = 2, ["Liabilitas"] = 2,
            ["Modal"] = 3, ["Ekuitas"] = 3, ["Pendapatan"] = 4, ["Beban"] = 5, ["Biaya"] = 5
        };

        return new ImportPlan<ChartOfAccount>
        {
            EntityName = "Daftar Akun",
            SheetName = "Akun",
            KeyOf = a => a.AccountCode,
            ExistingKeys = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase),
            Notes =
            {
                "Jenis akun diisi salah satu dari: Aktiva, Kewajiban, Modal, Pendapatan, Beban.",
                "Kode induk diisi kode akun lain yang bertanda Header = ya.",
                "Akun header adalah kelompok, tidak menampung transaksi."
            },
            Columns = new()
            {
                Col<ChartOfAccount>("Kode Akun", (a, v) => a.AccountCode = v, true, "1-1100",
                    "Kode unik, contoh 1-1100"),
                Col<ChartOfAccount>("Nama Akun", (a, v) => a.AccountName = v, true, "Kas Kecil"),
                Col<ChartOfAccount>("Jenis", (a, v) => a.AccountType = ImportService.Lookup(types, v, "Jenis akun"),
                    true, "Aktiva", "Aktiva / Kewajiban / Modal / Pendapatan / Beban"),
                Col<ChartOfAccount>("Kode Induk", (a, v) => a.ParentId = ImportService.Lookup(byCode, v, "Kode induk"),
                    false, "1-1000", "Kosongkan bila akun ini tidak punya induk"),
                Col<ChartOfAccount>("Header", (a, v) => a.IsHeader = ImportService.ParseBool(v), false, "tidak",
                    "ya bila akun kelompok"),
                Col<ChartOfAccount>("Saldo Awal", (a, v) => a.OpeningBalance = ImportService.ParseDecimal(v, "Saldo Awal"),
                    false, "0"),
                Col<ChartOfAccount>("Aktif", (a, v) => a.IsActive = ImportService.ParseBool(v), false, "ya"),
            },
            Commit = async (db, rows) =>
            {
                var summary = new ImportSummary();
                var current = await db.ChartOfAccounts.ToDictionaryAsync(a => a.AccountCode, StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows.Where(r => r.IsValid))
                {
                    var incoming = row.Entity;
                    if (current.TryGetValue(incoming.AccountCode, out var target))
                    {
                        target.AccountName = incoming.AccountName;
                        target.AccountType = incoming.AccountType;
                        target.ParentId = incoming.ParentId ?? target.ParentId;
                        target.IsHeader = incoming.IsHeader;
                        target.OpeningBalance = incoming.OpeningBalance;
                        target.IsActive = incoming.IsActive;
                        target.UpdatedAt = DateTime.UtcNow;
                        summary.Updated++;
                    }
                    else
                    {
                        db.ChartOfAccounts.Add(incoming);
                        summary.Inserted++;
                    }
                }

                await db.SaveChangesAsync();
                return summary;
            }
        };
    }

    // =====================================================================
    // Barang
    // =====================================================================
    public async Task<ImportPlan<Item>> ItemsAsync()
    {
        var existing = await _db.Items.Select(i => i.ItemCode).ToListAsync();
        var categories = await _db.ItemCategories.ToDictionaryAsync(c => c.Name, c => c.Id);
        var units = await _db.UnitOfMeasures.ToDictionaryAsync(u => u.Code, u => u.Id);

        return new ImportPlan<Item>
        {
            EntityName = "Barang",
            SheetName = "Barang",
            KeyOf = i => i.ItemCode,
            ExistingKeys = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase),
            Notes =
            {
                $"Kategori yang tersedia: {string.Join(", ", categories.Keys)}.",
                $"Satuan yang tersedia: {string.Join(", ", units.Keys)}.",
                "Stok awal hanya dipakai saat barang pertama kali dibuat; untuk barang yang sudah ada, " +
                "ubah kuantitasnya lewat Penyesuaian Stok agar mutasinya tercatat."
            },
            Columns = new()
            {
                Col<Item>("Kode Barang", (i, v) => i.ItemCode = v, true, "BRG-001"),
                Col<Item>("Nama Barang", (i, v) => i.ItemName = v, true, "Kertas A4 80gr"),
                Col<Item>("Kategori", (i, v) => i.CategoryId = ImportService.Lookup(categories, v, "Kategori"),
                    true, "Alat Tulis Kantor"),
                Col<Item>("Satuan", (i, v) => i.UnitOfMeasureId = ImportService.Lookup(units, v, "Satuan"),
                    true, "PCS"),
                Col<Item>("Harga Pokok", (i, v) => i.CostPrice = ImportService.ParseDecimal(v, "Harga Pokok"),
                    false, "45000"),
                Col<Item>("Harga Jual", (i, v) => i.SellingPrice = ImportService.ParseDecimal(v, "Harga Jual"),
                    false, "55000"),
                Col<Item>("Stok Awal", (i, v) => i.StockQuantity = ImportService.ParseDecimal(v, "Stok Awal"),
                    false, "0", "Hanya berlaku untuk barang baru"),
                Col<Item>("Stok Minimum", (i, v) => i.MinimumStock = ImportService.ParseDecimal(v, "Stok Minimum"),
                    false, "10"),
                Col<Item>("Stok Maksimum", (i, v) => i.MaximumStock = ImportService.ParseDecimal(v, "Stok Maksimum"),
                    false, "100"),
                Col<Item>("Jasa", (i, v) => i.IsService = ImportService.ParseBool(v), false, "tidak",
                    "ya bila tidak punya stok"),
                Col<Item>("Aktif", (i, v) => i.IsActive = ImportService.ParseBool(v), false, "ya"),
                Col<Item>("Keterangan", (i, v) => i.Description = v, false),
            },
            Validate = item => item.SellingPrice < 0 || item.CostPrice < 0
                ? "Harga tidak boleh negatif"
                : null,
            Commit = async (db, rows) =>
            {
                var summary = new ImportSummary();
                var current = await db.Items.ToDictionaryAsync(i => i.ItemCode, StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows.Where(r => r.IsValid))
                {
                    var incoming = row.Entity;
                    if (current.TryGetValue(incoming.ItemCode, out var target))
                    {
                        target.ItemName = incoming.ItemName;
                        target.CategoryId = incoming.CategoryId;
                        target.UnitOfMeasureId = incoming.UnitOfMeasureId;
                        target.CostPrice = incoming.CostPrice;
                        target.SellingPrice = incoming.SellingPrice;
                        target.MinimumStock = incoming.MinimumStock;
                        target.MaximumStock = incoming.MaximumStock;
                        target.IsService = incoming.IsService;
                        target.IsActive = incoming.IsActive;
                        target.Description = incoming.Description;
                        // Stok tidak ditimpa: perubahannya harus lewat mutasi stok.
                        target.UpdatedAt = DateTime.UtcNow;
                        summary.Updated++;
                    }
                    else
                    {
                        db.Items.Add(incoming);
                        summary.Inserted++;
                    }
                }

                await db.SaveChangesAsync();
                return summary;
            }
        };
    }

    // =====================================================================
    // Pelanggan
    // =====================================================================
    public async Task<ImportPlan<Customer>> CustomersAsync()
    {
        var existing = await _db.Customers.Select(c => c.CustomerCode).ToListAsync();

        return new ImportPlan<Customer>
        {
            EntityName = "Pelanggan",
            SheetName = "Pelanggan",
            KeyOf = c => c.CustomerCode,
            ExistingKeys = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase),
            Notes = { "NPWP diperlukan bila pelanggan akan diterbitkan faktur pajak." },
            Columns = new()
            {
                Col<Customer>("Kode Pelanggan", (c, v) => c.CustomerCode = v, true, "CUS-001"),
                Col<Customer>("Nama", (c, v) => c.Name = v, true, "Toko Maju Jaya"),
                Col<Customer>("Alamat", (c, v) => c.Address = v, false, "Jl. Merdeka No. 10"),
                Col<Customer>("Kota", (c, v) => c.City = v, false, "Jakarta"),
                Col<Customer>("Telepon", (c, v) => c.Phone = v, false, "021-111-0001"),
                Col<Customer>("Email", (c, v) => c.Email = v, false, "toko@email.com"),
                Col<Customer>("NPWP", (c, v) => c.TaxNumber = v, false, "01.111.111.1-001.000"),
                Col<Customer>("Batas Kredit", (c, v) => c.CreditLimit = ImportService.ParseDecimal(v, "Batas Kredit"),
                    false, "50000000"),
                Col<Customer>("Saldo Piutang", (c, v) => c.Balance = ImportService.ParseDecimal(v, "Saldo Piutang"),
                    false, "0", "Hanya dipakai saat pelanggan pertama kali dibuat"),
                Col<Customer>("Aktif", (c, v) => c.IsActive = ImportService.ParseBool(v), false, "ya"),
            },
            Commit = async (db, rows) =>
            {
                var summary = new ImportSummary();
                var current = await db.Customers.ToDictionaryAsync(c => c.CustomerCode, StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows.Where(r => r.IsValid))
                {
                    var incoming = row.Entity;
                    if (current.TryGetValue(incoming.CustomerCode, out var target))
                    {
                        target.Name = incoming.Name;
                        target.Address = incoming.Address;
                        target.City = incoming.City;
                        target.Phone = incoming.Phone;
                        target.Email = incoming.Email;
                        target.TaxNumber = incoming.TaxNumber;
                        target.CreditLimit = incoming.CreditLimit;
                        target.IsActive = incoming.IsActive;
                        // Saldo piutang tidak ditimpa: nilainya berasal dari transaksi.
                        summary.Updated++;
                    }
                    else
                    {
                        db.Customers.Add(incoming);
                        summary.Inserted++;
                    }
                }

                await db.SaveChangesAsync();
                return summary;
            }
        };
    }

    // =====================================================================
    // Pemasok
    // =====================================================================
    public async Task<ImportPlan<Supplier>> SuppliersAsync()
    {
        var existing = await _db.Suppliers.Select(s => s.SupplierCode).ToListAsync();

        return new ImportPlan<Supplier>
        {
            EntityName = "Pemasok",
            SheetName = "Pemasok",
            KeyOf = s => s.SupplierCode,
            ExistingKeys = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase),
            Columns = new()
            {
                Col<Supplier>("Kode Pemasok", (s, v) => s.SupplierCode = v, true, "SUP-001"),
                Col<Supplier>("Nama", (s, v) => s.Name = v, true, "PT. Distribusi Nusantara"),
                Col<Supplier>("Alamat", (s, v) => s.Address = v, false, "Jl. Industri No. 1"),
                Col<Supplier>("Kota", (s, v) => s.City = v, false, "Jakarta"),
                Col<Supplier>("Telepon", (s, v) => s.Phone = v, false, "021-901-0001"),
                Col<Supplier>("Email", (s, v) => s.Email = v, false, "supplier@email.com"),
                Col<Supplier>("NPWP", (s, v) => s.TaxNumber = v, false, "02.111.111.1-001.000"),
                Col<Supplier>("Saldo Hutang", (s, v) => s.Balance = ImportService.ParseDecimal(v, "Saldo Hutang"),
                    false, "0", "Hanya dipakai saat pemasok pertama kali dibuat"),
                Col<Supplier>("Aktif", (s, v) => s.IsActive = ImportService.ParseBool(v), false, "ya"),
            },
            Commit = async (db, rows) =>
            {
                var summary = new ImportSummary();
                var current = await db.Suppliers.ToDictionaryAsync(s => s.SupplierCode, StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows.Where(r => r.IsValid))
                {
                    var incoming = row.Entity;
                    if (current.TryGetValue(incoming.SupplierCode, out var target))
                    {
                        target.Name = incoming.Name;
                        target.Address = incoming.Address;
                        target.City = incoming.City;
                        target.Phone = incoming.Phone;
                        target.Email = incoming.Email;
                        target.TaxNumber = incoming.TaxNumber;
                        target.IsActive = incoming.IsActive;
                        summary.Updated++;
                    }
                    else
                    {
                        db.Suppliers.Add(incoming);
                        summary.Inserted++;
                    }
                }

                await db.SaveChangesAsync();
                return summary;
            }
        };
    }

    // =====================================================================
    // Gudang & cabang
    // =====================================================================
    public async Task<ImportPlan<Warehouse>> WarehousesAsync()
    {
        var existing = await _db.Warehouses.Select(w => w.Code).ToListAsync();

        return new ImportPlan<Warehouse>
        {
            EntityName = "Gudang & Cabang",
            SheetName = "Gudang",
            KeyOf = w => w.Code,
            ExistingKeys = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase),
            Columns = new()
            {
                Col<Warehouse>("Kode", (w, v) => w.Code = v, true, "GD-PST"),
                Col<Warehouse>("Nama", (w, v) => w.Name = v, true, "Gudang Pusat - Jakarta"),
                Col<Warehouse>("Alamat", (w, v) => w.Address = v, false, "Jl. Sudirman No. 123"),
                Col<Warehouse>("Cabang", (w, v) => w.IsBranch = ImportService.ParseBool(v), false, "ya",
                    "ya bila lokasi ini cabang, tidak bila hanya gudang"),
                Col<Warehouse>("Aktif", (w, v) => w.IsActive = ImportService.ParseBool(v), false, "ya"),
            },
            Commit = async (db, rows) =>
            {
                var summary = new ImportSummary();
                var current = await db.Warehouses.ToDictionaryAsync(w => w.Code, StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows.Where(r => r.IsValid))
                {
                    var incoming = row.Entity;
                    if (current.TryGetValue(incoming.Code, out var target))
                    {
                        target.Name = incoming.Name;
                        target.Address = incoming.Address;
                        target.IsBranch = incoming.IsBranch;
                        target.IsActive = incoming.IsActive;
                        summary.Updated++;
                    }
                    else
                    {
                        db.Warehouses.Add(incoming);
                        summary.Inserted++;
                    }
                }

                await db.SaveChangesAsync();
                return summary;
            }
        };
    }

    // =====================================================================
    // Mata uang
    // =====================================================================
    public async Task<ImportPlan<Currency>> CurrenciesAsync()
    {
        var existing = await _db.Currencies.Select(c => c.Code).ToListAsync();

        return new ImportPlan<Currency>
        {
            EntityName = "Mata Uang",
            SheetName = "MataUang",
            KeyOf = c => c.Code,
            ExistingKeys = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase),
            Notes = { "Kurs dinyatakan terhadap mata uang dasar. IDR sebagai mata uang dasar berkurs 1." },
            Columns = new()
            {
                Col<Currency>("Kode", (c, v) => c.Code = v.ToUpperInvariant(), true, "USD", "Tiga huruf, mis. USD"),
                Col<Currency>("Nama", (c, v) => c.Name = v, true, "US Dollar"),
                Col<Currency>("Simbol", (c, v) => c.Symbol = v, false, "$"),
                Col<Currency>("Kurs", (c, v) => c.ExchangeRate = ImportService.ParseDecimal(v, "Kurs"), false, "15800"),
                Col<Currency>("Aktif", (c, v) => c.IsActive = ImportService.ParseBool(v), false, "ya"),
            },
            Validate = c => c.Code.Length != 3 ? "Kode mata uang harus tiga huruf" : null,
            Commit = async (db, rows) =>
            {
                var summary = new ImportSummary();
                var current = await db.Currencies.ToDictionaryAsync(c => c.Code, StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows.Where(r => r.IsValid))
                {
                    var incoming = row.Entity;
                    if (current.TryGetValue(incoming.Code, out var target))
                    {
                        target.Name = incoming.Name;
                        target.Symbol = incoming.Symbol;
                        target.ExchangeRate = incoming.ExchangeRate;
                        target.IsActive = incoming.IsActive;
                        summary.Updated++;
                    }
                    else
                    {
                        db.Currencies.Add(incoming);
                        summary.Inserted++;
                    }
                }

                await db.SaveChangesAsync();
                return summary;
            }
        };
    }

    // =====================================================================
    // Jenis pajak
    // =====================================================================
    public async Task<ImportPlan<Tax>> TaxesAsync()
    {
        var existing = await _db.Taxes.Select(t => t.Code).ToListAsync();

        return new ImportPlan<Tax>
        {
            EntityName = "Jenis Pajak",
            SheetName = "Pajak",
            KeyOf = t => t.Code,
            ExistingKeys = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase),
            Notes = { "Tarif ditulis dalam persen, mis. 11 untuk PPN 11%." },
            Columns = new()
            {
                Col<Tax>("Kode", (t, v) => t.Code = v, true, "PPN11"),
                Col<Tax>("Nama", (t, v) => t.Name = v, true, "PPN 11% (Umum)"),
                Col<Tax>("Tarif", (t, v) => t.Rate = ImportService.ParseDecimal(v, "Tarif"), true, "11",
                    "Dalam persen"),
                Col<Tax>("Aktif", (t, v) => t.IsActive = ImportService.ParseBool(v), false, "ya"),
            },
            Validate = t => t.Rate is < 0 or > 100 ? "Tarif harus antara 0 dan 100" : null,
            Commit = async (db, rows) =>
            {
                var summary = new ImportSummary();
                var current = await db.Taxes.ToDictionaryAsync(t => t.Code, StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows.Where(r => r.IsValid))
                {
                    var incoming = row.Entity;
                    if (current.TryGetValue(incoming.Code, out var target))
                    {
                        target.Name = incoming.Name;
                        target.Rate = incoming.Rate;
                        target.IsActive = incoming.IsActive;
                        summary.Updated++;
                    }
                    else
                    {
                        db.Taxes.Add(incoming);
                        summary.Inserted++;
                    }
                }

                await db.SaveChangesAsync();
                return summary;
            }
        };
    }

    // =====================================================================
    // Rekening bank
    // =====================================================================
    public async Task<ImportPlan<BankAccount>> BankAccountsAsync()
    {
        var existing = await _db.BankAccounts.Select(b => b.AccountNumber).ToListAsync();
        var accounts = await _db.ChartOfAccounts.Where(a => !a.IsHeader)
            .ToDictionaryAsync(a => a.AccountCode, a => a.Id);
        var currencies = await _db.Currencies.ToDictionaryAsync(c => c.Code, c => c.Id);

        return new ImportPlan<BankAccount>
        {
            EntityName = "Rekening Bank",
            SheetName = "Bank",
            KeyOf = b => b.AccountNumber,
            ExistingKeys = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase),
            Notes =
            {
                "Kode akun buku besar menautkan rekening ke akun kas/bank-nya. " +
                "Tanpa tautan ini, mutasi bank tidak bisa dijurnal dan tidak muncul di laporan arus kas."
            },
            Columns = new()
            {
                Col<BankAccount>("Nomor Rekening", (b, v) => b.AccountNumber = v, true, "123-456-7890"),
                Col<BankAccount>("Nama Bank", (b, v) => b.BankName = v, true, "BCA"),
                Col<BankAccount>("Atas Nama", (b, v) => b.AccountHolder = v, false, "PT. CUAN MAKMUR SENTOSA"),
                Col<BankAccount>("Kode Akun", (b, v) => b.ChartOfAccountId = ImportService.Lookup(accounts, v, "Kode akun"),
                    false, "1-1200", "Akun buku besar untuk rekening ini"),
                Col<BankAccount>("Mata Uang", (b, v) => b.CurrencyId = ImportService.Lookup(currencies, v, "Mata uang"),
                    false, "IDR"),
                Col<BankAccount>("Saldo", (b, v) => b.Balance = ImportService.ParseDecimal(v, "Saldo"), false, "0",
                    "Hanya dipakai saat rekening pertama kali dibuat"),
                Col<BankAccount>("Aktif", (b, v) => b.IsActive = ImportService.ParseBool(v), false, "ya"),
            },
            Commit = async (db, rows) =>
            {
                var summary = new ImportSummary();
                var current = await db.BankAccounts.ToDictionaryAsync(b => b.AccountNumber, StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows.Where(r => r.IsValid))
                {
                    var incoming = row.Entity;
                    if (current.TryGetValue(incoming.AccountNumber, out var target))
                    {
                        target.BankName = incoming.BankName;
                        target.AccountHolder = incoming.AccountHolder;
                        target.ChartOfAccountId = incoming.ChartOfAccountId ?? target.ChartOfAccountId;
                        target.CurrencyId = incoming.CurrencyId ?? target.CurrencyId;
                        target.IsActive = incoming.IsActive;
                        summary.Updated++;
                    }
                    else
                    {
                        db.BankAccounts.Add(incoming);
                        summary.Inserted++;
                    }
                }

                await db.SaveChangesAsync();
                return summary;
            }
        };
    }
}
