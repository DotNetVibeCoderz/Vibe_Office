namespace Cuan.Services;

/// <summary>
/// Tipe editor yang dipakai halaman Pengaturan untuk merender sebuah parameter.
/// </summary>
public enum SettingEditor
{
    Text,
    Number,
    Bool,
    Select,
    Password,
    TextArea,
    AccountCode,   // dipilih dari Chart of Accounts
    CurrencyCode,  // dipilih dari master Mata Uang
    Date
}

/// <summary>
/// Definisi satu parameter sistem: kunci, nilai bawaan, grup, dan cara mengeditnya.
/// </summary>
public sealed record SettingDef(
    string Key,
    string Label,
    string Default,
    string Group,
    SettingEditor Editor = SettingEditor.Text,
    string? Description = null,
    string[]? Options = null,
    bool Secret = false);

/// <summary>
/// Katalog seluruh parameter yang bisa diubah dari halaman Pengaturan.
///
/// Satu-satunya sumber kebenaran: DataSeeder memakainya untuk mengisi tabel
/// SystemSettings, SettingsService memakainya sebagai nilai bawaan, dan
/// halaman /admin/settings merender formulirnya dari sini. Menambah parameter
/// baru cukup dengan menambah satu baris di daftar ini.
/// </summary>
public static class SettingsCatalog
{
    // ---- Grup ----
    public const string GroupCompany = "Perusahaan";
    public const string GroupCurrency = "Mata Uang & Format";
    public const string GroupTax = "Pajak";
    public const string GroupDjp = "Integrasi DJP";
    public const string GroupAccounting = "Akuntansi";
    public const string GroupMapping = "Pemetaan Akun";
    public const string GroupNumbering = "Penomoran Dokumen";
    public const string GroupTrx = "Penjualan & Pembelian";
    public const string GroupUi = "Tampilan";
    public const string GroupSystem = "Sistem";

    public static readonly string[] GroupOrder =
    {
        GroupCompany, GroupCurrency, GroupTax, GroupDjp, GroupAccounting,
        GroupMapping, GroupNumbering, GroupTrx, GroupUi, GroupSystem
    };

    // ---- Kunci yang dirujuk dari kode ----
    public const string CompanyName = "CompanyName";
    public const string CompanyAddress = "CompanyAddress";
    public const string CompanyCity = "CompanyCity";
    public const string CompanyPhone = "CompanyPhone";
    public const string CompanyEmail = "CompanyEmail";
    public const string CompanyTaxNumber = "CompanyTaxNumber";
    public const string CompanyNitku = "CompanyNitku";
    public const string CompanyIsPkp = "CompanyIsPkp";
    public const string CompanyPkpNumber = "CompanyPkpNumber";
    public const string CompanyPkpDate = "CompanyPkpDate";
    public const string CompanyKlu = "CompanyKlu";
    public const string CompanySignatory = "CompanySignatory";
    public const string CompanySignatoryPosition = "CompanySignatoryPosition";

    public const string BaseCurrency = "BaseCurrency";
    public const string CurrencySymbol = "CurrencySymbol";
    public const string CurrencyDecimals = "CurrencyDecimals";
    public const string CurrencyShowSymbol = "CurrencyShowSymbol";
    public const string Locale = "Locale";
    public const string DateFormat = "DateFormat";

    public const string PpnEnabled = "PpnEnabled";
    public const string PpnRate = "PpnRate";
    public const string PpnPriceInclusive = "PpnPriceInclusive";
    public const string PpnDppFactor = "PpnDppFactor";
    public const string Pph21Rate = "Pph21Rate";
    public const string Pph23Rate = "Pph23Rate";
    public const string Pph42Rate = "Pph42Rate";
    public const string Pph25Monthly = "Pph25Monthly";
    public const string PpnbmRate = "PpnbmRate";
    public const string FakturTransactionCode = "FakturTransactionCode";
    public const string FakturStatusCode = "FakturStatusCode";
    public const string NsfpPrefix = "NsfpPrefix";
    public const string NsfpRangeStart = "NsfpRangeStart";
    public const string NsfpRangeEnd = "NsfpRangeEnd";
    public const string NsfpNext = "NsfpNext";

