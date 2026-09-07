# OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
"""Generates the Polyglot notebooks in notebooks/.

The notebooks are generated rather than hand-written because .ipynb is JSON with per-cell
metadata that has to be exactly right for the .NET Interactive kernel to pick up C#. Writing
that by hand once is fine; keeping five of them consistent by hand is not.

    python tools/gen_notebooks.py
"""

import json
import os

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "notebooks")

KERNEL_META = {
    "kernelspec": {"display_name": ".NET (C#)", "language": "C#", "name": ".net-csharp"},
    "language_info": {
        "file_extension": ".cs",
        "mimetype": "text/x-csharp",
        "name": "C#",
        "pygments_lexer": "csharp",
        "version": "12.0",
    },
    "polyglot_notebook": {"kernelInfo": {"defaultKernelName": "csharp", "items": [
        {"aliases": [], "name": "csharp"}]}},
}

CELL_META = {
    "dotnet_interactive": {"language": "csharp"},
    "polyglot_notebook": {"kernelName": "csharp"},
}


def md(text):
    return {"cell_type": "markdown", "metadata": {}, "source": text.strip("\n").splitlines(True)}


def cs(code):
    return {
        "cell_type": "code",
        "execution_count": None,
        "metadata": dict(CELL_META),
        "outputs": [],
        "source": code.strip("\n").splitlines(True),
    }


def write(name, cells):
    notebook = {"cells": cells, "metadata": KERNEL_META, "nbformat": 4, "nbformat_minor": 4}
    path = os.path.join(OUT, name)

    with open(path, "w", encoding="utf-8") as handle:
        json.dump(notebook, handle, indent=1, ensure_ascii=False)
        handle.write("\n")

    print(f"  {name}")


# The reference cell every notebook opens with. Local DLLs rather than `#r "nuget:"` so the
# notebooks run against the working tree — the version on NuGet is one release behind whatever
# you are editing, which is exactly the wrong thing to be testing against.
def refs(*projects):
    lines = ["// Build first:  dotnet build OfficeNet.sln -c Release", ""]

    for project, assembly in projects:
        lines.append(f'#r "../src/{project}/bin/Release/net10.0/{assembly}.dll"')

    lines += [
        "",
        "// Published instead? Swap the lines above for:",
        '//   #r "nuget: Gravicode.OfficeNet, *"',
    ]

    return "\n".join(lines)


CORE = ("OfficeNet.Core", "Gravicode.OfficeNet.Core")
PDF = ("PdfNet", "Gravicode.OfficeNet.PdfNet")
WORD = ("WordNet", "Gravicode.OfficeNet.WordNet")
EXCEL = ("ExcelNet", "Gravicode.OfficeNet.ExcelNet")
PPT = ("PowerPointNet", "Gravicode.OfficeNet.PowerPointNet")
RENDER = ("OfficeNet.Rendering", "Gravicode.OfficeNet.Rendering")
META = ("OfficeNet", "Gravicode.OfficeNet")

# A display helper shared by the notebooks that show a rendered page. Defined once here so the
# five notebooks cannot drift apart.
SHOW = """
using OfficeNet.Rendering;
using Microsoft.DotNet.Interactive.Formatting;

// Renders a document and shows the first page inline, so a cell's effect is visible rather than
// described. Base64 in an <img> because the notebook has nowhere to serve a file from.
void Show(string path, int width = 520)
{
    var png = DocumentRenderer.RenderThumbnail(path, width);
    var data = Convert.ToBase64String(png);

    display(HTML($"<img src='data:image/png;base64,{data}' style='border:1px solid #ddd' />"));
}

var work = Path.Combine(Path.GetTempPath(), "officenet-notebook");
Directory.CreateDirectory(work);
string At(string name) => Path.Combine(work, name);

Console.WriteLine($"Berkas ditulis ke / files written to: {work}");
"""


