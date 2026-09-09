# WordNet

`.docx` documents for .NET 10 — the library python-docx would be if it were written in C#.

*Bahasa Indonesia: [docs/id/WordNet.md](id/WordNet.md)* · [Back to index](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.WordNet
```

---

![A Word document produced by the code on this page, rendered to PNG](screenshots/wordnet-document.png)

*Every element on that page — the heading styles, the header and footer, the page-number field, the
shaded table header, the justified paragraph — comes from the code in this guide. The image is
rendered from the exported PDF by [`OfficeNet.Rendering`](Rendering.md).*

---

## Opening and saving

```csharp
using WordNet;

using var document = WordDocument.Create();          // a new, empty document
using var existing = WordDocument.Open("input.docx"); // from a path, Stream or byte[]
using var fromTemplate = WordDocument.FromTemplate("template.dotx");

document.Save("output.docx");
document.SaveAsPdf("output.pdf");
```

`WordDocument` edits the XML tree in place. Two handles to the same paragraph cannot disagree, and
parts the library does not model — a custom XML part, a macro, an unusual content control — survive
a round trip byte for byte. That is a deliberate difference from ExcelNet; see
[Core concepts](Core.md#two-models-on-purpose).

## Paragraphs and runs

A paragraph is a sequence of runs; a run is a stretch of text with one set of formatting. That is
WordprocessingML's model and the library does not hide it, because hiding it is what makes
"why is half my sentence bold" impossible to answer.

```csharp
var paragraph = document.AddParagraph();
paragraph.Alignment = ParagraphAlignment.Justify;

paragraph.AddRun("Pendapatan tumbuh ");
paragraph.AddRun("32%", bold: true);
paragraph.AddRun(" dibanding tahun sebelumnya.");
```

Formatting is **tri-state**: `null` means inherit, `false` means explicitly off.

```csharp
run.Format.Bold = true;    // bold
run.Format.Bold = false;   // not bold, even inside a bold style
run.Format.Bold = null;    // whatever the style says
```

That distinction is the only way to write an unbolded word inside a bold heading, which is why the
properties are `bool?` and not `bool`.

```csharp
run.Format.Italic = true;
run.Format.Underline = UnderlineStyle.Single;
run.Format.FontSize = Units.Pt(14);
run.Format.Color = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
run.Format.FontName = "Calibri";
run.Format.Highlight = "yellow";
```

## Headings, lists and page breaks

```csharp
document.AddHeading("Laporan Tahunan", 0);   // 0 is Title
document.AddHeading("Ringkasan", 1);

document.AddList([
    "WordNet — penulisan ulang python-docx",
    "ExcelNet — penulisan ulang openpyxl dan pandas",
]);

document.AddList(["Pertama", "Kedua"], numbered: true);
document.AddList(["Sub-butir"], level: 1);

document.AddPageBreak();
document.AddTableOfContents(levels: 3);
```

A table of contents is a Word *field*. It is written correctly but shows "Update this field" until
the reader refreshes it — Word computes the entries, not the file. That is how every generator
behaves, including Word's own.

## Styles

```csharp
document.AddParagraph("Kutipan.", "Quote");
document.AddParagraph("Sub-judul.", "Subtitle");

var style = document.Styles.GetOrAdd("Catatan", "Catatan", StyleType.Paragraph, basedOn: "Normal");
style.RunFormat.FontSize = Units.Pt(9);
style.RunFormat.Italic = true;
document.AddParagraph("Catatan kaki.", "Catatan");
```

`document.Styles` exposes the styles the document actually has, including those inherited from a
template. `GetOrAdd` returns the existing style when there is one, so calling it on every run of a
batch job is safe; `Add` always creates. A style carries both `RunFormat` (character properties) and
`ParagraphFormat` (spacing, alignment, indentation).

## Tables

```csharp
var table = document.AddTable(new[]
{
    new[] { "Wilayah", "2025", "2026", "Pertumbuhan" },
    new[] { "Jakarta", "1.120", "1.480", "+32%" },
    new[] { "Bandung", "860", "1.150", "+34%" },
});

table.Rows[0].SetShading(OfficeColor.FromRgb(0x1F, 0x38, 0x64));

foreach (var cell in table.Rows[0].Cells)
{
    foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
    {
        run.Format.Color = OfficeColor.White;
    }
}
```

Or build one cell at a time:

```csharp
var grid = document.AddTable(rows: 3, columns: 2);
grid[0, 0].Text = "Nama";
grid[0, 1].Text = "Nilai";
grid.SetColumnWidth(0, Units.Cm(4));
grid.MergeCells(firstRow: 1, firstColumn: 0, lastRow: 1, lastColumn: 1);
```

`SetColumnWidth` also switches the table to a fixed layout. Without that, Word recomputes every
column from the content and the widths you set are ignored — a detail that costs an afternoon if
you meet it the hard way.

`table[r, c]` is **O(row)**: LINQ to XML stores children as a linked list, so reaching row 900 walks
past 899 others, and filling a whole table that way is quadratic. For a large table, walk it once:

```csharp
foreach (var row in table.Rows)
{
    var cells = row.Cells;
    for (var c = 0; c < cells.Count; c++) cells[c].Text = values[c];
}
```

On a 2000-row table that is 36 ms against 236 ms through the indexer.

## Sections, headers and footers

```csharp
var section = document.Section;         // the last section
section.SetPageSize("A4");              // or Letter, Legal, A3, A5
section.SetMargins(Units.Cm(2.2));
section.Orientation = PageOrientation.Landscape;

section.GetHeader().AddParagraph("Gravicode Studios").Alignment = ParagraphAlignment.Right;

var footer = section.GetFooter().AddParagraph();
footer.Alignment = ParagraphAlignment.Center;
footer.AddRun("Halaman ");
footer.AddPageNumber();
footer.AddRun(" dari ");
footer.AddPageCount();

document.AddSection(SectionStart.NextPage);   // a new section from here on
```

Page numbers are fields, and a generated document's cached field result is stale — it says "1" on
every page. The PDF exporter substitutes markers and fills in the real numbers per page, so
`SaveAsPdf` produces correct pagination even though the `.docx` itself carries a stale cache until
Word refreshes it.

## Footnotes, endnotes and comments

```csharp
var paragraph = document.AddParagraph("Pendapatan tumbuh 32% pada 2026.");

var note = paragraph.AddFootnote("Sumber: laporan internal, Januari 2026.");
note.AddParagraph("Angka telah diaudit.");          // a note holds paragraphs, not a string

paragraph.AddEndnote("Lihat lampiran B.");
paragraph.AddComment("Tolong konfirmasi angkanya.", "Kang Fadhil");
```

Reading and removing them:

```csharp
foreach (var note in document.Footnotes.All)
{
    Console.WriteLine($"{note.Id}: {note.Text}");
}

document.Footnotes.Remove(id);                       // also removes the reference in the body
document.Comments.Remove(id);                        // and its range markers

foreach (var comment in document.Comments.ByAuthor("Kang Fadhil")) { /* … */ }
```

A comment can bracket one run rather than the whole paragraph:

```csharp
var run = paragraph.AddRun("angka ini");
run.AddComment("Dari mana asalnya?", "Kang Fadhil");
```

Three things worth knowing:

- **The parts are created on first use.** A document with no notes carries no `footnotes.xml`, which
  is what Word does too.
- **Ids 0 and 1 are reserved.** They hold the separator drawn above the notes and the continuation
  separator; `Footnotes.All` filters them out, and removing one throws.
- **Removing a note removes its reference.** A reference pointing at a deleted note is exactly what
  Word reports as unreadable content, so the two go together.

**In the PDF export**, footnotes are drawn at the foot of the page their reference falls on, under a
short rule, with the reference itself set as a superscript number. **Endnotes are not exported** —
they belong in a block after the last page, which is separate work, and drawing them as footnotes
would put them somewhere they do not belong. **Comments are not exported either**: they are review
metadata rather than content, and Word does not print them by default.

## Finding and replacing text

```csharp
document.ReplaceText("2025", "2026");

// Word splits text across runs at arbitrary points — a spell-check pass alone can do it — so a
// phrase you can see is often not in any single run.
document.ReplaceTextAcrossRuns("Kang Fadhil", "K. Fadhil");

document.MailMerge(new Dictionary<string, string>
{
    ["nama"] = "Budi",
    ["kota"] = "Bandung",
});   // replaces {{nama}} and {{kota}}
```

`ReplaceText` is per-run and fast; `ReplaceTextAcrossRuns` joins the paragraph, replaces, and
redistributes the text while preserving each run's formatting. Use the second when a replacement
"mysteriously does nothing".

## Images

```csharp
document.AddPicture("logo.png", width: Units.Cm(4));
document.AddPicture(bytes, width: Units.Cm(6), height: Units.Cm(3));
```

Aspect ratio is preserved when only one dimension is given. PNG, JPEG, GIF, BMP and TIFF are
recognised by content, not by file extension.

## Text boxes and shapes

A text box is a rectangle with words in it that floats over the page. It is what a pull quote, a
caption, a stamp and a callout all are.

```csharp
using WordNet.Drawing;

var quote = paragraph.AddTextBox(
    "Marjin kotor tetap di kisaran empat puluh persen.",
    width: Units.Cm(6), height: Units.Cm(3));

quote.FillColor = OfficeColor.FromRgb(0xF2, 0xEC, 0xE3);
quote.LineColor = OfficeColor.FromRgb(0x1F, 0x3A, 0x5F);
quote.MoveTo(Units.Cm(9), Units.Cm(0.5));
```

`AddShape` takes a geometry instead: `Rectangle`, `RoundedRectangle`, `Ellipse`, `Triangle`,
`Diamond`, `Hexagon`, `Star`, `RightArrow`, `DownArrow`, `Callout` and `Line`. The `Geometry`
property also accepts any of DrawingML's other 180-odd preset names as a string.

Every shape carries text, so `AddParagraph` works on all of them:

```csharp
var banner = paragraph.AddShape(ShapeGeometry.RoundedRectangle,
    Units.Cm(15), Units.Cm(1.6), TextWrap.TopAndBottom);

banner.FillColor = OfficeColor.FromRgb(0x1F, 0x3A, 0x5F);
banner.LineColor = null;
banner.AddParagraph("Ringkasan Operasional").Runs[0].WithBold().WithColor(OfficeColor.White);
```

### Floating and inline

A drawing is one of two things, and they are different elements in the file:

| | Element | Behaviour |
| --- | --- | --- |
| **Inline** — `wrap: null` | `wp:inline` | laid out as if it were one very large character |
| **Floating** — any other wrap | `wp:anchor` | placed at its own coordinates, text flows around it |

Only a floating object has a position, so `MoveTo` throws on an inline one. `MoveTo` measures from
whatever you name — `HorizontalAnchor.Column` (the default) and `.Page`, `.Margin`, `.Character`;
`VerticalAnchor.Paragraph` (the default) and `.Page`, `.Margin`, `.Line`.

The wraps are `Square` (text down both sides), `Tight` (around the outline), `TopAndBottom` (text
stops above and resumes below), `InFrontOfText` and `BehindText` — the last two ignore each other,
and `BehindText` is what a watermark is.

Pictures float the same way, since the container is what differs and not the content:

```csharp
run.AddPicture("foto.jpg", width: Units.Cm(5), wrap: TextWrap.Square);
run.Drawings[0].MoveTo(Units.Cm(10), Units.Cm(0));
```

`document.Shapes` finds every shape and text box in the body; `run.Drawings` finds the ones in a
single run, pictures included.

### What Word needs, and what it will not tell you

`wp:anchor` carries ten attributes with no schema defaults. Leaving one out does not degrade the
layout — Word reports the whole document as unreadable, without saying which. OfficeNet writes all
of them, which is the main reason to build a shape through this API rather than by hand.

The shape itself lives in the `wps` namespace, a Microsoft extension rather than an ECMA one: the
standard's own answer was VML, deprecated in the release that shipped it. Word 2010 and later, and
LibreOffice, read `wps` directly. Word 2007 does not, and a fallback for it would mean writing every
shape twice inside an `mc:AlternateContent`.

### In the PDF export

Floating objects are exported: the shape is drawn as a path with its fill and outline, its text is
set inside it with Word's own insets and clipped to the box, and the body text flows around the
space it takes.

Two limits worth knowing. `Tight` and `Through` wrap around the bounding box rather than the
outline — visibly close, not identical. And a line is broken around one object rather than several,
so text between two floats goes to the wider side instead of filling both gaps.

## Reading a document

```csharp
using var document = WordDocument.Open("laporan.docx");

Console.WriteLine(document.ExtractText());
Console.WriteLine($"{document.WordCount} kata");

foreach (var paragraph in document.Paragraphs)
{
    Console.WriteLine($"[{paragraph.StyleId}] {paragraph.Text}");
}

foreach (var table in document.Tables)
{
    foreach (var row in table.Rows)
    {
        Console.WriteLine(string.Join(" | ", row.Cells.Select(c => c.Text)));
    }
}
```

`Paragraphs` is the body's top level. `AllParagraphs` also walks into tables, headers and footers —
which is what you want for a word count and not what you want for "the document's outline".

## Metadata

```csharp
document.Properties.Title = "Laporan Tahunan";
document.Properties.Creator = "Gravicode Studios";
document.Properties.Keywords = "laporan; 2026";
document.Properties.Category = "Internal";
```

## PDF export

```csharp
document.SaveAsPdf("laporan.pdf");

document.SaveAsPdf("laporan.pdf", new PdfExportOptions
{
    Watermark = "DRAF",
    WatermarkOpacity = 0.12,
    IncludeHeadersAndFooters = true,
    EvaluatePageFields = true,
});

using var pdf = document.ToPdf();   // a PdfDocument you can merge, encrypt or annotate
```

The exporter resolves style inheritance before laying anything out. A Heading 1 paragraph carries no
direct formatting at all — a converter that reads only a run's own `w:rPr` renders the whole
document as body text, which is the single most common way a home-grown exporter goes wrong.

**What it does:** flowed text with correct line breaking and justification, headings, styles,
numbered and bulleted lists, tables with shading and borders, inline images, headers and footers,
page fields, watermarks, page sizes and margins, sections.

**What it does not:** floating objects positioned by `wrap="square"` (drawn inline), footnotes,
comments, text boxes, and Word's exact hyphenation and kerning. See [Plan.md](../Plan.md).

## Common mistakes

**Setting a property and seeing no change.** Almost always a style is overriding it, or the property
was set on the paragraph when it belongs on the run. `run.Format` is character formatting;
`paragraph.Format` is paragraph formatting. Font size is a run property even when you want it for a
whole paragraph — set it on every run, or make a style.

**A replacement that does nothing.** The phrase is split across runs; use `ReplaceTextAcrossRuns`.

**Column widths ignored.** The table is in autofit layout; `SetColumnWidth` switches it to fixed for
you, but a table you built by hand from XML will not be.

**An empty table cell making Word report a damaged file.** A `w:tc` must contain at least one
block-level element and must end with a paragraph. The library maintains this; hand-edited XML has
to as well.

## See also

- [Core concepts](Core.md) — units, colour, and the OPC package underneath
- [PdfNet](PdfNet.md) — what you can do with the exported PDF
- [Rendering](Rendering.md) — turning a document into a PNG
- `notebooks/WordNet.ipynb` — the same material, runnable
