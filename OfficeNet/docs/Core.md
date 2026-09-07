# Core concepts

`OfficeNet.Core` is the layer the four format libraries share: the OPC container, units, colour,
image sniffing and metadata. Read this once and the other libraries stop surprising you.

*Bahasa Indonesia: [docs/id/Core.md](id/Core.md)* · [Back to index](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

---

## The OPC package

`.docx`, `.xlsx` and `.pptx` are the same container with different contents: a ZIP archive holding
XML parts, a `[Content_Types].xml` manifest that gives every part a MIME type, and `.rels` files that
say which part points at which. That container is genuinely identical across the three formats, so
it is implemented once here rather than three times.

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

You rarely need this directly — but when a file behaves oddly, opening the package and listing the
parts is the fastest way to see why.

Two rules the package layer enforces, because breaking either produces a file Office calls damaged:

- **Every part must have a content type**, either by extension default or by an explicit override.
- **A relationship target must resolve.** A dangling `r:id` is not ignored; it is an error.

## Units

Every length in OfficeNet is a `Length`, stored in **EMU** — English Metric Units, 914,400 per inch
and 360,000 per centimetre. That number is chosen so inches, centimetres, points and twips all divide
exactly, which means converting between them never accumulates error.

```csharp
using OfficeNet.Core;

var width = Units.Cm(2.5);
var margin = Units.Inches(1);
var size = Units.Pt(11);
var indent = Units.Twips(720);
var pixels = Units.Px(96);          // at 96 DPI
var at300 = Length.FromPixels(96, dpi: 300);

double cm = width.Centimeters;
double points = width.Points;
long twips = width.Twips;
long emu = width.Emu;

var total = width + margin;
var half = width / 2;
```

The catch is that **the file formats store coarser units than EMU**, so a round trip quantises:

| Property | Stored as |
|---|---|
| Page size, margins, indents | twips (1/1440 in) |
| Font size (run) | half-points |
| Font size (DrawingML) | centipoints |
| Border width | eighths of a point |
| Image and shape geometry | EMU |

So 1.5 cm becomes 850 twips, which reads back as 1.4993 cm. That is not a bug and not fixable — it
is what the format stores. Compare lengths with a tolerance of one twip rather than for exact
equality.

One trap worth naming: `w:sz` means **half-points on a run** and **eighths of a point on a border**.
Same attribute name, two units.

## Colour

```csharp
using OfficeNet.Core.Drawing;

var navy = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
var alsoNavy = OfficeColor.FromRgb(0x1F3864);
var parsed = OfficeColor.Parse("1F3864");        // or "#1F3864"

OfficeColor.TryParse(value, out var color);

var themed = OfficeColor.FromTheme(ThemeColor.Accent1);
var lighter = OfficeColor.FromTheme(ThemeColor.Accent1, luminanceModulation: 0.4);

OfficeColor.Black; OfficeColor.White; OfficeColor.Red;
OfficeColor.Green; OfficeColor.Blue; OfficeColor.Yellow; OfficeColor.Gray;

string hex = navy.ToHex();      // "1F3864"
```

`OfficeColor.Automatic` is a real value, not a null stand-in: it means "let the consumer decide",
which for text usually resolves to black on white and white on a dark background. `ToHex()` returns
`"auto"` for it, which is what the formats expect.

A **theme colour** follows the document's theme; an explicit RGB does not. That is the whole
difference, and it is why restyling a deck changes some shapes and not others.

## Metadata

The same three property sets exist in all three OOXML formats:

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

## Images

Format is detected from the bytes, never from the file extension — a `.png` that is really a JPEG is
common enough to matter:

```csharp
var info = ImageInfo.Read(bytes);
Console.WriteLine($"{info.Format} {info.PixelWidth}x{info.PixelHeight} @ {info.HorizontalDpi} DPI");
Console.WriteLine(info.NaturalWidth.Centimeters);
```

PNG, JPEG, GIF, BMP and TIFF are recognised, with pixel dimensions and DPI where the format records
them. DPI is what turns pixels into a physical size, so an image inserted "at natural size" lands
correctly instead of at an arbitrary 96 DPI guess.

## Two models, on purpose

WordNet and ExcelNet are built differently, and the difference is deliberate:

**WordNet edits the XML tree live.** A `Paragraph` holds its own `w:p` element. Two handles to one
paragraph cannot disagree, and parts the library does not understand — a content control, a macro, a
custom XML part — survive a round trip byte for byte. A Word document is a tree of meaningful,
heterogeneous nodes, and preserving what you did not parse matters.

**ExcelNet parses into a model and writes it back.** A worksheet's XML is a flat list of rows with
nothing worth preserving byte-for-byte, and a dictionary makes random cell access O(1) instead of a
scan of the sheet.

Do not "unify" these. The trade is different in each case, and unifying means picking the wrong
answer for one of them.

## Strict versus transitional OOXML

ECMA-376 Strict uses different namespace URIs from the Transitional variant that Office writes by
default. A strict `.docx` opened by a reader that only knows transitional namespaces comes back with
**zero paragraphs and no error at all**.

OfficeNet normalises strict namespaces to transitional at load, so both open the same way. If you
ever write your own OOXML reader, this is the silent failure to guard against first.

## Element order is a schema *sequence*

OOXML content models are ordered sequences, not choices. A `w:rPr` that lists `w:sz` before `w:b` is
not reordered and not ignored — Word reports the file as unreadable content.

Every property setter in OfficeNet goes through a helper that inserts in schema order. It is the
single most common way hand-written OOXML produces a "damaged file" dialogue, and the reason the
libraries never simply append a child element.

## Exceptions

Everything throws `OfficeNetException` or a subclass, so a batch job can catch one type:

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

## See also

- [WordNet](WordNet.md) · [ExcelNet](ExcelNet.md) · [PowerPointNet](PowerPointNet.md) · [PdfNet](PdfNet.md)
- [Unified API](OfficeNet.md)