    public const string DjpEnabled = "DjpEnabled";
    public const string DjpMode = "DjpMode";
    public const string DjpBaseUrl = "DjpBaseUrl";
    public const string DjpClientId = "DjpClientId";
    public const string DjpClientSecret = "DjpClientSecret";
    public const string DjpNpwpUser = "DjpNpwpUser";
    public const string DjpCertificatePath = "DjpCertificatePath";
    public const string DjpCertificatePassword = "DjpCertificatePassword";
    public const string DjpTimeoutSeconds = "DjpTimeoutSeconds";
    public const string EfakturExportFormat = "EfakturExportFormat";

    public const string FiscalYearStart = "FiscalYearStart";
    public const string FiscalYearEnd = "FiscalYearEnd";
    public const string AutoPostJournal = "AutoPostJournal";
    public const string PostingUpdatesBalance = "PostingUpdatesBalance";
    public const string StockMovementUpdatesQty = "StockMovementUpdatesQty";
    public const string RoundingDecimals = "RoundingDecimals";

    public const string AccountAr = "AccountAr";
    public const string AccountAp = "AccountAp";
    public const string AccountSales = "AccountSales";
    public const string AccountSalesDiscount = "AccountSalesDiscount";
    public const string AccountPurchase = "AccountPurchase";
    public const string AccountInventory = "AccountInventory";
    public const string AccountCogs = "AccountCogs";
    public const string AccountPpnKeluaran = "AccountPpnKeluaran";
    public const string AccountPpnMasukan = "AccountPpnMasukan";
    public const string AccountCash = "AccountCash";
    public const string AccountShipping = "AccountShipping";
    public const string AccountGiroIn = "AccountGiroIn";
    public const string AccountGiroOut = "AccountGiroOut";
    public const string AccountStockAdjustment = "AccountStockAdjustment";
    public const string AccountBankFee = "AccountBankFee";
    public const string AccountOtherIncome = "AccountOtherIncome";
    public const string AccountOtherExpense = "AccountOtherExpense";

    public const string PrefixJournal = "PrefixJournal";
    public const string PrefixSalesInvoice = "PrefixSalesInvoice";
    public const string PrefixPurchaseInvoice = "PrefixPurchaseInvoice";
    public const string PrefixCashIn = "PrefixCashIn";
    public const string PrefixCashOut = "PrefixCashOut";
    public const string PrefixBankIn = "PrefixBankIn";
    public const string PrefixBankOut = "PrefixBankOut";
    public const string PrefixGiroIn = "PrefixGiroIn";
    public const string PrefixGiroOut = "PrefixGiroOut";
    public const string PrefixStock = "PrefixStock";
    public const string PrefixTransfer = "PrefixTransfer";
    public const string DocumentNumberFormat = "DocumentNumberFormat";
    public const string DocumentNumberReset = "DocumentNumberReset";
    public const string DocumentNumberPadding = "DocumentNumberPadding";

    public const string DefaultPaymentTermDays = "DefaultPaymentTermDays";
    public const string AllowNegativeStock = "AllowNegativeStock";
    public const string MaxSalesDiscountPercent = "MaxSalesDiscountPercent";
    public const string DefaultShippingCost = "DefaultShippingCost";

    public const string DefaultTheme = "DefaultTheme";
    public const string ItemsPerPage = "ItemsPerPage";
    public const string LowStockTileCount = "LowStockTileCount";
    public const string EnableNotifications = "EnableNotifications";
    public const string ShowDemoCredentials = "ShowDemoCredentials";

    public const string EnableAuditLog = "EnableAuditLog";
    public const string EnableMultiCurrency = "EnableMultiCurrency";
    public const string EnableMultiWarehouse = "EnableMultiWarehouse";
    public const string SessionTimeoutMinutes = "SessionTimeoutMinutes";
    public const string MaxLoginAttempts = "MaxLoginAttempts";

