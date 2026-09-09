# PdfNet

PDF untuk .NET 10 — membaca, menulis, menggabung, memisah, mengekstrak, mengenkripsi, mengisi
formulir, menganotasi, dan menggambar. Yang dilakukan PyPDF2, plus sebuah kanvas.

*English: [docs/PdfNet.md](../PdfNet.md)* · [Kembali ke indeks](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.PdfNet
```

---

![PDF hasil ekspor dokumen Word, dirender ke PNG](../screenshots/pdfnet-export.png)

*Dokumen Word dari [panduan WordNet](WordNet.md), diekspor lewat PdfNet. Word, Excel, dan PowerPoint
semuanya mengekspor melalui library ini — ia adalah daun dari graf dependensi dan tidak pernah
bergantung balik kepada ketiganya.*

---

## Membuka dan menyimpan

```csharp
using PdfNet.Document;

using var document = PdfDocument.Create();
using var existing = PdfDocument.Open("masukan.pdf");
using var locked = PdfDocument.Open("terkunci.pdf", password: "rahasia");

document.Save("keluaran.pdf");
byte[] bytes = document.ToArray();

Console.WriteLine(document.WasEncrypted);   // berkasnya terenkripsi dan berhasil dibuka
Console.WriteLine(document.WasRepaired);    // xref-nya rusak dan dibangun ulang
```

`WasRepaired` layak diperiksa dalam sebuah pipeline. Berkas yang hanya bisa dibuka setelah
direkonstruksi adalah berkas yang produsernya punya bug, dan biasanya berkas itulah yang akan
merepotkan Anda nanti.

## Halaman

```csharp
var page = document.Pages.Add(PageSize.A4);
document.Pages.Add(PageSize.Letter);
document.Pages.Add(PageSize.Points(400, 600));

Console.WriteLine(document.Pages.Count);
var first = document.Pages[0];

first.Rotation = 90;                  // 0, 90, 180, 270
Console.WriteLine(first.Width);       // titik
Console.WriteLine(first.MediaBox);

document.Pages.RemoveAt(2);
document.Pages.Move(0, 3);
```

## Menggabung dan memisah

```csharp
using var a = PdfDocument.Open("bagian-1.pdf");
using var b = PdfDocument.Open("bagian-2.pdf");

a.Merge(b);                            // menambahkan seluruh halaman
a.MergeRange(b, startIndex: 2, count: 3);
a.Save("gabungan.pdf");

using var merged = PdfDocument.ConcatFiles(["a.pdf", "b.pdf", "c.pdf"]);

var singles = document.Split();        // satu dokumen per halaman
var chunks = document.Split(pagesPerPart: 10);
```

Penggabungan menulis ulang setiap nomor objek pada dokumen yang diimpor lalu membangun ulang
referensinya, sehingga dua berkas yang sama-sama menyebut objek font-nya `7 0 R` tidak bertabrakan.

## Ekstraksi teks

```csharp
Console.WriteLine(document.ExtractText());
Console.WriteLine(document.Pages[0].ExtractText());

foreach (var fragment in document.Pages[0].ExtractTextFragments())
{
    Console.WriteLine($"{fragment.Text} @ ({fragment.X}, {fragment.Y}) {fragment.FontSize}pt");
}
```

PDF tidak memuat teks dalam urutan baca — ia memuat instruksi menggambar. Mengekstrak berarti
memainkan ulang operator penempatan teks, mengelompokkan run menjadi baris berdasarkan garis
alasnya, dan *menyimpulkan spasi yang tidak pernah disimpan berkasnya* — sebab produser yang
menempatkan tiap kata dengan `Td` sama sekali tidak menulis karakter spasi. Itulah sebabnya dua
perkakas bisa berbeda hasil pada berkas yang sama.

Dua konsekuensi yang layak diketahui:

- **Teks tak terlihat tetap diekstrak.** Render mode 3 adalah cara pemindai menyimpan lapisan OCR-nya,
  dan melewatinya berarti kehilangan satu-satunya teks yang dimiliki halaman hasil pindaian.
- **Font komposit tanpa `/ToUnicode` memang tidak bisa didekode.** PdfNet tidak mengeluarkan apa pun
  alih-alih mengeluarkan kode mentah yang tampak seperti derau CJK. Tidak mengeluarkan apa-apa itu
  jujur; derau tidak.

## Gambar

```csharp
foreach (var image in document.Pages[0].ExtractImages())
{
    Console.WriteLine($"{image.Name}: {image.Width}x{image.Height} {image.Extension}");
    image.SaveTo("keluaran");
}
```

## Menggambar

```csharp
using PdfNet.Content;

var page = document.Pages.Add(PageSize.A4);
using var canvas = page.OpenCanvas();

canvas.TopDown = true;      // mengukur dari atas, seperti semua format Office

canvas.SetFont(StandardFont.HelveticaBold, 24);
canvas.SetFillColor(OfficeColor.FromRgb(0x1F, 0x38, 0x64));
canvas.DrawText("Laporan", 72, 72);

canvas.SetFont(StandardFont.Helvetica, 11);
canvas.SetFillColor(OfficeColor.Black);
canvas.DrawText("Rata kiri-kanan.", 72, 110, width: 450, TextAlignment.Justify);

canvas.SetStrokeColor(OfficeColor.Gray);
canvas.SetLineWidth(0.5);
canvas.MoveTo(72, 130).LineTo(522, 130).Stroke();

canvas.SetFillColor(OfficeColor.FromRgb(0xFF, 0xF2, 0xCC));
canvas.Rectangle(72, 150, 200, 60).Fill();
canvas.RoundedRectangle(72, 230, 200, 60, radius: 8).Stroke();

canvas.DrawImage(File.ReadAllBytes("logo.png"), 72, 320, 120, 60);

double width = canvas.MeasureText("Berapa lebarnya?");
```

`TopDown` diset oleh setiap pengekspor OfficeNet. Titik asal PDF ada di kiri-bawah sementara semua
format Office mengukur dari atas; melakukan konversi di tiap tempat pemanggilan alih-alih sekali di
sini persis cara sebuah konverter berakhir membalik sebagian elemen dan sebagian lagi tidak.

Ke-14 font standar tidak perlu disematkan dan metriknya sudah tertanam, sehingga `MeasureText` akurat
tanpa berkas font apa pun. Menyematkan font TrueType ada di [peta jalan](../../Plan.md).

## Menyematkan font

14 font baku hanya mencakup Latin-1 dan tidak lebih. Dokumen berbahasa Jawa, Arab, Thai, atau
Tionghoa — atau yang harus memakai font merek tertentu — perlu fontnya ikut di dalam berkas:

```csharp
using PdfNet.Fonts;

using var pdf = PdfDocument.Create();
var font = pdf.EmbedFont("NotoSans-Regular.ttf");

using var canvas = pdf.Pages.Add(PageSize.A4).OpenCanvas();
canvas.SetFont(font, 12);
canvas.DrawText("ꦲꦏ꧀ꦱꦫꦗꦮ", 72, 700);
```

Hanya glyph yang benar-benar digambar yang masuk ke berkas, dan hanya saat dokumennya disimpan.
Sembilan aksara dari font 22 MB menghasilkan **PDF 30 KB** — subset-nya sendiri 38 KB sebelum
dikompres, dan justru itulah intinya: font CJK punya lima puluh ribu glyph sementara satu dokumen
memakai seratus.

`SetFont` bisa kembali ke `StandardFont` kapan saja; keduanya bisa berbagi satu halaman.
`MeasureText` memakai font yang sedang dipilih.

### Sebelum menulis halamannya

```csharp
if (!font.CanRender(text))
{
    Console.WriteLine("tidak ada glyph untuk: " + string.Join(", ", font.MissingCharacters(text)));
}
```

Karakter yang tidak punya glyph digambar sebagai kotak kosong, dan mengetahuinya pada tahap itu
berarti cetak ulang. `TrueTypeFont.Load` juga melaporkan `EmbeddingRestricted`, yaitu flag `fsType`
dari penerbit fontnya sendiri — pustaka ini melaporkannya, bukan menegakkannya, karena lisensinya
adalah urusan antara Anda dan penerbit font.

### Apa yang ditulis, dan kenapa bentuknya begitu

Fontnya masuk sebagai font **komposit**: `/Type0` dengan encoding `/Identity-H` di atas turunan
`/CIDFontType2`. Itu satu-satunya susunan yang bisa melewati batas 256 karakter tanpa akrobat
encoding, dan itulah yang ditulis semua produsen modern.

Ada satu konsekuensi yang harus dibayar. Teksnya ditulis sebagai **id glyph** dua byte, sehingga
pembaca yang mengekstrak teks hanya melihat angka dan tidak tahu artinya — teksnya jadi tidak bisa
dicari dan tidak bisa disalin. CMap `/ToUnicode` yang ditulis bersamanya itulah yang memetakannya
kembali, jadi ia selalu ditulis dan tidak pernah opsional.

Dua keputusan lebih kecil yang perlu diketahui:

- **Glyph dinomori ulang** rapat mulai dari nol, dan `/CIDToGIDMap` yang menerjemahkannya.
  Mempertahankan id aslinya memang lebih sederhana, tetapi membuat `loca` dan `hmtx` sepanjang id
  tertinggi yang dipakai — dokumen dengan satu glyph CJK di id 40.000 akan membayar 320 KB untuk
  celahnya.
- **Glyph komposit membawa komponennya.** Huruf "é" biasanya adalah "e" plus aksen, dirujuk lewat id
  glyph. Subset yang tidak mengikuti rujukan itu menghasilkan font yang huruf beraksennya kosong,
  tanpa ada apa pun yang menjelaskan kenapa.

**Belum didukung:** font OpenType dengan outline PostScript (`.otf` bertabel `CFF `) dan koleksi
TrueType (`.ttc`). Keduanya ditolak dengan menyebutkan alasannya, bukan dimuat menjadi PDF tanpa
glyph sama sekali.

## Enkripsi

```csharp
document.Encrypt("rahasia");                       // kata sandi pengguna, AES-256

document.Encrypt(
    userPassword: "buka",
    ownerPassword: "pemilik",
    permissions: PdfPermissions.Print | PdfPermissions.Copy,
    cipher: PdfCipher.Aes256);

document.Decrypt();                                // melepasnya
```

Didukung: RC4 40/128-bit (revisi 2–4), AES-128 (revisi 4), dan AES-256 (revisi 6). Dekripsi menangani
semuanya; dokumen baru memakai AES-256 secara baku.

Dua hal yang membuat enkripsi rumit, keduanya sudah ditangani di sini dan layak diketahui kalau Anda
memeriksa berkas dengan tangan: objek di dalam object stream *tidak* dienkripsi satu per satu —
kontainernya sudah, sehingga mendekripsi dua kali menghasilkan sampah yang tetap bisa diurai — dan
XRef stream sama sekali tidak pernah dienkripsi.

## Formulir

```csharp
var form = AcroForm.Open(document);        // null bila berkasnya tidak punya formulir
var created = AcroForm.OpenOrCreate(document);

foreach (var field in form!.Fields)
{
    Console.WriteLine($"{field.FullName} ({field.FieldType}) = {field.Value}");
}

form["nama"]?.SetValue("Budi Santoso");
form["setuju"]?.SetValue("Yes");        // checkbox menerima salah satu on-state miliknya

form.Flatten();                          // menanam nilai ke halaman, menghapus field-nya
```

`Flatten` adalah yang Anda perlukan sebelum mengirim formulir terisi ke mana pun. *Nilai* sebuah
field dan *tampilannya* adalah dua hal terpisah di PDF, sehingga penampil yang tidak membuat ulang
tampilan akan menunjukkan kotak kosong di atas jawaban yang benar.

`field.OnStates` menyebutkan apa yang sebenarnya diterima sebuah checkbox — jarang sekadar `"Yes"`.

## Anotasi

```csharp
using PdfNet.Annotations;

page.AddTextNote(72, 700, "Perlu ditinjau.", author: "Kang Fadhil");
page.AddLink(new PdfRectangle(72, 680, 200, 696), "https://gravicode.com");
page.AddInternalLink(area, targetPage: document.Pages[4]);
page.AddHighlight(areas, OfficeColor.FromRgb(0xFF, 0xF0, 0x00));
page.AddUnderline(areas, OfficeColor.Blue);
page.AddStrikeOut(areas, OfficeColor.Red);
page.AddStamp(area, "DISETUJUI", OfficeColor.Green);
page.AddWatermark("DRAF", OfficeColor.Gray, opacity: 0.12);

var existing = page.GetAnnotations();
page.RemoveAnnotations(a => a.AnnotationType == PdfAnnotationType.Link);
page.ClearAnnotations();
```

Titik quad untuk sorotan berurutan kiri-atas, kanan-atas, kiri-bawah, kanan-bawah. Urutan searah
jarum jam — urutan yang terasa benar — menghasilkan bentuk dasi kupu-kupu.

## Metadata

```csharp
document.Info.Title = "Laporan Tahunan";
document.Info.Author = "Gravicode Studios";
document.Info.Subject = "Ringkasan 2026";
document.Info.Keywords = "laporan; 2026";
```

## Merender ke gambar

Rasterisasi hidup di paket terpisah supaya PdfNet sendiri bebas dependensi native:

```csharp
using OfficeNet.Rendering;

var png = DocumentRenderer.RenderPage(document.Pages[0], new RenderOptions { Dpi = 150 });
```

Lihat [panduan Rendering](Rendering.md).

## Ketahanan

PDF di dunia nyata sering cacat, dan PdfNet mengasumsikan demikian:

- `/Length` lazimnya adalah referensi tak langsung dan lazimnya salah, sehingga parser memastikan
  `endstream` memang menyusul sebelum mempercayainya.
- Tabel xref yang rusak atau hilang memicu pemindaian seluruh berkas untuk mencari penanda `obj`, dan
  `WasRepaired` memberi tahu bahwa itu terjadi.
- `/Predictor` pada stream Flate bukan opsional. Mengabaikannya tetap berhasil melakukan inflate
  tetapi menghasilkan byte yang sepenuhnya salah — pada xref stream itu berarti setiap offset objek
  menjadi sampah.

## Kesalahan yang sering terjadi

**Ekstraksi teks tidak menghasilkan apa-apa.** Halamannya hasil pindaian tanpa lapisan OCR. Memang
tidak ada teks di sana; periksa `ExtractImages`.

**Formulir terisi terlihat kosong.** Panggil `Flatten()`, atau buka di penampil yang membangun ulang
tampilan.

**Koordinat terbalik.** Set `canvas.TopDown = true`, atau ukur dari bawah.

**Dokumen hasil gabungan berukuran raksasa.** Font dan gambar belum dideduplikasi antar-berkas.

## Lihat juga

- [Rendering](Rendering.md) — halaman ke PNG
- [API terpadu](OfficeNet.md) — mengonversi format apa pun yang didukung ke PDF
- `notebooks/PdfNet.ipynb` — materi yang sama, bisa dijalankan
