// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using ExcelNet;
using ExcelNet.DataFrames;
using ExcelNet.Io;
using ExcelNet.Styles;
using OfficeNet.Core;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using Xunit;

namespace ExcelNet.Tests;

/// <summary>Structural rules an .xlsx must satisfy, checked without going through ExcelNet.</summary>
internal static class XlsxValidator
{
    private static readonly string[] WorksheetChildOrder =
    [
        "sheetPr", "dimension", "sheetViews", "sheetFormatPr", "cols", "sheetData", "sheetCalcPr",
        "sheetProtection", "protectedRanges", "scenarios", "autoFilter", "sortState",
        "dataConsolidate", "customSheetViews", "mergeCells", "phoneticPr", "conditionalFormatting",
        "dataValidations", "hyperlinks", "printOptions", "pageMargins", "pageSetup", "headerFooter",
        "rowBreaks", "colBreaks", "customProperties", "cellWatches", "ignoredErrors", "smartTags",
        "drawing",
    ];

    internal static void AssertValid(byte[] xlsx)
    {
        var report = OpcValidator.Validate(xlsx);
        var problems = new List<string>(report.Problems);

        using var stream = new MemoryStream(xlsx, writable: false);
        using var archive = new System.IO.Compression.ZipArchive(stream);

        foreach (var entry in archive.Entries.Where(e =>
                     e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) &&
                     e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            using var entryStream = entry.Open();
            var sheet = XDocument.Load(entryStream);
            var root = sheet.Root!;

            if (OpcValidator.CheckChildOrder(root, WorksheetChildOrder) is { } problem)
            {
                problems.Add($"{entry.FullName}: {problem}");
            }

            // Excel does not sort rows or cells; one out of order shows as a blank.
            var rows = root.Element(Ns.S + "sheetData")?.Elements(Ns.S + "row")
                .Select(r => int.Parse(r.Attribute("r")!.Value))
                .ToList() ?? [];

            if (!rows.SequenceEqual(rows.Order()))
            {
                problems.Add($"{entry.FullName}: rows are not in ascending order.");
            }
        }

        var styles = archive.GetEntry("xl/styles.xml");

        if (styles is not null)
        {
            using var stylesStream = styles.Open();
            var document = XDocument.Load(stylesStream);
            var root = document.Root!;

            var fills = root.Element(Ns.S + "fills")?.Elements(Ns.S + "fill")
                .Select(f => f.Element(Ns.S + "patternFill")?.Attribute("patternType")?.Value)
                .ToList() ?? [];

            // Excel hard-codes these two; a stylesheet whose first fill is a real colour renders
            // every unfilled cell in that colour.
            if (fills.Count < 2 || fills[0] != "none" || fills[1] != "gray125")
            {
                problems.Add(
                    $"xl/styles.xml fill 0 must be 'none' and fill 1 'gray125', got " +
                    $"[{string.Join(", ", fills.Take(2))}].");
            }

            foreach (var required in (string[])["fonts", "borders", "cellStyleXfs", "cellXfs"])
            {
                if (root.Element(Ns.S + required) is null)
                {
                    problems.Add($"xl/styles.xml is missing <{required}>.");
                }
            }
        }

        var sharedStrings = archive.GetEntry("xl/sharedStrings.xml");

        if (sharedStrings is not null)
        {
            using var sharedStream = sharedStrings.Open();
            var document = XDocument.Load(sharedStream);
            var root = document.Root!;

            var unique = root.Elements(Ns.S + "si").Count();
            var declared = int.Parse(root.Attribute("uniqueCount")!.Value);

            if (unique != declared)
            {
                problems.Add($"sharedStrings declares uniqueCount={declared} but holds {unique} entries.");
            }
        }

        Assert.True(problems.Count == 0,
            "The .xlsx is structurally invalid:\n  - " + string.Join("\n  - ", problems));
    }
}