    private static readonly string[] YesNo = { "true", "false" };

    /// <summary>Seluruh parameter, berurut sesuai tampilannya di halaman Pengaturan.</summary>
    public static readonly IReadOnlyList<SettingDef> All = new List<SettingDef>
    {
        // ---------------- Perusahaan ----------------
        new(CompanyName, "Nama perusahaan", "PT. CUAN MAKMUR SENTOSA", GroupCompany,
            Description: "Dicetak di kepala semua laporan dan faktur."),
        new(CompanyAddress, "Alamat", "Jl. Sudirman No. 123", GroupCompany, SettingEditor.TextArea),
        new(CompanyCity, "Kota", "Jakarta Pusat", GroupCompany),
        new(CompanyPhone, "Telepon", "021-555-0123", GroupCompany),
        new(CompanyEmail, "Email", "info@cuanmakmur.id", GroupCompany),
        new(CompanyTaxNumber, "NPWP", "01.234.567.8-012.000", GroupCompany,
            Description: "Dipakai pada faktur pajak dan seluruh berkas pelaporan DJP."),
        new(CompanyNitku, "NITKU", "0123456780120000000000", GroupCompany,
            Description: "Nomor Identitas Tempat Kegiatan Usaha — wajib pada Coretax."),
        new(CompanyIsPkp, "Berstatus PKP", "true", GroupCompany, SettingEditor.Bool,
            "Kalau bukan PKP, PPN tidak dipungut dan faktur pajak dinonaktifkan.", YesNo),
        new(CompanyPkpNumber, "No. pengukuhan PKP", "PEM-00123/WPJ.06/KP.0103/2020", GroupCompany),
        new(CompanyPkpDate, "Tanggal pengukuhan PKP", "2020-01-15", GroupCompany, SettingEditor.Date),
        new(CompanyKlu, "KLU", "46900", GroupCompany, Description: "Klasifikasi Lapangan Usaha."),
        new(CompanySignatory, "Penanda tangan", "Fadhil Gravicode", GroupCompany,
            Description: "Nama yang tercetak di kolom tanda tangan faktur pajak."),
        new(CompanySignatoryPosition, "Jabatan penanda tangan", "Direktur", GroupCompany),

        // ---------------- Mata Uang & Format ----------------
        new(BaseCurrency, "Mata uang dasar", "IDR", GroupCurrency, SettingEditor.CurrencyCode,
            "Semua laporan disajikan dalam mata uang ini."),
        new(CurrencySymbol, "Simbol mata uang", "Rp", GroupCurrency),
        new(CurrencyDecimals, "Angka di belakang koma", "0", GroupCurrency, SettingEditor.Select,
            "Rupiah lazimnya tanpa desimal.", new[] { "0", "2" }),
        new(CurrencyShowSymbol, "Tampilkan simbol pada angka", "true", GroupCurrency, SettingEditor.Bool,
            Options: YesNo),
        new(Locale, "Format angka & tanggal", "id-ID", GroupCurrency, SettingEditor.Select,
            "id-ID memakai titik sebagai pemisah ribuan: Rp 1.500.000.",
            new[] { "id-ID", "en-US" }),
        new(DateFormat, "Format tanggal", "dd/MM/yyyy", GroupCurrency, SettingEditor.Select,
            Options: new[] { "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "d MMMM yyyy" }),

        // ---------------- Pajak ----------------
        new(PpnEnabled, "Pungut PPN", "true", GroupTax, SettingEditor.Bool,
            "Mematikan ini menghilangkan baris PPN dari faktur penjualan dan pembelian.", YesNo),
        new(PpnRate, "Tarif PPN (%)", "11", GroupTax, SettingEditor.Number,
            "Tarif berlaku 11%. Naikkan ke 12% saat ketentuan barunya berlaku untuk perusahaan Anda."),
        new(PpnPriceInclusive, "Harga sudah termasuk PPN", "false", GroupTax, SettingEditor.Bool,
            "Kalau ya, PPN dihitung mundur dari harga jual, bukan ditambahkan.", YesNo),
        new(PpnDppFactor, "Faktor DPP nilai lain", "1", GroupTax, SettingEditor.Number,
            "Isi 0.9166667 bila memakai DPP nilai lain 11/12."),
        new(Pph21Rate, "Tarif PPh 21 (%)", "5", GroupTax, SettingEditor.Number),
        new(Pph23Rate, "Tarif PPh 23 jasa (%)", "2", GroupTax, SettingEditor.Number),
        new(Pph42Rate, "Tarif PPh 4 ayat 2 (%)", "10", GroupTax, SettingEditor.Number),
        new(Pph25Monthly, "Angsuran PPh 25 per bulan", "0", GroupTax, SettingEditor.Number),
        new(PpnbmRate, "Tarif PPnBM (%)", "20", GroupTax, SettingEditor.Number),
        new(FakturTransactionCode, "Kode transaksi faktur pajak", "01", GroupTax, SettingEditor.Select,
            "01 penyerahan kepada selain pemungut PPN.",
            new[] { "01", "02", "03", "04", "05", "06", "07", "08", "09" }),
        new(FakturStatusCode, "Kode status faktur", "0", GroupTax, SettingEditor.Select,
            "0 normal, 1 pengganti.", new[] { "0", "1" }),
        new(NsfpPrefix, "Kode cabang NSFP", "000", GroupTax,
            Description: "Tiga digit pertama nomor seri faktur pajak."),
        new(NsfpRangeStart, "Awal jatah NSFP", "00000001", GroupTax,
            Description: "Nomor seri faktur pajak yang dijatah DJP."),
        new(NsfpRangeEnd, "Akhir jatah NSFP", "00001000", GroupTax),
        new(NsfpNext, "NSFP berikutnya", "00000001", GroupTax,
            Description: "Naik otomatis setiap faktur pajak diterbitkan."),

        // ---------------- Integrasi DJP ----------------
        new(DjpEnabled, "Aktifkan integrasi DJP", "false", GroupDjp, SettingEditor.Bool,
            "Perlu kredensial resmi dari DJP. Selama nonaktif, pelaporan tetap bisa lewat berkas ekspor.", YesNo),
        new(DjpMode, "Lingkungan", "Sandbox", GroupDjp, SettingEditor.Select,
            Options: new[] { "Sandbox", "Production" }),
        new(DjpBaseUrl, "Alamat layanan", "https://api-sandbox.pajak.go.id", GroupDjp,
            Description: "Endpoint Coretax/DJP. Ganti ke alamat produksi saat sudah disetujui."),
        new(DjpClientId, "Client ID", "", GroupDjp),
        new(DjpClientSecret, "Client secret", "", GroupDjp, SettingEditor.Password, Secret: true),
        new(DjpNpwpUser, "NPWP pengguna aplikasi", "", GroupDjp,
            Description: "NPWP penandatangan yang terdaftar di akun DJP."),
        new(DjpCertificatePath, "Berkas sertifikat elektronik", "", GroupDjp,
            Description: "Path .p12 sertifikat elektronik untuk penandatanganan."),
        new(DjpCertificatePassword, "Passphrase sertifikat", "", GroupDjp, SettingEditor.Password, Secret: true),
        new(DjpTimeoutSeconds, "Batas waktu koneksi (detik)", "30", GroupDjp, SettingEditor.Number),
        new(EfakturExportFormat, "Format ekspor faktur pajak", "CSV", GroupDjp, SettingEditor.Select,
            "CSV untuk aplikasi e-Faktur desktop, XML untuk unggahan Coretax.",
            new[] { "CSV", "XML" }),

        // ---------------- Akuntansi ----------------
        new(FiscalYearStart, "Awal tahun buku", "01-01", GroupAccounting, SettingEditor.Select,
            Options: new[] { "01-01", "04-01", "07-01", "10-01" }),
        new(FiscalYearEnd, "Akhir tahun buku", "12-31", GroupAccounting, SettingEditor.Select,
            Options: new[] { "12-31", "03-31", "06-30", "09-30" }),
        new(AutoPostJournal, "Buat jurnal otomatis dari transaksi", "true", GroupAccounting, SettingEditor.Bool,
            "Faktur, kas, bank, giro, dan penyesuaian stok langsung membentuk jurnal saat disimpan.", YesNo),
        new(PostingUpdatesBalance, "Posting memperbarui saldo akun", "true", GroupAccounting, SettingEditor.Bool,
            "Mematikan ini membuat laporan keuangan berhenti mengikuti transaksi baru.", YesNo),
        new(StockMovementUpdatesQty, "Mutasi stok memperbarui kuantitas barang", "true", GroupAccounting,
            SettingEditor.Bool, Options: YesNo),
        new(RoundingDecimals, "Pembulatan nilai transaksi", "0", GroupAccounting, SettingEditor.Select,
            Options: new[] { "0", "2" }),

        // ---------------- Pemetaan Akun ----------------
        // Kode bawaan mengikuti COA contoh yang dibuat DataSeeder.
        new(AccountAr, "Piutang usaha", "1-1400", GroupMapping, SettingEditor.AccountCode,
            "Didebit saat faktur penjualan diposting."),
        new(AccountAp, "Hutang usaha", "2-1100", GroupMapping, SettingEditor.AccountCode,
            "Dikredit saat faktur pembelian diposting."),
        new(AccountSales, "Pendapatan penjualan", "4-1100", GroupMapping, SettingEditor.AccountCode),
        new(AccountSalesDiscount, "Diskon penjualan", "4-1600", GroupMapping, SettingEditor.AccountCode),
        new(AccountPurchase, "Pembelian / persediaan masuk", "1-1500", GroupMapping, SettingEditor.AccountCode),
        new(AccountInventory, "Persediaan barang", "1-1500", GroupMapping, SettingEditor.AccountCode),
        new(AccountCogs, "Harga pokok penjualan", "5-1100", GroupMapping, SettingEditor.AccountCode),
        new(AccountPpnKeluaran, "PPN keluaran", "2-1200", GroupMapping, SettingEditor.AccountCode),
        new(AccountPpnMasukan, "PPN masukan", "1-1600", GroupMapping, SettingEditor.AccountCode),
        new(AccountCash, "Kas", "1-1100", GroupMapping, SettingEditor.AccountCode),
        new(AccountShipping, "Ongkos kirim", "5-2100", GroupMapping, SettingEditor.AccountCode),
        new(AccountGiroIn, "Giro masuk belum cair", "1-1800", GroupMapping, SettingEditor.AccountCode),
        new(AccountGiroOut, "Giro keluar belum cair", "2-1700", GroupMapping, SettingEditor.AccountCode),
        new(AccountStockAdjustment, "Selisih penyesuaian stok", "5-2300", GroupMapping, SettingEditor.AccountCode),
        new(AccountBankFee, "Biaya administrasi bank", "5-2200", GroupMapping, SettingEditor.AccountCode),
        new(AccountOtherIncome, "Pendapatan lain-lain", "4-1400", GroupMapping, SettingEditor.AccountCode,
            "Lawan jurnal kas/bank masuk yang tidak terkait pelanggan."),
        new(AccountOtherExpense, "Beban lain-lain", "5-2400", GroupMapping, SettingEditor.AccountCode,
            "Lawan jurnal kas/bank keluar yang tidak terkait pemasok."),

        // ---------------- Penomoran ----------------
        new(DocumentNumberFormat, "Pola nomor dokumen", "{PREFIX}-{YYYY}-{SEQ}", GroupNumbering, SettingEditor.Select,
            "{PREFIX} awalan, {YYYY} tahun, {MM} bulan, {SEQ} urutan.",
            new[] { "{PREFIX}-{YYYY}-{SEQ}", "{PREFIX}/{YYYY}/{SEQ}", "{PREFIX}-{YYYY}{MM}-{SEQ}", "{PREFIX}{SEQ}" }),
        new(DocumentNumberPadding, "Panjang digit urutan", "4", GroupNumbering, SettingEditor.Select,
            Options: new[] { "3", "4", "5", "6" }),
        new(DocumentNumberReset, "Urutan diulang setiap", "Tahun", GroupNumbering, SettingEditor.Select,
            Options: new[] { "Tahun", "Bulan", "Tidak pernah" }),
        new(PrefixJournal, "Awalan jurnal umum", "JU", GroupNumbering),
        new(PrefixSalesInvoice, "Awalan faktur penjualan", "INV", GroupNumbering),
        new(PrefixPurchaseInvoice, "Awalan faktur pembelian", "PB", GroupNumbering),
        new(PrefixCashIn, "Awalan kas masuk", "KM", GroupNumbering),
        new(PrefixCashOut, "Awalan kas keluar", "KK", GroupNumbering),
        new(PrefixBankIn, "Awalan bank masuk", "BM", GroupNumbering),
        new(PrefixBankOut, "Awalan bank keluar", "BK", GroupNumbering),
        new(PrefixGiroIn, "Awalan giro masuk", "GM", GroupNumbering),
        new(PrefixGiroOut, "Awalan giro keluar", "GK", GroupNumbering),
        new(PrefixStock, "Awalan penyesuaian stok", "ADJ", GroupNumbering),
        new(PrefixTransfer, "Awalan transfer bank", "TRF", GroupNumbering),

        // ---------------- Penjualan & Pembelian ----------------
        new(DefaultPaymentTermDays, "Termin pembayaran bawaan (hari)", "30", GroupTrx, SettingEditor.Number,
            "Jatuh tempo faktur baru dihitung dari tanggal faktur ditambah angka ini."),
        new(MaxSalesDiscountPercent, "Batas diskon penjualan (%)", "20", GroupTrx, SettingEditor.Number,
            "Diskon di atas angka ini ditolak saat faktur disimpan."),
        new(DefaultShippingCost, "Ongkos kirim bawaan", "0", GroupTrx, SettingEditor.Number),
        new(AllowNegativeStock, "Izinkan stok minus", "false", GroupTrx, SettingEditor.Bool,
            "Kalau tidak, penjualan yang melebihi stok akan ditolak.", YesNo),

        // ---------------- Tampilan ----------------
        new(DefaultTheme, "Tema bawaan", "light", GroupUi, SettingEditor.Select,
            Options: new[] { "light", "dark" }),
        new(ItemsPerPage, "Baris per halaman", "25", GroupUi, SettingEditor.Select,
            Options: new[] { "10", "25", "50", "100" }),
        new(LowStockTileCount, "Jumlah barang di panel stok rendah", "6", GroupUi, SettingEditor.Number),
        new(EnableNotifications, "Notifikasi real-time", "true", GroupUi, SettingEditor.Bool, Options: YesNo),
        new(ShowDemoCredentials, "Tampilkan akun contoh di halaman masuk", "true", GroupUi, SettingEditor.Bool,
            "Matikan sebelum dipakai di lingkungan sungguhan.", YesNo),

        // ---------------- Sistem ----------------
        new(EnableAuditLog, "Catat jejak audit", "true", GroupSystem, SettingEditor.Bool, Options: YesNo),
        new(EnableMultiCurrency, "Multi mata uang", "true", GroupSystem, SettingEditor.Bool, Options: YesNo),
        new(EnableMultiWarehouse, "Multi gudang", "true", GroupSystem, SettingEditor.Bool, Options: YesNo),
        new(SessionTimeoutMinutes, "Sesi berakhir setelah (menit)", "30", GroupSystem, SettingEditor.Number),
        new(MaxLoginAttempts, "Batas percobaan masuk", "5", GroupSystem, SettingEditor.Number),
    };

    private static readonly Dictionary<string, SettingDef> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    public static SettingDef? Find(string key) => ByKey.GetValueOrDefault(key);

    public static string DefaultOf(string key) => ByKey.TryGetValue(key, out var d) ? d.Default : string.Empty;
}
