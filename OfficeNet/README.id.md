# OfficeNet

[English](README.md)

Word, Excel, PowerPoint, dan PDF untuk .NET 10 — tanpa instalasi Office, tanpa dependensi native,
hasil identik di Windows, Linux, dan macOS.

OfficeNet adalah penulisan ulang ekosistem dokumen Python. Setiap library mencerminkan satu
library aslinya dan mempertahankan bentuknya, jadi kalau Anda sudah paham versi Python-nya, Anda
sudah paham API-nya.

| Paket | Penulisan ulang dari | Fungsinya |
|---|---|---|
| `Gravicode.OfficeNet.WordNet` | python-docx | `.docx` — paragraf, style, tabel, gambar, section, header, mail merge |
| `Gravicode.OfficeNet.ExcelNet` | openpyxl + pandas | `.xlsx` — sel, formula, style, CSV/JSON/SQL, DataFrame |
| `Gravicode.OfficeNet.PowerPointNet` | python-pptx + PptxGenJS | `.pptx` — slide, layout, tema, tabel, chart, media, **HTML → slide** |
| `Gravicode.OfficeNet.PdfNet` | PyPDF2 | `.pdf` — gabung, pecah, rotasi, ekstraksi, form, enkripsi, anotasi, gambar |
| `Gravicode.OfficeNet.Core` | — | Kontainer OPC dan satuan yang dipakai bersama |
| `Gravicode.OfficeNet` | — | Paket meta yang menarik semuanya |

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*

---

## Pemasangan

```bash
dotnet add package Gravicode.OfficeNet          # semuanya
dotnet add package Gravicode.OfficeNet.WordNet  # atau hanya yang Anda perlukan
```

## Contoh singkat

### Word

```csharp
using WordNet;
using OfficeNet.Core;

using var doc = WordDocument.Create();
doc.Properties.Title = "Laporan Tahunan";

doc.Section.SetPageSize("F4");        // ukuran kertas kantor Indonesia
doc.Section.SetMargins(Units.Cm(2.5));

doc.AddHeading("Laporan Tahunan", 0);
doc.AddParagraph("Disusun oleh Gravicode Studios.", "Subtitle");

doc.AddHeading("Ringkasan", 1);
var p = doc.AddParagraph();
p.Alignment = ParagraphAlignment.Justify;
p.AddRun("Pendapatan tumbuh ");
p.AddRun("32%", bold: true);
p.AddRun(" dibanding tahun lalu.");

doc.AddList(["Word", "Excel", "PowerPoint", "PDF"]);

doc.AddTable(new[]
{
    new[] { "Wilayah", "Pendapatan" },
    new[] { "Jakarta", "1.250" },
    new[] { "Bandung", "980" },
});

doc.Save("laporan.docx");
doc.SaveAsPdf("laporan.pdf");
```

### Excel

```csharp
using ExcelNet;
using ExcelNet.Styles;

using var wb = Workbook.Create("Penjualan");
var sheet = wb["Penjualan"];

sheet.WriteHeader("A1", ["Tanggal", "Produk", "Qty", "Harga", "Total"]);

sheet["A2"].Set(new DateTime(2026, 1, 15));
sheet["B2"].Set("WordNet");
sheet["C2"].Set(12);
sheet["D2"].Set(85_000.0).WithNumberFormat(NumberFormats.Rupiah);
sheet["E2"].SetFormula("C2*D2").WithNumberFormat(NumberFormats.Rupiah);

wb.Recalculate();                 // mengisi hasil cache yang biasanya dihitung Excel saat dibuka

Console.WriteLine(sheet["E2"].Number);   // 1020000

sheet.AddColorScale("C2:C100", OfficeColor.Red, OfficeColor.Green);
sheet.AutoFitColumns();

wb.Save("penjualan.xlsx");
wb.SaveAsPdf("penjualan.pdf");
```

**Impor CSV dengan format Indonesia** — pemisah titik koma, koma desimal:

```csharp
using ExcelNet.Io;

CsvIo.Import(wb, "data.csv", "Impor", CsvOptions.Indonesian);
```

Kode dengan angka nol di depan (`007`) dan nomor telepon (`+62812…`) tetap disimpan sebagai teks —
mengubahnya jadi angka adalah kehilangan data yang tidak bisa dibatalkan.