public class CellReferenceTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    [InlineData(16383, "XFD")]
    public void ColumnNamesUseBijectiveBase26(int index, string name)
    {
        // Plain base-26 would give "@A" for 26 because it has a zero digit.
        Assert.Equal(name, CellReference.ColumnIndexToName(index));
        Assert.Equal(index, CellReference.ColumnNameToIndex(name));
    }

    [Theory]
    [InlineData("A1", 0, 0)]
    [InlineData("B2", 1, 1)]
    [InlineData("$C$3", 2, 2)]
    [InlineData("Sheet1!D4", 3, 3)]
    [InlineData("XFD1048576", 1048575, 16383)]
    public void ParsesTheFormsFormulasContain(string text, int row, int column)
    {
        var reference = CellReference.Parse(text);
        Assert.Equal(row, reference.Row);
        Assert.Equal(column, reference.Column);
    }

    [Fact]
    public void RangesNormaliseTheirCorners()
    {
        // "D10:A1" names the same block as "A1:D10".
        var range = CellRangeReference.Parse("D10:A1");

        Assert.Equal("A1", range.Start.A1);
        Assert.Equal("D10", range.End.A1);
        Assert.Equal(10, range.RowCount);
        Assert.Equal(4, range.ColumnCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1A")]
    [InlineData("A0")]
    [InlineData("ABCD1")]
    public void RejectsMalformedReferences(string text) =>
        Assert.False(CellReference.TryParse(text, out _));
}

public class CellValueTests
{
    [Fact]
    public void DatesUseTheLotusCompatibleEpoch()
    {
        // Excel treats 1900 as a leap year for Lotus compatibility; the epoch is 1899-12-30, not
        // 1900-01-01, and serial 1 is 1 January 1900.
        Assert.Equal(new DateTime(1900, 1, 1), CellValue.FromSerial(1));
        Assert.Equal(45658, CellValue.ToSerial(new DateTime(2025, 1, 1)), 6);
    }

    [Fact]
    public void DateRoundTripsThroughItsSerial()
    {
        var when = new DateTime(2026, 3, 14, 15, 9, 26);
        var value = CellValue.FromDateTime(when);

        Assert.Equal(CellValueType.DateTime, value.ValueType);
        Assert.Equal(when, value.AsDateTime(), TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("General", false)]
    [InlineData("#,##0.00", false)]
    [InlineData("\"Rp\"#,##0", false)]
    [InlineData("yyyy-mm-dd", true)]
    [InlineData("[Red]0.00", false)]
    [InlineData("hh:mm:ss", true)]
    public void DateFormatDetectionSkipsQuotedAndBracketedText(string format, bool isDate) =>
        // "Rp" contains no date letters once the quotes are honoured; [Red] must not match 'd'.
        Assert.Equal(isDate, NumberFormats.IsDateFormat(format));
}

public class StyleTests
{
    [Fact]
    public void ADefaultConstructedStyleIsUsable()
    {
        // new CellFont() on a record struct does NOT run the primary constructor, so the parameter
        // defaults never apply. Without the normalising accessors this font has a null name and
        // writing the stylesheet throws.
        CellStyle style = default;

        Assert.Equal("Calibri", style.Font.Name);
        Assert.Equal(11, style.Font.SizePoints);
        Assert.True(style.Locked);
        Assert.Equal(VerticalAlignment.Bottom, style.Vertical);
    }

    [Fact]
    public void DefaultAndExplicitStylesCompareEqual()
    {
        // The generated record equality compares backing fields, which would make these unequal
        // and fill the style table with duplicates.
        Assert.Equal(CellStyle.Default, default(CellStyle));
        Assert.Equal(CellFont.Default, default(CellFont));
    }

    [Fact]
    public void IdenticalStylesShareOneEntry()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        var style = CellStyle.Default.Bold().WithBackground(OfficeColor.Yellow);

        for (var row = 0; row < 100; row++)
        {
            sheet[row, 0].Set(row).WithStyle(style);
        }

        // One default plus one bold-yellow. Deduplication is the whole point of the model.
        Assert.Equal(2, workbook.Styles.Count);
    }

    [Fact]
    public void StylesRoundTrip()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet["A1"].Set("Judul").WithStyle(CellStyle.Default
            .Bold()
            .WithSize(14)
            .WithColor(OfficeColor.White)
            .WithBackground(OfficeColor.FromRgb(0x1F, 0x38, 0x64))
            .WithAlignment(HorizontalAlignment.Center, VerticalAlignment.Center)
            .WithBorder(CellBorder.All(BorderLineStyle.Thin, OfficeColor.Gray))
            .Wrapped());

        var bytes = workbook.ToArray();
        XlsxValidator.AssertValid(bytes);

        using var reopened = Workbook.Open(bytes);
        var style = reopened[0]["A1"].Style;

        Assert.True(style.Font.Bold);
        Assert.Equal(14, style.Font.SizePoints);
        Assert.Equal(OfficeColor.White, style.Font.Color);
        Assert.Equal(OfficeColor.FromRgb(0x1F, 0x38, 0x64), style.BackgroundColor);
        Assert.Equal(HorizontalAlignment.Center, style.Horizontal);
        Assert.Equal(VerticalAlignment.Center, style.Vertical);
        Assert.True(style.WrapText);
        Assert.Equal(BorderLineStyle.Thin, style.Border.Top);
    }

    [Fact]
    public void ANumberFormatSurvivesAndKeepsTheValueNumeric()
    {
        using var workbook = Workbook.Create();
        workbook[0]["A1"].Set(85000.0).WithNumberFormat(NumberFormats.Rupiah);

        using var reopened = Workbook.Open(workbook.ToArray());
        var cell = reopened[0]["A1"];

        Assert.Equal(NumberFormats.Rupiah, cell.Style.NumberFormat);
        Assert.Equal(CellValueType.Number, cell.Value.ValueType);
        Assert.Equal(85000, cell.Number);
    }
}

