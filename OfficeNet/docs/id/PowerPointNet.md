# PowerPointNet

Presentasi `.pptx` untuk .NET 10 — model python-pptx, plus fitur yang membuat PptxGenJS berguna:
HTML → slide, chart native, media, dan efek bentuk.

*English: [docs/PowerPointNet.md](../PowerPointNet.md)* · [Kembali ke indeks](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.PowerPointNet
```

---

| | |
|---|---|
| ![Slide chart](../screenshots/powerpointnet-deck-03.png) | ![Slide tabel](../screenshots/powerpointnet-deck-04.png) |

*Kedua slide berasal dari kode di halaman ini. Chart-nya adalah **chart PowerPoint native** —
angkanya ikut serta dan tema bisa menata ulang tampilannya — di sini digambar oleh pengekspor PDF
dari data cache-nya sendiri.*

---

## Membuka dan menyimpan

```csharp
using PowerPointNet;

using var deck = Presentation.Create();          // 16:9 secara baku
using var existing = Presentation.Open("deck.pptx");
using var fromTemplate = Presentation.FromTemplate("brand.potx");

deck.UseWidescreen();    // 13,333 x 7,5 inci
deck.UseStandard();      // 10 x 7,5 inci

deck.Save("deck.pptx");
deck.SaveAsPdf("deck.pdf");
```

## Slide

```csharp
deck.AddTitleSlide("OfficeNet", "Word, Excel, PowerPoint dan PDF untuk .NET 10");

deck.AddBulletSlide("Komponen", [
    "WordNet — python-docx",
    "ExcelNet — openpyxl + pandas",
    "PowerPointNet — python-pptx + PptxGenJS",
    "PdfNet — PyPDF2",
]);

deck.AddSectionSlide("Bagian II");

var slide = deck.AddSlide(layoutIndex: 1);   // Title and Content
slide.SetTitle("Judul");
slide.SetBody(["Butir satu", "Butir dua"]);

deck.MoveSlide(3, 0);
deck.DuplicateSlide(0);
deck.RemoveSlide(4);
deck.ReverseSlides();
```

Master bawaan menyertakan enam layout, dengan urutan berikut:

| Indeks | Layout |
|---|---|
| 0 | Title Slide |
| 1 | Title and Content |
| 2 | Title Only |
| 3 | Blank |
| 4 | Two Content |
| 5 | Section Header |

Urutan ini **bukan** urutan bawaan PowerPoint, dan sebuah template punya urutannya sendiri. Cari
menurut nama alih-alih menuliskan indeks secara langsung bila deck berasal dari template:

```csharp
var layout = deck.FindLayout("Blank") ?? deck.Layouts[3];
deck.AddSlide(layout);
```

Indeks di luar jangkauan dijepit, bukan melempar — jadi `AddSlide(99)` diam-diam memberi Anda layout
terakhir. Satu alasan lagi untuk mencari menurut nama.

## Teks

```csharp
var box = slide.AddTextBox("Halo", Units.Inches(1), Units.Inches(2),
    Units.Inches(4), Units.Inches(1));

box.TextFrame!.Text = "Baris pertama\nBaris kedua";

var paragraph = box.TextFrame.AddParagraph("Butir");
paragraph.Level = 1;
paragraph.Alignment = TextAlignment.Center;
paragraph.HasBullet = true;

var run = paragraph.AddRun("tebal");
run.Bold = true;
run.FontSize = Units.Pt(24);
run.Color = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
```

`TextFrame` bertipe nullable, karena gambar atau chart tidak memilikinya. Text box dan autoshape
selalu punya — itulah sebabnya `!` memang tepat untuk shape yang baru saja Anda buat, dan sebaliknya
membaca teks dari shape sembarang memerlukan `?.`.

## Bentuk dan efek

```csharp
var shape = slide.AddShape(ShapeGeometry.RoundedRectangle,
    Units.Inches(1), Units.Inches(1), Units.Inches(3), Units.Inches(1.5));

shape.WithFill(OfficeColor.FromRgb(0x1F, 0x38, 0x64))
     .WithOutline(OfficeColor.White, Units.Pt(2))
     .WithShadow(blur: Units.Pt(8))
     .WithHyperlink("https://github.com/DotNetVibeCoderz/Vibe_Office");

slide.AddShape(ShapeGeometry.Ellipse, /* … */)
     .WithGradientFill(
         OfficeColor.FromRgb(0x1F, 0x38, 0x64),
         OfficeColor.FromRgb(0x63, 0x8E, 0xC6),
         GradientDirection.DiagonalDown)
     .WithGlow(OfficeColor.White, Units.Pt(6));
```

Metode efeknya adalah extension generik yang mengembalikan bentuk yang diberikan, jadi bisa
dirangkai dan berlaku untuk gambar dan tabel, bukan hanya autoshape.

## Tabel

```csharp
var table = slide.AddTable(rows: 5, columns: 3,
    Units.Inches(1), Units.Inches(1.9),
    deck.SlideWidth - Units.Inches(2), Units.Inches(2.6));

table.SetData(new[]
{
    new[] { "Komponen", "Analog Python", "Status" },
    new[] { "WordNet", "python-docx", "Selesai" },
    new[] { "ExcelNet", "openpyxl + pandas", "Selesai" },
});

table[0, 0].Text = "Komponen";
table.SetColumnWidth(0, Units.Inches(3));
```

## Chart

Chart di `.pptx` adalah data, bukan gambar: angkanya ikut serta, tema menata ulang tampilannya, dan
pembaca bisa mengarahkan kursor ke sebuah batang untuk melihat nilainya.

```csharp
using PowerPointNet.Charts;

slide.AddChart(new ChartData
{
    Type = ChartType.Column,
    Categories = ["Jakarta", "Bandung", "Surabaya", "Medan"],
    Series =
    [
        new ChartSeries("2025", [1120, 860, 740, 410]),
        new ChartSeries("2026", [1480, 1150, 905, 520]),
    ],
    ValueAxisTitle = "Juta Rupiah",
    ValueFormat = "#,##0",
    ShowDataLabels = true,
    Legend = LegendPosition.Bottom,
});
```

Dua belas tipe: `Column`, `ColumnStacked`, `Bar`, `BarStacked`, `Line`, `LineMarkers`, `Area`,
`AreaStacked`, `Pie`, `Doughnut`, `Scatter`, `Radar`.

Jalan pintas untuk kasus umum:

```csharp
ChartData.Simple(ChartType.Pie, "Pangsa", ["A", "B", "C"], [50, 30, 20]);
ChartData.FromMap(ChartType.Bar, "Penjualan", dictionary);
```

Membaca chart kembali:

```csharp
var chart = slide.Charts.First();
var data = chart.GetData();      // kategori, seri, label, format
chart.SetData(data with { Type = ChartType.Line });
```

`GetData` membaca nilai cache — itulah yang ditampilkan pembaca dan itulah semua yang ditulis library
ini. Chart yang dibuat di PowerPoint dengan workbook tersemat juga mengembalikan cache-nya, tetapi
bukan formula di baliknya.

## SmartArt

```csharp
using PowerPointNet.Diagrams;

slide.AddSmartArt(DiagramKind.Process, "Kumpulkan", "Olah", "Laporkan");
```

Lima jenis: `List` (kotak bertumpuk ke bawah), `Process` (chevron melintang), `Cycle` (cincin dengan
panah), `Hierarchy` (pohon), dan `Pyramid` (pita bertumpuk). Warnanya ditentukan pemanggil, dan
posisi serta ukurannya punya overload sendiri:

```csharp
var diagram = slide.AddSmartArt(DiagramKind.Cycle,
    [new DiagramNode("Rencana"), new DiagramNode("Kerjakan"), new DiagramNode("Periksa")],
    Units.Cm(3), Units.Cm(4), Units.Cm(18), Units.Cm(10),
    fill: OfficeColor.FromRgb(0x1F, 0x3A, 0x5F),
    text: OfficeColor.White);
```

`Hierarchy` satu-satunya jenis yang menggambar anak node, dan `DiagramNode.With` membuatnya:

```csharp
slide.AddSmartArt(DiagramKind.Hierarchy,
[
    DiagramNode.With("Direktur",
        DiagramNode.With("Operasi", new DiagramNode("Gudang"), new DiagramNode("Armada")),
        DiagramNode.With("Keuangan", new DiagramNode("Penagihan"))),
]);
```

Jenis lain meratakan pohonnya menjadi daftar alih-alih membuang level yang lebih dalam, sehingga
tidak ada isi yang hilang diam-diam.

`slide.Diagrams` menemukannya kembali, dan `diagram.Nodes` membaca teksnya dari data model.

### Apa yang ada di dalam berkas, dan di mana batasnya

Sebuah diagram adalah **lima part**, bukan satu: `data` (node dan hubungannya), `layout`, `colors`,
`quickStyle`, ditambah satu part ekstensi Microsoft yang menyimpan bentuk-bentuk hasil render. Slide
menunjuk empat yang pertama lewat satu elemen `dgm:relIds` yang menyebut keempat id relasi sekaligus;
part *data*-lah yang menunjuk part kelima.

`layout` yang menarik. Isinya bukan gambar — melainkan sebuah **algoritma**, sistem constraint yang
diselesaikan PowerPoint saat menggambar untuk menentukan posisi setiap node. Mengimplementasikannya
ulang bukan pekerjaan satu sore, dan mengirim versi kosongnya menghasilkan diagram yang terbuka
sebagai kotak kosong.

Karena itu OfficeNet menghitung geometrinya sendiri dan menuliskannya ke part drawing — persis yang
di-cache PowerPoint di sana. Part itulah yang digambar setiap konsumen: PowerPoint, LibreOffice,
Google Slides, dan ekspor PDF pustaka ini sendiri. **Begitu seseorang mengedit diagramnya di
PowerPoint, PowerPoint menjalankan mesin layout-nya sendiri dan bentuknya berpindah ke tempat yang
ia tentukan.** Isinya tetap; penempatan persisnya menjadi milik PowerPoint. Itu batas yang nyata, dan
sama dengan batas yang PowerPoint terapkan pada cache-nya sendiri.

## SmartArt

```csharp
using PowerPointNet.Diagrams;

slide.AddSmartArt(DiagramKind.Process, "Kumpulkan", "Olah", "Laporkan");
```

Lima jenis: `List` (kotak bertumpuk ke bawah), `Process` (chevron melintang), `Cycle` (cincin dengan
panah), `Hierarchy` (pohon), dan `Pyramid` (pita bertumpuk). Warnanya ditentukan pemanggil, dan
posisi serta ukurannya punya overload sendiri:

```csharp
var diagram = slide.AddSmartArt(DiagramKind.Cycle,
    [new DiagramNode("Rencana"), new DiagramNode("Kerjakan"), new DiagramNode("Periksa")],
    Units.Cm(3), Units.Cm(4), Units.Cm(18), Units.Cm(10),
    fill: OfficeColor.FromRgb(0x1F, 0x3A, 0x5F),
    text: OfficeColor.White);
```

`Hierarchy` satu-satunya jenis yang menggambar anak node, dan `DiagramNode.With` membuatnya:

```csharp
slide.AddSmartArt(DiagramKind.Hierarchy,
[
    DiagramNode.With("Direktur",
        DiagramNode.With("Operasi", new DiagramNode("Gudang"), new DiagramNode("Armada")),
        DiagramNode.With("Keuangan", new DiagramNode("Penagihan"))),
]);
```

Jenis lain meratakan pohonnya menjadi daftar alih-alih membuang level yang lebih dalam, sehingga
tidak ada isi yang hilang diam-diam.

`slide.Diagrams` menemukannya kembali, dan `diagram.Nodes` membaca teksnya dari data model.

### Apa yang ada di dalam berkas, dan di mana batasnya

Sebuah diagram adalah **lima part**, bukan satu: `data` (node dan hubungannya), `layout`, `colors`,
`quickStyle`, ditambah satu part ekstensi Microsoft yang menyimpan bentuk-bentuk hasil render. Slide
menunjuk empat yang pertama lewat satu elemen `dgm:relIds` yang menyebut keempat id relasi sekaligus;
part *data*-lah yang menunjuk part kelima.

`layout` yang menarik. Isinya bukan gambar — melainkan sebuah **algoritma**, sistem constraint yang
diselesaikan PowerPoint saat menggambar untuk menentukan posisi setiap node. Mengimplementasikannya
ulang bukan pekerjaan satu sore, dan mengirim versi kosongnya menghasilkan diagram yang terbuka
sebagai kotak kosong.

Karena itu OfficeNet menghitung geometrinya sendiri dan menuliskannya ke part drawing — persis yang
di-cache PowerPoint di sana. Part itulah yang digambar setiap konsumen: PowerPoint, LibreOffice,
Google Slides, dan ekspor PDF pustaka ini sendiri. **Begitu seseorang mengedit diagramnya di
PowerPoint, PowerPoint menjalankan mesin layout-nya sendiri dan bentuknya berpindah ke tempat yang
ia tentukan.** Isinya tetap; penempatan persisnya menjadi milik PowerPoint. Itu batas yang nyata, dan
sama dengan batas yang PowerPoint terapkan pada cache-nya sendiri.

## Gambar dan media

```csharp
slide.AddPicture("logo.png", Units.Inches(1), Units.Inches(1), width: Units.Inches(3));

slide.AddVideo("demo.mp4", Units.Inches(1), Units.Inches(1),
    Units.Inches(6), Units.Inches(3.4), autoPlay: true);

slide.AddAudio("narasi.m4a", Units.Inches(1), Units.Inches(5));
slide.AddOnlineVideo("https://www.youtube.com/watch?v=…", /* … */);
```

Klip tersemat membutuhkan *dua* relationship — satu `video`/`audio` dan satu `media` — yang menunjuk
ke part yang sama. PowerPoint diam-diam membuang klip yang hanya punya satu, dan itu alasan biasa
sebuah deck buatan tangan tidak memutar apa pun.

## HTML → slide

Fitur PptxGenJS yang paling dicari orang. Beri ia HTML, dapatkan sebuah deck.

```csharp
using PowerPointNet.Html;

using var deck = HtmlToSlides.CreatePresentation(html, new HtmlSlideOptions
{
    TitleSlide = "Tinjauan Kuartal",
    SubtitleSlide = "Disusun otomatis",
    SplitOnHeadingLevel = 2,     // slide baru pada setiap <h2>
    MaxLinesPerSlide = 9,        // kelebihan isi dilanjutkan ke slide berikutnya
});

deck.Save("dari-html.pptx");
```

![Slide yang dihasilkan dari HTML](../screenshots/html-to-slides-03.png)

Yang dipahami: `h1`–`h6`, `p`, `ul`/`ol`/`li` dengan penyarangan yang dipetakan ke tingkat butir,
`table`/`tr`/`th`/`td`, `img`, `strong`/`b`, `em`/`i`, `u`, `code`, `br`, `hr`, `blockquote`, serta
atribut `style` inline untuk warna, ukuran, ketebalan, dan perataan.

Isi yang panjang dipecah ke slide lanjutan alih-alih meluber ke bawah — ambangnya `MaxLinesPerSlide`
dan `MaxTableRowsPerSlide`.

Titik masuk lain:

```csharp
HtmlToSlides.CreatePresentationFromFile("laporan.html");
HtmlToSlides.Convert(existingDeck, html);          // menambahkan ke deck yang sudah ada
HtmlToSlides.TableToSlides(existingDeck, html);    // hanya tabelnya, dipaginasi
```

Gambar dicari relatif terhadap `BaseDirectory`, atau lewat `ImageResolver` Anda sendiri bila gambarnya
berada di tempat yang tidak bisa dijangkau sistem berkas:

```csharp
new HtmlSlideOptions
{
    ImageResolver = url => httpClient.GetByteArrayAsync(url).Result,
}
```

## Transisi, animasi, dan catatan

```csharp
slide.SetTransition(SlideTransition.Fade, TimeSpan.FromSeconds(0.7));
slide.AnimateOnClick(AnimationEffect.Fade, shape1, shape2);
slide.Notes = "Sebutkan angka pertumbuhan di sini.";
slide.IsHidden = true;
slide.BackgroundColor = OfficeColor.FromRgb(0xF2, 0xF2, 0xF2);
```

Animasi hanya berurutan mengikuti klik. Motion path dan pemicu membutuhkan pohon timing SMIL penuh
dan ada di [peta jalan](../../Plan.md).

### Animasi

```csharp
using PowerPointNet.Animations;

slide.Animate(
    Animation.Entrance(title, AnimationEffectKind.Fade),
    Animation.Entrance(bullet1, AnimationEffectKind.Fly).From(AnimationDirection.Left),
    Animation.Entrance(bullet2, AnimationEffectKind.Fly).WithPrevious(),
    Animation.Emphasis(logo, AnimationEffectKind.Spin).AfterPrevious(),
    Animation.Exit(cover).Lasting(TimeSpan.FromSeconds(1)),
    Animation.Motion(arrow, MotionPath.Line(0.3, 0)).OnClickOf(button));
```

Empat kelas, dan tidak bisa saling ditukar: **entrance** meninggalkan bentuknya terlihat, **exit**
meninggalkannya tersembunyi, **emphasis** mengandaikan bentuknya sudah terlihat, dan **motion path**
memindahkannya. Memasangkan efek dengan kelas yang salah akan melempar exception, bukan menulis
berkas yang dibuka PowerPoint tanpa memainkan apa pun.

| Kelas | Efek |
| --- | --- |
| `Entrance`, `Exit` | `Appear`, `Fade`, `Fly`, `Wipe`, `Zoom` |
| `Emphasis` | `Pulse`, `Spin`, `Grow` |
| `MotionPath` | `Move`, lewat sebuah `MotionPath` |

**Urutannya adalah urutan klik**, dan itu juga yang menjadi acuan `WithPrevious()` dan
`AfterPrevious()` — keduanya berarti "animasi sebelum ini di daftar ini". Hanya `OnClick` yang
memulai klik baru; dua lainnya bergabung ke klik yang sudah terbuka, dan itulah bedanya antara satu
build tiga bagian dan tiga klik terpisah.

`OnClickOf(shape)` adalah pengecualian. Ia tidak mengambil giliran dalam urutan klik: ia masuk ke
sequence-nya sendiri yang terkunci pada bentuk itu, dan berjalan setiap kali bentuk itu diklik,
berapa kali pun. Itulah yang membuat sebuah slide jadi interaktif — tombol yang membuka jawaban.

`Lasting()` dan `After()` mengatur durasi dan jeda. `HasAnimations` memberi tahu apakah sebuah slide
punya animasi, dan `ClearAnimations()` menghapusnya.

#### Motion path

```csharp
MotionPath.Line(0.3, -0.1);                         // sepertiga slide ke kanan, sepersepuluh ke atas
MotionPath.Through((0.1, 0), (0.1, 0.2), (0, 0.2)); // tiga ruas
MotionPath.Rectangle(0.25, 0.1);                    // lintasan tertutup
MotionPath.Custom("M 0 0 C 0.2 -0.3 0.4 0.3 0.6 0 E");
```

Koordinatnya berkisar 0 sampai 1 melintang dan menurun slide, **relatif terhadap posisi bentuknya
sekarang**. Jadi `0.25` berarti seperempat lebar slide dari posisi bentuk itu sendiri, bukan
seperempat jalan dari tepi kiri slide — membacanya sebagai absolut itulah sebabnya motion path yang
ditulis tangan begitu sering melempar bentuknya keluar halaman.

#### Apa yang ditulis, dan apa yang dilakukan PowerPoint dengannya

Model animasinya adalah SMIL: satu time root, sequence di bawahnya, grup klik di bawah itu, dan di
bawah tiap grup ada behaviour yang benar-benar mengubah sesuatu. Bahkan satu "fade in saat diklik"
saja sudah empat lapis `p:par` sebelum ada yang terjadi.

Behaviour itulah yang dimainkan. Atribut `presetID` di sebelahnya hanya memberi tahu panel animasi
PowerPoint entri galeri mana ini, jadi ia ditulis untuk nomor entrance dan exit yang terdokumentasi
dan dibiarkan kosong di tempat lain alih-alih ditebak: PowerPoint kemudian melabelinya "Custom" dan
memainkannya persis seperti yang ditulis — lebih baik daripada label salah pada efek yang lalu tidak
bisa diedit dengan benar oleh siapa pun.

Animasi tidak dibaca kembali ke dalam model. Mengenali setiap efek yang bisa ditulis PowerPoint jauh
lebih besar daripada menulis yang ada di sini, dan melaporkan sebuah slide tidak punya animasi hanya
karena satu di antaranya tidak dikenali akan lebih buruk daripada tidak menawarkan pembacaan sama
sekali — jadi `Animate` mengganti apa pun yang dipunya slide itu, dan `HasAnimations` adalah
satu-satunya pembaca yang ada.


## Tema

```csharp
deck.Master!.SetThemeColor("accent1", OfficeColor.FromRgb(0x1F, 0x38, 0x64));
deck.Master.SetThemeFonts(majorFont: "Poppins", minorFont: "Inter");
```

Mengubah warna tema menata ulang setiap bentuk yang memakainya — memang itu gunanya tema. Bentuk yang
diberi warna eksplisit tidak ikut berubah.

## Membaca deck

```csharp
using var deck = Presentation.Open("deck.pptx");

Console.WriteLine(deck.ExtractText());

foreach (var slide in deck.Slides)
{
    Console.WriteLine($"#{slide.SlideNumber} {slide.Title?.TextFrame?.Text}");

    foreach (var table in slide.Tables) { /* … */ }
    foreach (var chart in slide.Charts) { /* … */ }
    foreach (var picture in slide.Pictures) { /* … */ }
}
```

## Ekspor PDF

```csharp
deck.SaveAsPdf("deck.pdf", new SlidePdfOptions
{
    IncludeHiddenSlides = false,
    IncludeNotes = true,
    IncludeImages = true,
});
```

Teks, placeholder, autoshape dengan fill dan outline, gambar, tabel, dan **chart** digambar. Gradien,
efek 3-D, SmartArt, dan animasi tidak.

## Kesalahan yang sering terjadi

**`AddTitleSlide` melempar pada template kustom.** Layout-nya tidak punya placeholder `subTitle`;
PowerPointNet mundur ke placeholder body, tetapi layout yang tidak punya keduanya memang tidak bisa
menerima subjudul. Pakai `AddSlide(layout)` dan `SetTitle` sebagai gantinya.

**Tabel atau chart terbaca kembali sebagai `Shape` biasa.** Keduanya hidup di dalam `graphicFrame`
dan dibedakan oleh `graphicData/@uri`. Kalau frame-nya Anda susun sendiri, atribut itulah yang hilang.

**Video tidak diputar.** Hanya satu dari dua relationship yang diperlukan ditulis.

**ID slide.** `sldId` minimal harus 256. Library ini mengurusnya; XML yang disunting tangan harus
mengurusnya juga.

## Lihat juga

- [Konsep inti](Core.md) — EMU, satuan, dan warna
- [Rendering](Rendering.md) — slide ke PNG
- `notebooks/PowerPointNet.ipynb` — materi yang sama, bisa dijalankan