def quickstart():
    return [
        md("""
# OfficeNet — Mulai Cepat / Quick Start

**ID** — Word, Excel, PowerPoint, dan PDF untuk .NET 10. Notebook ini menyentuh keempatnya dalam
beberapa sel. Untuk pendalaman, buka notebook per library.

**EN** — Word, Excel, PowerPoint and PDF for .NET 10. This notebook touches all four in a few
cells. For depth, open the per-library notebooks.

> Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Jalankan `dotnet build OfficeNet.sln -c Release` sekali sebelum sel pertama. /
Run `dotnet build OfficeNet.sln -c Release` once before the first cell.
"""),
        cs(refs(CORE, PDF, WORD, EXCEL, PPT, META, RENDER)),
        cs(SHOW),
        md("""
## Word

Satu dokumen dengan heading, paragraf, dan tabel. /
One document with a heading, a paragraph and a table.
"""),
        cs("""
using WordNet;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;

using (var document = WordDocument.Create())
{
    document.AddHeading("Laporan Tahunan", 0);
    document.AddParagraph("Dibuat oleh Gravicode Studios.", "Subtitle");

    document.AddHeading("Pendapatan", 1);

    var table = document.AddTable(new[]
    {
        new[] { "Wilayah", "2025", "2026" },
        new[] { "Jakarta", "1.120", "1.480" },
        new[] { "Bandung", "860", "1.150" },
    });

    table.Rows[0].SetShading(OfficeColor.FromRgb(0x1F, 0x38, 0x64));

    foreach (var cell in table.Rows[0].Cells)
        foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
            run.Format.Color = OfficeColor.White;

    document.Save(At("laporan.docx"));
}

Show(At("laporan.docx"));
"""),
        md("""
## Excel

Perhatikan `Recalculate()`: tanpa itu, hasil formula tersimpan sebagai nol untuk setiap konsumen
selain Excel. /
Note `Recalculate()`: without it the cached results are zero for every consumer except Excel.
"""),
        cs("""
using ExcelNet;
using ExcelNet.Styles;

using (var workbook = Workbook.Create("Penjualan"))
{
    var sheet = workbook["Penjualan"];
    sheet.WriteHeader("A1", ["Produk", "Qty", "Harga", "Total"]);

    string[] products = ["WordNet", "ExcelNet", "PowerPointNet", "PdfNet"];

    for (var i = 0; i < products.Length; i++)
    {
        var row = i + 1;
        sheet[row, 0].Set(products[i]);
        sheet[row, 1].Set((i + 2) * 7);
        sheet[row, 2].Set(150_000.0).WithNumberFormat(NumberFormats.Rupiah);
        sheet[row, 3].SetFormula($"B{row + 1}*C{row + 1}").WithNumberFormat(NumberFormats.Rupiah);
    }

    sheet["C6"].Set("TOTAL").Bold();
    sheet["D6"].SetFormula("SUM(D2:D5)").WithNumberFormat(NumberFormats.Rupiah);

    sheet.AutoFitColumns();
    workbook.Recalculate();

    Console.WriteLine($"Total = {sheet["D6"].Number:N0}");
    workbook.Save(At("penjualan.xlsx"));
}

Show(At("penjualan.xlsx"), 640);
"""),
        md("""
## PowerPoint

Chart-nya native: angkanya ikut serta di dalam berkas. /
The chart is native: the numbers travel inside the file.
"""),
        cs("""
using PowerPointNet;
using PowerPointNet.Charts;
using OfficeNet.Core.Charts;

using (var deck = Presentation.Create())
{
    deck.AddTitleSlide("OfficeNet", "Empat format, satu API");

    // Layout 2 is "Title Only": the title sits at the top and the rest of the slide is free.
    var slide = deck.AddSlide(2);
    slide.SetTitle("Pendapatan per Wilayah");

    slide.AddChart(new ChartData
    {
        Type = ChartType.Column,
        Categories = ["Jakarta", "Bandung", "Surabaya"],
        Series = [new ChartSeries("2026", [1480, 1150, 905])],
        ValueFormat = "#,##0",
        ShowDataLabels = true,
    });

    deck.Save(At("deck.pptx"));
}

Show(At("deck.pptx"), 640);
"""),
        md("""
## PDF, dan konversi lintas format / PDF, and cross-format conversion
"""),
        cs("""
using OfficeNet;

foreach (var name in new[] { "laporan.docx", "penjualan.xlsx", "deck.pptx" })
{
    var pdf = Office.ConvertToPdf(At(name));
    Console.WriteLine($"{name,-20} -> {Path.GetFileName(pdf)}  ({new FileInfo(pdf).Length / 1024.0:0.0} KB)");
}

Console.WriteLine();
var text = Office.ExtractText(At("laporan.docx"));
Console.WriteLine(text[..Math.Min(120, text.Length)] + "…");
"""),
        md("""
## Selanjutnya / Next

- [`WordNet.ipynb`](WordNet.ipynb) · [`ExcelNet.ipynb`](ExcelNet.ipynb) ·
  [`PowerPointNet.ipynb`](PowerPointNet.ipynb) · [`PdfNet.ipynb`](PdfNet.ipynb)
- Dokumentasi lengkap: [`docs/`](../docs/README.md) — [Bahasa Indonesia](../docs/id/README.md)
"""),
    ]