public class WorksheetTests
{
    [Fact]
    public void ValuesAndTypesRoundTrip()
    {
        var when = new DateTime(2026, 1, 15);

        using var file = new TempFile(".xlsx");

        using (var workbook = Workbook.Create("Data"))
        {
            var sheet = workbook["Data"];
            sheet["A1"].Set("teks");
            sheet["A2"].Set(42);
            sheet["A3"].Set(3.5);
            sheet["A4"].Set(true);
            sheet["A5"].Set(when);
            workbook.Save(file.Path);
        }

        XlsxValidator.AssertValid(file.ReadAllBytes());

        using var reopened = Workbook.Open(file.Path);
        var read = reopened["Data"];

        Assert.Equal("teks", read["A1"].Text);
        Assert.Equal(42, read["A2"].Number);
        Assert.Equal(3.5, read["A3"].Number);
        Assert.True(read["A4"].Value.AsBoolean());

        // A date is a number plus a format; without consulting the stylesheet it reads back as
        // 46037 rather than as a date.
        Assert.Equal(CellValueType.DateTime, read["A5"].Value.ValueType);
        Assert.Equal(when, read["A5"].DateTime);
    }

    [Fact]
    public void EmptyCellsCostNothing()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet["A1"].Set(1);
        sheet["Z1000"].Set(2);

