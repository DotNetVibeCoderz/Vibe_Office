using Cuan.Models;
using Cuan.Services;
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
        // Seluruh parameter berasal dari SettingsCatalog — satu-satunya daftar
        // yang juga dipakai halaman Pengaturan untuk merender formulirnya.
        ctx.SystemSettings.AddRange(
            SettingsCatalog.All.Select(def => new SystemSetting
            {
                SettingKey = def.Key,
                SettingValue = def.Default,
                Group = def.Group,
                Description = def.Description ?? def.Label,
                UpdatedAt = DateTime.UtcNow
            })
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
        var coaGiroMasuk = new ChartOfAccount { AccountCode = "1-1800", AccountName = "Giro Masuk Belum Cair", AccountType = 1, ParentId = coaCurrentAsset.Id };
        ctx.ChartOfAccounts.AddRange(coaKasKecil, coaBankBCA, coaBankMandiri, coaPiutang, coaPersediaan, coaPpnMasukan, coaPrepaid, coaGiroMasuk);

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
            new ChartOfAccount { AccountCode = "2-1600", AccountName = "Pendapatan Diterima Dimuka", AccountType = 2, ParentId = coaShortTerm.Id },
            new ChartOfAccount { AccountCode = "2-1700", AccountName = "Giro Keluar Belum Cair", AccountType = 2, ParentId = coaShortTerm.Id }
        );

        // Equity
        ctx.ChartOfAccounts.AddRange(
            // Modal disetel supaya saldo awal seimbang: aktiva 3.075.000.000
            // = kewajiban 35.000.000 + modal 3.040.000.000. Tanpa ini neraca
            // saldo sudah timpang sejak baris pertama.
            new ChartOfAccount { AccountCode = "3-1100", AccountName = "Modal Saham", AccountType = 3, ParentId = coaEquity.Id, OpeningBalance = 2790000000 },
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
            new ChartOfAccount { AccountCode = "4-1500", AccountName = "Retur Penjualan", AccountType = 4, ParentId = coaRevenue.Id },
            new ChartOfAccount { AccountCode = "4-1600", AccountName = "Diskon Penjualan", AccountType = 4, ParentId = coaRevenue.Id }
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
            new ChartOfAccount { AccountCode = "5-2000", AccountName = "Beban Pajak", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-2100", AccountName = "Beban Ongkos Kirim", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-2200", AccountName = "Beban Administrasi Bank", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-2300", AccountName = "Selisih Penyesuaian Stok", AccountType = 5, ParentId = coaExpense.Id },
            new ChartOfAccount { AccountCode = "5-2400", AccountName = "Beban Lain-lain", AccountType = 5, ParentId = coaExpense.Id }
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
        // Tiap rekening ditautkan ke akun buku besarnya, supaya mutasi bank
        // bisa dijurnal dan muncul di laporan arus kas.
        var coaBcaId = await ctx.ChartOfAccounts.Where(c => c.AccountCode == "1-1200").Select(c => c.Id).FirstAsync();
        var coaMandiriId = await ctx.ChartOfAccounts.Where(c => c.AccountCode == "1-1300").Select(c => c.Id).FirstAsync();

        ctx.BankAccounts.AddRange(
            new BankAccount { BankName = "BCA", AccountNumber = "123-456-7890", AccountHolder = "PT. CUAN MAKMUR SENTOSA", Balance = 150000000, ChartOfAccountId = coaBcaId },
            new BankAccount { BankName = "Mandiri", AccountNumber = "098-765-4321", AccountHolder = "PT. CUAN MAKMUR SENTOSA", Balance = 75000000, ChartOfAccountId = coaMandiriId },
            new BankAccount { BankName = "BNI", AccountNumber = "555-111-2222", AccountHolder = "PT. CUAN MAKMUR SENTOSA", Balance = 25000000, ChartOfAccountId = coaMandiriId }
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
            new AccountingPeriod { Name = $"Tahun Buku {DateTime.Today.Year}", StartDate = new DateTime(DateTime.Today.Year, 1, 1), EndDate = new DateTime(DateTime.Today.Year, 12, 31), IsActive = true },
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
                JournalNumber = $"JU-{DateTime.Today.Year}-0001", TransactionDate = new DateTime(DateTime.Today.Year, 1, 15),
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
                JournalNumber = $"JU-{DateTime.Today.Year}-0002", TransactionDate = new DateTime(DateTime.Today.Year, 2, 20),
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
                JournalNumber = $"JU-{DateTime.Today.Year}-0003", TransactionDate = new DateTime(DateTime.Today.Year, 3, 10),
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
                JournalNumber = $"JU-{DateTime.Today.Year}-0004", TransactionDate = new DateTime(DateTime.Today.Year, 3, 25),
                Description = "Pembayaran gaji karyawan", Reference = "PAY-001", IsPosted = true, SourceType = JournalSource.CashOut,
                Details = new List<JournalEntryDetail>
                {
                    new() { ChartOfAccountId = bebanGaji.Id, Description = "Beban Gaji", Debit = 15000000 },
                    new() { ChartOfAccountId = bankBca.Id, Description = "Bank BCA", Credit = 15000000 }
                }
            },
            new JournalEntry
            {
                JournalNumber = $"JU-{DateTime.Today.Year}-0005", TransactionDate = new DateTime(DateTime.Today.Year, 4, 5),
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

        // Data contoh dibuat bergulir: 12 bulan terakhir yang berakhir di bulan
        // berjalan, supaya dasbor dan laporan pajak tidak pernah kosong berapa
        // pun tahun aplikasi ini dijalankan.
        var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        DateTime MonthOf(int index) => thisMonth.AddMonths(index - 12);

        int invCounter = 1;
        int pinvCounter = 1;
        int cashCounter = 1;
        int bankCounter = 1;

        // Faktur penjualan: 3 per bulan selama 12 bulan terakhir
        for (int month = 1; month <= 12; month++)
        {
            for (int i = 0; i < 3; i++)
            {
                var invoiceDate = MonthOf(month).AddDays((i * 7 + 3) % 27);
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
                    InvoiceNumber = $"INV-{invoiceDate.Year}-{invCounter:0000}",
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
                            TransactionNumber = $"KM-{invoiceDate.Year}-{cashCounter:0000}",
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
                            TransactionNumber = $"BM-{invoiceDate.Year}-{bankCounter:0000}",
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

        // Faktur pembelian: 2 per bulan selama 12 bulan terakhir
        for (int month = 1; month <= 12; month++)
        {
            for (int i = 0; i < 2; i++)
            {
                var invoiceDate = MonthOf(month).AddDays((i * 10 + 5) % 27);
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
                    InvoiceNumber = $"PB-{invoiceDate.Year}-{pinvCounter:0000}",
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
                            TransactionNumber = $"KK-{invoiceDate.Year}-{cashCounter:0000}",
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
                            TransactionNumber = $"BK-{invoiceDate.Year}-{bankCounter:0000}",
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

        // Setiap dokumen contoh dibuatkan jurnalnya, lalu seluruh saldo akun
        // dihitung dari saldo awal ditambah jurnal-jurnal itu. Dengan begitu
        // Neraca Saldo benar-benar seimbang dan angka di laporan bisa
        // ditelusuri balik ke transaksinya — bukan angka yang ditanam langsung.
        await ctx.SaveChangesAsync();

        var demoJournals = new List<JournalEntry>();
        var journalSeq = 1000;

        JournalEntry NewJournal(DateTime date, string description, string reference, JournalSource source)
        {
            var journal = new JournalEntry
            {
                JournalNumber = $"JU-{date.Year}-{journalSeq++:0000}",
                TransactionDate = date,
                Description = description,
                Reference = reference,
                SourceType = source,
                IsPosted = true,
                PostedAt = DateTime.UtcNow,
                Details = new List<JournalEntryDetail>()
            };
            demoJournals.Add(journal);
            return journal;
        }

        void Line(JournalEntry journal, ChartOfAccount account, decimal debit, decimal credit, string note)
        {
            if (debit == 0 && credit == 0) return;
            journal.Details.Add(new JournalEntryDetail
            {
                ChartOfAccountId = account.Id,
                Debit = debit,
                Credit = credit,
                Description = note
            });
        }

        // Faktur penjualan: piutang, pendapatan, PPN keluaran, dan harga pokok.
        foreach (var invoice in salesInvoices)
        {
            var journal = NewJournal(invoice.InvoiceDate, $"Faktur penjualan {invoice.InvoiceNumber}",
                invoice.InvoiceNumber, JournalSource.SalesInvoice);

            Line(journal, piutang, invoice.GrandTotal, 0, "Piutang usaha");
            Line(journal, penjualan, 0, invoice.SubTotal, "Penjualan barang");
            Line(journal, ppnKeluaran, 0, invoice.TaxAmount, "PPN keluaran");

            var cost = invoice.Details.Sum(d => d.Quantity * items.First(i => i.Id == d.ItemId).CostPrice);
            Line(journal, bebanHpp, cost, 0, "Harga pokok penjualan");
            Line(journal, persediaan, 0, cost, "Pengurangan persediaan");
        }

        // Faktur pembelian: persediaan, PPN masukan, dan hutang usaha.
        foreach (var invoice in purchaseInvoices)
        {
            var journal = NewJournal(invoice.InvoiceDate, $"Faktur pembelian {invoice.InvoiceNumber}",
                invoice.InvoiceNumber, JournalSource.PurchaseInvoice);

            Line(journal, persediaan, invoice.SubTotal, 0, "Persediaan barang");
            Line(journal, ppnMasukan, invoice.TaxAmount, 0, "PPN masukan");
            Line(journal, hutangUsaha, 0, invoice.GrandTotal, "Hutang usaha");
        }

        // Penerimaan dan pengeluaran kas.
        foreach (var trx in cashTransactions)
        {
            var isIn = trx.Type == CashTransactionType.CashIn;
            var journal = NewJournal(trx.TransactionDate, trx.Description ?? (isIn ? "Kas masuk" : "Kas keluar"),
                trx.TransactionNumber, isIn ? JournalSource.CashIn : JournalSource.CashOut);

            if (isIn)
            {
                Line(journal, kasKecil, trx.Amount, 0, "Kas masuk");
                Line(journal, piutang, 0, trx.Amount, "Pelunasan piutang");
            }
            else
            {
                Line(journal, hutangUsaha, trx.Amount, 0, "Pelunasan hutang");
                Line(journal, kasKecil, 0, trx.Amount, "Kas keluar");
            }
        }

        // Mutasi bank, memakai akun buku besar rekening yang bersangkutan.
        foreach (var trx in bankTransactions)
        {
            var bankCoa = bankAccounts.FirstOrDefault(b => b.Id == trx.BankAccountId)?.ChartOfAccountId == bankMandiri.Id
                ? bankMandiri
                : bankBca;
            var isIn = trx.Type == BankTransactionType.BankIn;
            var journal = NewJournal(trx.TransactionDate, trx.Description ?? (isIn ? "Bank masuk" : "Bank keluar"),
                trx.TransactionNumber, isIn ? JournalSource.BankIn : JournalSource.BankOut);

            if (isIn)
            {
                Line(journal, bankCoa, trx.Amount, 0, "Penerimaan bank");
                Line(journal, piutang, 0, trx.Amount, "Pelunasan piutang");
            }
            else
            {
                Line(journal, hutangUsaha, trx.Amount, 0, "Pembayaran hutang");
                Line(journal, bankCoa, 0, trx.Amount, "Pengeluaran bank");
            }
        }

        // Beban operasional bulanan, dibayar lewat bank.
        var monthlyExpenses = new (ChartOfAccount Account, decimal Monthly, string Note)[]
        {
            (bebanGaji, 800_000m, "Gaji karyawan"),
            (bebanSewa, 400_000m, "Sewa kantor"),
            (bebanListrik, 150_000m, "Listrik & air"),
            (bebanInternet, 100_000m, "Internet & telepon"),
            (bebanTransport, 120_000m, "Transportasi"),
            (bebanMarketing, 150_000m, "Pemasaran")
        };

        for (var m = 1; m <= 12; m++)
        {
            var monthEnd = MonthOf(m).AddDays(27);
            foreach (var (account, monthly, note) in monthlyExpenses)
            {
                var journal = NewJournal(monthEnd, note, $"OPS-{monthEnd:yyyyMM}", JournalSource.BankOut);
                Line(journal, account, monthly, 0, note);
                Line(journal, bankBca, 0, monthly, "Bank BCA");
            }
        }

        ctx.JournalEntries.AddRange(demoJournals);
        await ctx.SaveChangesAsync();

        // Saldo akun = saldo awal + seluruh mutasi jurnal yang sudah diposting.
        var postedLines = await ctx.JournalEntryDetails
            .Include(d => d.JournalEntry)
            .Where(d => d.JournalEntry!.IsPosted)
            .Select(d => new { d.ChartOfAccountId, d.Debit, d.Credit })
            .ToListAsync();

        var movement = postedLines
            .GroupBy(l => l.ChartOfAccountId)
            .ToDictionary(g => g.Key, g => (Debit: g.Sum(x => x.Debit), Credit: g.Sum(x => x.Credit)));

        foreach (var coa in allCoa.Where(c => !c.IsHeader))
        {
            var delta = movement.TryGetValue(coa.Id, out var mv)
                ? (coa.AccountType is 1 or 5 ? mv.Debit - mv.Credit : mv.Credit - mv.Debit)
                : 0m;
            coa.CurrentBalance = coa.OpeningBalance + delta;
        }

        // Akun header menampung jumlah keturunannya.
        var childrenOf = allCoa.Where(c => c.ParentId.HasValue)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        decimal Roll(ChartOfAccount node)
        {
            if (!node.IsHeader) return node.CurrentBalance;
            var sum = 0m;
            if (childrenOf.TryGetValue(node.Id, out var kids)) sum = kids.Sum(Roll);
            node.CurrentBalance = sum;
            return sum;
        }

        foreach (var root in allCoa.Where(c => c.ParentId is null)) Roll(root);

        // Saldo rekening bank mengikuti akun buku besarnya.
        foreach (var bank in bankAccounts)
        {
            var coa = allCoa.FirstOrDefault(c => c.Id == bank.ChartOfAccountId);
            if (coa is not null) bank.Balance = coa.CurrentBalance;
        }

        // Piutang dan hutang per mitra disamakan dengan saldo akun kontrolnya.
        var totalCustomerBalance = customers.Sum(c => c.Balance);
        if (totalCustomerBalance > 0)
        {
            var factor = piutang.CurrentBalance / totalCustomerBalance;
            foreach (var customer in customers) customer.Balance = Math.Round(customer.Balance * factor, 0);
        }

        var totalSupplierBalance = suppliers.Sum(sp => sp.Balance);
        if (totalSupplierBalance > 0)
        {
            var factor = hutangUsaha.CurrentBalance / totalSupplierBalance;
            foreach (var supplier in suppliers) supplier.Balance = Math.Round(supplier.Balance * factor, 0);
        }

        await ctx.SaveChangesAsync();

        // ===== PAJAK =====
        await SeedTaxAsync(ctx, salesInvoices, customers, suppliers, thisMonth);
    }

    /// <summary>
    /// Faktur pajak, bukti potong PPh, dan SPT Masa PPN untuk data contoh.
    /// Masa berjalan sengaja ditinggalkan berstatus draf supaya alur
    /// "terbitkan faktur → susun SPT → lapor" bisa dicoba dari awal.
    /// </summary>
    private static async Task SeedTaxAsync(AppDbContext ctx, List<SalesInvoice> salesInvoices,
        List<Customer> customers, List<Supplier> suppliers, DateTime thisMonth)
    {
        var ppnRate = decimal.Parse(SettingsCatalog.DefaultOf(SettingsCatalog.PpnRate));
        var branch = SettingsCatalog.DefaultOf(SettingsCatalog.NsfpPrefix);
        var serial = 1L;

        // Faktur pajak diterbitkan untuk semua masa kecuali bulan berjalan.
        var invoiced = salesInvoices
            .Where(s => s.TaxAmount > 0 && s.InvoiceDate < thisMonth)
            .OrderBy(s => s.InvoiceDate)
            .ToList();

        foreach (var invoice in invoiced)
        {
            var customer = customers.FirstOrDefault(c => c.Id == invoice.CustomerId);
            var year2 = (invoice.InvoiceDate.Year % 100).ToString("00");

            ctx.TaxInvoices.Add(new TaxInvoice
            {
                FakturNumber = $"010.{branch}-{year2}.{serial:00000000}",
                TransactionCode = "01",
                StatusCode = "0",
                FakturDate = invoice.InvoiceDate,
                TaxPeriodMonth = invoice.InvoiceDate.Month,
                TaxPeriodYear = invoice.InvoiceDate.Year,
                SalesInvoiceId = invoice.Id,
                CustomerId = invoice.CustomerId,
                BuyerNpwp = customer?.TaxNumber,
                BuyerName = customer?.Name,
                BuyerAddress = customer?.Address,
                Dpp = invoice.SubTotal - invoice.DiscountAmount,
                PpnAmount = invoice.TaxAmount,
                PpnRate = ppnRate,
                Status = invoice.InvoiceDate < thisMonth.AddMonths(-1)
                    ? TaxInvoiceStatus.Approved
                    : TaxInvoiceStatus.Issued,
                DjpApprovalCode = invoice.InvoiceDate < thisMonth.AddMonths(-1)
                    ? $"APV{invoice.InvoiceDate:yyyyMM}{serial:0000}"
                    : null,
                DjpSubmittedAt = invoice.InvoiceDate < thisMonth.AddMonths(-1)
                    ? invoice.InvoiceDate.AddDays(3)
                    : null
            });
            serial++;
        }

        // Nomor seri berikutnya melanjutkan yang sudah terpakai.
        var nsfpNext = await ctx.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == SettingsCatalog.NsfpNext);
        if (nsfpNext is not null) nsfpNext.SettingValue = serial.ToString("00000000");

        // Bukti potong PPh 23 atas jasa dari pemasok.
        var slip = 1;
        for (var i = 11; i >= 1; i--)
        {
            var month = thisMonth.AddMonths(-i);
            var supplier = suppliers[i % suppliers.Count];
            var dpp = 12_000_000m + i * 750_000m;

            ctx.WithholdingTaxes.Add(new WithholdingTax
            {
                SlipNumber = $"BP23-{month:yyyyMM}-{slip:0000}",
                TaxType = "PPh23",
                SlipDate = month.AddDays(19),
                TaxPeriodMonth = month.Month,
                TaxPeriodYear = month.Year,
                IsWithholder = true,
                PartyName = supplier.Name,
                PartyNpwp = supplier.TaxNumber,
                SupplierId = supplier.Id,
                Description = "Jasa perawatan dan perbaikan",
                Dpp = dpp,
                Rate = 2,
                TaxAmount = Math.Round(dpp * 0.02m, 0),
                IsReported = true
            });
            slip++;

            // PPh 21 gaji karyawan.
            var gaji = 95_000_000m;
            ctx.WithholdingTaxes.Add(new WithholdingTax
            {
                SlipNumber = $"BP21-{month:yyyyMM}-{slip:0000}",
                TaxType = "PPh21",
                SlipDate = month.AddDays(24),
                TaxPeriodMonth = month.Month,
                TaxPeriodYear = month.Year,
                IsWithholder = true,
                PartyName = "Karyawan tetap",
                Description = "PPh 21 atas gaji bulanan",
                Dpp = gaji,
                Rate = 5,
                TaxAmount = Math.Round(gaji * 0.05m, 0),
                IsReported = true
            });
            slip++;
        }

        await ctx.SaveChangesAsync();

        // SPT Masa PPN untuk masa-masa yang sudah lewat.
        var fakturs = await ctx.TaxInvoices.ToListAsync();
        var purchases = await ctx.PurchaseInvoices.ToListAsync();

        for (var i = 11; i >= 1; i--)
        {
            var month = thisMonth.AddMonths(-i);
            var outputs = fakturs.Where(f => f.TaxPeriodMonth == month.Month && f.TaxPeriodYear == month.Year).ToList();
            var inputs = purchases.Where(p => p.InvoiceDate.Month == month.Month && p.InvoiceDate.Year == month.Year).ToList();

            var outputTax = outputs.Sum(o => o.PpnAmount);
            var inputTax = inputs.Sum(p => p.TaxAmount);
            var submitted = i > 1;

            ctx.TaxReturns.Add(new TaxReturn
            {
                PeriodMonth = month.Month,
                PeriodYear = month.Year,
                ReturnType = "PPN",
                OutputDpp = outputs.Sum(o => o.Dpp),
                OutputTax = outputTax,
                InputDpp = inputs.Sum(p => p.SubTotal - p.DiscountAmount),
                InputTax = inputTax,
                PayableAmount = outputTax - inputTax,
                Status = submitted ? TaxReturnStatus.Paid : TaxReturnStatus.Draft,
                NtteNumber = submitted ? $"NTTE{month:yyyyMM}00{i:00}" : null,
                NtpnNumber = submitted ? $"NTPN{month:yyyyMM}77{i:00}" : null,
                SubmittedAt = submitted ? month.AddMonths(1).AddDays(14) : null,
                PaidAt = submitted ? month.AddMonths(1).AddDays(9) : null
            });
        }

        await ctx.SaveChangesAsync();
    }
}
