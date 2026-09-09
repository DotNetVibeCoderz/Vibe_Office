# WordNet

Dokumen `.docx` untuk .NET 10 — python-docx kalau ia ditulis dalam C#.

*English: [docs/WordNet.md](../WordNet.md)* · [Kembali ke indeks](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.WordNet
```

---

![Dokumen Word hasil kode di halaman ini, dirender ke PNG](../screenshots/wordnet-document.png)

*Semua elemen di halaman itu — style heading, header dan footer, field nomor halaman, header tabel
berwarna, paragraf rata kiri-kanan — berasal dari kode di panduan ini. Gambarnya dirender dari PDF
hasil ekspor oleh [`OfficeNet.Rendering`](Rendering.md).*

---

## Membuka dan menyimpan

```csharp
using WordNet;

using var document = WordDocument.Create();           // dokumen baru, kosong
using var existing = WordDocument.Open("masukan.docx"); // dari path, Stream, atau byte[]
using var fromTemplate = WordDocument.FromTemplate("template.dotx");

document.Save("keluaran.docx");
document.SaveAsPdf("keluaran.pdf");
```

`WordDocument` menyunting pohon XML secara langsung. Dua handle ke paragraf yang sama tidak mungkin
berbeda isinya, dan bagian yang tidak dimodelkan library ini — custom XML part, makro, content
control yang tidak lazim — bertahan utuh byte demi byte setelah round trip. Ini perbedaan yang
disengaja dari ExcelNet; lihat [Konsep inti](Core.md#dua-model-yang-disengaja).

## Paragraf dan run

Paragraf adalah urutan run; run adalah potongan teks dengan satu set format. Itu memang model
WordprocessingML, dan library ini tidak menyembunyikannya — karena menyembunyikannya justru membuat
"kenapa setengah kalimat saya jadi tebal" mustahil dijawab.

```csharp
var paragraph = document.AddParagraph();
paragraph.Alignment = ParagraphAlignment.Justify;

paragraph.AddRun("Pendapatan tumbuh ");
paragraph.AddRun("32%", bold: true);
paragraph.AddRun(" dibanding tahun sebelumnya.");
```

Format bersifat **tiga keadaan**: `null` berarti mewarisi, `false` berarti dimatikan secara eksplisit.

```csharp
run.Format.Bold = true;    // tebal
run.Format.Bold = false;   // tidak tebal, bahkan di dalam style yang tebal
run.Format.Bold = null;    // ikut apa kata style
```

Perbedaan itu satu-satunya cara menulis kata yang tidak tebal di dalam heading yang tebal — itulah
sebabnya propertinya `bool?` dan bukan `bool`.

```csharp
run.Format.Italic = true;
run.Format.Underline = UnderlineStyle.Single;
run.Format.FontSize = Units.Pt(14);
run.Format.Color = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
run.Format.FontName = "Calibri";
run.Format.Highlight = "yellow";
```

## Heading, daftar, dan pemisah halaman

```csharp
document.AddHeading("Laporan Tahunan", 0);   // 0 berarti Title
document.AddHeading("Ringkasan", 1);

document.AddList([
    "WordNet — penulisan ulang python-docx",
    "ExcelNet — penulisan ulang openpyxl dan pandas",
]);

document.AddList(["Pertama", "Kedua"], numbered: true);
document.AddList(["Sub-butir"], level: 1);

document.AddPageBreak();
document.AddTableOfContents(levels: 3);
```

Daftar isi adalah *field* Word. Ia ditulis dengan benar, tetapi menampilkan "Update this field"
sampai pembacanya menyegarkan — Word-lah yang menghitung entrinya, bukan berkasnya. Semua generator
berperilaku begini, termasuk Word sendiri.

## Style

```csharp
document.AddParagraph("Kutipan.", "Quote");
document.AddParagraph("Sub-judul.", "Subtitle");

var style = document.Styles.GetOrAdd("Catatan", "Catatan", StyleType.Paragraph, basedOn: "Normal");
style.RunFormat.FontSize = Units.Pt(9);
style.RunFormat.Italic = true;
document.AddParagraph("Catatan kaki.", "Catatan");
```

`document.Styles` memaparkan style yang benar-benar dimiliki dokumen, termasuk yang diwarisi dari
template. `GetOrAdd` mengembalikan style yang sudah ada bila ada, jadi memanggilnya di setiap iterasi
pekerjaan batch itu aman; `Add` selalu membuat baru. Sebuah style membawa `RunFormat` (properti
karakter) sekaligus `ParagraphFormat` (spasi, perataan, indentasi).

## Tabel

```csharp
var table = document.AddTable(new[]
{
    new[] { "Wilayah", "2025", "2026", "Pertumbuhan" },
    new[] { "Jakarta", "1.120", "1.480", "+32%" },
    new[] { "Bandung", "860", "1.150", "+34%" },
});

table.Rows[0].SetShading(OfficeColor.FromRgb(0x1F, 0x38, 0x64));

foreach (var cell in table.Rows[0].Cells)
{
    foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
    {
        run.Format.Color = OfficeColor.White;
    }
}
```

Atau bangun sel per sel:

```csharp
var grid = document.AddTable(rows: 3, columns: 2);
grid[0, 0].Text = "Nama";
grid[0, 1].Text = "Nilai";
grid.SetColumnWidth(0, Units.Cm(4));
grid.MergeCells(firstRow: 1, firstColumn: 0, lastRow: 1, lastColumn: 1);
```

`SetColumnWidth` sekaligus mengalihkan tabel ke layout tetap. Tanpa itu Word menghitung ulang setiap
kolom dari isinya dan lebar yang Anda set diabaikan — detail yang memakan waktu satu sore kalau Anda
menemukannya dengan cara yang sulit.

`table[r, c]` bersifat **O(baris)**: LINQ to XML menyimpan anak sebagai linked list, jadi mencapai
baris ke-900 berarti melewati 899 baris sebelumnya, dan mengisi seluruh tabel dengan cara itu
menjadi kuadratik. Untuk tabel besar, telusuri sekali saja:

```csharp
foreach (var row in table.Rows)
{
    var cells = row.Cells;
    for (var c = 0; c < cells.Count; c++) cells[c].Text = values[c];
}
```

Pada tabel 2000 baris: 36 ms, dibanding 236 ms lewat indexer.

## Section, header, dan footer

```csharp
var section = document.Section;         // section terakhir
section.SetPageSize("A4");              // atau Letter, Legal, A3, A5
section.SetMargins(Units.Cm(2.2));
section.Orientation = PageOrientation.Landscape;

section.GetHeader().AddParagraph("Gravicode Studios").Alignment = ParagraphAlignment.Right;

var footer = section.GetFooter().AddParagraph();
footer.Alignment = ParagraphAlignment.Center;
footer.AddRun("Halaman ");
footer.AddPageNumber();
footer.AddRun(" dari ");
footer.AddPageCount();

document.AddSection(SectionStart.NextPage);   // section baru mulai di sini
```

Nomor halaman adalah field, dan hasil cache field pada dokumen yang dibuat program selalu basi — ia
menulis "1" di setiap halaman. Pengekspor PDF mengganti field dengan penanda lalu mengisi nomor
sebenarnya per halaman, jadi `SaveAsPdf` menghasilkan penomoran yang benar meski berkas `.docx`-nya
sendiri masih membawa cache basi sampai Word menyegarkannya.

## Footnote, endnote, dan komentar

```csharp
var paragraph = document.AddParagraph("Pendapatan tumbuh 32% pada 2026.");

var note = paragraph.AddFootnote("Sumber: laporan internal, Januari 2026.");
note.AddParagraph("Angka telah diaudit.");          // catatan berisi paragraf, bukan string

paragraph.AddEndnote("Lihat lampiran B.");
paragraph.AddComment("Tolong konfirmasi angkanya.", "Kang Fadhil");
```

Membaca dan menghapusnya:

```csharp
foreach (var note in document.Footnotes.All)
{
    Console.WriteLine($"{note.Id}: {note.Text}");
}

document.Footnotes.Remove(id);                       // sekaligus menghapus rujukannya di body
document.Comments.Remove(id);                        // beserta marker rentangnya

foreach (var comment in document.Comments.ByAuthor("Kang Fadhil")) { /* … */ }
```

Komentar bisa membungkus satu run saja, bukan seluruh paragraf:

```csharp
var run = paragraph.AddRun("angka ini");
run.AddComment("Dari mana asalnya?", "Kang Fadhil");
```

Tiga hal yang perlu diketahui:

- **Part-nya dibuat saat pertama dipakai.** Dokumen tanpa catatan tidak membawa `footnotes.xml`,
  sama seperti yang dilakukan Word.
- **Id 0 dan 1 dicadangkan.** Keduanya memuat garis pemisah di atas catatan dan pemisah lanjutan;
  `Footnotes.All` menyaringnya, dan menghapus salah satunya melempar exception.
- **Menghapus catatan menghapus rujukannya.** Rujukan yang menunjuk catatan terhapus persis yang
  dilaporkan Word sebagai konten tak terbaca, jadi keduanya berjalan bersama.

**Pada ekspor PDF**, footnote digambar di kaki halaman tempat rujukannya berada, di bawah garis
pendek, dengan rujukannya sendiri sebagai angka superskrip. **Endnote tidak diekspor** — tempatnya
di blok setelah halaman terakhir, dan itu pekerjaan tersendiri; menggambarnya sebagai footnote akan
menaruhnya di tempat yang salah. **Komentar juga tidak diekspor**: ia metadata tinjauan, bukan isi,
dan Word pun tidak mencetaknya secara baku.

## Mencari dan mengganti teks

```csharp
document.ReplaceText("2025", "2026");

// Word memecah teks antar-run di titik yang sembarang — sekadar pemeriksaan ejaan bisa
// menyebabkannya — sehingga frasa yang Anda lihat sering tidak berada dalam satu run pun.
document.ReplaceTextAcrossRuns("Kang Fadhil", "K. Fadhil");

document.MailMerge(new Dictionary<string, string>
{
    ["nama"] = "Budi",
    ["kota"] = "Bandung",
});   // mengganti {{nama}} dan {{kota}}
```

`ReplaceText` bekerja per run dan cepat; `ReplaceTextAcrossRuns` menggabungkan paragraf, mengganti,
lalu membagikan teksnya kembali sambil mempertahankan format tiap run. Pakai yang kedua kalau
penggantian "entah kenapa tidak terjadi apa-apa".

## Gambar

```csharp
document.AddPicture("logo.png", width: Units.Cm(4));
document.AddPicture(bytes, width: Units.Cm(6), height: Units.Cm(3));
```

Rasio aspek dipertahankan bila hanya satu dimensi diberikan. PNG, JPEG, GIF, BMP, dan TIFF dikenali
dari isinya, bukan dari ekstensi berkas.

## Text box dan shape

Text box adalah persegi berisi kata-kata yang mengambang di atas halaman. Kutipan, keterangan
gambar, stempel, dan callout semuanya adalah itu.

```csharp
using WordNet.Drawing;

var quote = paragraph.AddTextBox(
    "Marjin kotor tetap di kisaran empat puluh persen.",
    width: Units.Cm(6), height: Units.Cm(3));

quote.FillColor = OfficeColor.FromRgb(0xF2, 0xEC, 0xE3);
quote.LineColor = OfficeColor.FromRgb(0x1F, 0x3A, 0x5F);
quote.MoveTo(Units.Cm(9), Units.Cm(0.5));
```

`AddShape` menerima bentuk sebagai gantinya: `Rectangle`, `RoundedRectangle`, `Ellipse`, `Triangle`,
`Diamond`, `Hexagon`, `Star`, `RightArrow`, `DownArrow`, `Callout`, dan `Line`. Properti `Geometry`
juga menerima nama preset DrawingML lainnya — ada sekitar 180 — sebagai string.

Setiap shape bisa berisi teks, jadi `AddParagraph` berlaku untuk semuanya:

```csharp
var banner = paragraph.AddShape(ShapeGeometry.RoundedRectangle,
    Units.Cm(15), Units.Cm(1.6), TextWrap.TopAndBottom);

banner.FillColor = OfficeColor.FromRgb(0x1F, 0x3A, 0x5F);
banner.LineColor = null;
banner.AddParagraph("Ringkasan Operasional").Runs[0].WithBold().WithColor(OfficeColor.White);
```

### Mengambang dan inline

Sebuah drawing adalah salah satu dari dua hal, dan keduanya elemen yang berbeda di dalam berkas:

| | Elemen | Perilaku |
| --- | --- | --- |
| **Inline** — `wrap: null` | `wp:inline` | ditata seolah-olah satu karakter yang sangat besar |
| **Mengambang** — wrap lainnya | `wp:anchor` | ditempatkan pada koordinatnya sendiri, teks mengalir mengelilinginya |

Hanya objek mengambang yang punya posisi, jadi `MoveTo` melempar exception pada objek inline.
`MoveTo` mengukur dari apa pun yang Anda sebut — `HorizontalAnchor.Column` (default) serta `.Page`,
`.Margin`, `.Character`; `VerticalAnchor.Paragraph` (default) serta `.Page`, `.Margin`, `.Line`.

Pilihan wrap-nya: `Square` (teks di kedua sisi), `Tight` (mengikuti garis luar), `TopAndBottom`
(teks berhenti di atas dan lanjut di bawah), `InFrontOfText`, dan `BehindText` — dua yang terakhir
saling mengabaikan, dan `BehindText` itulah watermark.

Gambar mengambang dengan cara yang sama, karena yang berbeda adalah wadahnya, bukan isinya:

```csharp
run.AddPicture("foto.jpg", width: Units.Cm(5), wrap: TextWrap.Square);
run.Drawings[0].MoveTo(Units.Cm(10), Units.Cm(0));
```

`document.Shapes` menemukan semua shape dan text box di body; `run.Drawings` menemukan yang ada di
satu run, termasuk gambar.

### Yang Word butuhkan, dan yang tidak akan diberitahukannya

`wp:anchor` membawa sepuluh atribut tanpa default skema. Menghilangkan satu saja tidak membuat tata
letaknya menurun — Word melaporkan seluruh dokumen tidak terbaca, tanpa menyebut yang mana. OfficeNet
menulis semuanya, dan itu alasan utama membangun shape lewat API ini alih-alih menulisnya sendiri.

Shape-nya sendiri berada di namespace `wps`, ekstensi Microsoft dan bukan ECMA: jawaban standar
sendiri adalah VML, yang sudah ditandai usang di rilis yang sama yang mengirimkannya. Word 2010 ke
atas dan LibreOffice membaca `wps` langsung. Word 2007 tidak, dan fallback untuknya berarti menulis
setiap shape dua kali di dalam `mc:AlternateContent`.

### Di ekspor PDF

Objek mengambang ikut diekspor: shape digambar sebagai jalur lengkap dengan isian dan garis
luarnya, teksnya ditata di dalamnya memakai inset bawaan Word dan dipotong pada batas kotak, dan
teks body mengalir mengelilingi ruang yang dipakainya.

Dua batasan yang perlu diketahui. `Tight` dan `Through` mengelilingi kotak pembatas, bukan garis
luar — mirip, tapi tidak persis. Dan satu baris dipecah mengelilingi satu objek, bukan beberapa,
sehingga teks di antara dua objek mengambang pergi ke sisi yang lebih lebar alih-alih mengisi kedua
celah.

## PDF → Word

```csharp
using WordNet.Import;

PdfToWord.Convert("laporan.pdf", "laporan.docx");

// Atau simpan dokumennya untuk diolah lagi:
using var document = PdfToWord.Convert("laporan.pdf");
```

Yang kembali: paragraf, heading pada level yang benar, dan tabel. Baris yang membungkus disatukan
lagi — pemisah baris di PDF adalah tempat teksnya kehabisan kolom, bukan tempat penulisnya menekan
enter — dan level heading ditentukan dari ukuran hurufnya, yang terbesar jadi `Heading1` dan ukuran
di bawahnya jadi `Heading2`.

```csharp
PdfToWord.Convert("laporan.pdf", "laporan.docx", new PdfImportOptions
{
    Pages = 1..5,               // berbasis nol; null mengambil semuanya
    KeepPageBreaks = false,     // satu aliran menerus sebagai gantinya
    ConvertTables = false,      // tabel jadi paragraf, kalau yang dicari hanya teksnya
    Structure = new StructureOptions { MinimumTableRows = 4 },
});
```

### Ini rekonstruksi, bukan konversi

PDF tidak punya paragraf dan tidak punya tabel. Yang ada hanyalah instruksi menaruh glyph pada
koordinat, dan semua di atas adalah kesimpulan dari tempat glyph itu mendarat: baris dari baseline
yang sama, paragraf dari jarak vertikal, tabel dari kolom yang sejajar, heading dari huruf yang lebih
besar daripada badan teksnya.

Kegagalannya disengaja agar terlihat, bukan tersembunyi. Struktur yang luput dari heuristiknya
kembali sebagai paragraf; **tidak ada teks yang pernah hilang**. Yang benar-benar hilang: warna, font
selain tebal, gambar, posisi persis, urutan baca multi-kolom, dan apa pun yang digambar sebagai jalur
alih-alih ditulis sebagai teks. Halaman hasil pindaian tidak punya lapisan teks sama sekali, jadi
hasilnya dokumen kosong — membacanya butuh OCR, dan itu alat yang berbeda.

Gunanya adalah mengembalikan teks ke bentuk yang bisa diedit. Ini bukan round trip, dan dokumen yang
awalnya berasal dari Word tidak akan kembali seperti aslinya.

## Membaca dokumen

```csharp
using var document = WordDocument.Open("laporan.docx");

Console.WriteLine(document.ExtractText());
Console.WriteLine($"{document.WordCount} kata");

foreach (var paragraph in document.Paragraphs)
{
    Console.WriteLine($"[{paragraph.StyleId}] {paragraph.Text}");
}

foreach (var table in document.Tables)
{
    foreach (var row in table.Rows)
    {
        Console.WriteLine(string.Join(" | ", row.Cells.Select(c => c.Text)));
    }
}
```

`Paragraphs` adalah tingkat teratas body. `AllParagraphs` juga masuk ke tabel, header, dan footer —
itu yang Anda inginkan untuk menghitung kata, dan bukan yang Anda inginkan untuk "kerangka dokumen".

## Metadata

```csharp
document.Properties.Title = "Laporan Tahunan";
document.Properties.Creator = "Gravicode Studios";
document.Properties.Keywords = "laporan; 2026";
document.Properties.Category = "Internal";
```

## Ekspor PDF

```csharp
document.SaveAsPdf("laporan.pdf");

document.SaveAsPdf("laporan.pdf", new PdfExportOptions
{
    Watermark = "DRAF",
    WatermarkOpacity = 0.12,
    IncludeHeadersAndFooters = true,
    EvaluatePageFields = true,
});

using var pdf = document.ToPdf();   // PdfDocument yang bisa digabung, dienkripsi, atau dianotasi
```

Pengekspor menyelesaikan pewarisan style sebelum menata apa pun. Paragraf Heading 1 sama sekali tidak
membawa format langsung — konverter yang hanya membaca `w:rPr` milik run merender seluruh dokumen
sebagai teks biasa, dan itu cara paling umum pengekspor buatan sendiri gagal.

**Yang dilakukan:** teks mengalir dengan pemenggalan baris dan perataan yang benar, heading, style,
daftar bernomor dan berbutir, tabel dengan warna dan garis, gambar inline, header dan footer, field
halaman, watermark, ukuran halaman dan margin, section.

**Yang tidak:** objek mengambang dengan `wrap="square"` (digambar inline), catatan kaki, komentar,
text box, serta hifenasi dan kerning persis seperti Word. Lihat [Plan.md](../../Plan.md).

## Kesalahan yang sering terjadi

**Mengatur properti tapi tidak ada yang berubah.** Hampir selalu ada style yang menimpanya, atau
propertinya diset di paragraf padahal tempatnya di run. `run.Format` adalah format karakter;
`paragraph.Format` adalah format paragraf. Ukuran font adalah properti run walaupun Anda
menginginkannya untuk satu paragraf penuh — set di setiap run, atau buat style.

**Penggantian teks tidak terjadi.** Frasanya terpecah antar-run; pakai `ReplaceTextAcrossRuns`.

**Lebar kolom diabaikan.** Tabelnya masih layout autofit; `SetColumnWidth` mengalihkannya untuk Anda,
tetapi tabel yang Anda susun sendiri dari XML tidak.

**Sel tabel kosong membuat Word melaporkan berkas rusak.** Sebuah `w:tc` harus berisi setidaknya satu
elemen setingkat blok dan harus diakhiri paragraf. Library ini menjaganya; XML yang disunting tangan
harus menjaganya juga.

## Lihat juga

- [Konsep inti](Core.md) — satuan, warna, dan paket OPC di baliknya
- [PdfNet](PdfNet.md) — apa yang bisa dilakukan dengan PDF hasil ekspor
- [Rendering](Rendering.md) — mengubah dokumen menjadi PNG
- `notebooks/WordNet.ipynb` — materi yang sama, bisa dijalankan
