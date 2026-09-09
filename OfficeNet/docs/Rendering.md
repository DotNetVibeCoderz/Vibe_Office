# OfficeNet.Rendering

Documents to images — PNG, JPEG and WebP — for thumbnails, previews and documentation screenshots.

*Bahasa Indonesia: [docs/id/Rendering.md](id/Rendering.md)* · [Back to index](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.Rendering
```

---

## Why this is a separate package

Rasterisation is the one part of OfficeNet that needs a native dependency: it requires a font engine
and a path rasteriser, and SkiaSharp is the sane way to get both. Everything else — reading, writing,
converting to PDF — runs on managed code alone, and that is what makes the core libraries produce
identical output on every platform.

Keeping SkiaSharp here means a service that only writes `.xlsx` files never ships a native binary it
does not use, and a container without system font packages still works for everything except
rendering.

On Linux the package pulls in `SkiaSharp.NativeAssets.Linux`. Text whose font the file embeds is
drawn from that font and needs nothing installed; text that falls back to substitution needs real
fonts present, so a slim container with none will produce boxes for it, and that is an environment
problem rather than a library bug.

## Rendering anything

```csharp
using OfficeNet.Rendering;

// One call, any supported format
var pages = DocumentRenderer.Render("laporan.docx");
var files = DocumentRenderer.RenderToFiles("laporan.docx", "keluaran");
var thumb = DocumentRenderer.RenderThumbnail("laporan.docx", widthPixels: 400);
```

`.docx`, `.xlsx`, `.pptx` and `.pdf` all work. Word, Excel and PowerPoint are rendered by converting
to PDF first — not as a shortcut, but because their PDF exporters already resolve styles, lay out
flow content and paginate. Rendering through PDF means one layout engine rather than four, and a
rendered page always matches the PDF a user would get from the same document.

## Per-format entry points

```csharp
using var pdf = PdfDocument.Open("input.pdf");
byte[] first = DocumentRenderer.RenderPage(pdf.Pages[0]);
var all = DocumentRenderer.RenderPdf(pdf);

using var document = WordDocument.Open("laporan.docx");
var wordPages = DocumentRenderer.RenderWord(document);

using var workbook = Workbook.Open("data.xlsx");
var sheets = DocumentRenderer.RenderExcel(workbook);

using var deck = Presentation.Open("deck.pptx");
var slides = DocumentRenderer.RenderPowerPoint(deck);
```

### One page, and ranges

A deck of two hundred slides laid out to produce one thumbnail is most of a second per thumbnail.
Ask for the page you want instead:

```csharp
byte[] third = DocumentRenderer.RenderSlide(deck, index: 2);
var middle  = DocumentRenderer.RenderPdf(pdf, 2..4);      // pages 3 and 4
var lastTwo = DocumentRenderer.RenderPdf(pdf, ^2..);
```

The layout still runs — a slide's master and its page number both come from the whole document — but
only the pages asked for are rasterised, and that is where the time and nearly all the memory go.

### Writing files from a document you already have

```csharp
DocumentRenderer.RenderToFiles(deck, "keluaran");                       // slide-01.png, slide-02.png…
DocumentRenderer.RenderToFiles(document, "keluaran", namePrefix: "hal");
DocumentRenderer.RenderToFiles(workbook, "keluaran");
DocumentRenderer.RenderToFiles(pdf, "keluaran");
```

Numbers are zero-padded, so a directory listing sorts the way the document reads, and a single page
is written without a number at all. `WriteImages` does the same for images you rendered yourself.

### Video

There is none, and it is not an oversight. Rendering a deck to video means encoding, which means
FFmpeg, which is a native binary rather than a NuGet package — and the one thing that makes
`OfficeNet.Rendering` predictable is that its only native dependency is SkiaSharp. Adding a second
one that has to be found on the host, in the right version, would make every deployment a support
question.

If you need a video, render the slides here and hand the images to FFmpeg yourself:

```
ffmpeg -framerate 1/5 -i slide-%02d.png -c:v libx264 -pix_fmt yuv420p deck.mp4
```


## Options

```csharp
var options = new RenderOptions
{
    Dpi = 150,                                   // 96 screen, 150 thumbnail, 300 print
    Format = RenderFormat.Png,                   // Png, Jpeg, Webp
    Quality = 90,                                // JPEG and WebP only
    Background = OfficeColor.White,              // PDF pages are transparent until painted
    MaxPixels = 4000,                            // cap on the longest side
    DrawPageBorder = true,                       // a hairline, so a white page reads as a page
};

var png = DocumentRenderer.RenderPage(page, options);
```

`MaxPixels` is a guard, not a preference. An A0 poster at 300 DPI is 9933 × 14043 pixels and around
560 MB of bitmap — enough to take a server down. Exceeding the cap scales the DPI down rather than
cropping, so you get a smaller correct image instead of a large fragment.

`Background` matters more than it looks: a PDF page is transparent until something paints it, so
rendering onto nothing gives you a transparent PNG that looks black in half the viewers that open it.

## What it draws, and what it does not

**Draws:** filled and stroked paths with the correct colours and even-odd/winding fill rules,
positioned text in its own fill colour, and images placed by the content stream's transform.

**Draws too:** clipping paths, and axial and radial gradients — whether painted by the `sh`
operator or used as a pattern fill.

**Text uses the file's own embedded font** when that font is a TrueType program, which is what makes
a page in a script the machine has no font for come out as text rather than as a row of empty boxes.
A CFF or Type 1 program, or a font the file does not embed at all, still falls back to a substituted
system face, and its glyph shapes are then close but not exact.

**Does not:** tiling patterns, soft masks, transparency groups, blend modes, or mesh shadings
(types 4 to 7). Each of those is skipped rather than approximated: a mesh drawn as a linear gradient
is a plausible-looking wrong answer, and a tiling pattern drawn as a flat colour floods the shape.

That is still a real limit, and it is why this is a renderer for thumbnails and previews rather than
a viewer. A page that is a report comes out looking like the report — which is what the screenshots
throughout this documentation are.

A rotated or skewed image is drawn upright in its bounding box: wrong, but still recognisable at
thumbnail size, which beats dropping it.

## A web thumbnail endpoint

```csharp
app.MapGet("/thumbnail/{id}", (string id) =>
{
    var path = storage.PathFor(id);
    var png = DocumentRenderer.RenderThumbnail(path, widthPixels: 320);

    return Results.File(png, "image/png");
});
```

Rendering is CPU-bound and allocates a full-page bitmap, so cache the result rather than rendering
per request, and keep `MaxPixels` set.

## Generating documentation screenshots

Every image in this documentation is produced by [`tools/ScreenshotGen`](../tools/ScreenshotGen),
which builds documents with the libraries and renders them here:

```bash
dotnet run --project tools/ScreenshotGen -c Release
```

They are generated rather than captured because a generated screenshot cannot drift away from what
the code does. If a change breaks table shading, the next run shows it.

## See also

- [PdfNet](PdfNet.md) — the document model underneath
- [Unified API](OfficeNet.md) — format detection
