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

## Embedding a font

The standard 14 fonts cover Latin-1 and nothing else. A document in Javanese, Arabic, Thai or
Chinese — or one that has to use a brand face — needs the font in the file:

```csharp
using PdfNet.Fonts;

using var pdf = PdfDocument.Create();
var font = pdf.EmbedFont("NotoSans-Regular.ttf");

using var canvas = pdf.Pages.Add(PageSize.A4).OpenCanvas();
canvas.SetFont(font, 12);
canvas.DrawText("ꦲꦏ꧀ꦱꦫꦗꦮ", 72, 700);
```

Only the glyphs actually drawn go into the file, and only when the document is saved. Nine scripts
out of a 22 MB font came to a **30 KB PDF** — the subset itself was 38 KB before compression, and
the whole point is that a CJK face has fifty thousand glyphs and a document uses a hundred.

`SetFont` back to a `StandardFont` at any time; the two share a page happily. `MeasureText` uses
whichever is selected.

### Before you write the page

```csharp
if (!font.CanRender(text))
{
    Console.WriteLine("no glyph for: " + string.Join(", ", font.MissingCharacters(text)));
}
```

A character the font has no glyph for is drawn as an empty box, and finding out at that point costs
a reprint. `TrueTypeFont.Load` also reports `EmbeddingRestricted`, which is the font publisher's own
`fsType` flag — this library reports it rather than enforcing it, because the licence is between you
and the publisher.

### What is written, and why it looks like that

The font goes in as a **composite**: a `/Type0` with `/Identity-H` encoding over a `/CIDFontType2`
descendant. That is the only arrangement that reaches past 256 characters without encoding
gymnastics, and it is what every modern producer emits.

It has one consequence that must be paid for. Text is written as two-byte **glyph ids**, so a reader
extracting it sees numbers and has no idea what they mean — the text would be unsearchable and
uncopyable. The `/ToUnicode` CMap written alongside is what maps them back, so it is always written
and never optional.

Two smaller decisions worth knowing:

- **Glyphs are renumbered** densely from zero, and a `/CIDToGIDMap` translates. Keeping the original
  ids would be simpler and would make `loca` and `hmtx` span the highest id used — a document with
  one CJK glyph at id 40,000 would pay 320 KB for the gap.
- **Composite glyphs bring their components.** An "é" is usually an "e" and an accent, referenced by
  glyph id. A subset that does not follow those references produces a font whose accented characters
  are blank, with nothing anywhere to say why.

**Not supported:** OpenType fonts with PostScript outlines (`.otf` with a `CFF ` table) and TrueType
collections (`.ttc`). Both are refused by name rather than loaded into a PDF with no glyphs in it.

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

## Digital signatures

### Checking one

```csharp
using PdfNet.Security;

foreach (var result in PdfSignatures.VerifyAll("laporan-signed.pdf"))
{
    Console.WriteLine($"{result.Signature.Name}: {result}");
}
```

Three separate answers, kept apart because they are genuinely different questions:

| | Means |
| --- | --- |
| `DigestMatches` | the bytes still hash to what the signature says |
| `CoversWholeDocument` | the signature covers the whole file, not part of it |
| `IsIntact` | both of the above |

**A signature can be cryptographically perfect and still not protect the document.** `/ByteRange`
says which parts of the file were hashed, and nothing forces it to cover all of them — a file can
carry a genuine signature over its first half and arbitrary unsigned content after it. That is a
real attack, and it is why `DigestMatches` alone is never the answer.

What none of these say is whether the **signer** should be trusted. Chain validation, expiry and
revocation need a trust store and usually a network, and every organisation answers them
differently, so the certificate is handed back to answer them properly:

```csharp
if (result.IsIntact && result.Certificate is { } certificate)
{
    using var chain = new X509Chain();
    chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;

    var trusted = chain.Build(certificate);
}
```

### Signing

```csharp
using var certificate = X509CertificateLoader.LoadPkcs12FromFile("signer.pfx", "password");
using var pdf = PdfDocument.Open("laporan.pdf");

PdfSigner.Sign(pdf, certificate, "laporan-signed.pdf", new PdfSignOptions
{
    Reason = "Disetujui",
    Location = "Jakarta",
});
```

The certificate needs its private key — a `.pfx` or `.p12`, not a `.cer` — and saying so is what
you get instead of a file that looks signed and is not. SHA-256 by default; SHA-384 and SHA-512 are
available and SHA-1 is not, because it has been unsafe for signatures since 2017 and offering it
would mean seeing it used.

`ReservedBytes` sizes the hole the signature goes into, 8 KB by default. A signature that does not
fit is refused rather than truncated.

### Why the order looks strange

A signature covers a byte range of the finished file, and it cannot cover itself. So the file is
written first with a hole where the signature will go and placeholders where the byte range will go;
only then can the hash be taken and the signature spliced in. **Nothing may change length** during
that splice, which is why the byte range is written as fixed-width numbers padded with spaces.

Signing a document that already has a signature works: the placeholders are found by their sentinel
value rather than by position, so the new range is filled in and the existing signature is left
alone. The first signature then reports `CoversWholeDocument = false`, which is the honest answer —
that is what an incrementally signed document looks like.

**What this does not produce:** a trusted timestamp, a revocation response, or a long-term-validation
archive. Those are what make a signature PAdES-LTV, they need a network service, and their absence is
why a signature checked years from now may fail even though nothing was tampered with.

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