**DataFrame.** ExcelNet tidak menulis ulang pandas — ia menjembatani ke
[GraviFrame](https://github.com/DotNetVibeCoderz/Vibe_ML/tree/main/GravicodeScience), analog pandas
yang sudah dibuat Gravicode Studios:

```csharp
using ExcelNet.DataFrames;

var frame = sheet.ToDataFrame();
var perWilayah = frame.GroupBy("Wilayah").Sum("Total");
wb.WriteDataFrame(perWilayah, "Ringkasan");
```

### PowerPoint

```csharp
using PowerPointNet;
using PowerPointNet.Charts;

using var deck = Presentation.Create();          // 16:9, master + 6 layout + tema

deck.AddTitleSlide("OfficeNet", "Empat library, satu API");
deck.AddBulletSlide("Komponen", ["WordNet", "ExcelNet", "PowerPointNet", "PdfNet"]);

var slide = deck.AddSlide(2);
slide.SetTitle("Pendapatan per wilayah");
slide.AddChart(new ChartData
{
    Type = ChartType.Column,
    Categories = ["Jakarta", "Bandung", "Surabaya"],
    Series = [new ChartSeries("2026", [150, 110, 105])],
    ValueFormat = "#,##0",
    ShowDataLabels = true,
});

deck.Save("deck.pptx");
deck.SaveAsPdf("deck.pdf");
```

**HTML → slide**, fitur yang di PptxGenJS disebut `html2ppt`:

```csharp
using PowerPointNet.Html;

using var deck = HtmlToSlides.CreatePresentation(html, new HtmlSlideOptions
{
    TitleSlide = "Tinjauan Kuartal 1",
    SplitOnHeadingLevel = 2,     // h1 dan h2 memulai slide baru
});
```

Heading menjadi judul slide, konten di bawahnya menjadi isi, dan yang tidak muat dipaginasi ke
slide lanjutan. Format inline — tebal, miring, garis bawah, warna, font, ukuran, tautan —
dipertahankan per run. `TableToSlides` memecah satu tabel besar ke sebanyak mungkin slide yang
dibutuhkan, dengan baris header diulang di tiap halaman.

### PDF

```csharp
using PdfNet.Document;
using PdfNet.Content;

// Gabung, pecah, rotasi
using var merged = PdfDocument.ConcatFiles(["a.pdf", "b.pdf"]);
merged.Pages.RotateAll(90);
merged.Save("gabungan.pdf");

// Ekstraksi
using var sumber = PdfDocument.Open("pindaian.pdf");
Console.WriteLine(sumber.ExtractText());
foreach (var gambar in sumber.Pages[0].ExtractImages())
    gambar.SaveTo("keluaran");

// Menggambar
using var doc = PdfDocument.Create();
var page = doc.Pages.Add(PageSize.A4);
using (var canvas = page.OpenCanvas())
{
    canvas.TopDown = true;
    canvas.SetFont(StandardFont.HelveticaBold, 22);
    canvas.DrawText("Faktur", 50, 60);
}

// Enkripsi — AES-256 secara bawaan
doc.Encrypt("kata-sandi-pengguna", "kata-sandi-pemilik", PdfPermissions.Print);
doc.Save("faktur.pdf");
```

---

## Catatan desain

**PdfNet adalah daun.** Word, Excel, dan PowerPoint semuanya mengekspor *melalui* PdfNet, jadi
ekspor PDF tidak butuh dependensi tambahan dan berperilaku sama dari ketiganya.

**Semuanya satu kontainer OPC.** `.docx`, `.xlsx`, dan `.pptx` hanya berbeda pada bagian apa yang
ada di dalamnya; `OfficeNet.Core` mengimplementasikan kontainer itu sekali dan ketiganya mewarisi
packaging, relasi, satuan, warna, deteksi gambar, dan metadata.

**Bagian yang tidak dimodelkan tetap utuh.** Buka file, ubah satu paragraf, simpan: makro, custom
XML, pivot cache, dan ekstensi vendor semuanya kembali persis byte demi byte.

**Formatting bersifat tiga-nilai.** `null` berarti mewarisi, `false` berarti dimatikan secara
eksplisit. Itulah yang memungkinkan Anda menulis satu kata tidak tebal di dalam heading tebal —
perbedaan yang tidak bisa diungkapkan oleh `bool` biasa.

**Diverifikasi oleh pembaca independen.** `.docx` yang ditulis library ini lalu dibaca library ini
sendiri tidak membuktikan apa pun tentang apakah Word bisa membukanya. Karena itu setiap format
diperiksa oleh validator yang tidak berbagi kode dengan library: cakupan content type, resolusi
relasi, urutan anak sesuai skema, dan invarian khusus format yang ditegakkan Office.

## Membangun

```bash
dotnet build OfficeNet.sln -c Release
dotnet test
```

310 tes, tanpa warning.

## Yang belum diimplementasikan

Kekurangan yang jujur, dilacak di [Progress.md](Progress.md):

- **WordNet** — footnote, endnote, komentar, text box
- **ExcelNet** — chart, pivot table, data validation, proteksi sheet
- **PowerPointNet** — SmartArt, ekspor image/video
- **PdfNet** — rasterisasi (PDF → gambar), konversi PDF → Word/Excel
- Ekspor Word→PDF menangani layout mengalir, bukan objek mengambang, footnote, atau hifenasi

## Lisensi

MIT. *Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
