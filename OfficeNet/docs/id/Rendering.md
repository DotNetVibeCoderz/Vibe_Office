# OfficeNet.Rendering

Dokumen menjadi gambar — PNG, JPEG, dan WebP — untuk thumbnail, pratinjau, dan tangkapan layar
dokumentasi.

*English: [docs/Rendering.md](../Rendering.md)* · [Kembali ke indeks](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.Rendering
```

---

## Kenapa ini paket terpisah

Rasterisasi adalah satu-satunya bagian OfficeNet yang membutuhkan dependensi native: ia memerlukan
mesin font dan perasteran path, dan SkiaSharp adalah cara paling masuk akal mendapatkan keduanya.
Semua yang lain — membaca, menulis, mengonversi ke PDF — berjalan sepenuhnya di kode terkelola, dan
itulah yang membuat library intinya menghasilkan keluaran identik di setiap platform.

Menahan SkiaSharp di sini berarti layanan yang hanya menulis berkas `.xlsx` tidak pernah ikut
mengirim biner native yang tidak dipakainya, dan kontainer tanpa paket font sistem tetap berfungsi
untuk semuanya kecuali rendering.

Di Linux, paket ini menarik `SkiaSharp.NativeAssets.Linux`. Perenderan teks membutuhkan font
sungguhan yang terpasang — kontainer ramping tanpa font akan menghasilkan kotak-kotak, dan itu
masalah lingkungan, bukan bug library.

## Merender apa saja

```csharp
using OfficeNet.Rendering;

// Satu panggilan, format apa pun yang didukung
var pages = DocumentRenderer.Render("laporan.docx");
var files = DocumentRenderer.RenderToFiles("laporan.docx", "keluaran");
var thumb = DocumentRenderer.RenderThumbnail("laporan.docx", widthPixels: 400);
```

`.docx`, `.xlsx`, `.pptx`, dan `.pdf` semuanya bisa. Word, Excel, dan PowerPoint dirender dengan
mengonversinya ke PDF terlebih dahulu — bukan sebagai jalan pintas, melainkan karena pengekspor PDF
mereka sudah menyelesaikan style, menata konten yang mengalir, dan memaginasi. Merender lewat PDF
berarti satu mesin layout, bukan empat, dan halaman yang dirender selalu sama dengan PDF yang akan
didapat pengguna dari dokumen yang sama.

## Titik masuk per format

```csharp
using var pdf = PdfDocument.Open("masukan.pdf");
byte[] first = DocumentRenderer.RenderPage(pdf.Pages[0]);
var all = DocumentRenderer.RenderPdf(pdf);

using var document = WordDocument.Open("laporan.docx");
var wordPages = DocumentRenderer.RenderWord(document);

using var workbook = Workbook.Open("data.xlsx");
var sheets = DocumentRenderer.RenderExcel(workbook);

using var deck = Presentation.Open("deck.pptx");
var slides = DocumentRenderer.RenderPowerPoint(deck);
```

### Satu halaman, dan rentang

Deck berisi dua ratus slide yang ditata seluruhnya demi satu thumbnail memakan hampir satu detik per
thumbnail. Minta halaman yang Anda butuhkan saja:

```csharp
byte[] third = DocumentRenderer.RenderSlide(deck, index: 2);
var middle  = DocumentRenderer.RenderPdf(pdf, 2..4);      // halaman 3 dan 4
var lastTwo = DocumentRenderer.RenderPdf(pdf, ^2..);
```

Layout-nya tetap dijalankan — master sebuah slide dan nomor halamannya sama-sama berasal dari
keseluruhan dokumen — tetapi hanya halaman yang diminta yang dirasterisasi, dan di situlah waktu
serta hampir seluruh memorinya terpakai.

### Menulis berkas dari dokumen yang sudah Anda pegang

```csharp
DocumentRenderer.RenderToFiles(deck, "keluaran");                       // slide-01.png, slide-02.png…
DocumentRenderer.RenderToFiles(document, "keluaran", namePrefix: "hal");
DocumentRenderer.RenderToFiles(workbook, "keluaran");
DocumentRenderer.RenderToFiles(pdf, "keluaran");
```

Nomornya diberi nol di depan agar daftar direktori terurut sesuai urutan bacanya, dan satu halaman
ditulis tanpa nomor sama sekali. `WriteImages` melakukan hal yang sama untuk gambar yang Anda render
sendiri.

### Video

Tidak ada, dan itu bukan kelalaian. Merender deck menjadi video berarti encoding, yang berarti
FFmpeg, yang berupa biner native dan bukan paket NuGet — sementara satu hal yang membuat
`OfficeNet.Rendering` bisa diandalkan adalah bahwa satu-satunya dependensi native-nya adalah
SkiaSharp. Menambah satu lagi yang harus ditemukan di host, dalam versi yang tepat, akan membuat
setiap deployment jadi pertanyaan dukungan.

Kalau Anda butuh video, render slide-nya di sini lalu serahkan gambarnya ke FFmpeg sendiri:

```
ffmpeg -framerate 1/5 -i slide-%02d.png -c:v libx264 -pix_fmt yuv420p deck.mp4
```


## Opsi

```csharp
var options = new RenderOptions
{
    Dpi = 150,                                   // 96 layar, 150 thumbnail, 300 cetak
    Format = RenderFormat.Png,                   // Png, Jpeg, Webp
    Quality = 90,                                // hanya untuk JPEG dan WebP
    Background = OfficeColor.White,              // halaman PDF transparan sampai ada yang mengecatnya
    MaxPixels = 4000,                            // batas sisi terpanjang
    DrawPageBorder = true,                       // garis tipis, agar halaman putih terbaca sebagai halaman
};

var png = DocumentRenderer.RenderPage(page, options);
```

`MaxPixels` adalah pengaman, bukan preferensi. Poster A0 pada 300 DPI berukuran 9933 × 14043 piksel
dan sekitar 560 MB bitmap — cukup untuk menjatuhkan sebuah server. Melampaui batasnya membuat DPI
diturunkan, bukan gambar dipotong, sehingga Anda mendapat gambar kecil yang benar alih-alih potongan
besar.

`Background` lebih penting daripada kelihatannya: halaman PDF transparan sampai ada yang mengecatnya,
jadi merender ke atas ketiadaan menghasilkan PNG transparan yang terlihat hitam di separuh penampil
yang membukanya.

## Yang digambar, dan yang tidak

**Digambar:** path yang diisi dan digaris dengan warna serta aturan pengisian even-odd/winding yang
benar, teks pada posisinya dengan warna isiannya sendiri, dan gambar yang ditempatkan oleh transform
di content stream.

**Tidak digambar:** gradien, pattern, soft mask, grup transparansi, blend mode, atau clipping path.
Teks digambar dengan font sistem pengganti alih-alih font tersemat berkasnya, sehingga bentuk glyph
dan panjang barisnya mendekati tetapi tidak persis.

Itu batasan yang nyata, dan itulah sebabnya ini renderer untuk thumbnail dan pratinjau, bukan
penampil. Halaman yang sebagian besar berupa diagram penuh gradien akan keluar lebih datar dari
seharusnya. Halaman yang berupa laporan keluar terlihat seperti laporan — dan itulah yang menjadi
tangkapan layar di seluruh dokumentasi ini.

Gambar yang diputar atau dimiringkan digambar tegak di dalam kotak pembatasnya: salah, tetapi masih
dikenali pada ukuran thumbnail, dan itu lebih baik daripada membuangnya.

## Endpoint thumbnail untuk web

```csharp
app.MapGet("/thumbnail/{id}", (string id) =>
{
    var path = storage.PathFor(id);
    var png = DocumentRenderer.RenderThumbnail(path, widthPixels: 320);

    return Results.File(png, "image/png");
});
```

Rendering terikat CPU dan mengalokasikan bitmap satu halaman penuh, jadi simpan hasilnya di cache
alih-alih merender per permintaan, dan biarkan `MaxPixels` tetap terpasang.

## Menghasilkan tangkapan layar dokumentasi

Setiap gambar di dokumentasi ini dihasilkan oleh [`tools/ScreenshotGen`](../../tools/ScreenshotGen),
yang membangun dokumen dengan library-nya lalu merendernya di sini:

```bash
dotnet run --project tools/ScreenshotGen -c Release
```

Gambarnya dihasilkan, bukan ditangkap, karena tangkapan layar yang dihasilkan tidak mungkin
menyimpang dari apa yang dilakukan kodenya. Kalau sebuah perubahan merusak pewarnaan tabel,
penjalanan berikutnya langsung memperlihatkannya.

## Lihat juga

- [PdfNet](PdfNet.md) — model dokumen di baliknya
- [API terpadu](OfficeNet.md) — deteksi format
