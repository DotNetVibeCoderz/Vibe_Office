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
