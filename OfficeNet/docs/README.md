# OfficeNet documentation

Word, Excel, PowerPoint and PDF for .NET 10 — no Office install, no COM, no native dependencies in
the core, identical output on Windows, Linux and macOS.

*Bahasa Indonesia: [docs/id/README.md](id/README.md)*

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

---

## The libraries

| Package | Namespace | Replaces | Guide |
|---|---|---|---|
| `Gravicode.OfficeNet.WordNet` | `WordNet` | python-docx | [WordNet](WordNet.md) |
| `Gravicode.OfficeNet.ExcelNet` | `ExcelNet` | openpyxl + pandas | [ExcelNet](ExcelNet.md) |
| `Gravicode.OfficeNet.PowerPointNet` | `PowerPointNet` | python-pptx + PptxGenJS | [PowerPointNet](PowerPointNet.md) |
| `Gravicode.OfficeNet.PdfNet` | `PdfNet` | PyPDF2 | [PdfNet](PdfNet.md) |
| `Gravicode.OfficeNet.Rendering` | `OfficeNet.Rendering` | pdf2image / poppler | [Rendering](Rendering.md) |
| `Gravicode.OfficeNet` | `OfficeNet` | — | [Unified API](OfficeNet.md) |
| `Gravicode.OfficeNet.Core` | `OfficeNet.Core` | — | [Core concepts](Core.md) |

Install the one you need; `Gravicode.OfficeNet` pulls in all four format libraries plus a
format-agnostic entry point.

```bash
dotnet add package Gravicode.OfficeNet
```

## Which library depends on what

```
OfficeNet.Core ──┬── PdfNet ──┬── WordNet
                 │            ├── ExcelNet ── Gravicode.Science.GraviFrame
                 │            └── PowerPointNet
                 │
                 └── OfficeNet.Rendering (SkiaSharp)
```

`PdfNet` is the leaf. Word, Excel and PowerPoint all export *through* it, which is why there is one
layout engine rather than four and why a rendered page always matches the PDF you would get from
the same document.

`OfficeNet.Rendering` is a separate package on purpose: rasterisation is the only part of OfficeNet
that needs a native dependency, and keeping it out of the core is what makes "identical on every
platform" true rather than aspirational.

## Thirty seconds each

```csharp
// Word
using var document = WordDocument.Create();
document.AddHeading("Laporan", 0);
document.AddParagraph("Isi paragraf.");
document.Save("laporan.docx");

// Excel
using var workbook = Workbook.Create("Data");
workbook["Data"]["A1"].Set("Total");
workbook["Data"]["B1"].SetFormula("SUM(B2:B10)");
workbook.Recalculate();
workbook.Save("data.xlsx");

// PowerPoint
using var deck = Presentation.Create();
deck.AddTitleSlide("OfficeNet", "Satu API untuk empat format");
deck.Save("deck.pptx");

// PDF
using var pdf = PdfDocument.Open("input.pdf");
Console.WriteLine(pdf.ExtractText());

// Any of them
Office.ConvertToPdf("laporan.docx", "laporan.pdf");
```

## What the output looks like

Every image below is produced by [`tools/ScreenshotGen`](../tools/ScreenshotGen) from the libraries'
own output — rendered through `OfficeNet.Rendering`, never captured from a viewer. Re-running the
tool regenerates them, so they cannot drift away from what the code actually does.

| | |
|---|---|
| **WordNet** — headings, styles, header/footer, page fields, a shaded table<br>[full guide](WordNet.md) | ![A Word document rendered to PNG](screenshots/wordnet-document.png) |
| **ExcelNet** — typed cells, formulas evaluated, number formats, autofit<br>[full guide](ExcelNet.md) | ![An Excel workbook rendered to PNG](screenshots/excelnet-workbook.png) |
| **PowerPointNet** — a native chart drawn from its cached data<br>[full guide](PowerPointNet.md) | ![A PowerPoint chart slide rendered to PNG](screenshots/powerpointnet-deck-03.png) |
| **HTML → slides** — one call, no manual editing<br>[full guide](PowerPointNet.md#html--slides) | ![A slide generated from HTML](screenshots/html-to-slides-03.png) |

## Guides

- **[Core concepts](Core.md)** — OPC packages, units, colour, metadata. Read this once and the other
  four libraries stop surprising you.
- **[WordNet](WordNet.md)** — documents, styles, tables, sections, mail merge, PDF export.
- **[ExcelNet](ExcelNet.md)** — cells, formulas, styles, conditional formatting, CSV/JSON/DataFrame.
- **[PowerPointNet](PowerPointNet.md)** — slides, layouts, charts, media, HTML conversion.
- **[PdfNet](PdfNet.md)** — merge, split, extract, encrypt, forms, annotations, drawing.
- **[Rendering](Rendering.md)** — pages to PNG/JPEG/WebP, thumbnails.
- **[Unified API](OfficeNet.md)** — format detection and format-agnostic operations.

## Notebooks

`notebooks/` holds a .NET Interactive notebook per library. They are the fastest way to try the API
without creating a project — open one in VS Code with the Polyglot Notebooks extension and run the
cells.

## Known limits

Stated plainly, because a limit you know about is a limit you can plan around:

- **PDF export is a real layout engine, not Word's.** Flowed text, headings, lists, tables, images
  and page fields are laid out correctly. Floating objects with `wrap="square"` are drawn inline.
- **The renderer is for thumbnails and previews.** It draws paths, images, text, clipping paths and
  gradients, and uses the file's own embedded font when that font is TrueType. It does not do tiling
  patterns, soft masks, transparency groups or mesh shadings.
- **The formula engine fills the cache; it is not Excel.** About 60 functions, enough that a
  non-Excel consumer sees numbers rather than blanks. Array formulas and iterative calculation are
  out of scope.
- **PDF → Word and PDF → Excel are not implemented.** Text extraction is; reconstructing paragraphs
  and tables from glyph positions is a different problem and is on the [roadmap](../Plan.md).

The full picture is in [Plan.md](../Plan.md) (roadmap) and [Progress.md](../Progress.md)
(what is done and verified).
