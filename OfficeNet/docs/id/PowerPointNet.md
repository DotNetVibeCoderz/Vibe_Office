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
