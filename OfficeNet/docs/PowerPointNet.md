# PowerPointNet

`.pptx` presentations for .NET 10 — python-pptx's model, plus the features that make PptxGenJS
useful: HTML → slides, native charts, media and shape effects.

*Bahasa Indonesia: [docs/id/PowerPointNet.md](id/PowerPointNet.md)* · [Back to index](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.PowerPointNet
```

---

| | |
|---|---|
| ![A chart slide](screenshots/powerpointnet-deck-03.png) | ![A table slide](screenshots/powerpointnet-deck-04.png) |

*Both slides come from the code on this page. The chart is a **native PowerPoint chart** — the
numbers travel with it and the theme restyles it — drawn here by the PDF exporter from its own
cached data.*

---

## Opening and saving

```csharp
using PowerPointNet;

using var deck = Presentation.Create();          // 16:9 by default
using var existing = Presentation.Open("deck.pptx");
using var fromTemplate = Presentation.FromTemplate("brand.potx");

deck.UseWidescreen();    // 13.333 x 7.5 in
deck.UseStandard();      // 10 x 7.5 in

deck.Save("deck.pptx");
deck.SaveAsPdf("deck.pdf");
```

## Slides

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

The default master ships six layouts, in this order:

| Index | Layout |
|---|---|
| 0 | Title Slide |
| 1 | Title and Content |
| 2 | Title Only |
| 3 | Blank |
| 4 | Two Content |
| 5 | Section Header |

That is **not** PowerPoint's own ordering, and a template will have its own. Look one up by name
rather than hard-coding an index when the deck comes from a template:

```csharp
var layout = deck.FindLayout("Blank") ?? deck.Layouts[3];
deck.AddSlide(layout);
```

An out-of-range index is clamped rather than throwing, so `AddSlide(99)` quietly gives you the last
layout — another reason to look up by name.

## Text

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

`TextFrame` is nullable, because a picture or a chart has none. A text box or an autoshape always
does, which is why `!` is the honest thing to write on one you just created — and why reading text
off an arbitrary shape needs `?.`.

## Shapes and effects

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

The effect methods are generic extensions that return the shape they were given, so they chain and
work on pictures and tables as well as autoshapes.

## Tables

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

## Charts

A chart in a `.pptx` is data, not a picture: the numbers travel with it, the theme restyles it, and
a reader can hover a bar and see its value.

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

Twelve types: `Column`, `ColumnStacked`, `Bar`, `BarStacked`, `Line`, `LineMarkers`, `Area`,
`AreaStacked`, `Pie`, `Doughnut`, `Scatter`, `Radar`.

Shorthand for the common cases:

```csharp
ChartData.Simple(ChartType.Pie, "Pangsa", ["A", "B", "C"], [50, 30, 20]);
ChartData.FromMap(ChartType.Bar, "Penjualan", dictionary);
```

Reading a chart back:

```csharp
var chart = slide.Charts.First();
var data = chart.GetData();      // categories, series, labels, format
chart.SetData(data with { Type = ChartType.Line });
```

`GetData` reads the cached values, which is what a viewer displays and everything this library
writes. A chart authored in PowerPoint against an embedded workbook returns its cache too, but not
the formulas behind it.

## Pictures and media

```csharp
slide.AddPicture("logo.png", Units.Inches(1), Units.Inches(1), width: Units.Inches(3));

slide.AddVideo("demo.mp4", Units.Inches(1), Units.Inches(1),
    Units.Inches(6), Units.Inches(3.4), autoPlay: true);

slide.AddAudio("narasi.m4a", Units.Inches(1), Units.Inches(5));
slide.AddOnlineVideo("https://www.youtube.com/watch?v=…", /* … */);
```

An embedded clip needs *two* relationships — one `video`/`audio` and one `media` — pointing at the
same part. PowerPoint silently drops a clip that has only one, which is the usual reason a
hand-built deck plays nothing.

## HTML → slides

The PptxGenJS feature people actually come for. Give it HTML, get a deck.

```csharp
using PowerPointNet.Html;

using var deck = HtmlToSlides.CreatePresentation(html, new HtmlSlideOptions
{
    TitleSlide = "Tinjauan Kuartal",
    SubtitleSlide = "Disusun otomatis",
    SplitOnHeadingLevel = 2,     // a new slide at each <h2>
    MaxLinesPerSlide = 9,        // overflow continues on the next slide
});

deck.Save("dari-html.pptx");
```

![A slide generated from HTML](screenshots/html-to-slides-03.png)

Understood: `h1`–`h6`, `p`, `ul`/`ol`/`li` with nesting mapped to bullet levels, `table`/`tr`/`th`/
`td`, `img`, `strong`/`b`, `em`/`i`, `u`, `code`, `br`, `hr`, `blockquote`, and inline `style`
attributes for colour, size, weight and alignment.

Long content splits across continuation slides rather than overflowing off the bottom — that
threshold is `MaxLinesPerSlide` and `MaxTableRowsPerSlide`.

Other entry points:

```csharp
HtmlToSlides.CreatePresentationFromFile("laporan.html");
HtmlToSlides.Convert(existingDeck, html);          // append into a deck you already have
HtmlToSlides.TableToSlides(existingDeck, html);    // just the tables, paginated
```

Images are resolved relative to `BaseDirectory`, or by your own `ImageResolver` when they live
somewhere the file system cannot reach:

```csharp
new HtmlSlideOptions
{
    ImageResolver = url => httpClient.GetByteArrayAsync(url).Result,
}
```

## Transitions, animation and notes

```csharp
slide.SetTransition(SlideTransition.Fade, TimeSpan.FromSeconds(0.7));
slide.AnimateOnClick(AnimationEffect.Fade, shape1, shape2);
slide.Notes = "Sebutkan angka pertumbuhan di sini.";
slide.IsHidden = true;
slide.BackgroundColor = OfficeColor.FromRgb(0xF2, 0xF2, 0xF2);
```

Animation is click-ordered only. Motion paths and triggers need a full SMIL timing tree and are on
the [roadmap](../Plan.md).

## Themes

```csharp
deck.Master!.SetThemeColor("accent1", OfficeColor.FromRgb(0x1F, 0x38, 0x64));
deck.Master.SetThemeFonts(majorFont: "Poppins", minorFont: "Inter");
```

Setting a theme colour restyles every shape that uses it, which is the point of a theme — a shape
given an explicit colour does not follow.

## Reading a deck

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

## PDF export

```csharp
deck.SaveAsPdf("deck.pdf", new SlidePdfOptions
{
    IncludeHiddenSlides = false,
    IncludeNotes = true,
    IncludeImages = true,
});
```

Text, placeholders, autoshapes with fills and outlines, pictures, tables and **charts** are drawn.
Gradients, 3-D effects, SmartArt and animation are not.

## Common mistakes

**`AddTitleSlide` throws on a custom template.** The layout has no `subTitle` placeholder;
PowerPointNet falls back to the body placeholder, but a layout with neither cannot take a subtitle.
Use `AddSlide(layout)` and `SetTitle` instead.

**A table or chart reads back as a plain `Shape`.** Both live in a `graphicFrame` and are told apart
by the `graphicData/@uri`. If you built the frame by hand, that attribute is what is missing.

**A video does not play.** Only one of the two required relationships was written.

**Slide IDs.** `sldId` must be at least 256. The library handles it; hand-edited XML must too.

## See also

- [Core concepts](Core.md) — EMU, units and colour
- [Rendering](Rendering.md) — slides to PNG
- `notebooks/PowerPointNet.ipynb` — the same material, runnable
