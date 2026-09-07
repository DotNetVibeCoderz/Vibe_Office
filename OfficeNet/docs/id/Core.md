# Konsep inti

`OfficeNet.Core` adalah lapisan yang dipakai bersama keempat library format: kontainer OPC, satuan,
warna, pengenalan gambar, dan metadata. Baca sekali, dan library lainnya berhenti membuat Anda
terkejut.

*English: [docs/Core.md](../Core.md)* · [Kembali ke indeks](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

---

## Paket OPC

`.docx`, `.xlsx`, dan `.pptx` adalah kontainer yang sama dengan isi berbeda: arsip ZIP berisi bagian
XML, sebuah manifes `[Content_Types].xml` yang memberi tipe MIME pada setiap bagian, dan berkas
`.rels` yang menyatakan bagian mana menunjuk bagian mana. Kontainer itu benar-benar identik di
ketiga format, jadi ia diimplementasikan sekali di sini, bukan tiga kali.

```csharp
using OfficeNet.Core.Packaging;

using var package = OpcPackage.Open("laporan.docx");

foreach (var part in package.Parts)
{
    // A part exposes its bytes rather than a length: it may still be an in-memory XDocument
    // that has never been serialised, so there is no byte count to report until you ask.
    Console.WriteLine($"{part.Name} — {part.ContentType} ({part.GetBytes().Length} bytes)");
}

var document = package.MainDocumentPart;

Console.WriteLine(document!.Xml.Root!.Name);
```

Anda jarang membutuhkannya secara langsung — tetapi ketika sebuah berkas berperilaku aneh, membuka
paketnya dan mendaftar bagian-bagiannya adalah cara tercepat melihat sebabnya.

Dua aturan yang ditegakkan lapisan paket, karena melanggar salah satunya menghasilkan berkas yang
disebut Office sebagai rusak:

- **Setiap bagian harus punya tipe konten**, baik lewat default ekstensi maupun override eksplisit.
- **Target sebuah relationship harus dapat diselesaikan.** `r:id` yang menggantung bukan diabaikan;
  itu sebuah kesalahan.

## Satuan

Setiap panjang di OfficeNet adalah `Length`, disimpan dalam **EMU** — English Metric Units, 914.400
per inci dan 360.000 per sentimeter. Angka itu dipilih supaya inci, sentimeter, titik, dan twip
semuanya membagi habis, yang berarti konversi antar satuan tidak pernah menumpuk galat.

```csharp
using OfficeNet.Core;

var width = Units.Cm(2.5);
var margin = Units.Inches(1);
var size = Units.Pt(11);
var indent = Units.Twips(720);
var pixels = Units.Px(96);          // pada 96 DPI
var at300 = Length.FromPixels(96, dpi: 300);

double cm = width.Centimeters;
double points = width.Points;
long twips = width.Twips;
long emu = width.Emu;

var total = width + margin;
var half = width / 2;
```

Masalahnya, **format berkasnya menyimpan satuan yang lebih kasar daripada EMU**, sehingga round trip
membuat pembulatan:

| Properti | Disimpan sebagai |
|---|---|
| Ukuran halaman, margin, indentasi | twip (1/1440 inci) |
| Ukuran font (run) | setengah titik |
| Ukuran font (DrawingML) | seperseratus titik |
| Tebal garis | seperdelapan titik |
| Geometri gambar dan bentuk | EMU |

Jadi 1,5 cm menjadi 850 twip, yang terbaca kembali sebagai 1,4993 cm. Itu bukan bug dan tidak bisa
diperbaiki — memang begitulah formatnya menyimpan. Bandingkan panjang dengan toleransi satu twip,
bukan dengan kesamaan persis.

Satu jebakan yang layak disebut: `w:sz` berarti **setengah titik pada sebuah run** dan
**seperdelapan titik pada sebuah garis tepi**. Nama atribut sama, dua satuan berbeda.

## Warna

```csharp
using OfficeNet.Core.Drawing;

var navy = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
var alsoNavy = OfficeColor.FromRgb(0x1F3864);
var parsed = OfficeColor.Parse("1F3864");        // atau "#1F3864"

OfficeColor.TryParse(value, out var color);

var themed = OfficeColor.FromTheme(ThemeColor.Accent1);
var lighter = OfficeColor.FromTheme(ThemeColor.Accent1, luminanceModulation: 0.4);

OfficeColor.Black; OfficeColor.White; OfficeColor.Red;
OfficeColor.Green; OfficeColor.Blue; OfficeColor.Yellow; OfficeColor.Gray;

string hex = navy.ToHex();      // "1F3864"
```

`OfficeColor.Automatic` adalah nilai sungguhan, bukan pengganti null: artinya "biar konsumennya yang
memutuskan", yang untuk teks biasanya menjadi hitam di atas putih dan putih di atas latar gelap.
`ToHex()` mengembalikan `"auto"` untuknya, dan itulah yang diharapkan format-formatnya.

**Warna tema** mengikuti tema dokumen; RGB eksplisit tidak. Hanya itu perbedaannya, dan itulah
sebabnya menata ulang sebuah deck mengubah sebagian bentuk dan tidak mengubah sebagian lainnya.

## Metadata

Tiga kumpulan properti yang sama ada di ketiga format OOXML:

```csharp
document.Properties.Title = "Laporan Tahunan";
document.Properties.Creator = "Gravicode Studios";
document.Properties.Subject = "Ringkasan 2026";
document.Properties.Keywords = "laporan; 2026";
document.Properties.Category = "Internal";
document.Properties.Created = DateTime.UtcNow;

document.Custom["Departemen"] = "Riset";
document.Custom["Disetujui"] = true;
```

## Gambar

Format dikenali dari byte-nya, tidak pernah dari ekstensi berkas — `.png` yang sebenarnya JPEG cukup
sering ditemui sehingga ini penting:

```csharp
var info = ImageInfo.Read(bytes);
Console.WriteLine($"{info.Format} {info.PixelWidth}x{info.PixelHeight} @ {info.HorizontalDpi} DPI");
Console.WriteLine(info.NaturalWidth.Centimeters);
```

PNG, JPEG, GIF, BMP, dan TIFF dikenali, lengkap dengan dimensi piksel dan DPI bila formatnya
mencatatnya. DPI itulah yang mengubah piksel menjadi ukuran fisik, sehingga gambar yang disisipkan
"pada ukuran aslinya" mendarat dengan benar alih-alih pada tebakan 96 DPI yang sembarang.

## Dua model yang disengaja

WordNet dan ExcelNet dibangun berbeda, dan perbedaannya disengaja:

**WordNet menyunting pohon XML secara langsung.** Sebuah `Paragraph` memegang elemen `w:p`-nya
sendiri. Dua handle ke satu paragraf tidak mungkin berbeda isinya, dan bagian yang tidak dipahami
library — content control, makro, custom XML part — bertahan utuh byte demi byte setelah round trip.
Dokumen Word adalah pohon berisi simpul yang bermakna dan beragam, dan mempertahankan apa yang tidak
Anda urai itu penting.

**ExcelNet mengurai menjadi model lalu menuliskannya kembali.** XML worksheet hanyalah daftar baris
yang datar tanpa sesuatu yang perlu dipertahankan byte demi byte, sedangkan dictionary membuat akses
sel acak menjadi O(1) alih-alih penelusuran seluruh sheet.

Jangan "menyatukan" keduanya. Pertimbangannya berbeda di masing-masing kasus, dan menyatukannya
berarti memilih jawaban yang salah untuk salah satunya.

## Strict versus transitional OOXML

ECMA-376 Strict memakai URI namespace yang berbeda dari varian Transitional yang ditulis Office
secara baku. Berkas `.docx` strict yang dibuka pembaca yang hanya mengenal namespace transitional
kembali dengan **nol paragraf dan tanpa kesalahan apa pun**.

OfficeNet menormalkan namespace strict menjadi transitional saat memuat, sehingga keduanya terbuka
sama. Kalau Anda pernah menulis pembaca OOXML sendiri, inilah kegagalan senyap yang pertama harus
dijaga.

## Urutan elemen adalah *sequence* skema

Model konten OOXML adalah urutan berurut, bukan pilihan. Sebuah `w:rPr` yang menuliskan `w:sz`
sebelum `w:b` tidak diurutkan ulang dan tidak diabaikan — Word melaporkan berkasnya sebagai konten
yang tidak terbaca.

Setiap setter properti di OfficeNet melalui pembantu yang menyisipkan sesuai urutan skema. Ini cara
paling umum OOXML tulisan tangan memunculkan dialog "berkas rusak", dan alasan library ini tidak
pernah sekadar menambahkan elemen anak di akhir.

## Exception

Semuanya melempar `OfficeNetException` atau turunannya, sehingga pekerjaan batch cukup menangkap satu
tipe:

```csharp
try
{
    using var document = WordDocument.Open(path);
}
catch (OfficeNetException ex)
{
    logger.LogWarning(ex, "Lewati {Path}", path);
}
```

## Lihat juga

- [WordNet](WordNet.md) · [ExcelNet](ExcelNet.md) · [PowerPointNet](PowerPointNet.md) · [PdfNet](PdfNet.md)
- [API terpadu](OfficeNet.md)