def wordnet():
    return [
        md("""
# WordNet

**ID** — Dokumen `.docx`: paragraf, style, tabel, section, mail merge, ekspor PDF.
**EN** — `.docx` documents: paragraphs, styles, tables, sections, mail merge, PDF export.

> Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Panduan lengkap / full guide: [`docs/WordNet.md`](../docs/WordNet.md) ·
[Bahasa Indonesia](../docs/id/WordNet.md)
"""),
        cs(refs(CORE, PDF, WORD, RENDER)),
        cs(SHOW),
        md("""
## Paragraf dan run / Paragraphs and runs

Format bersifat tiga keadaan: `null` mewarisi, `false` mematikan secara eksplisit. Itulah satu-satunya
cara menulis kata tidak tebal di dalam heading tebal. /
Formatting is tri-state: `null` inherits, `false` is explicitly off. That is the only way to write an
unbolded word inside a bold heading.
"""),
        cs("""
using WordNet;
using WordNet.Styles;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;

var document = WordDocument.Create();

var paragraph = document.AddParagraph();
paragraph.Alignment = ParagraphAlignment.Justify;
paragraph.AddRun("Pendapatan tumbuh ");
paragraph.AddRun("32%", bold: true);
paragraph.AddRun(" dibanding tahun sebelumnya, ditopang permintaan yang kuat di Jakarta.");

var emphasis = document.AddParagraph().AddRun("Miring, bergaris bawah, berwarna.");
emphasis.Format.Italic = true;
emphasis.Format.Underline = UnderlineStyle.Single;
emphasis.Format.Color = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
emphasis.Format.FontSize = Units.Pt(13);

document.ExtractText()
"""),
        md("""
## Heading, daftar, style / Headings, lists, styles
"""),
        cs("""
document.AddHeading("Komponen", 1);

document.AddList([
    "WordNet — python-docx",
    "ExcelNet — openpyxl + pandas",
    "PowerPointNet — python-pptx",
    "PdfNet — PyPDF2",
]);

document.AddHeading("Langkah", 1);
document.AddList(["Pasang paket", "Tulis kode", "Simpan"], numbered: true);

var style = document.Styles.GetOrAdd("Catatan", "Catatan", StyleType.Paragraph, basedOn: "Normal");
style.RunFormat.FontSize = Units.Pt(9);
style.RunFormat.Italic = true;

document.AddParagraph("Angka bersifat ilustratif.", "Catatan");

document.Styles.All.Count()
"""),
        md("""
## Tabel / Tables

`SetColumnWidth` juga mengalihkan tabel ke layout tetap — tanpa itu Word menghitung ulang setiap
kolom dan lebar yang Anda set diabaikan. /
`SetColumnWidth` also switches the table to fixed layout — without it Word recomputes every column
and your widths are ignored.
"""),
        cs("""
document.AddHeading("Pendapatan per Wilayah", 1);

var table = document.AddTable(new[]
{
    new[] { "Wilayah", "2025", "2026", "Pertumbuhan" },
    new[] { "Jakarta", "1.120", "1.480", "+32%" },
    new[] { "Bandung", "860", "1.150", "+34%" },
    new[] { "Surabaya", "740", "905", "+22%" },
});

table.Rows[0].SetShading(OfficeColor.FromRgb(0x1F, 0x38, 0x64));

foreach (var cell in table.Rows[0].Cells)
    foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
        run.Format.Color = OfficeColor.White;

$"{table.Count} baris x {table.ColumnCount} kolom"
"""),
        md("""
## Header, footer, dan nomor halaman / Headers, footers and page numbers

Nomor halaman adalah *field*. Cache-nya pada dokumen buatan program selalu basi — pengekspor PDF
mengisinya per halaman. /
Page numbers are *fields*. A generated document's cache is always stale — the PDF exporter fills in
the real numbers per page.
"""),
        cs("""
var section = document.Section;
section.SetPageSize("A4");
section.SetMargins(Units.Cm(2.2));

section.GetHeader().AddParagraph("Gravicode Studios").Alignment = ParagraphAlignment.Right;

var footer = section.GetFooter().AddParagraph();
footer.Alignment = ParagraphAlignment.Center;
footer.AddRun("Halaman ");
footer.AddPageNumber();
footer.AddRun(" dari ");
footer.AddPageCount();

document.Properties.Title = "Laporan Tahunan";
document.Properties.Creator = "Gravicode Studios";

document.Save(At("wordnet.docx"));
Show(At("wordnet.docx"));
"""),
        md("""
## Mail merge dan penggantian teks / Mail merge and replacement

Word memecah teks antar-run di titik sembarang, jadi frasa yang Anda lihat sering tidak ada di satu
run pun — itulah gunanya `ReplaceTextAcrossRuns`. /
Word splits text across runs at arbitrary points, so a phrase you can see is often in no single
run — that is what `ReplaceTextAcrossRuns` is for.
"""),
        cs("""
using var letter = WordDocument.Create();
letter.AddParagraph("Kepada {{nama}} di {{kota}},");
letter.AddParagraph("Terima kasih atas pesanan Anda pada tahun 2025.");

letter.MailMerge(new Dictionary<string, string>
{
    ["nama"] = "Budi Santoso",
    ["kota"] = "Bandung",
});

letter.ReplaceTextAcrossRuns("2025", "2026");

letter.ExtractText()
"""),
        md("""
## Ekspor PDF / PDF export
"""),
        cs("""
document.SaveAsPdf(At("wordnet.pdf"), new WordNet.Export.PdfExportOptions
{
    Watermark = "DRAF",
    WatermarkOpacity = 0.10,
});

Show(At("wordnet.pdf"));
"""),
        cs("""
document.Dispose();
"""),
    ]


