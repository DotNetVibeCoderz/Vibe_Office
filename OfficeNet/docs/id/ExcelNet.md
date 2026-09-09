# ExcelNet

Workbook `.xlsx` untuk .NET 10 — pekerjaan openpyxl, plus jembatan ke GraviFrame untuk pekerjaan
pandas.

*English: [docs/ExcelNet.md](../ExcelNet.md)* · [Kembali ke indeks](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.ExcelNet
```

---

![Workbook Excel hasil kode di halaman ini, dirender ke PNG](../screenshots/excelnet-workbook.png)

*Tanggal bertipe, format angka Rupiah, `SUM` dan `AVERAGE` yang benar-benar **dihitung** — total di
gambar itu dihasilkan mesin formula, bukan diketik — dan kolom yang dilebarkan `AutoFitColumns`.*

---

## Membuka dan menyimpan

```csharp
using ExcelNet;
using ExcelNet.Styles;

using var workbook = Workbook.Create("Penjualan");
using var existing = Workbook.Open("data.xlsx");   // path, Stream, atau byte[]

workbook.Save("data.xlsx");
workbook.SaveAsPdf("data.pdf");
```

Berbeda dari WordNet, ExcelNet mengurai tiap sheet menjadi model lalu menuliskannya kembali. XML
worksheet hanyalah daftar baris yang datar dan tidak ada yang perlu dipertahankan byte demi byte,
sedangkan dictionary membuat akses sel acak menjadi O(1) alih-alih penelusuran. Pertimbangannya
berbeda dari Word, jadi modelnya juga berbeda — lihat
[Konsep inti](Core.md#dua-model-yang-disengaja).

## Sheet

```csharp
var sheet = workbook["Penjualan"];      // menurut nama — melempar bila tidak ada
var first = workbook[0];                // menurut indeks
var maybe = workbook.Find("Arsip");     // null bila tidak ada

workbook.AddSheet("Ringkasan");
workbook.AddSheetUnique("Data");        // "Data", lalu "Data1", "Data2"…
workbook.CopySheet("Penjualan", "Penjualan (salinan)");
workbook.MoveSheet("Ringkasan", 0);
workbook.RemoveSheet("Arsip");
```

## Sel

Alamat bisa gaya A1 atau baris/kolom berbasis nol — keduanya menunjuk sel yang sama.

```csharp
sheet["A1"].Set("Tanggal");
sheet[0, 1].Set("Produk");        // baris 0, kolom 1 = B1

sheet["B2"].Set(42);              // angka
sheet["B3"].Set(3.14);
sheet["B4"].Set(DateTime.Now);    // angka + format tanggal
sheet["B5"].Set(true);            // boolean
sheet["B6"].Set("teks");
sheet["B7"].Set(null);            // mengosongkan nilainya
```

`Set(object?)` memilih tipe sel dari tipe runtime-nya. Tanggal disimpan sebagai angka *ditambah*
format angka — memang hanya itu arti tanggal di spreadsheet, sehingga membedakan tanggal dari angka
biasa membutuhkan stylesheet, dan itulah sebabnya ExcelNet membaca style sebelum sheet mana pun.

Membacanya kembali:

```csharp
string text = sheet["B6"].Text;
double number = sheet["B2"].Number;
DateTime date = sheet["B4"].DateTime;
bool empty = sheet["Z99"].IsEmpty;
```

Penulisan massal menghindari satu panggilan per sel:

```csharp
sheet.WriteHeader("A1", ["Tanggal", "Produk", "Qty", "Total"]);
sheet.WriteRow("A2", DateTime.Today, "WordNet", 12, 480_000);
sheet.WriteColumn("F1", "Jan", "Feb", "Mar");
sheet.WriteRange("A5", rows);     // IEnumerable<IEnumerable<object?>>
```

## Formula

```csharp
sheet["F2"].SetFormula("D2*E2");
sheet["F16"].SetFormula("SUM(F2:F15)");
sheet["F17"].SetFormula("AVERAGE(F2:F15)");
sheet["G2"].SetFormula("IF(F2>1000000,\"Besar\",\"Kecil\")");

workbook.Recalculate();           // mengisi setiap hasil cache
```

**Panggil `Recalculate()` sebelum menyimpan.** Sel formula punya dua bagian: ekspresinya dan hasil
yang di-cache. Excel menghitung ulang saat dibuka dan tidak peduli pada cache — tetapi semua konsumen
lain membacanya, sehingga workbook yang disimpan tanpa hitung ulang menampilkan nol di Google Sheets,
di ekspor PDF, dan di apa pun yang mengurai berkasnya secara langsung.

Mesinnya mengimplementasikan sekitar 60 fungsi:

`ABS AND AVERAGE CONCAT CONCATENATE COUNT COUNTA COUNTBLANK COUNTIF DATE DAY ERROR.TYPE EXP HOUR IF
IFERROR IFNA INT ISERR ISERROR ISNA LEFT LEN LN LOG10 LOWER MAX MEDIAN MID MIN MINUTE MOD MONTH NOT
NOW OR POWER PRODUCT RIGHT ROUND ROUNDDOWN ROUNDUP SECOND SIGN SQRT STDEV STDEV.P STDEV.S SUBSTITUTE
SUM SUMIF TEXTJOIN TODAY TRIM UPPER VALUE VAR VAR.P VAR.S VLOOKUP WEEKDAY YEAR`

Ia meniru aritmetika Excel di tempat Excel berbeda dari .NET, karena ketidakcocokan halus lebih buruk
daripada fungsi yang tidak ada:

- `ROUND` membulatkan menjauhi nol, bukan pembulatan bankir — `ROUND(2,5; 0)` adalah 3, bukan 2.
- `MOD` mengikuti tanda pembaginya, sehingga `MOD(-3; 2)` adalah 1.
- `-2^2` adalah 4: minus uner mengikat lebih kuat daripada `^`.
- Epoch tanggalnya 1899-12-30, dan serial 60 adalah 29 Februari 1900 yang sebenarnya tidak ada — bug
  yang dibawa Excel sejak 1985.

Kesalahan mengalir sebagai nilai, bukan sebagai exception, sehingga `IFERROR` bisa menangkapnya:

```csharp
sheet["A1"].SetFormula("1/0");                 // #DIV/0!
sheet["A2"].SetFormula("IFERROR(A1,\"n/a\")"); // "n/a"
```

**Belum ada:** array formula, iterative calculation, referensi antar-workbook, serta
`INDEX`/`MATCH`/`XLOOKUP` (`VLOOKUP` mengasumsikan tabel dua kolom). Lihat [Plan.md](../../Plan.md).

## Style

`CellStyle` adalah record yang immutable; setiap `With…` mengembalikan yang baru, sehingga style bisa
disusun dan aman dipakai bersama.

```csharp
sheet["A1"].Bold();
sheet["A1"].WithBackground(OfficeColor.FromRgb(0xFF, 0xF2, 0xCC));
sheet["B2"].WithNumberFormat(NumberFormats.Rupiah);

var header = CellStyle.Default
    .Bold()
    .WithColor(OfficeColor.White)
    .WithBackground(OfficeColor.FromRgb(0x1F, 0x38, 0x64))
    .WithAlignment(HorizontalAlignment.Center)
    .WithBorder(CellBorder.All(BorderLineStyle.Thin));

sheet.Range("A1:F1").ApplyStyle(header);
```

Format bawaan ada di `NumberFormats`: `General`, `Integer`, `TwoDecimals`, `Thousands`,
`ThousandsTwoDecimals`, `Percent`, `PercentTwoDecimals`, `Rupiah`, `RupiahTwoDecimals`, ditambah
format tanggal dan waktu. String format Excel apa pun juga bisa dipakai.

### Kenapa sebuah style kadang "tidak berpengaruh"

Excel mengabaikan format kecuali penanda `apply…` yang bersesuaian diset pada record-nya. ExcelNet
mengurusnya untuk Anda; ini alasan utama style OOXML yang ditulis tangan tidak berefek apa-apa, dan
layak diketahui kalau Anda pernah memeriksa XML-nya. Dua lagi dari keluarga yang sama:

- Warna fill solid ditaruh di `fgColor`. Menaruhnya di `bgColor` menghasilkan putih.
- Indeks fill 0 harus `none` dan indeks 1 harus `gray125`. Excel mematoknya, sehingga fill pertama
  yang bisa dipakai adalah indeks 2.

## Range

```csharp
var range = sheet.Range("A1:D10");

range.Fill(0);
range.ApplyStyle(header);
range.ModifyStyle(s => s.Bold());          // mempertahankan yang lain
range.SetOutlineBorder(BorderLineStyle.Medium);
range.Merge();

object?[,] values = range.ToArray();

foreach (var cell in range)
{
    Console.WriteLine($"{cell.Address}: {cell.Text}");
}
```

## Tata letak

```csharp
sheet.SetColumnWidth("A", 18);
sheet.AutoFitColumns();                    // diukur dari isinya
sheet.SetRowHeight(0, 22);
sheet.HideColumn(3);

sheet.Frozen = new FreezePanes(Rows: 1, Columns: 0);
sheet.AutoFilter = CellRangeReference.Parse("A1:F15");
sheet.TabColor = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
sheet.ShowGridLines = false;
sheet.MergeCells("A1:D1");
```

Lebar kolom diukur dalam *jumlah karakter angka nol* pada font baku, bukan dalam titik atau piksel.
`AutoFitColumns` melakukan konversinya; menetapkan lebar dengan tangan berarti berpikir dalam satuan
itu.

## Format bersyarat

```csharp
sheet.AddColorScale("D2:D15",
    OfficeColor.FromRgb(0xF8, 0x69, 0x6B),   // rendah
    OfficeColor.FromRgb(0x63, 0xBE, 0x7B));  // tinggi

sheet.AddDataBar("E2:E15", OfficeColor.FromRgb(0x63, 0x8E, 0xC6));

sheet.AddConditionalFormat(CellRangeReference.Parse("F2:F15"), "greaterThan",
    CellStyle.Default.WithBackground(OfficeColor.FromRgb(0xC6, 0xEF, 0xCE)),
    "1000000");   // operand di akhir, supaya satu aturan bisa menerima dua operand
```

## Chart

Chart di worksheet adalah part DrawingML yang sama dengan chart PowerPoint — modelnya ada di
`OfficeNet.Core.Charts` dan dipakai bersama oleh kedua library. Yang berbeda adalah cara
melekatkannya:

```
sheet1.xml  --drawing-->  drawing1.xml  --chart-->  chart1.xml
```

Excel mencapai chart lewat part *drawing* perantara. Kalau part itu tidak ada, berkasnya terbuka
tanpa chart dan tanpa keluhan apa pun — begitulah workbook buatan tangan biasanya kehilangan chart.

```csharp
using OfficeNet.Core.Charts;

sheet.AddChart(new ChartData
{
    Type = ChartType.Column,
    Title = "Pendapatan per Wilayah",
    Categories = ["Jakarta", "Bandung", "Surabaya", "Medan"],
    Series =
    [
        new ChartSeries("2025", [1120, 860, 740, 410]),
        new ChartSeries("2026", [1480, 1150, 905, 520]),
    ],
    ValueFormat = "#,##0",
    ShowDataLabels = true,
}, "E2:M20");
```

Jangkarnya berupa rentang sel, persis cara Excel memandang chart mengambang: kedua sudutnya
dipatok, sehingga chart ikut berubah ukuran saat baris dan kolom di bawahnya berubah.

Membacanya kembali:

```csharp
foreach (var chart in sheet.Charts)
{
    var data = chart.GetData();
    Console.WriteLine($"{data.Type} di {chart.Anchor.A1}, {data.Series.Count} seri");

    chart.SetData(data with { Type = ChartType.Bar });
}
```

Dua belas tipe yang sama seperti di [PowerPointNet](PowerPointNet.md#chart). **Pengekspor PDF tidak
menggambar chart worksheet** — hanya chart PowerPoint yang dirender saat ekspor. Excel sendiri
menampilkannya seperti biasa.

## Pivot table

```csharp
using ExcelNet.Pivot;

var summary = workbook.AddSheet("Ringkasan");

summary.AddPivotTable(new PivotTableDefinition
{
    Source = workbook["Data"],
    SourceRange = CellRangeReference.Parse("A1:D100"),   // termasuk baris header
    Target = CellReference.Parse("A3"),
    Rows = ["Wilayah"],
    Columns = ["Produk"],
    Values = [new PivotValue("Total", PivotFunction.Sum)],
});
```

Field dialamatkan lewat teks header-nya, jadi rentang sumber harus dimulai dari baris header.
Sebelas fungsi: `Sum` (baku), `Count`, `CountNumbers`, `Average`, `Max`, `Min`, `Product`,
`StdDev`, `StdDevP`, `Var`, `VarP`.

Satu pivot adalah empat part, bukan satu:

```
workbook.xml  --pivotCacheDefinition-->  pivotCacheDefinition1.xml  --pivotCacheRecords-->  records
sheet2.xml    --pivotTable-->            pivotTable1.xml            --pivotCacheDefinition-->  ^
```

Cache adalah cuplikan data sumber — itulah sebabnya menyunting sumber tidak mengubah apa pun sampai
seseorang me-refresh — sedangkan tabelnya hanya menyimpan tata letak. Workbook dan tabel harus
menyebut `cacheId` yang sama, atau Excel melaporkan berkasnya rusak.

**Grid hasilnya tidak ditulis.** Part-nya menjelaskan cache dan tata letak; Excel menghitung selnya
saat membuka berkas, sesuai permintaan `refreshOnLoad`. Jadi Excel menampilkan pivot table yang
lengkap, sementara konsumen non-Excel — termasuk ekspor PDF library ini sendiri — melihat area itu
kosong. Menghitung grid-nya di sini berarti menulis ulang agregasi dan tata letak subtotal Excel,
dan setiap ketidakcocokan akan tampak sebagai tabel yang berubah begitu seseorang membukanya.

## Validasi data

Dropdown adalah yang membuat sheet bisa diisi manusia, bukan hanya oleh program — bedanya antara
kolom yang bersih dan kolom berisi "Jakarta", "jakarta", dan "DKI Jakarta".

```csharp
using ExcelNet.Validation;

sheet.AddDropdown("B2:B200", "Jakarta", "Bandung", "Surabaya", "Medan");
```

`AddValidation` menerima jenis aturan lainnya. Builder mengembalikan aturan polos dan pesannya
berupa properti `init`, jadi tambahkan dengan `with`:

```csharp
sheet.AddValidation(DataValidation.WholeNumberBetween(
    CellRangeReference.Parse("C2:C200"), 1, 1000) with
{
    ErrorTitle = "Di luar rentang",
    ErrorMessage = "Isi angka antara 1 dan 1000.",
    PromptTitle = "Jumlah",
    PromptMessage = "1 sampai 1000.",
});
```

| Builder | Yang diizinkan |
| --- | --- |
| `List(range, values)` | salah satu dari daftar tetap, tampil sebagai dropdown |
| `ListFromRange(range, source)` | salah satu nilai di `source`, misalnya `Lists!$A$1:$A$50` |
| `WholeNumberBetween(range, min, max)` | bilangan bulat, inklusif |
| `DecimalBetween(range, min, max)` | angka apa pun, inklusif |
| `DateBetween(range, from, to)` | tanggal, inklusif |
| `TextLengthAtMost(range, max)` | teks tidak lebih panjang dari `max` |
| `Custom(range, formula)` | apa pun yang diterima formula |

Daftar inline disimpan sebagai string dalam tanda kutip yang dipisah koma, dan **Excel membatasinya
di 255 karakter** — lewat dari itu seluruh aturan dibuang dan file terbuka tanpa dropdown, tanpa
pesan apa pun. `List` justru melempar exception dan menyebutkan panjangnya. Nilai yang mengandung
koma ditolak dengan alasan yang sama: koma adalah pemisahnya, sehingga `"Jakarta, DKI"` akan menjadi
dua entri. Untuk kedua kasus itulah `ListFromRange` ada; beri referensi absolut, karena referensi
relatif bergeser per sel sehingga dropdown di baris 2 membaca range yang berbeda dari baris 3.

`ErrorStyle` menentukan perlakuan atas entri yang ditolak: `Stop` menolaknya (default, dan biasanya
itu yang diinginkan sebuah template), `Warning` dan `Information` tetap meloloskannya.

## Proteksi sheet

Proteksi sheet **bukan keamanan**. Passwordnya adalah hash 16-bit yang bisa dilepas alat apa pun
dalam hitungan milidetik, dan isinya tetap terbaca. Fungsinya mencegah orang menimpa kolom formula
karena tidak sengaja — masalah yang nyata dan sering terjadi — dan hanya itu.

```csharp
sheet.Protect(editable: "B2:B200");
```

Interaksi yang sering menjebak: proteksi hanya berlaku pada sel yang style-nya `locked`, dan
**setiap sel terkunci secara default**. Memproteksi sheet tanpa membuka kunci sel isian membekukan
seluruh sheet — karena itu `Protect` menerima range yang boleh diisi dan membuka kuncinya untuk
Anda.

Flag-nya menyatakan apa yang masih *diizinkan*:

```csharp
sheet.Protection = new SheetProtection
{
    PasswordHash = SheetProtection.WithPassword("rahasia").PasswordHash,
    Sort = true,
    AutoFilter = true,
    FormatCells = true,
};
```

Di dalam file semuanya terbalik — `formatCells="0"` berarti memformat *diizinkan* — dan default
skemanya tidak seragam: sebagian besar flag default-nya melarang, tetapi `selectLockedCells` dan
`selectUnlockedCells` default-nya mengizinkan. ExcelNet menulis semuanya secara eksplisit dan
membalik masing-masing tepat satu kali, baik saat menulis maupun saat membaca kembali.

`Unprotect()` menghapus elemennya. `workbook.Protection` melakukan hal yang sama untuk struktur
workbook — sheet mana yang boleh ditambah, dihapus, diganti nama, atau diurutkan ulang.

## Streaming ekspor besar

`Workbook` mengurai berkas menjadi model lalu menulis modelnya kembali. Itulah yang membuat membaca
sebuah sel menjadi O(1), dan yang membuat ekspor sejuta baris mustahil. `StreamingWorkbook` menulis
langsung ke dalam paket — satu baris diserialisasi lalu dilupakan:

```csharp
using ExcelNet.Streaming;

using var workbook = StreamingWorkbook.Create("besar.xlsx", "Data");
var sheet = workbook.Sheet("Data");

sheet.WriteHeader("Id", "Nama", "Wilayah", "Jumlah", "Tanggal");

foreach (var record in source)
{
    sheet.WriteRow(record.Id, record.Name, record.Region, record.Amount, record.Date);
}
```

Sejuta baris memakan sekitar empat detik dan 34 MB working set, berapa pun jumlah barisnya — yang
memakai memori adalah buffer-nya, bukan datanya.

Konsekuensinya: penulis ini **hanya menulis dan hanya maju**. Tidak bisa membaca sel kembali, tidak
bisa kembali ke baris sebelumnya, dan tidak mendukung apa pun yang perlu tahu keseluruhan sheet:
tidak ada evaluasi formula, chart, pivot, format bersyarat, atau autofit. Pakai `Workbook` bila
salah satunya dibutuhkan. Keduanya alat yang berbeda karena pekerjaannya memang berbeda.

Dua hal yang perlu diketahui sebelum mulai:

**Nama sheet ditetapkan saat pembuatan.** `[Content_Types].xml` harus menjadi entri pertama di dalam
ZIP dan menyebut setiap part di dalam berkas, jadi daftar sheet-nya harus sudah diketahui sebelum
byte pertama dari sheet pertama ditulis. `Sheet(name)` membukanya; membuka yang kedua menutup yang
pertama, dan sebuah sheet tidak bisa dibuka ulang.

**String ditulis inline, bukan dibagi.** Tabel shared string harus lengkap sebelum bisa ditulis,
artinya menahan setiap string unik di memori — persis hal yang dihindari kelas ini. Gantinya, string
inline membuat berkasnya lebih besar, dan terasa pada data yang banyak berulang. Excel membaca
keduanya.

Nilai ditentukan dari objeknya: `string`, `bool`, tipe-tipe numerik, `DateTime`, `DateOnly`, dan
`DateTimeOffset` masing-masing menulis tipe sel yang tepat, dan `null` menulis sel *kosong*, bukan
string kosong — bedanya antara lubang di data dan nilai yang memang kosong, serta antara `COUNT` dan
`COUNTA` sepakat dengan sumbernya atau tidak. Selain itu ditulis sebagai `ToString()`-nya.

Tersedia empat indeks style, dan `WriteRow(values, styles)` menerapkannya per sel:
`StreamingSheet.GeneralStyle`, `.HeaderStyle` (tebal), `.DateStyle`, dan `.DateTimeStyle`.
`SkipRows(n)` meninggalkan celah.

## CSV, JSON, dan SQL

```csharp
using ExcelNet.Io;

CsvIo.Import(workbook, "penjualan.csv");
CsvIo.Export(sheet, "penjualan.csv");
CsvIo.Export(sheet, "penjualan.csv", CsvOptions.Indonesian);   // pemisah ;, desimal koma

JsonIo.Export(sheet, "penjualan.json");
JsonIo.Import(workbook, json, "Impor");

using var connection = new SqliteConnection("Data Source=data.db");
SqlIo.Import(workbook, connection, "SELECT * FROM penjualan", "Penjualan");
SqlIo.Export(sheet, connection, "penjualan");
```

`CsvOptions.Indonesian` lebih penting daripada kelihatannya: locale yang memakai `,` sebagai pemisah
desimal harus memakai `;` sebagai pemisah kolom, dan salah di sini mengubah setiap angka menjadi dua
kolom.

## DataFrame

ExcelNet tidak menulis ulang pandas. Ia menjembatani ke `GraviFrame` dari
[Gravicode.Science](https://github.com/DotNetVibeCoderz/Vibe_ML), yang memang library analisisnya.

```csharp
using ExcelNet.DataFrames;

var frame = sheet.ToDataFrame();               // baris pertama sebagai header
var stats = sheet.Describe();                  // count, mean, std, min, kuartil, max

workbook.WriteDataFrame(frame, "Hasil");
```

Seluruh permukaan integrasinya adalah `DataFrames/DataFrameBridge.cs`. Analisis adalah urusan
GraviFrame; membaca dan menulis `.xlsx` adalah urusan di sini.

## Ekspor PDF

```csharp
workbook.SaveAsPdf("laporan.pdf", new ExcelPdfOptions
{
    PageSize = PageSize.A4.Landscape(),
    SheetNames = ["Penjualan"],
    ShowGridLines = true,
    RepeatHeaderRow = true,     // baris header diulang di puncak tiap halaman
    Recalculate = true,         // supaya hasil cache-nya mutakhir
});
```

Nilai dirender sebagaimana format angkanya menampilkannya, jadi kolom Rupiah diekspor sebagai Rupiah,
bukan sebagai double mentah. Chart dan format bersyarat tidak digambar.

## Membaca workbook

```csharp
using var workbook = Workbook.Open("data.xlsx");

foreach (var sheet in workbook)
{
    Console.WriteLine($"{sheet.Name}: {sheet.RowCount} x {sheet.ColumnCount}");

    foreach (var cell in sheet.UsedCells)
    {
        Console.WriteLine($"  {cell.Address} = {cell.Text}");
    }
}
```

`UsedCells` hanya menelusuri sel yang benar-benar ada. Sheet dengan satu nilai di `ZZ10000` punya
satu sel terpakai, bukan sepuluh juta.

## Kesalahan yang sering terjadi

**Total tampil nol di mana-mana kecuali di Excel.** `Recalculate()` tidak dipanggil sebelum menyimpan.

**Tanggal tampil sebagai 45678.** Selnya punya nilai tetapi tidak punya format angka tanggal.
`Set(DateTime)` memasangkannya; `Set(45678.0)` mentah tidak.

**Style diabaikan.** Pastikan hasilnya Anda tampung — `CellStyle` immutable, jadi `style.Bold();`
sendirian tidak melakukan apa-apa. Gunakan `sheet["A1"].WithStyle(style.Bold())`.

**Angka terpecah jadi dua kolom setelah round trip CSV.** Pemisah dan desimalnya tidak cocok; pakai
`CsvOptions.Indonesian` atau tetapkan pemisahnya secara eksplisit.

## Lihat juga

- [Konsep inti](Core.md) — warna, satuan, dan paket di baliknya
- [PdfNet](PdfNet.md) — apa yang bisa dilakukan dengan PDF hasil ekspor
- `notebooks/ExcelNet.ipynb` — materi yang sama, bisa dijalankan
