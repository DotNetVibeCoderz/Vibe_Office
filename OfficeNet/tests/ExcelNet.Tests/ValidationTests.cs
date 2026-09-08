// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet;
using ExcelNet.Validation;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using Xunit;

namespace ExcelNet.Tests;

public class DataValidationTests
{
    private static byte[] Save(Workbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream);
        return stream.ToArray();
    }

    private static System.Xml.Linq.XElement SheetRoot(byte[] bytes)
    {
        using var package = OpcPackage.Open(new MemoryStream(bytes, writable: false));
        return package.Parts.Single(p => p.Name.ToString() == "/xl/worksheets/sheet1.xml").Xml.Root!;
    }

    [Fact]
    public void ADropdownProducesAValidWorkbook()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["Wilayah"]);
        sheet.AddDropdown("A2:A100", "Jakarta", "Bandung", "Surabaya");

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        var validation = SheetRoot(bytes)
            .Element(Ns.S + "dataValidations")!
            .Element(Ns.S + "dataValidation")!;

        Assert.Equal("list", validation.Attr("type"));
        Assert.Equal("A2:A100", validation.Attr("sqref"));
        Assert.Equal("\"Jakarta,Bandung,Surabaya\"",
            validation.Element(Ns.S + "formula1")!.Value);
    }

    [Fact]
    public void ShowDropDownIsInvertedInTheFile()
    {
        // The attribute means the opposite of its name: showDropDown="1" *hides* the arrow. Writing
        // it the intuitive way round produces a dropdown that is missing exactly when it was asked
        // for.
        using var workbook = Workbook.Create("Data");

        workbook["Data"].AddValidation(
            DataValidation.List(CellRangeReference.Parse("A1:A5"), "a", "b") with
            {
                ShowDropDown = true,
            });

        var shown = SheetRoot(Save(workbook))
            .Element(Ns.S + "dataValidations")!
            .Element(Ns.S + "dataValidation")!;

        // Asked to show it, so the attribute must be absent rather than "1".
        Assert.Null(shown.Attr("showDropDown"));

        using var hiddenWorkbook = Workbook.Create("Data");

        hiddenWorkbook["Data"].AddValidation(
            DataValidation.List(CellRangeReference.Parse("A1:A5"), "a", "b") with
            {
                ShowDropDown = false,
            });

        var hidden = SheetRoot(Save(hiddenWorkbook))
            .Element(Ns.S + "dataValidations")!
            .Element(Ns.S + "dataValidation")!;

        Assert.Equal("1", hidden.Attr("showDropDown"));
    }

    [Fact]
    public void AnInlineListPastExcelsLimitIsRejected()
    {
        // Excel drops the whole rule past 255 characters, so a file would open with the dropdown
        // silently missing. Failing here, with the length named, is more useful.
        var values = Enumerable.Range(0, 60).Select(i => $"Nilai-{i:000}").ToArray();

        var exception = Assert.Throws<OfficeNetException>(() =>
            DataValidation.List(CellRangeReference.Parse("A1:A9"), values));

        Assert.Contains("255", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ListFromRange", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AListValueContainingACommaIsRejected()
    {
        // The comma is the separator, so "Jakarta, DKI" would silently become two entries.
        var exception = Assert.Throws<OfficeNetException>(() =>
            DataValidation.List(CellRangeReference.Parse("A1:A9"), "Jakarta, DKI", "Bandung"));

        Assert.Contains("comma", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AListCanComeFromARangeInstead()
    {
        using var workbook = Workbook.Create("Data");
        workbook.AddSheet("Lists");

        workbook["Data"].AddValidation(
            DataValidation.ListFromRange(CellRangeReference.Parse("A2:A100"), "Lists!$A$1:$A$50"));

        var validation = SheetRoot(Save(workbook))
            .Element(Ns.S + "dataValidations")!
            .Element(Ns.S + "dataValidation")!;

        Assert.Equal("Lists!$A$1:$A$50", validation.Element(Ns.S + "formula1")!.Value);
    }

    [Fact]
    public void ANumericRuleCarriesItsOperatorAndBothOperands()
    {
        using var workbook = Workbook.Create("Data");

        workbook["Data"].AddValidation(
            DataValidation.WholeNumberBetween(CellRangeReference.Parse("B2:B50"), 1, 100));

        var validation = SheetRoot(Save(workbook))
            .Element(Ns.S + "dataValidations")!
            .Element(Ns.S + "dataValidation")!;

        Assert.Equal("whole", validation.Attr("type"));
        Assert.Equal("between", validation.Attr("operator"));
        Assert.Equal("1", validation.Element(Ns.S + "formula1")!.Value);
        Assert.Equal("100", validation.Element(Ns.S + "formula2")!.Value);
    }

    [Fact]
    public void AListRuleCarriesNoOperator()
    {
        // The operator is meaningless for a list, and Excel rejects the pair.
        using var workbook = Workbook.Create("Data");
        workbook["Data"].AddDropdown("A1:A5", "a", "b");

        var validation = SheetRoot(Save(workbook))
            .Element(Ns.S + "dataValidations")!
            .Element(Ns.S + "dataValidation")!;

        Assert.Null(validation.Attr("operator"));
    }

    [Fact]
    public void MessagesAreWrittenWithTheFlagsThatMakeThemShow()
    {
        // An error message with no showErrorMessage="1" never appears, which looks like the message
        // was ignored rather than never enabled.
        using var workbook = Workbook.Create("Data");

        workbook["Data"].AddValidation(
            DataValidation.List(CellRangeReference.Parse("A1:A5"), "Ya", "Tidak") with
            {
                ErrorTitle = "Nilai tidak sah",
                ErrorMessage = "Pilih Ya atau Tidak.",
                PromptTitle = "Pilihan",
                PromptMessage = "Pilih dari daftar.",
            });

        var validation = SheetRoot(Save(workbook))
            .Element(Ns.S + "dataValidations")!
            .Element(Ns.S + "dataValidation")!;

        Assert.Equal("1", validation.Attr("showErrorMessage"));
        Assert.Equal("Pilih Ya atau Tidak.", validation.Attr("error"));
        Assert.Equal("1", validation.Attr("showInputMessage"));
        Assert.Equal("Pilih dari daftar.", validation.Attr("prompt"));
    }

    [Fact]
    public void DataValidationsFollowConditionalFormattingInTheSchemaSequence()
    {
        // CT_Worksheet is a sequence; out of order, Excel offers to repair the file.
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["Nilai"]);
        sheet[1, 0].Set(5);

        sheet.AddColorScale("A2:A10",
            OfficeNet.Core.Drawing.OfficeColor.Red,
            OfficeNet.Core.Drawing.OfficeColor.Green);

        sheet.AddDropdown("B2:B10", "a", "b");
        sheet.MergeCells("D1:E1");

        var names = SheetRoot(Save(workbook)).Elements().Select(e => e.Name.LocalName).ToList();

        Assert.True(names.IndexOf("sheetData") < names.IndexOf("mergeCells"));
        Assert.True(names.IndexOf("mergeCells") < names.IndexOf("conditionalFormatting"));
        Assert.True(names.IndexOf("conditionalFormatting") < names.IndexOf("dataValidations"));
        Assert.True(names.IndexOf("dataValidations") < names.IndexOf("pageMargins"));
    }

    [Fact]
    public void ValidationsSurviveASaveAndReopen()
    {
        byte[] first;

        using (var workbook = Workbook.Create("Data"))
        {
            workbook["Data"].AddDropdown("A2:A100", "Jakarta", "Bandung");
            workbook["Data"].AddValidation(
                DataValidation.WholeNumberBetween(CellRangeReference.Parse("B2:B100"), 1, 10) with
                {
                    ErrorMessage = "1 sampai 10.",
                });

            first = Save(workbook);
        }

        // ExcelNet parses a sheet into a model and writes the model back, so a rule the reader does
        // not understand disappears on the next save. Opening a template and saving it must not
        // strip its dropdowns.
        using var reopened = Workbook.Open(new MemoryStream(first, writable: false));
        var rules = reopened["Data"].Validations;

        Assert.Equal(2, rules.Count);
        Assert.Equal(Validation.ValidationType.List, rules[0].Type);
        Assert.Equal("\"Jakarta,Bandung\"", rules[0].Formula1);
        Assert.Equal("A2:A100", rules[0].Range.A1);
        Assert.Equal(Validation.ValidationType.WholeNumber, rules[1].Type);
        Assert.Equal(ValidationOperator.Between, rules[1].Operator);
        Assert.Equal("10", rules[1].Formula2);
        Assert.Equal("1 sampai 10.", rules[1].ErrorMessage);

        var second = SheetRoot(Save(reopened)).Element(Ns.S + "dataValidations")!;
        var original = SheetRoot(first).Element(Ns.S + "dataValidations")!;

        Assert.Equal(original.ToString(), second.ToString());
    }

    [Fact]
    public void ARuleCoveringSeveralAreasBecomesOneRulePerArea()
    {
        // sqref can list disjoint areas separated by spaces. The model holds one range per rule, so
        // dropping the extra areas would silently narrow a rule written by Excel itself.
        using var workbook = Workbook.Create("Data");
        workbook["Data"].AddDropdown("A2:A10", "a", "b");

        var bytes = Save(workbook);

        // Rewrite the saved sqref by hand, the way Excel would have written it.
        using var package = OpcPackage.Open(new MemoryStream(bytes, writable: false));
        var part = package.Parts.Single(p => p.Name.ToString() == "/xl/worksheets/sheet1.xml");
        part.Xml.Root!.Element(Ns.S + "dataValidations")!
            .Element(Ns.S + "dataValidation")!
            .SetAttributeValue("sqref", "A2:A10 C2:C10");

        using var edited = new MemoryStream();
        package.Save(edited);

        using var reopened = Workbook.Open(new MemoryStream(edited.ToArray(), writable: false));

        Assert.Equal(["A2:A10", "C2:C10"],
            reopened["Data"].Validations.Select(v => v.Range.A1));
    }
}

public class SheetProtectionTests
{
    private static byte[] Save(Workbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream);
        return stream.ToArray();
    }

    private static System.Xml.Linq.XElement SheetRoot(byte[] bytes)
    {
        using var package = OpcPackage.Open(new MemoryStream(bytes, writable: false));
        return package.Parts.Single(p => p.Name.ToString() == "/xl/worksheets/sheet1.xml").Xml.Root!;
    }

    [Fact]
    public void ProtectingASheetProducesAValidWorkbook()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["Nama", "Nilai"]);
        sheet.Protect(editable: "B2:B100");

        var bytes = Save(workbook);
        XlsxValidator.AssertValid(bytes);

        Assert.Equal("1", SheetRoot(bytes).Element(Ns.S + "sheetProtection")!.Attr("sheet"));
    }

    [Fact]
    public void TheEditableRangeIsUnlocked()
    {
        // Every cell is locked by default and protection only bites on locked cells, so protecting
        // a sheet without unlocking the inputs freezes the whole thing.
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet["A1"].Set("Terkunci");
        sheet["B1"].Set("Bisa diisi");

        sheet.Protect(editable: "B1:B10");

        Assert.True(sheet["A1"].Style.Locked);
        Assert.False(sheet["B1"].Style.Locked);
    }

    [Fact]
    public void TheProtectionFlagsAreInvertedInTheFile()
    {
        // "1" forbids. Writing them the intuitive way round produces a sheet that permits exactly
        // what the caller asked to forbid.
        using var workbook = Workbook.Create("Data");

        workbook["Data"].Protection = new SheetProtection
        {
            Sort = true,             // allowed   -> sort="0"
            InsertRows = false,      // forbidden -> insertRows="1"
        };

        var protection = SheetRoot(Save(workbook)).Element(Ns.S + "sheetProtection")!;

        Assert.Equal("0", protection.Attr("sort"));
        Assert.Equal("1", protection.Attr("insertRows"));
    }

    [Fact]
    public void EveryFlagIsWrittenOutRatherThanLeftToItsDefault()
    {
        // The schema defaults are not uniform: most flags default to forbidden, but
        // selectLockedCells and selectUnlockedCells default to allowed. Leaving an attribute out
        // therefore means the opposite thing depending on which attribute it is.
        using var workbook = Workbook.Create("Data");

        workbook["Data"].Protection = new SheetProtection
        {
            SelectLockedCells = false,     // do not let anyone even click a locked cell
            SelectUnlockedCells = false,
        };

        var protection = SheetRoot(Save(workbook)).Element(Ns.S + "sheetProtection")!;

        Assert.Equal("1", protection.Attr("selectLockedCells"));
        Assert.Equal("1", protection.Attr("selectUnlockedCells"));

        string[] flags =
        [
            "formatCells", "formatColumns", "formatRows",
            "insertRows", "insertColumns", "deleteRows", "deleteColumns",
            "sort", "autoFilter", "selectLockedCells", "selectUnlockedCells",
        ];

        Assert.All(flags, flag => Assert.NotNull(protection.Attr(flag)));
    }

    [Fact]
    public void SheetProtectionSitsRightAfterSheetData()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet["A1"].Set("x");
        sheet.MergeCells("C1:D1");
        sheet.Protect();

        var names = SheetRoot(Save(workbook)).Elements().Select(e => e.Name.LocalName).ToList();

        Assert.Equal(names.IndexOf("sheetData") + 1, names.IndexOf("sheetProtection"));
        Assert.True(names.IndexOf("sheetProtection") < names.IndexOf("mergeCells"));
    }

    [Fact]
    public void APasswordIsHashedRatherThanStored()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"].Protect(password: "rahasia");

        var hash = SheetRoot(Save(workbook)).Element(Ns.S + "sheetProtection")!.Attr("password");

        Assert.NotNull(hash);
        Assert.DoesNotContain("rahasia", hash, StringComparison.OrdinalIgnoreCase);

        // Excel's legacy hash is a 16-bit value written as four hex digits — which is exactly why
        // this is not security.
        Assert.Equal(4, hash.Length);
        Assert.True(ushort.TryParse(hash, System.Globalization.NumberStyles.HexNumber, null, out _));
    }

    [Fact]
    public void TheSamePasswordAlwaysHashesTheSame()
    {
        // Excel checks against this exact value, so the algorithm cannot drift.
        Assert.Equal(SheetProtection.WithPassword("rahasia").PasswordHash,
                     SheetProtection.WithPassword("rahasia").PasswordHash);

        Assert.NotEqual(SheetProtection.WithPassword("rahasia").PasswordHash,
                        SheetProtection.WithPassword("berbeda").PasswordHash);
    }

    [Fact]
    public void ProtectionSurvivesOpeningAndSavingAgain()
    {
        // ExcelNet parses a sheet into a model and writes the model back, so anything the reader
        // does not understand is dropped. Opening a protected template and saving it must not
        // quietly unprotect it.
        byte[] first;

        using (var workbook = Workbook.Create("Data"))
        {
            workbook["Data"]["A1"].Set("x");
            workbook["Data"].Protection = new SheetProtection
            {
                PasswordHash = SheetProtection.WithPassword("rahasia").PasswordHash,
                Sort = true,
                InsertRows = true,
                SelectLockedCells = false,
            };

            first = Save(workbook);
        }

        using var reopened = Workbook.Open(new MemoryStream(first, writable: false));
        var protection = reopened["Data"].Protection;

        Assert.NotNull(protection);
        Assert.True(protection.Sort);
        Assert.True(protection.InsertRows);
        Assert.False(protection.FormatCells);
        Assert.False(protection.SelectLockedCells);
        Assert.Equal(SheetProtection.WithPassword("rahasia").PasswordHash, protection.PasswordHash);

        // And again through the writer, byte for byte on the flags.
        var second = SheetRoot(Save(reopened)).Element(Ns.S + "sheetProtection")!;
        var original = SheetRoot(first).Element(Ns.S + "sheetProtection")!;

        Assert.Equal(original.ToString(), second.ToString());
    }

    [Fact]
    public void UnprotectRemovesTheElementEntirely()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet["A1"].Set("x");
        sheet.Protect();
        sheet.Unprotect();

        Assert.Null(SheetRoot(Save(workbook)).Element(Ns.S + "sheetProtection"));
    }
}