def excelnet():
    return [
        md("""
# ExcelNet

**ID** — Workbook `.xlsx`: sel, formula, style, format bersyarat, CSV/JSON, DataFrame.
**EN** — `.xlsx` workbooks: cells, formulas, styles, conditional formatting, CSV/JSON, DataFrames.

> Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Panduan lengkap / full guide: [`docs/ExcelNet.md`](../docs/ExcelNet.md) ·
[Bahasa Indonesia](../docs/id/ExcelNet.md)
"""),
        cs(refs(CORE, PDF, EXCEL, RENDER)),
        cs(SHOW),
        md("""
## Sel bertipe / Typed cells

`Set(object?)` memilih tipe sel dari tipe runtime. Tanggal adalah angka *ditambah* format angka —
memang hanya itu arti tanggal di spreadsheet. /
`Set(object?)` picks the cell type from the runtime type. A date is a number *plus* a number
format — that is all a date is in a spreadsheet.
"""),
        cs("""
using ExcelNet;
using ExcelNet.Styles;
using OfficeNet.Core.Drawing;

var workbook = Workbook.Create("Penjualan");
var sheet = workbook["Penjualan"];

sheet.WriteHeader("A1", ["Tanggal", "Produk", "Wilayah", "Qty", "Harga", "Total"]);

string[] products = ["WordNet", "ExcelNet", "PowerPointNet", "PdfNet"];
string[] regions  = ["Jakarta", "Bandung", "Surabaya"];
var random = new Random(7);

for (var i = 0; i < 12; i++)
{
    var row = i + 1;
    sheet[row, 0].Set(new DateTime(2026, 1, 5).AddDays(i * 4));
    sheet[row, 1].Set(products[i % products.Length]);
    sheet[row, 2].Set(regions[i % regions.Length]);
    sheet[row, 3].Set(random.Next(4, 40));
    sheet[row, 4].Set(random.Next(60, 280) * 1000.0).WithNumberFormat(NumberFormats.Rupiah);
    sheet[row, 5].SetFormula($"D{row + 1}*E{row + 1}").WithNumberFormat(NumberFormats.Rupiah);
}

$"{sheet.RowCount} baris, {sheet.CellCount} sel terpakai"
"""),
        md("""
## Formula — dan kenapa `Recalculate()` wajib / and why `Recalculate()` matters

Sel formula punya dua bagian: ekspresi dan hasil cache. Excel menghitung ulang saat dibuka; semua
konsumen lain membaca cache. Tanpa `Recalculate()`, PDF dan Google Sheets menampilkan nol. /
A formula cell has two parts: the expression and a cached result. Excel recomputes on open; every
other consumer reads the cache. Without `Recalculate()`, PDFs and Google Sheets show zeros.
"""),
        cs("""
sheet["E14"].Set("TOTAL").Bold();
sheet["F14"].SetFormula("SUM(F2:F13)").WithStyle(
    CellStyle.Default.Bold()
        .WithNumberFormat(NumberFormats.Rupiah)
        .WithBackground(OfficeColor.FromRgb(0xFF, 0xF2, 0xCC)));

sheet["E15"].Set("RATA-RATA").Bold();
sheet["F15"].SetFormula("AVERAGE(F2:F13)").WithNumberFormat(NumberFormats.Rupiah);

Console.WriteLine($"Sebelum Recalculate: {sheet["F14"].Number:N0}");

workbook.Recalculate();

Console.WriteLine($"Sesudah  Recalculate: {sheet["F14"].Number:N0}");
"""),
        md("""
Mesinnya meniru aritmetika Excel di tempat Excel berbeda dari .NET. /
The engine reproduces Excel's arithmetic where Excel differs from .NET.
"""),
        cs("""
using var quirks = Workbook.Create("Quirks");
var q = quirks["Quirks"];

q["A1"].SetFormula("ROUND(2.5,0)");    // 3, bukan 2 (bukan pembulatan bankir)
q["A2"].SetFormula("MOD(-3,2)");       // 1, mengikuti tanda pembagi
q["A3"].SetFormula("-2^2");            // 4, minus uner mengikat lebih kuat
q["A4"].SetFormula("1/0");             // #DIV/0! sebagai nilai, bukan exception
q["A5"].SetFormula("IFERROR(A4,\\"aman\\")");

quirks.Recalculate();

foreach (var address in new[] { "A1", "A2", "A3", "A4", "A5" })
    Console.WriteLine($"{address}  {q[address].Formula,-22} = {q[address].Text}");
"""),
        md("""
## Style dan format bersyarat / Styles and conditional formatting

`CellStyle` immutable — setiap `With…` mengembalikan yang baru. /
`CellStyle` is immutable — every `With…` returns a new one.
"""),
        cs("""
var header = CellStyle.Default
    .Bold()
    .WithColor(OfficeColor.White)
    .WithBackground(OfficeColor.FromRgb(0x1F, 0x38, 0x64))
    .WithAlignment(HorizontalAlignment.Center);

sheet.Range("A1:F1").ApplyStyle(header);

sheet.AddColorScale("D2:D13",
    OfficeColor.FromRgb(0xF8, 0x69, 0x6B),
    OfficeColor.FromRgb(0x63, 0xBE, 0x7B));

sheet.Frozen = new FreezePanes(Rows: 1, Columns: 0);
sheet.AutoFitColumns();
sheet.TabColor = OfficeColor.FromRgb(0x1F, 0x38, 0x64);

workbook.Save(At("excelnet.xlsx"));
Show(At("excelnet.xlsx"), 700);
"""),
        md("""
## CSV dan JSON

`CsvOptions.Indonesian` penting: locale yang memakai `,` sebagai desimal harus memakai `;` sebagai
pemisah kolom. Salah di sini mengubah tiap angka jadi dua kolom. /
`CsvOptions.Indonesian` matters: a locale using `,` as the decimal separator must use `;` as the
field separator. Getting it wrong turns every number into two columns.
"""),
        cs("""
using ExcelNet.Io;

var csv = CsvIo.ExportText(sheet, CsvOptions.Indonesian);
Console.WriteLine(string.Join("\\n", csv.Split('\\n').Take(4)));

Console.WriteLine();
var json = JsonIo.ExportText(sheet);
Console.WriteLine(json[..Math.Min(260, json.Length)] + "…");
"""),
        md("""
## Ekspor PDF / PDF export
"""),
        cs("""
workbook.SaveAsPdf(At("excelnet.pdf"), new ExcelPdfOptions
{
    ShowGridLines = true,
    RepeatHeaderRow = true,
});

Show(At("excelnet.pdf"), 700);
"""),
        cs("""
workbook.Dispose();
"""),
    ]


