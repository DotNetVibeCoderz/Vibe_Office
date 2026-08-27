using Cuan.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Data;

public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        await ctx.Database.EnsureCreatedAsync();
        if (await ctx.Roles.AnyAsync()) return;

        // ===== ROLES =====
        await roleMgr.CreateAsync(new ApplicationRole { Name = "Admin", Description = "Akses penuh ke semua fitur" });
        await roleMgr.CreateAsync(new ApplicationRole { Name = "Akuntan", Description = "Mengelola jurnal, laporan keuangan, dan pajak" });
        await roleMgr.CreateAsync(new ApplicationRole { Name = "Kasir", Description = "Mengelola kas masuk/keluar dan bank" });
        await roleMgr.CreateAsync(new ApplicationRole { Name = "StafGudang", Description = "Mengelola stok, barang, dan gudang" });
        await roleMgr.CreateAsync(new ApplicationRole { Name = "Sales", Description = "Mengelola penjualan dan customer" });
        await roleMgr.CreateAsync(new ApplicationRole { Name = "Purchasing", Description = "Mengelola pembelian dan supplier" });
        await roleMgr.CreateAsync(new ApplicationRole { Name = "Viewer", Description = "Hanya melihat laporan (read-only)" });

        // ===== USERS =====
        var users = new (string Email, string Name, string Role, string Theme)[]
        {
            ("admin@cuan.id", "Budi Santoso", "Admin", "light"),
            ("akuntan@cuan.id", "Siti Nurhaliza", "Akuntan", "light"),
            ("kasir@cuan.id", "Agus Supriyadi", "Kasir", "light"),
            ("gudang@cuan.id", "Dewi Lestari", "StafGudang", "light"),
            ("sales@cuan.id", "Rudi Hermawan", "Sales", "light"),
            ("purchasing@cuan.id", "Linda Permata", "Purchasing", "light"),
            ("viewer@cuan.id", "Tono Wijaya", "Viewer", "dark")
        };
        foreach (var u in users)
        {
            var user = new ApplicationUser { UserName = u.Email, Email = u.Email, FullName = u.Name, IsActive = true, Theme = u.Theme };
            var r = await userMgr.CreateAsync(user, "Cuan@123");
            if (r.Succeeded) await userMgr.AddToRoleAsync(user, u.Role);
        }

        // ===== SETTINGS =====
        ctx.SystemSettings.AddRange(
            new SystemSetting { SettingKey = "CompanyName", SettingValue = "PT. CUAN MAKMUR SENTOSA", Group = "General" },
            new SystemSetting { SettingKey = "CompanyAddress", SettingValue = "Jl. Sudirman No. 123, Jakarta Pusat", Group = "General" },
            new SystemSetting { SettingKey = "CompanyPhone", SettingValue = "021-555-0123", Group = "General" },
            new SystemSetting { SettingKey = "CompanyEmail", SettingValue = "info@cuanmakmur.id", Group = "General" },
            new SystemSetting { SettingKey = "CompanyTaxNumber", SettingValue = "01.234.567.8-012.000", Group = "General" },
            new SystemSetting { SettingKey = "BaseCurrency", SettingValue = "IDR", Group = "General" },
            new SystemSetting { SettingKey = "EnableMultiCurrency", SettingValue = "true", Group = "Feature" },
            new SystemSetting { SettingKey = "EnableMultiWarehouse", SettingValue = "true", Group = "Feature" },
            new SystemSetting { SettingKey = "EnableAuditLog", SettingValue = "true", Group = "Feature" },
            new SystemSetting { SettingKey = "DefaultTaxRate", SettingValue = "11", Group = "Tax" },
            new SystemSetting { SettingKey = "FiscalYearStart", SettingValue = "01-01", Group = "Accounting" },
            new SystemSetting { SettingKey = "FiscalYearEnd", SettingValue = "12-31", Group = "Accounting" },
            new SystemSetting { SettingKey = "AutoPostJournal", SettingValue = "false", Group = "Accounting" },
            new SystemSetting { SettingKey = "ItemsPerPage", SettingValue = "20", Group = "UI" }
        );

        // ===== CURRENCIES =====
        ctx.Currencies.AddRange(
            new Currency { Code = "IDR", Name = "Indonesian Rupiah", Symbol = "Rp", IsBaseCurrency = true, ExchangeRate = 1 },
            new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", ExchangeRate = 15800 },
            new Currency { Code = "SGD", Name = "Singapore Dollar", Symbol = "S$", ExchangeRate = 11800 },
            new Currency { Code = "EUR", Name = "Euro", Symbol = "€", ExchangeRate = 17200 },
            new Currency { Code = "JPY", Name = "Japanese Yen", Symbol = "¥", ExchangeRate = 108 },
            new Currency { Code = "CNY", Name = "Chinese Yuan", Symbol = "¥", ExchangeRate = 2200 }
        );

        // ===== TAXES =====
        ctx.Taxes.AddRange(
            new Tax { Code = "PPN11", Name = "PPN 11% (Umum)", Rate = 11 },
            new Tax { Code = "PPN12", Name = "PPN 12% (2025)", Rate = 12 },
            new Tax { Code = "PPH21", Name = "PPh Pasal 21", Rate = 5 },
            new Tax { Code = "PPH23", Name = "PPh Pasal 23", Rate = 2 },
            new Tax { Code = "PPH25", Name = "PPh Pasal 25", Rate = 0 },
            new Tax { Code = "PPH4", Name = "PPh Pasal 4(2) Final", Rate = 10 },
            new Tax { Code = "PPNBM", Name = "PPnBM", Rate = 20 },
            new Tax { Code = "NO-TAX", Name = "Tidak Kena Pajak", Rate = 0 }
        );

        // ===== WAREHOUSES =====
        ctx.Warehouses.AddRange(
            new Warehouse { Code = "GD-PST", Name = "Gudang Pusat - Jakarta", Address = "Jl. Sudirman No. 123, Jakarta Pusat", IsBranch = true },
            new Warehouse { Code = "CB-BDG", Name = "Cabang Bandung", Address = "Jl. Asia Afrika No. 45, Bandung", IsBranch = true },
            new Warehouse { Code = "CB-SBY", Name = "Cabang Surabaya", Address = "Jl. Tunjungan No. 78, Surabaya", IsBranch = true },
            new Warehouse { Code = "GD-BKS", Name = "Gudang Bekasi", Address = "Jl. Industri No. 10, Bekasi", IsBranch = false }
        );

        // ===== PAYMENT TERMS =====
        ctx.PaymentTerms.AddRange(
            new PaymentTerm { Name = "COD", DueDays = 0 },
            new PaymentTerm { Name = "Net 15", DueDays = 15 },
            new PaymentTerm { Name = "Net 30", DueDays = 30 },
            new PaymentTerm { Name = "Net 60", DueDays = 60 },
            new PaymentTerm { Name = "2/10 Net 30", DueDays = 30, DiscountPercent = 2, DiscountDays = 10 }
        );

        // ===== UNITS OF MEASURE =====
        ctx.UnitOfMeasures.AddRange(
            new UnitOfMeasure { Code = "PCS", Name = "Pieces" },
            new UnitOfMeasure { Code = "BOX", Name = "Box / Kardus" },
            new UnitOfMeasure { Code = "KG", Name = "Kilogram" },
            new UnitOfMeasure { Code = "GRAM", Name = "Gram" },
            new UnitOfMeasure { Code = "LTR", Name = "Liter" },
            new UnitOfMeasure { Code = "MTR", Name = "Meter" },
            new UnitOfMeasure { Code = "DZ", Name = "Lusin (Dozen)" },
            new UnitOfMeasure { Code = "SET", Name = "Set" },
            new UnitOfMeasure { Code = "ROLL", Name = "Roll" },
            new UnitOfMeasure { Code = "PACK", Name = "Pack" }
        );

        // ===== ITEM CATEGORIES =====
        var catElektronik = new ItemCategory { Name = "Elektronik" };
        var catATK = new ItemCategory { Name = "Alat Tulis Kantor" };
        var catMakanan = new ItemCategory { Name = "Makanan & Minuman" };
        var catPakaian = new ItemCategory { Name = "Pakaian" };
        var catBahan = new ItemCategory { Name = "Bahan Baku" };
        ctx.ItemCategories.AddRange(catElektronik, catATK, catMakanan, catPakaian, catBahan);
        await ctx.SaveChangesAsync();

        ctx.ItemCategories.AddRange(
            new ItemCategory { Name = "Smartphone", ParentId = catElektronik.Id },
            new ItemCategory { Name = "Laptop", ParentId = catElektronik.Id },
            new ItemCategory { Name = "Kertas", ParentId = catATK.Id },
            new ItemCategory { Name = "Pulpen", ParentId = catATK.Id },
            new ItemCategory { Name = "Minuman", ParentId = catMakanan.Id },
            new ItemCategory { Name = "Makanan Ringan", ParentId = catMakanan.Id }
        );
        await ctx.SaveChangesAsync();

        // ===== COA =====
        // Use explicit ParentId after save
        var coaAsset = new ChartOfAccount { AccountCode = "1-0000", AccountName = "AKTIVA / ASET", AccountType = 1, IsHeader = true };
        var coaLiability = new ChartOfAccount { AccountCode = "2-0000", AccountName = "KEWAJIBAN / LIABILITAS", AccountType = 2, IsHeader = true };
        var coaEquity = new ChartOfAccount { AccountCode = "3-0000", AccountName = "MODAL / EKUITAS", AccountType = 3, IsHeader = true };
        var coaRevenue = new ChartOfAccount { AccountCode = "4-0000", AccountName = "PENDAPATAN", AccountType = 4, IsHeader = true };
        var coaExpense = new ChartOfAccount { AccountCode = "5-0000", AccountName = "BIAYA / BEBAN", AccountType = 5, IsHeader = true };
        ctx.ChartOfAccounts.AddRange(coaAsset, coaLiability, coaEquity, coaRevenue, coaExpense);
        await ctx.SaveChangesAsync();

        // Current Assets
        var coaCurrentAsset = new ChartOfAccount { AccountCode = "1-1000", AccountName = "Aktiva Lancar", AccountType = 1, IsHeader = true, ParentId = coaAsset.Id };
        ctx.ChartOfAccounts.Add(coaCurrentAsset);
        await ctx.SaveChangesAsync();

        var coaKasKecil = new ChartOfAccount { AccountCode = "1-1100", AccountName = "Kas Kecil", AccountType = 1, ParentId = coaCurrentAsset.Id, OpeningBalance = 5000000 };
        var coaBankBCA = new ChartOfAccount { AccountCode = "1-1200", AccountName = "Bank BCA", AccountType = 1, ParentId = coaCurrentAsset.Id, OpeningBalance = 150000000 };
        var coaBankMandiri = new ChartOfAccount { AccountCode = "1-1300", AccountName = "Bank Mandiri", AccountType = 1, ParentId = coaCurrentAsset.Id, OpeningBalance = 75000000 };
        var coaPiutang = new ChartOfAccount { AccountCode = "1-1400", AccountName = "Piutang Usaha", AccountType = 1, ParentId = coaCurrentAsset.Id, OpeningBalance = 45000000 };
        var coaPersediaan = new ChartOfAccount { AccountCode = "1-1500", AccountName = "Persediaan Barang", AccountType = 1, ParentId = coaCurrentAsset.Id, OpeningBalance = 200000000 };
        var coaPpnMasukan = new ChartOfAccount { AccountCode = "1-1600", AccountName = "PPN Masukan", AccountType = 1, ParentId = coaCurrentAsset.Id };
        var coaPrepaid = new ChartOfAccount { AccountCode = "1-1700", AccountName = "Uang Muka / Prepaid", AccountType = 1, ParentId = coaCurrentAsset.Id };
        ctx.ChartOfAccounts.AddRange(coaKasKecil, coaBankBCA, coaBankMandiri, coaPiutang, coaPersediaan, coaPpnMasukan, coaPrepaid);

        // Fixed Assets
        var coaFixedAsset = new ChartOfAccount { AccountCode = "1-2000", AccountName = "Aktiva Tetap", AccountType = 1, IsHeader = true, ParentId = coaAsset.Id };
        ctx.ChartOfAccounts.Add(coaFixedAsset);
        await ctx.SaveChangesAsync();
        ctx.ChartOfAccounts.AddRange(
            new ChartOfAccount { AccountCode = "1-2100", AccountName = "Tanah & Bangunan", AccountType = 1, ParentId = coaFixedAsset.Id, OpeningBalance = 2000000000 },
            new ChartOfAccount { AccountCode = "1-2200", AccountName = "Kendaraan", AccountType = 1, ParentId = coaFixedAsset.Id, OpeningBalance = 500000000 },
            new ChartOfAccount { AccountCode = "1-2300", AccountName = "Peralatan Kantor", AccountType = 1, ParentId = coaFixedAsset.Id, OpeningBalance = 100000000 },
            new ChartOfAccount { AccountCode = "1-2400", AccountName = "Akumulasi Penyusutan", AccountType = 1, ParentId = coaFixedAsset.Id }
        );

        // Liabilities
        var coaShortTerm = new ChartOfAccount { AccountCode = "2-1000", AccountName = "Kewajiban Jangka Pendek", AccountType = 2, IsHeader = true, ParentId = coaLiability.Id };
        ctx.ChartOfAccounts.Add(coaShortTerm);
        await ctx.SaveChangesAsync();
        ctx.ChartOfAccounts.AddRange(
            new ChartOfAccount { AccountCode = "2-1100", AccountName = "Hutang Usaha", AccountType = 2, ParentId = coaShortTerm.Id, OpeningBalance = 35000000 },
            new ChartOfAccount { AccountCode = "2-1200", AccountName = "PPN Keluaran", AccountType = 2, ParentId = coaShortTerm.Id },
            new ChartOfAccount { AccountCode = "2-1300", AccountName = "Hutang PPh 21", AccountType = 2, ParentId = coaShortTerm.Id },
            new ChartOfAccount { AccountCode = "2-1400", AccountName = "Hutang PPh 23", AccountType = 2, ParentId = coaShortTerm.Id },
            new ChartOfAccount { AccountCode = "2-1500", AccountName = "Biaya Masih Harus Dibayar", AccountType = 2, ParentId = coaShortTerm.Id },
            new ChartOfAccount { AccountCode = "2-1600", AccountName = "Pendapatan Diterima Dimuka", AccountType = 2, ParentId = coaShortTerm.Id }
        );

        // Equity
        ctx.ChartOfAccounts.AddRange(
            new ChartOfAccount { AccountCode = "3-1100", AccountName = "Modal Saham", AccountType = 3, ParentId = coaEquity.Id, OpeningBalance = 1500000000 },
            new ChartOfAccount { AccountCode = "3-1200", AccountName = "Laba Ditahan", AccountType = 3, ParentId = coaEquity.Id, OpeningBalance = 250000000 },
            new ChartOfAccount { AccountCode = "3-1300", AccountName = "Laba Tahun Berjalan", AccountType = 3, ParentId = coaEquity.Id }
        );

        // Revenue
        var coaPenjualanBarang = new ChartOfAccount { AccountCode = "4-1100", AccountName = "Penjualan Barang", AccountType = 4, ParentId = coaRevenue.Id };
        ctx.ChartOfAccounts.AddRange(
            coaPenjualanBarang,
            new ChartOfAccount { AccountCode = "4-1200", AccountName = "Penjualan Jasa", AccountType = 4, ParentId = coaRevenue.Id },
            new ChartOfAccount { AccountCode = "4-1300", AccountName = "Pendapatan Bunga", AccountType = 4, ParentId = coaRevenue.Id },
            new ChartOfAccount { AccountCode = "4-1400", AccountName = "Pendapatan Lain-lain", AccountType = 4, ParentId = coaRevenue.Id },
            new ChartOfAccount { AccountCode = "4-1500", AccountName = "Retur Penjualan", AccountType = 4, ParentId = coaRevenue.Id }
        );

        // Expenses
        ctx.ChartOfAccounts.AddRange(
            new ChartOfAccount { AccountCode = "5-1100", AccountName = "Harga Pokok Penjualan (HPP)", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-1200", AccountName = "Beban Gaji", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-1300", AccountName = "Beban Sewa", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-1400", AccountName = "Beban Listrik & Air", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-1500", AccountName = "Beban Internet & Telepon", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-1600", AccountName = "Beban Transportasi", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-1700", AccountName = "Beban Pemasaran", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-1800", AccountName = "Beban Administrasi", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-1900", AccountName = "Beban Penyusutan", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-2000", AccountName = "Beban Pajak", AccountType = 5, ParentId = coaExpense.Id }
        );

        await ctx.SaveChangesAsync();

        // ===== CUSTOMERS & SUPPLIERS =====
        ctx.Customers.AddRange(
            new Customer { CustomerCode = "CUS-001", Name = "Toko Maju Jaya", Address = "Jl. Merdeka No. 10, Jakarta", City = "Jakarta", Phone = "021-111-0001", Email = "maju@email.com", TaxNumber = "01.111.111.1-001.000", CreditLimit = 50000000, Balance = 12500000 },
            new Customer { CustomerCode = "CUS-002", Name = "PT. Sumber Rezeki", Address = "Jl. Gatot Subroto No. 25, Bandung", City = "Bandung", Phone = "022-222-0002", Email = "rezeki@email.com", TaxNumber = "01.222.222.2-002.000", CreditLimit = 100000000, Balance = 25000000 },
            new Customer { CustomerCode = "CUS-003", Name = "UD. Berkah Abadi", Address = "Jl. Ahmad Yani No. 50, Surabaya", City = "Surabaya", Phone = "031-333-0003", Email = "berkah@email.com", TaxNumber = "01.333.333.3-003.000", CreditLimit = 25000000, Balance = 8000000 },
            new Customer { CustomerCode = "CUS-004", Name = "CV. Mitra Usaha", Address = "Jl. Diponegoro No. 15, Semarang", City = "Semarang", Phone = "024-444-0004", Email = "mitra@email.com", CreditLimit = 75000000, Balance = 30000000 },
            new Customer { CustomerCode = "CUS-005", Name = "Toko Serba Ada", Address = "Jl. Malioboro No. 5, Yogyakarta", City = "Yogyakarta", Phone = "0274-555-0005", Email = "serba@email.com", CreditLimit = 15000000, Balance = 3500000 },
            new Customer { CustomerCode = "CUS-006", Name = "PT. Global Trade", Address = "Jl. Thamrin No. 100, Jakarta", City = "Jakarta", Phone = "021-666-0006", Email = "global@email.com", TaxNumber = "01.666.666.6-006.000", CreditLimit = 200000000 },
            new Customer { CustomerCode = "CUS-007", Name = "Toko Kelontong Makmur", Address = "Jl. Veteran No. 34, Medan", City = "Medan", Phone = "061-777-0007", CreditLimit = 10000000, Balance = 1200000 },
            new Customer { CustomerCode = "CUS-008", Name = "PT. Indo Prima", Address = "Jl. Sudirman No. 200, Jakarta", City = "Jakarta", Phone = "021-888-0008", Email = "prima@email.com", TaxNumber = "01.888.888.8-008.000", CreditLimit = 150000000, Balance = 45000000 }
        );

        ctx.Suppliers.AddRange(
            new Supplier { SupplierCode = "SUP-001", Name = "PT. Distribusi Nusantara", Address = "Jl. Industri No. 1, Jakarta", City = "Jakarta", Phone = "021-901-0001", Email = "distri@email.com", TaxNumber = "02.111.111.1-001.000", Balance = 20000000 },
            new Supplier { SupplierCode = "SUP-002", Name = "CV. Sumber Pangan", Address = "Jl. Pasar Induk No. 20, Bandung", City = "Bandung", Phone = "022-902-0002", Balance = 15000000 },
            new Supplier { SupplierCode = "SUP-003", Name = "PT. Elektronik Jaya", Address = "Jl. Mangga Dua No. 50, Jakarta", City = "Jakarta", Phone = "021-903-0003", Email = "elektro@email.com", TaxNumber = "02.333.333.3-003.000", Balance = 50000000 },
            new Supplier { SupplierCode = "SUP-004", Name = "UD. Bahan Baku Prima", Address = "Jl. Raya Bogor No. 100, Bogor", City = "Bogor", Phone = "0251-904-0004", Balance = 8000000 },
            new Supplier { SupplierCode = "SUP-005", Name = "PT. Indo Chemical", Address = "Jl. Rungkut No. 30, Surabaya", City = "Surabaya", Phone = "031-905-0005", Email = "chemical@email.com", TaxNumber = "02.555.555.5-005.000", Balance = 35000000 }
        );
        await ctx.SaveChangesAsync();

        // ===== BANK ACCOUNTS =====
        ctx.BankAccounts.AddRange(
            new BankAccount { BankName = "BCA", AccountNumber = "123-456-7890", AccountHolder = "PT. CUAN MAKMUR SENTOSA", Balance = 150000000 },
            new BankAccount { BankName = "Mandiri", AccountNumber = "098-765-4321", AccountHolder = "PT. CUAN MAKMUR SENTOSA", Balance = 75000000 },
            new BankAccount { BankName = "BNI", AccountNumber = "555-111-2222", AccountHolder = "PT. CUAN MAKMUR SENTOSA", Balance = 25000000 }
        );

        // ===== ITEMS (with valid CategoryId and UnitOfMeasureId) =====
        // ATK category is catATK (id 2), PCS unit is 1st unit (id 1)
        ctx.Items.AddRange(
            new Item { ItemCode = "BRG-001", ItemName = "Kertas A4 80gsm", Description = "Kertas HVS A4 80gsm 1 rim", CategoryId = catATK.Id, UnitOfMeasureId = 1, SellingPrice = 55000, CostPrice = 45000, StockQuantity = 500, MinimumStock = 50, MaximumStock = 1000 },
            new Item { ItemCode = "BRG-002", ItemName = "Pulpen Blue Gel", Description = "Pulpen gel tinta biru", CategoryId = catATK.Id, UnitOfMeasureId = 1, SellingPrice = 8000, CostPrice = 5000, StockQuantity = 1200, MinimumStock = 100, MaximumStock = 2000 },
            new Item { ItemCode = "BRG-003", ItemName = "Mouse Wireless", Description = "Mouse wireless 2.4GHz", CategoryId = catElektronik.Id, UnitOfMeasureId = 1, SellingPrice = 150000, CostPrice = 110000, StockQuantity = 85, MinimumStock = 10, MaximumStock = 150 },
            new Item { ItemCode = "BRG-004", ItemName = "Keyboard Mechanical", Description = "Keyboard mechanical RGB", CategoryId = catElektronik.Id, UnitOfMeasureId = 1, SellingPrice = 750000, CostPrice = 550000, StockQuantity = 30, MinimumStock = 5, MaximumStock = 50 },
            new Item { ItemCode = "BRG-005", ItemName = "Flashdisk 32GB", Description = "USB Flashdisk 32GB USB 3.0", CategoryId = catElektronik.Id, UnitOfMeasureId = 1, SellingPrice = 85000, CostPrice = 65000, StockQuantity = 200, MinimumStock = 20, MaximumStock = 300 },
            new Item { ItemCode = "BRG-006", ItemName = "Tinta Printer", Description = "Tinta printer Epson 001 Black", CategoryId = catATK.Id, UnitOfMeasureId = 1, SellingPrice = 95000, CostPrice = 75000, StockQuantity = 150, MinimumStock = 15, MaximumStock = 250 },
            new Item { ItemCode = "BRG-007", ItemName = "Kabel USB Type-C", Description = "Kabel data USB Type-C 1 meter", CategoryId = catElektronik.Id, UnitOfMeasureId = 1, SellingPrice = 45000, CostPrice = 30000, StockQuantity = 300, MinimumStock = 30, MaximumStock = 500 },
            new Item { ItemCode = "BRG-008", ItemName = "Buku Tulis A5", Description = "Buku tulis A5 100 lembar", CategoryId = catATK.Id, UnitOfMeasureId = 1, SellingPrice = 15000, CostPrice = 10000, StockQuantity = 800, MinimumStock = 100, MaximumStock = 1500 },
            new Item { ItemCode = "BRG-009", ItemName = "Stapler HD-10", Description = "Stapler kecil HD-10", CategoryId = catATK.Id, UnitOfMeasureId = 1, SellingPrice = 35000, CostPrice = 25000, StockQuantity = 120, MinimumStock = 10, MaximumStock = 200 },
            new Item { ItemCode = "BRG-010", ItemName = "Kalkulator Scientific", Description = "Kalkulator scientific 240 fungsi", CategoryId = catElektronik.Id, UnitOfMeasureId = 1, SellingPrice = 175000, CostPrice = 130000, StockQuantity = 40, MinimumStock = 5, MaximumStock = 80 },
            new Item { ItemCode = "BRG-011", ItemName = "Map Plastik", Description = "Map plastik folio transparan", CategoryId = catATK.Id, UnitOfMeasureId = 1, SellingPrice = 5000, CostPrice = 3000, StockQuantity = 1500, MinimumStock = 100, MaximumStock = 3000 },
            new Item { ItemCode = "BRG-012", ItemName = "Amplop Coklat", Description = "Amplop coklat ukuran folio", CategoryId = catATK.Id, UnitOfMeasureId = 1, SellingPrice = 2500, CostPrice = 1500, StockQuantity = 2000, MinimumStock = 200, MaximumStock = 5000 }
        );

        // ===== ACCOUNTING PERIODS =====
        ctx.AccountingPeriods.AddRange(
            new AccountingPeriod { Name = "Tahun Buku 2024", StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31), IsActive = true },
            new AccountingPeriod { Name = "Tahun Buku 2025", StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 12, 31) }
        );

        // ===== API KEY =====
        ctx.ApiKeys.Add(new ApiKey { KeyName = "Default API Key", KeyValue = "cu4n-4p1-k3y-2024-d3f4ult", IsActive = true, ExpiresAt = DateTime.UtcNow.AddYears(1) });

        await ctx.SaveChangesAsync();

        // ===== JOURNAL ENTRIES (use actual COA IDs from DB) =====
        var allCoa = await ctx.ChartOfAccounts.ToListAsync();
        var kasKecil = allCoa.First(c => c.AccountCode == "1-1100");
        var bankBca = allCoa.First(c => c.AccountCode == "1-1200");
        var bankMandiri = allCoa.First(c => c.AccountCode == "1-1300");
        var piutang = allCoa.First(c => c.AccountCode == "1-1400");
        var persediaan = allCoa.First(c => c.AccountCode == "1-1500");
        var ppnMasukan = allCoa.First(c => c.AccountCode == "1-1600");
        var ppnKeluaran = allCoa.First(c => c.AccountCode == "2-1200");
        var hutangUsaha = allCoa.First(c => c.AccountCode == "2-1100");
        var modalSaham = allCoa.First(c => c.AccountCode == "3-1100");
        var labaBerjalan = allCoa.First(c => c.AccountCode == "3-1300");
        var penjualan = allCoa.First(c => c.AccountCode == "4-1100");
        var bebanHpp = allCoa.First(c => c.AccountCode == "5-1100");
        var bebanGaji = allCoa.First(c => c.AccountCode == "5-1200");
        var bebanSewa = allCoa.First(c => c.AccountCode == "5-1300");
        var bebanListrik = allCoa.First(c => c.AccountCode == "5-1400");
        var bebanInternet = allCoa.First(c => c.AccountCode == "5-1500");
        var bebanTransport = allCoa.First(c => c.AccountCode == "5-1600");
        var bebanMarketing = allCoa.First(c => c.AccountCode == "5-1700");
        var bebanAdmin = allCoa.First(c => c.AccountCode == "5-1800");

        ctx.JournalEntries.AddRange(
            new JournalEntry
            {
                JournalNumber = "JU-2024-0001", TransactionDate = new DateTime(2024, 1, 15),
                Description = "Setoran modal awal", Reference = "MOD-001", IsPosted = true, SourceType = JournalSource.Manual,
                Details = new List<JournalEntryDetail>
                {
                    new() { ChartOfAccountId = kasKecil.Id, Description = "Kas Kecil", Debit = 5000000 },
                    new() { ChartOfAccountId = bankBca.Id, Description = "Bank BCA", Debit = 150000000 },
                    new() { ChartOfAccountId = modalSaham.Id, Description = "Modal Saham", Credit = 155000000 }
                }
            },
            new JournalEntry
            {
                JournalNumber = "JU-2024-0002", TransactionDate = new DateTime(2024, 2, 20),
                Description = "Pembelian barang dagang tunai", Reference = "PO-001", IsPosted = true, SourceType = JournalSource.PurchaseInvoice,
                Details = new List<JournalEntryDetail>
                {
                    new() { ChartOfAccountId = persediaan.Id, Description = "Persediaan Barang", Debit = 25000000 },
                    new() { ChartOfAccountId = ppnMasukan.Id, Description = "PPN Masukan", Debit = 2750000 },
                    new() { ChartOfAccountId = bankBca.Id, Description = "Bank BCA", Credit = 27750000 }
                }
            },
            new JournalEntry
            {
                JournalNumber = "JU-2024-0003", TransactionDate = new DateTime(2024, 3, 10),
                Description = "Penjualan barang ke Toko Maju Jaya", Reference = "INV-001", IsPosted = true, SourceType = JournalSource.SalesInvoice,
                Details = new List<JournalEntryDetail>
                {
                    new() { ChartOfAccountId = piutang.Id, Description = "Piutang Usaha", Debit = 33300000 },
                    new() { ChartOfAccountId = penjualan.Id, Description = "Penjualan Barang", Credit = 30000000 },
                    new() { ChartOfAccountId = ppnKeluaran.Id, Description = "PPN Keluaran", Credit = 3300000 }
                }
            },
            new JournalEntry
            {
                JournalNumber = "JU-2024-0004", TransactionDate = new DateTime(2024, 3, 25),
                Description = "Pembayaran gaji karyawan", Reference = "PAY-001", IsPosted = true, SourceType = JournalSource.CashOut,
                Details = new List<JournalEntryDetail>
                {
                    new() { ChartOfAccountId = bebanGaji.Id, Description = "Beban Gaji", Debit = 15000000 },
                    new() { ChartOfAccountId = bankBca.Id, Description = "Bank BCA", Credit = 15000000 }
                }
            },
            new JournalEntry
            {
                JournalNumber = "JU-2024-0005", TransactionDate = new DateTime(2024, 4, 5),
                Description = "Penerimaan pembayaran dari Toko Maju Jaya", Reference = "RCV-001", IsPosted = true, SourceType = JournalSource.CashIn,
                Details = new List<JournalEntryDetail>
                {
                    new() { ChartOfAccountId = bankBca.Id, Description = "Bank BCA", Debit = 33300000 },
                    new() { ChartOfAccountId = piutang.Id, Description = "Piutang Usaha", Credit = 33300000 }
                }
            }
        );

        await ctx.SaveChangesAsync();

        // ===== ADDITIONAL SAMPLE TRANSACTIONS =====
        var customers = await ctx.Customers.ToListAsync();
        var suppliers = await ctx.Suppliers.ToListAsync();
        var items = await ctx.Items.ToListAsync();
        var warehouses = await ctx.Warehouses.ToListAsync();
        var bankAccounts = await ctx.BankAccounts.ToListAsync();
        var baseCurrency = await ctx.Currencies.FirstAsync(c => c.Code == "IDR");
        var taxPpn = await ctx.Taxes.FirstAsync(t => t.Code == "PPN11");

        var salesInvoices = new List<SalesInvoice>();
        var purchaseInvoices = new List<PurchaseInvoice>();
        var cashTransactions = new List<CashTransaction>();
        var bankTransactions = new List<BankTransaction>();
        var stockMovements = new List<StockMovement>();
        var extraJournals = new List<JournalEntry>();

        var itemStockChanges = items.ToDictionary(i => i.Id, _ => 0m);

        int invCounter = 1;
        int pinvCounter = 1;
        int cashCounter = 1;
        int bankCounter = 1;

        // Beban operasional bulanan (biar jurnal lebih ramai)
        for (int m = 1; m <= 12; m++)
        {
            var amount = 8000000 + (m * 350000);
            extraJournals.Add(new JournalEntry
            {
                JournalNumber = $"JU-2024-{(100 + m):0000}",
                TransactionDate = new DateTime(2024, m, 25),
                Description = "Beban operasional bulanan",
                Reference = $"OPS-{m:000}",
                IsPosted = true,
                SourceType = JournalSource.CashOut,
                Details = new List<JournalEntryDetail>
                {
                    new() { ChartOfAccountId = bebanAdmin.Id, Description = "Beban Administrasi", Debit = amount },
                    new() { ChartOfAccountId = bankBca.Id, Description = "Bank BCA", Credit = amount }
                }
            });
        }

        // Sales invoices 2024 (3 per bulan)
        for (int month = 1; month <= 12; month++)
        {
            for (int i = 0; i < 3; i++)
            {
                var invoiceDate = new DateTime(2024, month, (i * 7 + 3) % 28 + 1);
                var customer = customers[(month + i) % customers.Count];
                var warehouse = warehouses[(month + i) % warehouses.Count];

                var detailItems = new List<Item>
                {
                    items[(month + i) % items.Count],
                    items[(month + i + 3) % items.Count]
                };

                var details = new List<SalesInvoiceDetail>();
                decimal subTotal = 0;
                decimal totalCost = 0;

                foreach (var item in detailItems)
                {
                    var qty = 3 + ((month + i + item.Id) % 6);
                    var unitPrice = item.SellingPrice;
                    var lineTotal = qty * unitPrice;
                    details.Add(new SalesInvoiceDetail
                    {
                        ItemId = item.Id,
                        Description = item.ItemName,
                        Quantity = qty,
                        UnitPrice = unitPrice,
                        TaxPercent = taxPpn.Rate,
                        TaxAmount = Math.Round(lineTotal * taxPpn.Rate / 100m, 0),
                        LineTotal = lineTotal
                    });

                    subTotal += lineTotal;
                    totalCost += qty * item.CostPrice;
                    itemStockChanges[item.Id] -= qty;

                    stockMovements.Add(new StockMovement
                    {
                        MovementDate = invoiceDate,
                        Type = StockMovementType.Sales,
                        ItemId = item.Id,
                        WarehouseId = warehouse.Id,
                        Quantity = -qty,
                        UnitCost = item.CostPrice,
                        Description = "Penjualan" 
                    });
                }

                var taxAmount = Math.Round(subTotal * taxPpn.Rate / 100m, 0);
                var grandTotal = subTotal + taxAmount;
                var paidAmount = (i % 3 == 0) ? grandTotal : (i % 3 == 1 ? Math.Round(grandTotal * 0.5m, 0) : 0);

                var invoice = new SalesInvoice
                {
                    InvoiceNumber = $"INV-2024-{invCounter:0000}",
                    InvoiceDate = invoiceDate,
                    DueDate = invoiceDate.AddDays(30),
                    CustomerId = customer.Id,
                    WarehouseId = warehouse.Id,
                    CurrencyId = baseCurrency.Id,
                    ExchangeRate = 1,
                    Description = "Penjualan barang",
                    SubTotal = subTotal,
                    TaxAmount = taxAmount,
                    GrandTotal = grandTotal,
                    PaidAmount = paidAmount,
                    Status = paidAmount == 0 ? InvoiceStatus.Confirmed : (paidAmount >= grandTotal ? InvoiceStatus.Paid : InvoiceStatus.PartialPaid),
                    IsPosted = true,
                    Details = details
                };

                salesInvoices.Add(invoice);
                customer.Balance += (grandTotal - paidAmount);

                if (paidAmount > 0)
                {
                    if ((month + i) % 2 == 0)
                    {
                        cashTransactions.Add(new CashTransaction
                        {
                            TransactionNumber = $"CSHIN-2024-{cashCounter:0000}",
                            TransactionDate = invoiceDate.AddDays(1),
                            Type = CashTransactionType.CashIn,
                            ChartOfAccountId = kasKecil.Id,
                            Amount = paidAmount,
                            Description = "Pembayaran invoice",
                            CustomerId = customer.Id,
                            SalesInvoice = invoice,
                            CurrencyId = baseCurrency.Id,
                            ExchangeRate = 1,
                            IsPosted = true
                        });
                        cashCounter++;
                    }
                    else
                    {
                        var bank = bankAccounts[(month + i) % bankAccounts.Count];
                        bankTransactions.Add(new BankTransaction
                        {
                            TransactionNumber = $"BNKIN-2024-{bankCounter:0000}",
                            TransactionDate = invoiceDate.AddDays(2),
                            Type = BankTransactionType.BankIn,
                            BankAccountId = bank.Id,
                            Amount = paidAmount,
                            Description = "Pembayaran invoice via bank",
                            CustomerId = customer.Id,
                            CurrencyId = baseCurrency.Id,
                            ExchangeRate = 1,
                            IsPosted = true
                        });
                        bankCounter++;
                    }
                }

                invCounter++;
            }
        }

        // Purchase invoices 2024 (2 per bulan)
        for (int month = 1; month <= 12; month++)
        {
            for (int i = 0; i < 2; i++)
            {
                var invoiceDate = new DateTime(2024, month, (i * 10 + 5) % 28 + 1);
                var supplier = suppliers[(month + i) % suppliers.Count];
                var warehouse = warehouses[(month + i) % warehouses.Count];

                var detailItems = new List<Item>
                {
                    items[(month + i + 1) % items.Count],
                    items[(month + i + 4) % items.Count]
                };

                var details = new List<PurchaseInvoiceDetail>();
                decimal subTotal = 0;
                decimal totalCost = 0;

                foreach (var item in detailItems)
                {
                    var qty = 5 + ((month + i + item.Id) % 7);
                    var unitPrice = item.CostPrice;
                    var lineTotal = qty * unitPrice;
                    details.Add(new PurchaseInvoiceDetail
                    {
                        ItemId = item.Id,
                        Description = item.ItemName,
                        Quantity = qty,
                        UnitPrice = unitPrice,
                        TaxPercent = taxPpn.Rate,
                        TaxAmount = Math.Round(lineTotal * taxPpn.Rate / 100m, 0),
                        LineTotal = lineTotal
                    });

                    subTotal += lineTotal;
                    totalCost += lineTotal;
                    itemStockChanges[item.Id] += qty;

                    stockMovements.Add(new StockMovement
                    {
                        MovementDate = invoiceDate,
                        Type = StockMovementType.Purchase,
                        ItemId = item.Id,
                        WarehouseId = warehouse.Id,
                        Quantity = qty,
                        UnitCost = item.CostPrice,
                        Description = "Pembelian" 
                    });
                }

                var taxAmount = Math.Round(subTotal * taxPpn.Rate / 100m, 0);
                var grandTotal = subTotal + taxAmount;
                var paidAmount = (i % 2 == 0) ? Math.Round(grandTotal * 0.6m, 0) : 0;

                var purchase = new PurchaseInvoice
                {
                    InvoiceNumber = $"PINV-2024-{pinvCounter:0000}",
                    InvoiceDate = invoiceDate,
                    DueDate = invoiceDate.AddDays(30),
                    SupplierId = supplier.Id,
                    WarehouseId = warehouse.Id,
                    CurrencyId = baseCurrency.Id,
                    ExchangeRate = 1,
                    Description = "Pembelian barang",
                    SubTotal = subTotal,
                    TaxAmount = taxAmount,
                    GrandTotal = grandTotal,
                    PaidAmount = paidAmount,
                    Status = paidAmount == 0 ? InvoiceStatus.Confirmed : InvoiceStatus.PartialPaid,
                    IsPosted = true,
                    Details = details
                };

                purchaseInvoices.Add(purchase);
                supplier.Balance += (grandTotal - paidAmount);

                if (paidAmount > 0)
                {
                    if ((month + i) % 2 == 0)
                    {
                        cashTransactions.Add(new CashTransaction
                        {
                            TransactionNumber = $"CSHOUT-2024-{cashCounter:0000}",
                            TransactionDate = invoiceDate.AddDays(1),
                            Type = CashTransactionType.CashOut,
                            ChartOfAccountId = kasKecil.Id,
                            Amount = paidAmount,
                            Description = "Pembayaran supplier",
                            SupplierId = supplier.Id,
                            PurchaseInvoice = purchase,
                            CurrencyId = baseCurrency.Id,
                            ExchangeRate = 1,
                            IsPosted = true
                        });
                        cashCounter++;
                    }
                    else
                    {
                        var bank = bankAccounts[(month + i) % bankAccounts.Count];
                        bankTransactions.Add(new BankTransaction
                        {
                            TransactionNumber = $"BNKOUT-2024-{bankCounter:0000}",
                            TransactionDate = invoiceDate.AddDays(2),
                            Type = BankTransactionType.BankOut,
                            BankAccountId = bank.Id,
                            Amount = paidAmount,
                            Description = "Pembayaran supplier via bank",
                            SupplierId = supplier.Id,
                            CurrencyId = baseCurrency.Id,
                            ExchangeRate = 1,
                            IsPosted = true
                        });
                        bankCounter++;
                    }
                }

                pinvCounter++;
            }
        }

        ctx.JournalEntries.AddRange(extraJournals);
        ctx.SalesInvoices.AddRange(salesInvoices);
        ctx.PurchaseInvoices.AddRange(purchaseInvoices);
        ctx.CashTransactions.AddRange(cashTransactions);
        ctx.BankTransactions.AddRange(bankTransactions);
        ctx.StockMovements.AddRange(stockMovements);

        // Update stok item berdasarkan transaksi
        foreach (var item in items)
        {
            if (itemStockChanges.TryGetValue(item.Id, out var delta))
            {
                item.StockQuantity += delta;
                if (item.StockQuantity < 0) item.StockQuantity = 0;
            }
        }

        // Update saldo COA biar laporan hidup
        foreach (var coa in allCoa)
        {
            if (coa.CurrentBalance == 0) coa.CurrentBalance = coa.OpeningBalance;
        }

        var totalSales = salesInvoices.Sum(s => s.SubTotal);
        var totalSalesTax = salesInvoices.Sum(s => s.TaxAmount);
        var totalPurchaseTax = purchaseInvoices.Sum(p => p.TaxAmount);
        var totalHpp = salesInvoices.Sum(s => s.Details.Sum(d => d.Quantity * items.First(i => i.Id == d.ItemId).CostPrice));
        var inventoryValue = items.Sum(i => i.StockQuantity * i.CostPrice);
        var cashIn = cashTransactions.Where(t => t.Type == CashTransactionType.CashIn).Sum(t => t.Amount);
        var cashOut = cashTransactions.Where(t => t.Type == CashTransactionType.CashOut).Sum(t => t.Amount);
        var bankIn = bankTransactions.Where(t => t.Type == BankTransactionType.BankIn).Sum(t => t.Amount);
        var bankOut = bankTransactions.Where(t => t.Type == BankTransactionType.BankOut).Sum(t => t.Amount);

        kasKecil.CurrentBalance = kasKecil.OpeningBalance + cashIn - cashOut;
        bankBca.CurrentBalance = bankBca.OpeningBalance + bankIn - bankOut;
        bankMandiri.CurrentBalance = bankMandiri.OpeningBalance + 15000000;
        piutang.CurrentBalance = customers.Sum(c => c.Balance);
        persediaan.CurrentBalance = inventoryValue;
        ppnMasukan.CurrentBalance = totalPurchaseTax;
        ppnKeluaran.CurrentBalance = totalSalesTax;
        hutangUsaha.CurrentBalance = suppliers.Sum(s => s.Balance);
        penjualan.CurrentBalance = totalSales;
        bebanHpp.CurrentBalance = totalHpp;

        var bebanGajiValue = 120000000m;
        var bebanSewaValue = 60000000m;
        var bebanListrikValue = 18000000m;
        var bebanInternetValue = 12000000m;
        var bebanTransportValue = 15000000m;
        var bebanMarketingValue = 22000000m;
        var bebanAdminValue = 35000000m;

        bebanGaji.CurrentBalance = bebanGajiValue;
        bebanSewa.CurrentBalance = bebanSewaValue;
        bebanListrik.CurrentBalance = bebanListrikValue;
        bebanInternet.CurrentBalance = bebanInternetValue;
        bebanTransport.CurrentBalance = bebanTransportValue;
        bebanMarketing.CurrentBalance = bebanMarketingValue;
        bebanAdmin.CurrentBalance = bebanAdminValue;

        var totalExpense = bebanHpp.CurrentBalance + bebanGajiValue + bebanSewaValue + bebanListrikValue + bebanInternetValue + bebanTransportValue + bebanMarketingValue + bebanAdminValue;
        labaBerjalan.CurrentBalance = penjualan.CurrentBalance - totalExpense;

        await ctx.SaveChangesAsync();
    }
}
