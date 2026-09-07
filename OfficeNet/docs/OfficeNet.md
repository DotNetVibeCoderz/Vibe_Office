# Unified API

One entry point when you do not know — or do not care — which format a file is.

*Bahasa Indonesia: [docs/id/OfficeNet.md](id/OfficeNet.md)* · [Back to index](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet
```

The `Gravicode.OfficeNet` package references all four format libraries and adds the `Office` static
class. Install it when you are handling whatever users upload; install the individual packages when
you know the format up front and want a smaller dependency graph.

---

## Detecting a format

```csharp
using OfficeNet;

var format = Office.DetectFormat("berkas.docx");   // path, Stream or byte[]

Console.WriteLine(format);   // Word, Excel, PowerPoint, Pdf, or Unknown
```

Detection reads the content, not the extension. A `.docx` that is really a `.xlsx` — which happens
whenever someone renames a file to get past an upload filter — is reported as Excel.

```csharp
if (Office.IsSupportedExtension(path))
{
    // …
}

Console.WriteLine(string.Join(", ", Office.SupportedExtensions));
```

## Opening without knowing the type

```csharp
using var document = Office.Open("berkas.pptx");   // IOfficeDocument

Console.WriteLine(document.ExtractText());
Console.WriteLine(document.Properties.Title);

switch (document)
{
    case WordNet.WordDocument word:
        Console.WriteLine($"{word.WordCount} kata");
        break;

    case ExcelNet.Workbook workbook:
        Console.WriteLine($"{workbook.Count} lembar");
        break;

    case PowerPointNet.Presentation deck:
        Console.WriteLine($"{deck.SlideCount} slide");
        break;
}
```

`IOfficeDocument` carries what the three OOXML formats genuinely share — text extraction, core
properties, saving — and nothing more. Anything format-specific needs the concrete type, which is
what the pattern match above is for.

## Extracting text from anything

```csharp
string text = Office.ExtractText("berkas.pdf");
```

Works for `.docx`, `.xlsx`, `.pptx` and `.pdf`. This is the one-liner behind most search-indexing
jobs.

Line endings are always `\n`, on every platform. That sounds obvious and is not: using
`StringBuilder.AppendLine` would emit `\r\n` on Windows and `\n` on Linux, so the same document would
produce different text — and different hashes — depending on where the job ran.

## Converting to PDF

```csharp
Office.ConvertToPdf("laporan.docx");                    // → laporan.pdf
Office.ConvertToPdf("data.xlsx", "keluaran/data.pdf");
```

Returns the path it wrote. Word, Excel and PowerPoint each convert through their own exporter; a
`.pdf` input is copied.

## A batch converter

```csharp
using OfficeNet;

var failures = new List<(string Path, string Reason)>();

foreach (var path in Directory.EnumerateFiles("masuk", "*.*", SearchOption.AllDirectories))
{
    if (!Office.IsSupportedExtension(path))
    {
        continue;
    }

    try
    {
        var pdf = Office.ConvertToPdf(path, Path.Combine("keluar",
            Path.GetFileNameWithoutExtension(path) + ".pdf"));

        Console.WriteLine($"OK   {Path.GetFileName(pdf)}");
    }
    catch (OfficeNetException ex)
    {
        // One malformed file in a thousand must not stop the other 999.
        failures.Add((path, ex.Message));
        Console.WriteLine($"GAGAL {Path.GetFileName(path)}: {ex.Message}");
    }
}
```

A working version of this is in [`samples/BatchConverter.Console`](../samples).

## Thumbnails for an upload

Combine with [`OfficeNet.Rendering`](Rendering.md):

```csharp
using OfficeNet;
using OfficeNet.Rendering;

if (!Office.IsSupportedExtension(uploaded))
{
    return Results.BadRequest("Format tidak didukung.");
}

var png = DocumentRenderer.RenderThumbnail(uploaded, widthPixels: 320);
return Results.File(png, "image/png");
```

## See also

- [Core concepts](Core.md) — what the formats share
- [Rendering](Rendering.md) — images from any of them
- The four format guides: [WordNet](WordNet.md) · [ExcelNet](ExcelNet.md) ·
  [PowerPointNet](PowerPointNet.md) · [PdfNet](PdfNet.md)