def powerpointnet():
    return [
        md("""
# PowerPointNet

**ID** — Presentasi `.pptx`: slide, bentuk, tabel, chart native, dan konversi HTML → slide.
**EN** — `.pptx` presentations: slides, shapes, tables, native charts, and HTML → slides.

> Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Panduan lengkap / full guide: [`docs/PowerPointNet.md`](../docs/PowerPointNet.md) ·
[Bahasa Indonesia](../docs/id/PowerPointNet.md)
"""),
        cs(refs(CORE, PDF, PPT, RENDER)),
        cs(SHOW),
        md("""
## Slide dan tema / Slides and themes

Mengubah warna tema menata ulang setiap bentuk yang memakainya — bentuk berwarna eksplisit tidak
ikut berubah. /
Changing a theme colour restyles every shape that uses it — a shape given an explicit colour does
not follow.
"""),
        cs("""
using PowerPointNet;
using PowerPointNet.Charts;
using PowerPointNet.Shapes;
using OfficeNet.Core;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;

var deck = Presentation.Create();
deck.Master!.SetThemeColor("accent1", OfficeColor.FromRgb(0x1F, 0x38, 0x64));

deck.AddTitleSlide("OfficeNet", "Word, Excel, PowerPoint, dan PDF untuk .NET 10");

deck.AddBulletSlide("Komponen", [
    "WordNet — python-docx",
    "ExcelNet — openpyxl + pandas",
    "PowerPointNet — python-pptx + PptxGenJS",
    "PdfNet — PyPDF2",
]);

$"{deck.SlideCount} slide, {deck.SlideWidth.Inches:0.###} x {deck.SlideHeight.Inches:0.###} inci"
"""),
        md("""
## Chart native / Native charts

Chart di `.pptx` adalah data, bukan gambar: angkanya ikut serta dan tema menata ulang tampilannya. /
A chart in a `.pptx` is data, not a picture: the numbers travel with it and the theme restyles it.
"""),
        cs("""
var slide = deck.AddSlide(2);
slide.SetTitle("Pendapatan per Wilayah");

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

deck.Save(At("deck.pptx"));
Show(At("deck.pptx"), 640);
"""),
        md("""
Chart bisa dibaca kembali — `GetData()` mengembalikan nilai cache, yang persis yang ditampilkan
pembaca. /
Charts read back — `GetData()` returns the cached values, exactly what a viewer displays.
"""),
        cs("""
var chart = slide.Charts.First();
var data = chart.GetData();

Console.WriteLine($"Tipe      : {data.Type}");
Console.WriteLine($"Kategori  : {string.Join(", ", data.Categories)}");

foreach (var series in data.Series)
    Console.WriteLine($"  {series.Name}: {string.Join(", ", series.Values)}");

// Ubah tipenya tanpa menyusun ulang datanya.
chart.SetData(data with { Type = ChartType.Bar });
chart.GetData().Type
"""),
        md("""
## Bentuk dan efek / Shapes and effects
"""),
        cs("""
var canvas = deck.AddSlide(3);   // Blank

canvas.AddShape(ShapeGeometry.RoundedRectangle,
        Units.Inches(1), Units.Inches(1.4), Units.Inches(3.4), Units.Inches(1.6))
    .WithGradientFill(
        OfficeColor.FromRgb(0x1F, 0x38, 0x64),
        OfficeColor.FromRgb(0x63, 0x8E, 0xC6),
        GradientDirection.DiagonalDown)
    .WithShadow(blur: Units.Pt(10));

canvas.AddShape(ShapeGeometry.Ellipse,
        Units.Inches(5.2), Units.Inches(1.4), Units.Inches(2.2), Units.Inches(2.2))
    .WithFill(OfficeColor.FromRgb(0xE8, 0x71, 0x22))
    .WithOutline(OfficeColor.White, Units.Pt(3));

deck.Save(At("deck.pptx"));
Show(At("deck.pptx"), 640);
"""),
        md("""
## HTML → slide

Fitur PptxGenJS yang paling dicari. Isi yang panjang dipecah ke slide lanjutan alih-alih meluber. /
The PptxGenJS feature people come for. Long content splits onto continuation slides rather than
overflowing.
"""),
        cs("""
using PowerPointNet.Html;

var html = @"
<h1>Tinjauan Kuartal</h1>
<p>Disusun oleh <strong>Gravicode Studios</strong>, dipimpin oleh <em>Kang Fadhil</em>.</p>
<h2>Sorotan</h2>
<ul>
  <li>Pendapatan naik <strong>32%</strong>
    <ul><li>Jakarta memimpin pertumbuhan</li></ul>
  </li>
  <li>Biaya operasional turun 4%</li>
</ul>
<h2>Ringkasan Angka</h2>
<table>
  <tr><th>Wilayah</th><th>2025</th><th>2026</th></tr>
  <tr><td>Jakarta</td><td>1.120</td><td>1.480</td></tr>
  <tr><td>Bandung</td><td>860</td><td>1.150</td></tr>
</table>";

using (var fromHtml = HtmlToSlides.CreatePresentation(html, new HtmlSlideOptions
{
    TitleSlide = "Dari HTML ke PowerPoint",
    SubtitleSlide = "Satu panggilan, tanpa penyuntingan manual",
}))
{
    Console.WriteLine($"{fromHtml.SlideCount} slide dihasilkan dari HTML");
    fromHtml.Save(At("html.pptx"));
}

Show(At("html.pptx"), 640);
"""),
        md("""
## Ekspor PDF / PDF export

Chart digambar oleh pengekspor dari data cache-nya. /
Charts are drawn by the exporter from their cached data.
"""),
        cs("""
deck.SaveAsPdf(At("deck.pdf"));
Show(At("deck.pdf"), 640);
"""),
        cs("""
deck.Dispose();
"""),
    ]


