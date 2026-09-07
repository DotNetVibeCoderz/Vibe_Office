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
