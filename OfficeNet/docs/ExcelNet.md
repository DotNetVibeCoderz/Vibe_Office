# ExcelNet

`.xlsx` workbooks for .NET 10 — openpyxl's job, plus a bridge to GraviFrame for pandas' job.

*Bahasa Indonesia: [docs/id/ExcelNet.md](id/ExcelNet.md)* · [Back to index](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet.ExcelNet
```

---

![An Excel workbook produced by the code on this page, rendered to PNG](screenshots/excelnet-workbook.png)

*Typed dates, Rupiah number formats, a `SUM` and an `AVERAGE` that were **evaluated** — the totals in
that image were computed by the formula engine, not typed in — and columns sized by `AutoFitColumns`.*

---

## Opening and saving

```csharp
using ExcelNet;
using ExcelNet.Styles;

using var workbook = Workbook.Create("Penjualan");
using var existing = Workbook.Open("data.xlsx");   // path, Stream or byte[]

workbook.Save("data.xlsx");
workbook.SaveAsPdf("data.pdf");
```

Unlike WordNet, ExcelNet parses each sheet into a model and writes it back. A worksheet's XML is a
flat list of rows with nothing worth preserving byte-for-byte, and a dictionary makes random cell
access O(1) instead of a scan. The trade is different from Word's, so the model is too — see
[Core concepts](Core.md#two-models-on-purpose).

## Sheets

```csharp
var sheet = workbook["Penjualan"];      // by name — throws if missing
var first = workbook[0];                // by index
var maybe = workbook.Find("Arsip");     // null if missing

workbook.AddSheet("Ringkasan");
workbook.AddSheetUnique("Data");        // "Data", then "Data1", "Data2"…
workbook.CopySheet("Penjualan", "Penjualan (salinan)");
workbook.MoveSheet("Ringkasan", 0);
workbook.RemoveSheet("Arsip");
```

## Cells

Addresses are A1 or zero-based row/column — both index the same cell.

```csharp
sheet["A1"].Set("Tanggal");
sheet[0, 1].Set("Produk");        // row 0, column 1 = B1

sheet["B2"].Set(42);              // number
sheet["B3"].Set(3.14);
sheet["B4"].Set(DateTime.Now);    // number + a date format
sheet["B5"].Set(true);            // boolean
sheet["B6"].Set("teks");
sheet["B7"].Set(null);            // clears the value
```

`Set(object?)` chooses the cell type from the runtime type. A date is stored as a number *plus* a
number format — that is all a date is in a spreadsheet, which is why telling a date from a plain
number requires the stylesheet and why ExcelNet reads styles before any sheet.

Reading back:

```csharp
string text = sheet["B6"].Text;
double number = sheet["B2"].Number;
DateTime date = sheet["B4"].DateTime;
bool empty = sheet["Z99"].IsEmpty;
```

Bulk writes avoid a call per cell:

```csharp
sheet.WriteHeader("A1", ["Tanggal", "Produk", "Qty", "Total"]);
sheet.WriteRow("A2", DateTime.Today, "WordNet", 12, 480_000);
sheet.WriteColumn("F1", "Jan", "Feb", "Mar");
sheet.WriteRange("A5", rows);     // IEnumerable<IEnumerable<object?>>
```

## Formulas

```csharp
sheet["F2"].SetFormula("D2*E2");
sheet["F16"].SetFormula("SUM(F2:F15)");
sheet["F17"].SetFormula("AVERAGE(F2:F15)");
sheet["G2"].SetFormula("IF(F2>1000000,\"Besar\",\"Kecil\")");

workbook.Recalculate();           // fills every cached result
```

**Call `Recalculate()` before saving.** A formula cell has two parts: the expression and a cached
result. Excel recomputes on open and does not care about the cache — but every other consumer reads
it, so a workbook saved without recalculating shows zeros in Google Sheets, in a PDF export, and in
anything that parses the file directly.

The engine implements about 60 functions:

`ABS AND AVERAGE CONCAT CONCATENATE COUNT COUNTA COUNTBLANK COUNTIF DATE DAY ERROR.TYPE EXP HLOOKUP
HOUR IF IFERROR IFNA INDEX INT ISERR ISERROR ISNA LEFT LEN LN LOG10 LOWER MATCH MAX MEDIAN MID MIN
MINUTE MOD MONTH NOT NOW OR POWER PRODUCT RIGHT ROUND ROUNDDOWN ROUNDUP SECOND SIGN SQRT STDEV
STDEV.P STDEV.S SUBSTITUTE SUM SUMIF TEXTJOIN TODAY TRIM UPPER VALUE VAR VAR.P VAR.S VLOOKUP
WEEKDAY XLOOKUP YEAR`

It reproduces Excel's arithmetic where Excel differs from .NET, because a subtle mismatch is worse
than a missing function:

- `ROUND` is away-from-zero, not banker's rounding — `ROUND(2.5, 0)` is 3, not 2.
- `MOD` takes the sign of the divisor, so `MOD(-3, 2)` is 1.
- `-2^2` is 4: unary minus binds tighter than `^`.
- The date epoch is 1899-12-30, and serial 60 is the phantom 29 February 1900 that Excel has carried
  since 1985.

Errors propagate as values rather than exceptions, so `IFERROR` can catch them:

```csharp
sheet["A1"].SetFormula("1/0");                 // #DIV/0!
sheet["A2"].SetFormula("IFERROR(A1,\"n/a\")"); // "n/a"
```

### Lookups

```csharp
sheet["E2"].SetFormula("VLOOKUP(D2, Harga!A2:C50, 3, FALSE)");
sheet["E3"].SetFormula("INDEX(A2:A50, MATCH(\"Adaptor\", B2:B50, 0))");
sheet["E4"].SetFormula("XLOOKUP(D2, Harga!A2:A50, Harga!C2:C50, \"tidak ada\")");
```

`XLOOKUP` is the one to reach for. It defaults to an **exact** match, the lookup and return ranges
are separate so the key need not sit left of the answer, and a miss can carry its own value instead
of `#N/A`:

```
XLOOKUP(needle, lookupRange, returnRange, [ifNotFound], [matchMode], [searchMode])
```

`matchMode`: 0 exact (the default), -1 exact or next smaller, 1 exact or next larger, 2 wildcard.
`searchMode`: 1 first to last (the default), -1 last to first. Unlike `VLOOKUP`'s approximate mode,
the nearest-match modes scan the whole range and do not assume it is sorted.

`VLOOKUP`'s fourth argument defaults to `TRUE` — approximate — which assumes the first column is
sorted ascending and silently returns the wrong row when it is not. That default has cost more
spreadsheets more silent errors than anything else in the format. It is kept because Excel's is:
a formula that behaves differently here than in Excel would be worse than one that shares its trap.
Pass `FALSE` unless you are looking up a band, like a commission tier.

`INDEX(range, row, [column])` and `MATCH(needle, range, [matchType])` are both one-based. Together
they do what `VLOOKUP` cannot — return a column to the *left* of the key. `MATCH`'s type defaults to
1 (largest value not over, ascending); pass 0 for exact, which is the only safe one on unsorted data
and the only one that supports wildcards.

Wildcards are Excel's: `*` for any run of characters, `?` for one, `~` to escape either.

**Not implemented:** array formulas, iterative calculation, and cross-workbook references. See
[Plan.md](../Plan.md).

## Styles

`CellStyle` is an immutable record; every `With…` returns a new one, so styles compose and are safe
to share.

```csharp
sheet["A1"].Bold();
sheet["A1"].WithBackground(OfficeColor.FromRgb(0xFF, 0xF2, 0xCC));
sheet["B2"].WithNumberFormat(NumberFormats.Rupiah);

var header = CellStyle.Default
    .Bold()
    .WithColor(OfficeColor.White)
    .WithBackground(OfficeColor.FromRgb(0x1F, 0x38, 0x64))
    .WithAlignment(HorizontalAlignment.Center)
    .WithBorder(CellBorder.All(BorderLineStyle.Thin));

sheet.Range("A1:F1").ApplyStyle(header);
```

Built-in formats live on `NumberFormats`: `General`, `Integer`, `TwoDecimals`, `Thousands`,
`ThousandsTwoDecimals`, `Percent`, `PercentTwoDecimals`, `Rupiah`, `RupiahTwoDecimals`, plus date and
time formats. Any Excel format string works too.

### Why a style sometimes "does nothing"

Excel ignores a format unless the corresponding `apply…` flag is set on the record. ExcelNet sets
them for you; this is the top reason hand-written OOXML styles have no effect, and worth knowing if
you ever inspect the XML. Two more from the same family:

- A solid fill's colour goes in `fgColor`. Putting it in `bgColor` renders white.
- Fill index 0 must be `none` and index 1 `gray125`. Excel hard-codes both, so the first usable fill
  is index 2.

## Ranges

```csharp
var range = sheet.Range("A1:D10");

range.Fill(0);
range.ApplyStyle(header);
range.ModifyStyle(s => s.Bold());          // keeps whatever else was there
range.SetOutlineBorder(BorderLineStyle.Medium);
range.Merge();

object?[,] values = range.ToArray();

foreach (var cell in range)
{
    Console.WriteLine($"{cell.Address}: {cell.Text}");
}
```

## Layout

```csharp
sheet.SetColumnWidth("A", 18);
sheet.AutoFitColumns();                    // measured from the content
sheet.SetRowHeight(0, 22);
sheet.HideColumn(3);

sheet.Frozen = new FreezePanes(Rows: 1, Columns: 0);
sheet.AutoFilter = CellRangeReference.Parse("A1:F15");
sheet.TabColor = OfficeColor.FromRgb(0x1F, 0x38, 0x64);
sheet.ShowGridLines = false;
sheet.MergeCells("A1:D1");
```

Column width is measured in *characters of the digit zero* in the default font, not in points or
pixels. `AutoFitColumns` does that conversion; setting a width by hand means thinking in those
units.

## Conditional formatting

```csharp
sheet.AddColorScale("D2:D15",
    OfficeColor.FromRgb(0xF8, 0x69, 0x6B),   // low
    OfficeColor.FromRgb(0x63, 0xBE, 0x7B));  // high

sheet.AddDataBar("E2:E15", OfficeColor.FromRgb(0x63, 0x8E, 0xC6));
sheet.AddConditionalFormat(CellRangeReference.Parse("F2:F15"), "greaterThan",
    CellStyle.Default.WithBackground(OfficeColor.FromRgb(0xC6, 0xEF, 0xCE)),
    "1000000");   // the operands come last, so a rule can take two of them
```

## Charts

A chart on a worksheet is the same DrawingML part a PowerPoint chart uses — the model lives in
`OfficeNet.Core.Charts` and is shared between the two libraries. What differs is the attachment:

```
sheet1.xml  --drawing-->  drawing1.xml  --chart-->  chart1.xml
```

Excel reaches a chart through an intermediate *drawing* part. Miss it and the file opens with no
chart and no complaint, which is how a hand-built workbook usually loses one.

```csharp
using OfficeNet.Core.Charts;

sheet.AddChart(new ChartData
{
    Type = ChartType.Column,
    Title = "Pendapatan per Wilayah",
    Categories = ["Jakarta", "Bandung", "Surabaya", "Medan"],
    Series =
    [
        new ChartSeries("2025", [1120, 860, 740, 410]),
        new ChartSeries("2026", [1480, 1150, 905, 520]),
    ],
    ValueFormat = "#,##0",
    ShowDataLabels = true,
}, "E2:M20");
```

The anchor is a cell range, which is how Excel thinks about a floating chart: both corners are
pinned, so the chart resizes when the rows and columns beneath it do.

Reading them back:

```csharp
foreach (var chart in sheet.Charts)
{
    var data = chart.GetData();
    Console.WriteLine($"{data.Type} at {chart.Anchor.A1}, {data.Series.Count} series");

    chart.SetData(data with { Type = ChartType.Bar });
}
```

The same twelve types as [PowerPointNet](PowerPointNet.md#charts). **The PDF exporter does not draw
worksheet charts** — only PowerPoint's are rendered on export. Excel itself shows them normally.

## Pivot tables

```csharp
using ExcelNet.Pivot;

var summary = workbook.AddSheet("Ringkasan");

summary.AddPivotTable(new PivotTableDefinition
{
    Source = workbook["Data"],
    SourceRange = CellRangeReference.Parse("A1:D100"),   // headers included
    Target = CellReference.Parse("A3"),
    Rows = ["Wilayah"],
    Columns = ["Produk"],
    Values = [new PivotValue("Total", PivotFunction.Sum)],
});
```

Fields are addressed by their header text, so the source range must start at the header row. Eleven
functions: `Sum` (the default), `Count`, `CountNumbers`, `Average`, `Max`, `Min`, `Product`,
`StdDev`, `StdDevP`, `Var`, `VarP`.

A pivot is four parts, not one:

```
workbook.xml  --pivotCacheDefinition-->  pivotCacheDefinition1.xml  --pivotCacheRecords-->  records
sheet2.xml    --pivotTable-->            pivotTable1.xml            --pivotCacheDefinition-->  ^
```

The cache is a snapshot of the source data — which is why editing the source changes nothing until
someone refreshes — and the table holds only the layout. Both the workbook and the table must name
the same `cacheId`, or Excel reports the file as damaged.

**The result grid is not written.** The parts describe the cache and the layout; Excel computes the
cells when it opens the file, which is what `refreshOnLoad` asks for. So Excel shows a complete
pivot table, and a non-Excel consumer — including this library's own PDF export — sees that area as
empty. Computing the grid here would mean reimplementing Excel's aggregation and subtotal layout,
and any disagreement would show up as a table that changes the moment someone opens it.

## Data validation

A dropdown is what makes a sheet fillable by a person rather than only by a program — the difference
between a clean column and one holding "Jakarta", "jakarta" and "DKI Jakarta".

```csharp
using ExcelNet.Validation;

sheet.AddDropdown("B2:B200", "Jakarta", "Bandung", "Surabaya", "Medan");
```

`AddValidation` takes the other rule types. The builders return a plain rule and the messages are
`init` properties, so add them with `with`:

```csharp
sheet.AddValidation(DataValidation.WholeNumberBetween(
    CellRangeReference.Parse("C2:C200"), 1, 1000) with
{
    ErrorTitle = "Out of range",
    ErrorMessage = "Enter a number between 1 and 1000.",
    PromptTitle = "Quantity",
    PromptMessage = "1 to 1000.",
});
```

| Builder | Allows |
| --- | --- |
| `List(range, values)` | one of a fixed set, shown as a dropdown |
| `ListFromRange(range, source)` | one of the values in `source`, e.g. `Lists!$A$1:$A$50` |
| `WholeNumberBetween(range, min, max)` | an integer, inclusive |
| `DecimalBetween(range, min, max)` | any number, inclusive |
| `DateBetween(range, from, to)` | a date, inclusive |
| `TextLengthAtMost(range, max)` | text no longer than `max` |
| `Custom(range, formula)` | whatever the formula accepts |

An inline list is stored as a quoted, comma-separated string, and **Excel caps that string at 255
characters** — past it the whole rule is dropped and the file opens with the dropdown silently
missing. `List` throws instead, and names the length. A value containing a comma is rejected for the
same reason: the comma is the separator, so `"Jakarta, DKI"` would become two entries. Both cases
are what `ListFromRange` is for; give it an absolute reference, since a relative one shifts per cell
and the dropdown in row 2 then reads a different range from the one in row 3.

`ErrorStyle` decides what a rejected entry does: `Stop` refuses it (the default, and what a template
usually wants), `Warning` and `Information` let it through.

## Protecting a sheet

Sheet protection is **not security**. The password is a 16-bit hash that any tool strips in
milliseconds, and the contents are readable regardless. It stops someone overwriting a formula
column by accident, which is a real and common problem, and that is all it is for.

```csharp
sheet.Protect(editable: "B2:B200");
```

The interaction that catches people out: protection only bites on cells whose style says `locked`,
and **every cell is locked by default**. Protecting a sheet without unlocking the inputs freezes the
whole thing — which is why `Protect` takes the editable range and unlocks it for you.

The flags say what is still *allowed*:

```csharp
sheet.Protection = new SheetProtection
{
    PasswordHash = SheetProtection.WithPassword("rahasia").PasswordHash,
    Sort = true,
    AutoFilter = true,
    FormatCells = true,
};
```

In the file itself every one of these is inverted — `formatCells="0"` means formatting is *allowed*
— and the schema defaults are not uniform: most flags default to forbidden, but `selectLockedCells`
and `selectUnlockedCells` default to allowed. ExcelNet writes all of them out explicitly and negates
each exactly once, on the way out and on the way back in.

`Unprotect()` removes the element. `workbook.Protection` does the same job for the workbook's
structure — which sheets can be added, removed, renamed or reordered.

## Streaming a large export

`Workbook` parses a file into a model and writes the model back. That is what makes reading a cell
an O(1) lookup, and what makes a million-row export impossible. `StreamingWorkbook` writes straight
into the package instead — a row is serialised and forgotten:

```csharp
using ExcelNet.Streaming;

using var workbook = StreamingWorkbook.Create("besar.xlsx", "Data");
var sheet = workbook.Sheet("Data");

sheet.WriteHeader("Id", "Nama", "Wilayah", "Jumlah", "Tanggal");

foreach (var record in source)
{
    sheet.WriteRow(record.Id, record.Name, record.Region, record.Amount, record.Date);
}
```

A million rows takes about four seconds and 34 MB of working set, whatever the row count — the
memory is the buffers, not the data.

The trade is that it is **write-only and forward-only**. No reading a cell back, no returning to an
earlier row, and nothing that needs to know the whole sheet: no formula evaluation, no charts, no
pivots, no conditional formatting, no autofit. Use `Workbook` when any of those matter. They are
different tools because the jobs are different.

Two consequences worth knowing before you start:

**The sheet names are fixed at creation.** `[Content_Types].xml` has to be the first entry in the
ZIP and it names every part in the file, so the sheets must be known before the first byte of the
first one is written. `Sheet(name)` opens one; opening a second closes the first, and a sheet cannot
be reopened.

**Strings are written inline, not shared.** A shared-string table has to be complete before it can
be written, which means holding every distinct string in memory — the thing this class exists to
avoid. Inline strings cost file size instead, noticeably so on repetitive data. Excel reads both.

Values are typed from the object: `string`, `bool`, the numeric types, `DateTime`, `DateOnly` and
`DateTimeOffset` each write the right cell type, and `null` writes an *empty* cell rather than an
empty string — the difference between a gap in the data and a blank value, and between `COUNT` and
`COUNTA` agreeing with the source and not. Anything else is written as its `ToString()`.

Four style indices are available, and `WriteRow(values, styles)` applies them per cell:
`StreamingSheet.GeneralStyle`, `.HeaderStyle` (bold), `.DateStyle` and `.DateTimeStyle`.
`SkipRows(n)` leaves a gap.

## CSV, JSON and SQL

```csharp
using ExcelNet.Io;

CsvIo.Import(workbook, "penjualan.csv");
CsvIo.Export(sheet, "penjualan.csv");
CsvIo.Export(sheet, "penjualan.csv", CsvOptions.Indonesian);   // ; separator, comma decimal

JsonIo.Export(sheet, "penjualan.json");
JsonIo.Import(workbook, json, "Impor");

using var connection = new SqliteConnection("Data Source=data.db");
SqlIo.Import(workbook, connection, "SELECT * FROM penjualan", "Penjualan");
SqlIo.Export(sheet, connection, "penjualan");
```

`CsvOptions.Indonesian` matters more than it looks: a locale that uses `,` as the decimal separator
must use `;` as the field separator, and getting that wrong turns every number into two columns.

## DataFrames

ExcelNet does not reimplement pandas. It bridges to `GraviFrame` from
[Gravicode.Science](https://github.com/DotNetVibeCoderz/Vibe_ML), which is the analysis library.

```csharp
using ExcelNet.DataFrames;

var frame = sheet.ToDataFrame();               // first row as the header
var stats = sheet.Describe();                  // count, mean, std, min, quartiles, max

workbook.WriteDataFrame(frame, "Hasil");
```

The whole integration surface is `DataFrames/DataFrameBridge.cs`. Analysis belongs in GraviFrame;
reading and writing `.xlsx` belongs here.

## PDF export

```csharp
workbook.SaveAsPdf("laporan.pdf");

workbook.SaveAsPdf("laporan.pdf", new ExcelPdfOptions
{
    PageSize = PageSize.A4.Landscape(),
    SheetNames = ["Penjualan"],
    ShowGridLines = true,
    RepeatHeaderRow = true,     // the header row again at the top of every page
    Recalculate = true,         // so the cached results are current
});
```

Values are rendered as the number format displays them, so a Rupiah column exports as Rupiah rather
than as a raw double. Charts and conditional formatting are not drawn.

## Reading a workbook

```csharp
using var workbook = Workbook.Open("data.xlsx");

foreach (var sheet in workbook)
{
    Console.WriteLine($"{sheet.Name}: {sheet.RowCount} x {sheet.ColumnCount}");

    foreach (var cell in sheet.UsedCells)
    {
        Console.WriteLine($"  {cell.Address} = {cell.Text}");
    }
}
```

`UsedCells` iterates only cells that exist. A sheet with one value in `ZZ10000` has one used cell,
not ten million.

## Common mistakes

**Totals show as zero everywhere except Excel.** `Recalculate()` was not called before saving.

**A date shows as 45678.** The cell has a value but no date number format. `Set(DateTime)` applies
one; a raw `Set(45678.0)` does not.

**A style is ignored.** Check that you assigned the result — `CellStyle` is immutable, so
`style.Bold();` on its own line does nothing. Use `sheet["A1"].WithStyle(style.Bold())`.

**Numbers split across two columns after a CSV round trip.** Separator and decimal mismatch; use
`CsvOptions.Indonesian` or set the separator explicitly.

## See also

- [Core concepts](Core.md) — colour, units and the package underneath
- [PdfNet](PdfNet.md) — what to do with the exported PDF
- `notebooks/ExcelNet.ipynb` — the same material, runnable