def pdfnet():
    return [
        md("""
# PdfNet

**ID** — PDF: menggambar, menggabung, memisah, mengekstrak teks, mengenkripsi, menganotasi.
**EN** — PDF: drawing, merging, splitting, text extraction, encryption, annotation.

> Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Panduan lengkap / full guide: [`docs/PdfNet.md`](../docs/PdfNet.md) ·
[Bahasa Indonesia](../docs/id/PdfNet.md)
"""),
        cs(refs(CORE, PDF, WORD, RENDER)),
        cs(SHOW),
        md("""
## Menggambar / Drawing

`TopDown` penting: titik asal PDF ada di kiri-bawah, sedangkan semua format Office mengukur dari
atas. /
`TopDown` matters: PDF's origin is bottom-left while every Office format measures from the top.
"""),
        cs("""
using PdfNet.Document;
using PdfNet.Content;
using PdfNet.Security;
using OfficeNet.Core.Drawing;

var document = PdfDocument.Create();
var page = document.Pages.Add(PageSize.A4);

using (var canvas = page.OpenCanvas())
{
    canvas.TopDown = true;

    canvas.SetFillColor(OfficeColor.FromRgb(0x1F, 0x38, 0x64));
    canvas.Rectangle(0, 0, page.Width, 90).Fill();

    canvas.SetFont(StandardFont.HelveticaBold, 26);
    canvas.SetFillColor(OfficeColor.White);
    canvas.DrawText("Laporan Tahunan", 56, 56);

    canvas.SetFont(StandardFont.Helvetica, 11);
    canvas.SetFillColor(OfficeColor.Black);
    canvas.DrawText(
        "Pendapatan tumbuh 32% dibanding tahun sebelumnya, ditopang permintaan yang " +
        "kuat di Jakarta dan Bandung. Seluruh wilayah menutup tahun di atas target.",
        56, 140, page.Width - 112, TextAlignment.Justify);

    var y = 210.0;

    foreach (var (label, value) in new[] {
        ("Jakarta", 1480.0), ("Bandung", 1150.0), ("Surabaya", 905.0), ("Medan", 520.0) })
    {
        canvas.SetFillColor(OfficeColor.FromRgb(0x63, 0x8E, 0xC6));
        canvas.Rectangle(140, y, value / 4, 18).Fill();

        canvas.SetFillColor(OfficeColor.Black);
        canvas.SetFont(StandardFont.Helvetica, 10);
        canvas.DrawText(label, 56, y + 13);
        canvas.DrawText($"{value:N0}", 148 + value / 4, y + 13);

        y += 28;
    }
}

document.Info.Title = "Laporan Tahunan";
document.Info.Author = "Gravicode Studios";
document.Save(At("gambar.pdf"));

Show(At("gambar.pdf"));
"""),
        md("""
## Ekstraksi teks / Text extraction

PDF tidak memuat teks dalam urutan baca — ia memuat instruksi menggambar. Mengekstrak berarti
memainkan ulang operatornya dan *menyimpulkan spasi yang tidak pernah disimpan*. /
A PDF does not contain text in reading order — it contains drawing instructions. Extracting means
replaying the operators and *inferring the spaces the file never stored*.
"""),
        cs("""
using var reopened = PdfDocument.Open(At("gambar.pdf"));

Console.WriteLine(reopened.ExtractText());
Console.WriteLine(new string('-', 60));

foreach (var fragment in reopened.Pages[0].ExtractTextFragments().Take(6))
    Console.WriteLine($"{fragment.Text,-28} @ ({fragment.X,6:0.#}, {fragment.Y,6:0.#})  {fragment.FontSize}pt");
"""),
        md("""
## Menggabung dan memisah / Merge and split

Penggabungan menulis ulang setiap nomor objek, jadi dua berkas yang sama-sama menyebut font-nya
`7 0 R` tidak bertabrakan. /
Merging rewrites every object number, so two files that both call their font `7 0 R` do not collide.
"""),
        cs("""
using var wordDocument = WordNet.WordDocument.Create();
wordDocument.AddHeading("Lampiran", 1);
wordDocument.AddParagraph("Halaman tambahan dari WordNet.");
wordDocument.SaveAsPdf(At("lampiran.pdf"));

using var a = PdfDocument.Open(At("gambar.pdf"));
using var b = PdfDocument.Open(At("lampiran.pdf"));

Console.WriteLine($"Sebelum: {a.Pages.Count} halaman");
a.Merge(b);
Console.WriteLine($"Sesudah: {a.Pages.Count} halaman");

a.Save(At("gabungan.pdf"));

var parts = PdfDocument.Open(At("gabungan.pdf")).Split();
Console.WriteLine($"Dipisah menjadi {parts.Count} dokumen");
foreach (var part in parts) part.Dispose();
"""),
        md("""
## Anotasi dan watermark / Annotations and watermarks

Titik quad sorotan berurutan kiri-atas, kanan-atas, kiri-bawah, kanan-bawah. Searah jarum jam
menghasilkan bentuk dasi kupu-kupu. /
Highlight quad points go upper-left, upper-right, lower-left, lower-right. Clockwise draws a bowtie.
"""),
        cs("""
using PdfNet.Annotations;

using var annotated = PdfDocument.Open(At("gambar.pdf"));
var target = annotated.Pages[0];

target.AddWatermark("DRAF", OfficeColor.Gray, 0.10);
target.AddTextNote(500, 200, "Perlu ditinjau sebelum rilis.", "Kang Fadhil");
target.AddHighlight([new PdfRectangle(56, 130, 300, 150)], OfficeColor.FromRgb(0xFF, 0xF0, 0x00));

annotated.Save(At("anotasi.pdf"));

Console.WriteLine($"{target.GetAnnotations().Count} anotasi");
Show(At("anotasi.pdf"));
"""),
        md("""
## Enkripsi / Encryption
"""),
        cs("""
using (var secret = PdfDocument.Open(At("gambar.pdf")))
{
    secret.Encrypt(
        userPassword: "buka",
        ownerPassword: "pemilik",
        permissions: PdfPermissions.Print,
        cipher: PdfCipher.Aes256);

    secret.Save(At("terkunci.pdf"));
}

try
{
    using var _ = PdfDocument.Open(At("terkunci.pdf"));
    Console.WriteLine("Terbuka tanpa kata sandi — tidak seharusnya!");
}
catch (Exception ex)
{
    Console.WriteLine($"Tanpa kata sandi: {ex.GetType().Name}");
}

using var unlocked = PdfDocument.Open(At("terkunci.pdf"), "buka");
Console.WriteLine($"Dengan kata sandi: {unlocked.Pages.Count} halaman, izin = {unlocked.Permissions}");
"""),
        cs("""
document.Dispose();
"""),
    ]


def main():
    os.makedirs(OUT, exist_ok=True)
    print("Writing notebooks:")

    write("00-Quickstart.ipynb", quickstart())
    write("WordNet.ipynb", wordnet())
    write("ExcelNet.ipynb", excelnet())
    write("PowerPointNet.ipynb", powerpointnet())
    write("PdfNet.ipynb", pdfnet())


if __name__ == "__main__":
    main()