        // A dense array would be 26,000 slots; the sparse model holds two.
        Assert.Equal(2, sheet.CellCount);
        Assert.Equal("A1:Z1000", sheet.UsedRange!.Value.A1);
    }

    [Fact]
    public void FreezePanesAndAutoFilterRoundTrip()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet.WriteHeader("A1", ["Nama", "Kota", "Jumlah"]);

        var bytes = workbook.ToArray();
        XlsxValidator.AssertValid(bytes);

        using var reopened = Workbook.Open(bytes);
        var read = reopened[0];

        Assert.Equal(new FreezePanes(1, 0), read.Frozen);
        Assert.Equal("A1:C1", read.AutoFilter!.Value.A1);
    }

    [Fact]
    public void MergingKeepsOnlyTheTopLeftValue()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet["A1"].Set("judul");
        sheet["B1"].Set("hilang");
        sheet.MergeCells("A1:C1");

        Assert.Equal("judul", sheet["A1"].Text);
        Assert.True(sheet["B1"].IsEmpty);

        using var reopened = Workbook.Open(workbook.ToArray());
        Assert.Equal("A1:C1", Assert.Single(reopened[0].MergedRanges).A1);
    }

    [Fact]
    public void OverlappingMergesAreRefused()
    {
        // Excel reports overlapping merges as corruption rather than repairing them.
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet.MergeCells("A1:C1");

        var ex = Assert.Throws<OfficeNetException>(() => sheet.MergeCells("B1:D1"));
        Assert.Contains("overlap", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Sheet:1")]
    [InlineData("Sheet/1")]
    [InlineData("Sheet[1]")]
    [InlineData("")]
    public void InvalidSheetNamesAreRefused(string name)
    {
        using var workbook = Workbook.Create();
        Assert.ThrowsAny<ArgumentException>(() => workbook.AddSheet(name));
    }

    [Fact]
    public void SheetNamesAreCappedAtThirtyOneCharacters()
    {
        using var workbook = Workbook.Create();
        Assert.Throws<ArgumentException>(() => workbook.AddSheet(new string('a', 32)));
    }

    [Fact]
    public void RemovingTheLastSheetIsRefused()
    {
        // A workbook with no worksheets is a file Excel cannot open.
        using var workbook = Workbook.Create("Satu");
        Assert.Throws<OfficeNetException>(() => workbook.RemoveSheet("Satu"));
    }

    [Fact]
    public void SheetsRoundTripInTabOrder()
    {
        using var workbook = Workbook.Create("Pertama");
        workbook.AddSheet("Kedua");
        workbook.AddSheet("Ketiga");
        workbook.MoveSheet("Ketiga", 0);

        using var reopened = Workbook.Open(workbook.ToArray());

        Assert.Equal(["Ketiga", "Pertama", "Kedua"], reopened.Worksheets.Select(s => s.Name));
    }

    [Fact]
    public void CopySheetDuplicatesCellsAndLayout()
    {
        using var workbook = Workbook.Create("Asli");
        var source = workbook["Asli"];

        source.WriteHeader("A1", ["A", "B"]);
        source["A2"].Set(1);
        source.MergeCells("A5:B5");
        source.SetColumnWidth(0, 25);

        var copy = workbook.CopySheet("Asli", "Salinan");

        Assert.Equal(source.CellCount, copy.CellCount);
        Assert.Single(copy.MergedRanges);
        Assert.Equal(25, copy.GetColumnWidth(0));
        Assert.Equal(source.Frozen, copy.Frozen);
    }
}

public class FormulaTests
{
    private static double Evaluate(string formula)
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet["A1"].Set(10);
        sheet["A2"].Set(20);
        sheet["A3"].Set(30);
        sheet["B1"].Set("Jakarta");
        sheet["B2"].Set("Bandung");
        sheet["B3"].Set("Jakarta");

        sheet["D1"].SetFormula(formula);
        workbook.Recalculate();
        return sheet["D1"].Number;
    }

    private static string EvaluateText(string formula)
    {
        using var workbook = Workbook.Create();
        workbook[0]["D1"].SetFormula(formula);
        workbook.Recalculate();
        return workbook[0]["D1"].Text;
    }

    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("2^3^2", 512)]
    [InlineData("10/4", 2.5)]
    [InlineData("50%", 0.5)]
    [InlineData("-3+10", 7)]
    public void ArithmeticFollowsExcelPrecedence(string formula, double expected) =>
        Assert.Equal(expected, Evaluate(formula), 9);

    [Fact]
    public void UnaryMinusBindsTighterThanExponent() =>
        // -2^2 is 4 in Excel, not -4. Getting this wrong is the classic sign the precedence was
        // implemented from intuition rather than from the specification.
        Assert.Equal(4, Evaluate("-2^2"), 9);

    [Theory]
    [InlineData("SUM(A1:A3)", 60)]
    [InlineData("AVERAGE(A1:A3)", 20)]
    [InlineData("MIN(A1:A3)", 10)]
    [InlineData("MAX(A1:A3)", 30)]
    [InlineData("COUNT(A1:A3)", 3)]
    [InlineData("MEDIAN(A1:A3)", 20)]
    [InlineData("SUMIF(B1:B3,\"Jakarta\",A1:A3)", 40)]
    [InlineData("COUNTIF(B1:B3,\"Jakarta\")", 2)]
    [InlineData("SUMIF(A1:A3,\">15\")", 50)]
    public void AggregatesWorkOverRanges(string formula, double expected) =>
        Assert.Equal(expected, Evaluate(formula), 9);

    [Fact]
    public void CountAndCountaDiffer()
    {
        // COUNT counts numbers; COUNTA counts anything non-empty.
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet["A1"].Set(1);
        sheet["A2"].Set("teks");
        sheet["A3"].Set(3);

        sheet["C1"].SetFormula("COUNT(A1:A3)");
        sheet["C2"].SetFormula("COUNTA(A1:A3)");
        workbook.Recalculate();

        Assert.Equal(2, sheet["C1"].Number);
        Assert.Equal(3, sheet["C2"].Number);
    }

    [Theory]
    [InlineData("ROUND(2.5,0)", 3)]
    [InlineData("ROUND(-2.5,0)", -3)]
    [InlineData("ROUNDDOWN(-2.7,0)", -2)]
    [InlineData("MOD(-3,2)", 1)]
    [InlineData("INT(2.9)", 2)]
    public void MathMatchesExcelsSemanticsNotDotNets(string formula, double expected) =>
        // .NET rounds to even and % takes the dividend's sign; Excel does neither.
        Assert.Equal(expected, Evaluate(formula), 9);

    [Theory]
    [InlineData("IF(1>0,\"ya\",\"tidak\")", "ya")]
    [InlineData("IF(1<0,\"ya\",\"tidak\")", "tidak")]
    [InlineData("CONCATENATE(\"a\",\"b\",\"c\")", "abc")]
    [InlineData("UPPER(\"abc\")", "ABC")]
    [InlineData("LEFT(\"OfficeNet\",6)", "Office")]
    [InlineData("RIGHT(\"OfficeNet\",3)", "Net")]
    [InlineData("MID(\"OfficeNet\",7,3)", "Net")]
    [InlineData("TRIM(\"  a  \")", "a")]
    [InlineData("\"a\"&\"b\"", "ab")]
    public void TextFunctionsWork(string formula, string expected) =>
        Assert.Equal(expected, EvaluateText(formula));

    [Fact]
    public void DivisionByZeroYieldsTheExcelError() =>
        Assert.Equal("#DIV/0!", EvaluateText("1/0"));

    [Fact]
    public void AnUnknownFunctionIsAnErrorNotAnException() =>
        // One unsupported function must not abort a workbook-wide recalculation.
        Assert.Equal("#NAME?", EvaluateText("FUNGSITIDAKADA(1)"));

    [Fact]
    public void IferrorCatchesIt() =>
        Assert.Equal("aman", EvaluateText("IFERROR(1/0,\"aman\")"));

    [Fact]
    public void ACircularReferenceIsReportedNotAStackOverflow()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet["A1"].SetFormula("A2+1");
        sheet["A2"].SetFormula("A1+1");

        workbook.Recalculate();

        Assert.Equal("#REF!", sheet["A1"].Text);
    }

    [Fact]
    public void CachedResultsSurviveTheRoundTrip()
    {
        // Everything that is not Excel reads the cached value; a formula with none shows blank.
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet["A1"].Set(10);
        sheet["A2"].Set(32);
        sheet["A3"].SetFormula("SUM(A1:A2)");

        workbook.Recalculate();

        using var reopened = Workbook.Open(workbook.ToArray());

        Assert.Equal(42, reopened[0]["A3"].Number);
        Assert.Equal("SUM(A1:A2)", reopened[0]["A3"].Formula);
    }

    [Fact]
    public void FormulasReachAcrossSheets()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"]["A1"].Set(100);

        var summary = workbook.AddSheet("Ringkasan");
        summary["A1"].SetFormula("Data!A1*2");

        workbook.Recalculate();

        Assert.Equal(200, summary["A1"].Number);
    }
}

