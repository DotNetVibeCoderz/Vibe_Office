// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet.Streaming;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.TestKit;
using Xunit;

namespace ExcelNet.Tests;

public class StreamingTests
{
    private static byte[] Write(Action<StreamingWorkbook> body, params string[] sheetNames)
    {
        using var stream = new MemoryStream();

        using (var workbook = StreamingWorkbook.Create(stream, sheetNames))
        {
            body(workbook);
        }

        return stream.ToArray();
    }

    [Fact]
    public void AStreamedWorkbookIsAValidPackage()
    {
        var bytes = Write(workbook =>
        {
            var sheet = workbook.Sheet("Data");
            sheet.WriteHeader("Nama", "Wilayah", "Jumlah");
            sheet.WriteRow("Andi", "Jakarta", 1480);
            sheet.WriteRow("Budi", "Bandung", 1150);
        }, "Data");

        XlsxValidator.AssertValid(bytes);
    }

    [Fact]
    public void ItReadsBackThroughTheOrdinaryReader()
    {
        // The whole point is a file Excel opens, and the closest independent reader available is the
        // library's own parser, which has never seen the streaming writer's output.
        var bytes = Write(workbook =>
        {
            var sheet = workbook.Sheet("Data");
            sheet.WriteHeader("Nama", "Jumlah", "Tanggal", "Aktif");
            sheet.WriteRow("Andi", 1480, new DateTime(2026, 3, 17), true);
            sheet.WriteRow("Budi", 1150.75, new DateTime(2026, 4, 1, 9, 30, 0), false);
        }, "Data");

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));
        var sheet = reopened["Data"];

        Assert.Equal("Nama", sheet["A1"].Text);
        Assert.Equal("Andi", sheet["A2"].Text);
        Assert.Equal(1480, sheet["B2"].Number);
        Assert.Equal(new DateTime(2026, 3, 17), sheet["C2"].DateTime);
        Assert.True(sheet["D2"].Value.AsBoolean());

        Assert.Equal(1150.75, sheet["B3"].Number);
        Assert.Equal(new DateTime(2026, 4, 1, 9, 30, 0), sheet["C3"].DateTime);
        Assert.False(sheet["D3"].Value.AsBoolean());
    }

    [Fact]
    public void ANullIsAnEmptyCellRatherThanAnEmptyString()
    {
        // The difference between a gap in the data and a value that happens to be blank — and the
        // difference between COUNT and COUNTA agreeing with the source and not.
        var bytes = Write(workbook =>
        {
            var sheet = workbook.Sheet("Data");
            sheet.WriteRow("Andi", null, 3);
        }, "Data");

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));
        var sheet = reopened["Data"];

        Assert.True(sheet["B1"].IsEmpty);
        Assert.Equal(3, sheet["C1"].Number);
    }

    [Fact]
    public void LeadingAndTrailingSpacesSurvive()
    {
        // Without xml:space="preserve" the reader discards them, silently turning " 001" into "001"
        // — which is exactly the padding someone put there on purpose.
        var bytes = Write(workbook => workbook.Sheet("Data").WriteRow(" 001 ", "x "), "Data");

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));

        Assert.Equal(" 001 ", reopened["Data"]["A1"].Text);
        Assert.Equal("x ", reopened["Data"]["B1"].Text);
    }

    [Fact]
    public void ControlCharactersAreDroppedRatherThanBreakingTheFile()
    {
        // A control byte in a database column is common and legal; in XML it is not, and writing one
        // produces a file every reader rejects. Losing the character beats losing the file.
        var bytes = Write(workbook =>
            workbook.Sheet("Data").WriteRow("Andi\u0001Budi", "Tab\there"), "Data");

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));

        // The control byte is gone; everything around it survived.
        Assert.Equal("AndiBudi", reopened["Data"]["A1"].Text);
        Assert.Equal("Tab\there", reopened["Data"]["B1"].Text);
    }

    [Fact]
    public void SkippedRowsLeaveAGapWithTheRightNumbering()
    {
        var bytes = Write(workbook =>
        {
            var sheet = workbook.Sheet("Data");
            sheet.WriteRow("Atas");
            sheet.SkipRows(3);
            sheet.WriteRow("Bawah");
        }, "Data");

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));

        Assert.Equal("Atas", reopened["Data"]["A1"].Text);
        Assert.True(reopened["Data"]["A2"].IsEmpty);
        Assert.Equal("Bawah", reopened["Data"]["A5"].Text);
    }

    [Fact]
    public void SeveralSheetsCanBeWrittenInTurn()
    {
        var bytes = Write(workbook =>
        {
            workbook.Sheet("Pertama").WriteRow("a");
            workbook.Sheet("Kedua").WriteRow("b");
            workbook.Sheet("Ketiga").WriteRow("c");
        }, "Pertama", "Kedua", "Ketiga");

        XlsxValidator.AssertValid(bytes);

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));

        Assert.Equal(["Pertama", "Kedua", "Ketiga"], reopened.Worksheets.Select(s => s.Name));
        Assert.Equal("b", reopened["Kedua"]["A1"].Text);
    }

    [Fact]
    public void ADuplicateSheetNameIsRefused()
    {
        using var stream = new MemoryStream();

        var exception = Assert.Throws<OfficeNetException>(() =>
            StreamingWorkbook.Create(stream, "Data", "data"));

        Assert.Contains("cannot share a name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASheetTheWorkbookDoesNotHaveIsRefusedWithTheOnesItDoes()
    {
        using var stream = new MemoryStream();
        using var workbook = StreamingWorkbook.Create(stream, "Data", "Ringkasan");

        var exception = Assert.Throws<OfficeNetException>(() => workbook.Sheet("Lain"));

        Assert.Contains("Data, Ringkasan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASheetCannotBeReopened()
    {
        // Forward-only at the workbook level: the entry is closed and the bytes have gone.
        using var stream = new MemoryStream();
        using var workbook = StreamingWorkbook.Create(stream, "Data", "Ringkasan");

        workbook.Sheet("Data").WriteRow("a");
        workbook.Sheet("Ringkasan").WriteRow("b");

        var exception = Assert.Throws<OfficeNetException>(() => workbook.Sheet("Data"));

        Assert.Contains("already been written", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredSheetNobodyWroteToStillExists()
    {
        // [Content_Types].xml has already promised the part. One named there but absent from the
        // package makes Excel offer to repair the file.
        var bytes = Write(workbook => workbook.Sheet("Data").WriteRow("a"), "Data", "Kosong");

        XlsxValidator.AssertValid(bytes);

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));

        Assert.Equal(2, reopened.Worksheets.Count);
        Assert.True(reopened["Kosong"]["A1"].IsEmpty);
    }

    [Fact]
    public void AWorkbookWithNoSheetsStillOpens()
    {
        // Excel refuses a workbook with an empty <sheets>, so naming none has to produce one sheet
        // rather than a file nobody can open.
        var bytes = Write(_ => { });

        XlsxValidator.AssertValid(bytes);

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));
        Assert.Single(reopened.Worksheets);
    }

    [Fact]
    public void TheReservedFillIndicesAreCorrect()
    {
        // Excel hard-codes fill 0 as none and fill 1 as gray125. A stylesheet that puts anything
        // else there renders every fill in the file wrong.
        var bytes = Write(workbook => workbook.Sheet("Data").WriteRow("x"), "Data");

        using var package = OpcPackage.Open(new MemoryStream(bytes, writable: false));
        var styles = package.Parts.Single(p => p.Name.ToString() == "/xl/styles.xml").Xml.Root!;

        var fills = styles.Element(OfficeNet.Core.Xml.Ns.S + "fills")!
            .Elements(OfficeNet.Core.Xml.Ns.S + "fill")
            .Select(f => f.Element(OfficeNet.Core.Xml.Ns.S + "patternFill")!.Attribute("patternType")!.Value)
            .ToList();

        Assert.Equal("none", fills[0]);
        Assert.Equal("gray125", fills[1]);
    }

    [Fact]
    public void AHeaderRowIsBoldAndSaysSoWithApplyFont()
    {
        // Excel ignores the font outright unless applyFont is set, which is the top reason a
        // hand-written stylesheet appears to do nothing at all.
        var bytes = Write(workbook => workbook.Sheet("Data").WriteHeader("Nama"), "Data");

        using var package = OpcPackage.Open(new MemoryStream(bytes, writable: false));
        var styles = package.Parts.Single(p => p.Name.ToString() == "/xl/styles.xml").Xml.Root!;

        var headerXf = styles.Element(OfficeNet.Core.Xml.Ns.S + "cellXfs")!
            .Elements(OfficeNet.Core.Xml.Ns.S + "xf")
            .ElementAt(StreamingSheet.HeaderStyle);

        Assert.Equal("1", headerXf.Attribute("applyFont")!.Value);
        Assert.Equal("1", headerXf.Attribute("fontId")!.Value);

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));
        Assert.True(reopened["Data"]["A1"].Style.Font.Bold);
    }

    [Fact]
    public void ADateCarriesANumberFormatOrItReadsBackAsANumber()
    {
        // A date is a number plus a number format. Without applyNumberFormat, the format is ignored
        // and the cell shows 46098 instead of a date.
        var bytes = Write(workbook =>
            workbook.Sheet("Data").WriteRow(new DateTime(2026, 3, 17)), "Data");

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));

        Assert.Equal(CellValueType.DateTime, reopened["Data"]["A1"].Value.ValueType);
    }

    [Fact]
    public void ColumnReferencesCrossTheLetterBoundariesCorrectly()
    {
        // Z to AA and ZZ to AAA are where a base-26 conversion that forgets the alphabet has no zero
        // goes wrong, and it goes wrong silently: the cells are simply in the wrong columns.
        var bytes = Write(workbook =>
            workbook.Sheet("Data").WriteRow([.. Enumerable.Range(0, 703).Select(i => (object?)i)]),
            "Data");

        using var reopened = Workbook.Open(new MemoryStream(bytes, writable: false));
        var sheet = reopened["Data"];

        Assert.Equal(0, sheet["A1"].Number);
        Assert.Equal(25, sheet["Z1"].Number);
        Assert.Equal(26, sheet["AA1"].Number);
        Assert.Equal(51, sheet["AZ1"].Number);
        Assert.Equal(52, sheet["BA1"].Number);
        Assert.Equal(701, sheet["ZZ1"].Number);
        Assert.Equal(702, sheet["AAA1"].Number);
    }

    [Fact]
    public void AColumnPastExcelsLastIsRefused()
    {
        using var stream = new MemoryStream();
        using var workbook = StreamingWorkbook.Create(stream, "Data");
        var sheet = workbook.Sheet("Data");

        var row = new object?[16385];
        row[^1] = "past the edge";

        var exception = Assert.Throws<OfficeNetException>(() => sheet.WriteRow(row));
        Assert.Contains("16,384", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyCellPastTheLastColumnIsNotAnError()
    {
        // Nothing is written for a null, so nothing is out of range either. Throwing here would
        // reject a jagged row that happens to have trailing nulls.
        using var stream = new MemoryStream();
        using var workbook = StreamingWorkbook.Create(stream, "Data");

        workbook.Sheet("Data").WriteRow(new object?[20000]);
    }

    [Fact]
    public void MemoryDoesNotGrowWithTheRowCount()
    {
        // The reason this class exists. Asserting on the managed heap rather than on time, because a
        // timing assertion is flaky on a shared build machine and this one is not.
        //
        // It has to write to a file, not to a MemoryStream: a MemoryStream *is* the output, so it
        // grows with the row count no matter how little the writer keeps, and the test would fail
        // for a reason that has nothing to do with the writer.
        const int Rows = 200_000;

        using var file = new TempFile(".xlsx");

        using (var workbook = StreamingWorkbook.Create(file.Path, "Data"))
        {
            var sheet = workbook.Sheet("Data");
            sheet.WriteHeader("Id", "Nama", "Jumlah");

            // Let the first rows settle, then measure over the rest. A writer that accumulates rows
            // — or a shared-string table — shows up here as steady growth.
            for (var i = 0; i < 20_000; i++)
            {
                sheet.WriteRow(i, $"Baris {i}", i * 1.5);
            }

            var before = GC.GetTotalMemory(forceFullCollection: true);

            for (var i = 20_000; i < Rows; i++)
            {
                sheet.WriteRow(i, $"Baris {i}", i * 1.5);
            }

            var after = GC.GetTotalMemory(forceFullCollection: true);

            Assert.True(after - before < 2 * 1024 * 1024,
                $"The heap grew by {(after - before) / 1024.0 / 1024:0.#} MB over {Rows - 20_000:N0} " +
                "rows, which means something is being kept.");

            Assert.Equal(Rows + 1, sheet.RowCount);
        }

        // And the file it produced is still a real one.
        XlsxValidator.AssertValid(File.ReadAllBytes(file.Path));
    }
}
