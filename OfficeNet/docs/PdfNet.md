# PdfNet

PDF for .NET 10 — read, write, merge, split, extract, encrypt, fill forms, annotate and draw. What
PyPDF2 does, plus a canvas.

*Bahasa Indonesia: [docs/id/PdfNet.md](id/PdfNet.md)* · [Back to index](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.PdfNet
```

---

![A PDF exported from a Word document, rendered to PNG](screenshots/pdfnet-export.png)

*The Word document from the [WordNet guide](WordNet.md), exported through PdfNet. Word, Excel and
PowerPoint all export through this library — it is the leaf of the dependency graph and never
depends back on them.*

---

## Opening and saving

```csharp
using PdfNet.Document;

using var document = PdfDocument.Create();
using var existing = PdfDocument.Open("input.pdf");
using var locked = PdfDocument.Open("locked.pdf", password: "rahasia");

document.Save("output.pdf");
byte[] bytes = document.ToArray();

Console.WriteLine(document.WasEncrypted);   // it was, and was decrypted to open
Console.WriteLine(document.WasRepaired);    // the xref was broken and got rebuilt
```

`WasRepaired` is worth checking in a pipeline. A file that only opens after reconstruction is a file
whose producer had a bug, and it is usually the one that will surprise you later.

## Pages

```csharp
var page = document.Pages.Add(PageSize.A4);
document.Pages.Add(PageSize.Letter);
document.Pages.Add(PageSize.Points(400, 600));

Console.WriteLine(document.Pages.Count);
var first = document.Pages[0];

first.Rotation = 90;                  // 0, 90, 180, 270
Console.WriteLine(first.Width);       // points
Console.WriteLine(first.MediaBox);

document.Pages.RemoveAt(2);
document.Pages.Move(0, 3);
```

## Merge and split

```csharp
using var a = PdfDocument.Open("bagian-1.pdf");
using var b = PdfDocument.Open("bagian-2.pdf");

a.Merge(b);                            // append every page
a.MergeRange(b, startIndex: 2, count: 3);
a.Save("gabungan.pdf");

using var merged = PdfDocument.ConcatFiles(["a.pdf", "b.pdf", "c.pdf"]);

var singles = document.Split();        // one document per page
var chunks = document.Split(pagesPerPart: 10);
```

Merging rewrites every object number in the imported document and rebuilds the references, so two
files that both call their font object `7 0 R` do not collide.

## Text extraction

```csharp
Console.WriteLine(document.ExtractText());
Console.WriteLine(document.Pages[0].ExtractText());

foreach (var fragment in document.Pages[0].ExtractTextFragments())
{
    Console.WriteLine($"{fragment.Text} @ ({fragment.X}, {fragment.Y}) {fragment.FontSize}pt");
}
```

A PDF does not contain text in reading order — it contains drawing instructions. Extraction means
replaying the text-positioning operators, grouping runs into lines by baseline, and *inferring the
spaces the file never stored*, because a producer that positions each word with `Td` writes no space
characters at all. That is why two tools disagree on the same file.

Two consequences worth knowing:

- **Invisible text is extracted.** Render mode 3 is how a scanner stores its OCR layer, and skipping
  it would lose the only text a scanned page has.
- **A composite font with no `/ToUnicode` cannot be decoded.** PdfNet emits nothing rather than
  emitting raw codes that look like CJK noise. Nothing is honest; noise is not.

## Images

```csharp
foreach (var image in document.Pages[0].ExtractImages())
{
    Console.WriteLine($"{image.Name}: {image.Width}x{image.Height} {image.Extension}");
    image.SaveTo("keluaran");
}
```

## Drawing

```csharp
using PdfNet.Content;

var page = document.Pages.Add(PageSize.A4);
using var canvas = page.OpenCanvas();

canvas.TopDown = true;      // measure from the top, like every Office format does

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

`TopDown` is set by every OfficeNet exporter. PDF's origin is bottom-left and every Office format
measures from the top; converting at each call site rather than once here is exactly how a converter
ends up flipping some elements and not others.

The 14 standard fonts need no embedding and their metrics are built in, so `MeasureText` is accurate
without any font files present. Embedding a TrueType font is on the [roadmap](../Plan.md).

## Encryption

```csharp
document.Encrypt("rahasia");                       // user password, AES-256

document.Encrypt(
    userPassword: "buka",
    ownerPassword: "pemilik",
    permissions: PdfPermissions.Print | PdfPermissions.Copy,
    cipher: PdfCipher.Aes256);

document.Decrypt();                                // remove it
```

Supported: RC4 40/128-bit (revisions 2–4), AES-128 (revision 4) and AES-256 (revision 6). Decryption
handles all of them; new documents default to AES-256.

Two things that make encryption subtle, both handled here and both worth knowing if you inspect a
file by hand: objects inside an object stream are *not* individually encrypted — the container
already was, so decrypting twice yields garbage that still parses — and an XRef stream is never
encrypted at all.

## Forms

```csharp
var form = AcroForm.Open(document);        // null when the file has no form
var created = AcroForm.OpenOrCreate(document);

foreach (var field in form!.Fields)
{
    Console.WriteLine($"{field.FullName} ({field.FieldType}) = {field.Value}");
}

form["nama"]?.SetValue("Budi Santoso");
form["setuju"]?.SetValue("Yes");        // a checkbox takes one of its on-states

form.Flatten();                          // burn values into the page, remove the fields
```

`Flatten` is the one to reach for before sending a filled form somewhere. A field's *value* and its
*appearance* are separate in PDF, so a viewer that does not regenerate appearances shows an empty
box over the right answer.

`field.OnStates` lists what a checkbox will actually accept — it is rarely just `"Yes"`.

## Annotations

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

Highlight quad points go upper-left, upper-right, lower-left, lower-right. Clockwise order — the
order that feels right — draws a bowtie.

## Metadata

```csharp
document.Info.Title = "Laporan Tahunan";
document.Info.Author = "Gravicode Studios";
document.Info.Subject = "Ringkasan 2026";
document.Info.Keywords = "laporan; 2026";
```

## Rendering to images

Rasterisation lives in a separate package so PdfNet itself stays free of native dependencies:

```csharp
using OfficeNet.Rendering;

var png = DocumentRenderer.RenderPage(document.Pages[0], new RenderOptions { Dpi = 150 });
```

See the [Rendering guide](Rendering.md).

## Robustness

Real PDFs are frequently malformed, and PdfNet assumes so:

- `/Length` is routinely an indirect reference and routinely wrong, so the parser verifies that
  `endstream` really follows before trusting it.
- A broken or missing xref table triggers a full scan of the file for `obj` markers, and
  `WasRepaired` says it happened.
- `/Predictor` on a Flate stream is not optional. Ignoring it inflates cleanly and produces
  completely wrong bytes — in an xref stream that means every object offset is garbage.

## Common mistakes

**Text extraction returns nothing.** The page is a scan with no OCR layer. There is no text to find;
check `ExtractImages`.

**A filled form looks empty.** Call `Flatten()`, or open it in a viewer that regenerates
appearances.

**Coordinates are upside down.** Set `canvas.TopDown = true`, or measure from the bottom.

**A merged document is enormous.** Fonts and images are not deduplicated across merged files yet.

## See also

- [Rendering](Rendering.md) — pages to PNG
- [Unified API](OfficeNet.md) — converting any supported format to PDF
- `notebooks/PdfNet.ipynb` — the same material, runnable