public class IoTests
{
    [Fact]
    public void CsvParsingHandlesQuotedSeparatorsAndNewlines()
    {
        // Splitting on the delimiter turns a quoted address into two columns and shifts every
        // later column on that row.
        const string Csv = "nama,alamat,kota\n\"Fadhil, Kang\",\"Jl. Merdeka 1\nRT 05\",Bandung\n";

        var rows = CsvIo.Parse(Csv).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(["Fadhil, Kang", "Jl. Merdeka 1\nRT 05", "Bandung"], rows[1]);
    }

    [Fact]
    public void CsvQuotesAreDoubledOnTheWayOutAndHalvedOnTheWayBack()
    {
        using var workbook = Workbook.Create();
        workbook[0]["A1"].Set("dia bilang \"halo\"");

        var csv = CsvIo.ExportText(workbook[0]);
        var rows = CsvIo.Parse(csv).ToList();

        Assert.Equal("dia bilang \"halo\"", rows[0][0]);
    }

    [Fact]
    public void CsvImportInfersTypes()
    {
        using var workbook = Workbook.Create();

        var sheet = CsvIo.ImportText(workbook,
            "nama,jumlah,tanggal\nBudi,42,2026-01-15\n", "Impor");

        Assert.Equal("Budi", sheet["A2"].Text);
        Assert.Equal(42, sheet["B2"].Number);
        Assert.Equal(new DateTime(2026, 1, 15), sheet["C2"].DateTime);
    }

    [Fact]
    public void LeadingZerosStayText()
    {
        // "007" and "+62812..." are identifiers, not numbers; converting them is data loss the
        // user cannot undo.
        using var workbook = Workbook.Create();

        var sheet = CsvIo.ImportText(workbook, "kode,telepon\n007,+628123456789\n", "Impor");

        Assert.Equal(CellValueType.Text, sheet["A2"].Value.ValueType);
        Assert.Equal("007", sheet["A2"].Text);
        Assert.Equal("+628123456789", sheet["B2"].Text);
    }

    [Fact]
    public void JsonExportUsesTheHeaderRowAsKeys()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet.WriteHeader("A1", ["nama", "jumlah"]);
        sheet["A2"].Set("Budi");
        sheet["B2"].Set(42);

        var json = JsonIo.ExportText(sheet, indented: false);

        Assert.Contains("\"nama\":\"Budi\"", json, StringComparison.Ordinal);
        Assert.Contains("\"jumlah\":42", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonImportUnionsTheKeysOfEveryRecord()
    {
        // A record that omits an optional field must not shift the later columns.
        using var workbook = Workbook.Create();

        var sheet = JsonIo.Import(workbook,
            """[{"a":1,"b":2},{"a":3,"c":4}]""", "Impor");

        Assert.Equal(["a", "b", "c"], new[] { sheet["A1"].Text, sheet["B1"].Text, sheet["C1"].Text });
        Assert.Equal(3, sheet["A3"].Number);
        Assert.True(sheet["B3"].IsEmpty);
        Assert.Equal(4, sheet["C3"].Number);
    }

    [Fact]
    public void ControlCharactersAreStrippedRatherThanBreakingTheFile()
    {
        // XML 1.0 has no escape for a NUL; writing one produces a file no reader can parse.
        using var workbook = Workbook.Create();
        workbook[0]["A1"].Set("baik\0buruk");

        var bytes = workbook.ToArray();
        XlsxValidator.AssertValid(bytes);

        using var reopened = Workbook.Open(bytes);
        Assert.Equal("baikburuk", reopened[0]["A1"].Text);
    }
}

public class DataFrameTests
{
    [Fact]
    public void ColumnTypesComeFromTheMajorityNotTheFirstCell()
    {
        // A column of a thousand numbers with one "TBD" is a numeric column with one missing
        // value; treating it as text makes every downstream sum impossible.
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet.WriteHeader("A1", ["nilai"]);
        sheet["A2"].Set("TBD");

        for (var row = 2; row < 20; row++)
        {
            sheet[row, 0].Set(row * 1.5);
        }

        var frame = sheet.ToDataFrame();

        Assert.Equal(Gravicode.Science.GraviFrame.DataType.Numeric, frame.Columns[0].DataType);
    }

    [Fact]
    public void RoundTripsThroughGraviFrame()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet.WriteHeader("A1", ["kota", "jumlah", "tanggal"]);

        string[] cities = ["Jakarta", "Bandung", "Surabaya"];

        for (var i = 0; i < 3; i++)
        {
            sheet[i + 1, 0].Set(cities[i]);
            sheet[i + 1, 1].Set((i + 1) * 10);
            sheet[i + 1, 2].Set(new DateTime(2026, 1, i + 1));
        }

        var frame = sheet.ToDataFrame();

        Assert.Equal(3, frame.RowCount);
        Assert.Equal(3, frame.ColumnCount);
        Assert.Equal(Gravicode.Science.GraviFrame.DataType.Text, frame.Columns[0].DataType);
        Assert.Equal(Gravicode.Science.GraviFrame.DataType.Numeric, frame.Columns[1].DataType);
        Assert.Equal(Gravicode.Science.GraviFrame.DataType.DateTime, frame.Columns[2].DataType);

        var written = workbook.WriteDataFrame(frame, "Kembali");

        Assert.Equal("kota", written["A1"].Text);
        Assert.Equal("Bandung", written["A3"].Text);
        Assert.Equal(30, written["B4"].Number);
        Assert.Equal(new DateTime(2026, 1, 2), written["C3"].DateTime);
    }

    [Fact]
    public void GroupingUsesGraviFrameRatherThanReimplementingIt()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        sheet.WriteHeader("A1", ["kota", "jumlah"]);

        string[] cities = ["Jakarta", "Bandung", "Jakarta", "Surabaya"];
        double[] amounts = [10, 20, 30, 40];

        for (var i = 0; i < cities.Length; i++)
        {
            sheet[i + 1, 0].Set(cities[i]);
            sheet[i + 1, 1].Set(amounts[i]);
        }

        var totals = sheet.ToDataFrame().GroupBy("kota").Sum("jumlah");

        Assert.Equal(3, totals.RowCount);
    }
}

public class PdfExportTests
{
    [Fact]
    public void ExportedPdfContainsTheSheetsValues()
    {
        using var workbook = Workbook.Create("Penjualan");
        var sheet = workbook["Penjualan"];

        sheet.WriteHeader("A1", ["Produk", "Jumlah"]);
        sheet["A2"].Set("WordNet");
        sheet["B2"].Set(1500);

        using var pdf = workbook.ToPdf();

        var text = pdf.Pages[0].ExtractText();

        Assert.Contains("Penjualan", text, StringComparison.Ordinal);
        Assert.Contains("Produk", text, StringComparison.Ordinal);
        Assert.Contains("WordNet", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NumberFormatsAreAppliedInTheExport()
    {
        using var workbook = Workbook.Create();
        workbook[0]["A1"].Set(85000.0).WithNumberFormat(NumberFormats.Rupiah);

        using var pdf = workbook.ToPdf();

        // The raw serial would read "85000"; the format makes it "Rp85,000".
        Assert.Contains("Rp", pdf.Pages[0].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void WideSheetsPaginateAcrossPagesRatherThanBeingClipped()
    {
        using var workbook = Workbook.Create();
        var sheet = workbook[0];

        for (var column = 0; column < 40; column++)
        {
            sheet[0, column].Set($"Kolom{column}");
            sheet.SetColumnWidth(column, 20);
        }

        using var pdf = workbook.ToPdf();

        Assert.True(pdf.Pages.Count > 1,
            "40 wide columns must spill onto a second page rather than being clipped");

        var all = string.Concat(pdf.Pages.Select(p => p.ExtractText()));
        Assert.Contains("Kolom39", all, StringComparison.Ordinal);
    }
}

public class SheetChartTests
{
    private static (byte[] Bytes, Workbook Reopened) BuildWithChart(ChartData data, string anchor)
    {
        using var stream = new MemoryStream();

        using (var workbook = Workbook.Create("Data"))
        {
            var sheet = workbook["Data"];
            sheet.WriteHeader("A1", ["Wilayah", "2026"]);
            sheet.WriteRow("A2", "Jakarta", 1480);
            sheet.WriteRow("A3", "Bandung", 1150);

            sheet.AddChart(data, anchor);
            workbook.Save(stream);
        }

        var bytes = stream.ToArray();
        return (bytes, Workbook.Open(bytes));
    }

    private static ChartData Sample => new()
    {
        Type = ChartType.Column,
        Title = "Pendapatan",
        Categories = ["Jakarta", "Bandung"],
        Series = [new ChartSeries("2026", [1480, 1150])],
        ValueFormat = "#,##0",
        ShowDataLabels = true,
    };

    [Fact]
    public void AChartSurvivesTheRoundTrip()
    {
        var (bytes, reopened) = BuildWithChart(Sample, "D2:J18");
        using var workbook = reopened;

        XlsxValidator.AssertValid(bytes);

        var chart = Assert.Single(workbook["Data"].Charts);
        var data = chart.GetData();

        Assert.Equal(ChartType.Column, data.Type);
        Assert.Equal(["Jakarta", "Bandung"], data.Categories);
        Assert.Equal([1480, 1150], data.Series[0].Values);
        Assert.Equal("#,##0", data.ValueFormat);
        Assert.True(data.ShowDataLabels);
    }

    [Fact]
    public void TheAnchorReadsBackAsTheRangeItWasGiven()
    {
        // The xdr "to" marker is exclusive, so a naive reader comes back one row and one column
        // wider than the caller asked for.
        var (_, reopened) = BuildWithChart(Sample, "D2:J18");
        using var workbook = reopened;

        Assert.Equal("D2:J18", workbook["Data"].Charts[0].Anchor.A1);
    }

    [Fact]
    public void TheChartIsReachedThroughADrawingPart()
    {
        // sheet -> drawing -> chart. Excel silently shows no chart when the middle link is missing,
        // so the relationship chain is worth asserting rather than assuming.
        var (bytes, reopened) = BuildWithChart(Sample, "D2:J18");
        using var workbook = reopened;

        using var package = OpcPackage.Open(new MemoryStream(bytes, writable: false));

        var sheetPart = package.Parts.Single(p => p.Name.ToString() == "/xl/worksheets/sheet1.xml");
        var drawing = sheetPart.RelatedPartByType(RelationshipTypes.Drawing);

        Assert.NotNull(drawing);
        Assert.Equal(ContentTypes.SpreadsheetDrawing, drawing.ContentType);

        var chart = drawing.RelatedPartByType(RelationshipTypes.Chart);

        Assert.NotNull(chart);
        Assert.Equal(ContentTypes.Chart, chart.ContentType);
    }

    [Fact]
    public void TheWorksheetsDrawingElementComesLast()
    {
        // CT_Worksheet is a sequence and `drawing` sits at the end of it. Excel repairs a file that
        // puts it before pageMargins.
        var (bytes, reopened) = BuildWithChart(Sample, "D2:J18");
        reopened.Dispose();

        using var package = OpcPackage.Open(new MemoryStream(bytes, writable: false));
        var sheet = package.Parts.Single(p => p.Name.ToString() == "/xl/worksheets/sheet1.xml");

        var children = sheet.Xml.Root!.Elements().Select(e => e.Name.LocalName).ToList();

        Assert.Equal("drawing", children[^1]);
        Assert.True(children.IndexOf("pageMargins") < children.IndexOf("drawing"));
    }

    [Fact]
    public void SetDataReplacesTheChartInPlace()
    {
        using var workbook = Workbook.Create("Data");
        var chart = workbook["Data"].AddChart(Sample, "D2:J18");

        chart.SetData(Sample with { Type = ChartType.Pie, Title = "Porsi" });

        var data = chart.GetData();

        Assert.Equal(ChartType.Pie, data.Type);
        Assert.Equal("Porsi", data.Title);
    }
}
