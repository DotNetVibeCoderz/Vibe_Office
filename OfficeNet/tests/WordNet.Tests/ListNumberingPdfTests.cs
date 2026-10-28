// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using WordNet.Numbering;
using Xunit;

namespace WordNet.Tests;

/// <summary>The labels a numbered list carries in the PDF export, counted as Word counts them.</summary>
public class ListNumberingPdfTests
{
    private static string[] Lines(WordDocument document)
    {
        using var pdf = document.ToPdf();
        return [.. pdf.ExtractText().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    [Fact]
    public void ANumberedListIsDrawnWithItsNumbers()
    {
        using var document = WordDocument.Create();
        document.AddList(["Jakarta", "Bandung", "Surabaya"], numbered: true);
        document.AddList(["catatan"]);

        Assert.Equal(["1. Jakarta", "2. Bandung", "3. Surabaya", "• catatan"], Lines(document));
    }

    [Fact]
    public void AnInterruptedListResumesAndANestedLevelRestarts()
    {
        using var document = WordDocument.Create();
        var outline = document.Numbering.AddOutlineList();

        document.AddParagraph("Satu").SetListItem(outline);
        document.AddParagraph("Satu titik satu").SetListItem(outline, 1);
        document.AddParagraph("Satu titik dua").SetListItem(outline, 1);
        document.AddParagraph("Paragraf di antara");
        document.AddParagraph("Dua").SetListItem(outline);
        document.AddParagraph("Dua titik satu").SetListItem(outline, 1);

        // The outline's level text names both levels: 1.1, 1.2, then 2.1 after the interruption.
        Assert.Equal(["1. Satu", "1.1. Satu titik satu", "1.2. Satu titik dua", "Paragraf di antara", "2. Dua", "2.1. Dua titik satu"],
            Lines(document));
    }

    [Fact]
    public void ANestedLevelSitsWhereItsDefinitionPutsIt()
    {
        using var document = WordDocument.Create();
        var outline = document.Numbering.AddOutlineList();
        document.AddParagraph("Induk").SetListItem(outline);
        document.AddParagraph("Anak").SetListItem(outline, 1);
        document.AddParagraph("Cucu").SetListItem(outline, 2);

        using var pdf = document.ToPdf();
        var fragments = pdf.Pages[0].ExtractTextFragments();
        double Left(string text) => fragments.First(f => f.Text.Contains(text, StringComparison.Ordinal)).X;

        // Each level further right, by the numbering definition's own indentation.
        Assert.True(Left("Anak") > Left("Induk") + 10);
        Assert.True(Left("Cucu") > Left("Anak") + 10);
    }

    [Fact]
    public void EachLevelInALabelKeepsItsOwnFormat()
    {
        // "%1.%2" with level one decimal and level two lettered reads 1.a, not a.a.
        using var document = WordDocument.Create();
        var outline = document.Numbering.AddOutlineList();

        Assert.True(document.Package.TryGetPart("/word/numbering.xml", out var part));
        var w = OfficeNet.Core.Xml.Ns.W;
        var second = part.Xml.Descendants(w + "lvl").First(l => l.Attribute(w + "ilvl")?.Value == "1");
        second.Element(w + "numFmt")!.SetAttributeValue(w + "val", "lowerLetter");
        second.Element(w + "lvlText")!.SetAttributeValue(w + "val", "%1.%2");

        document.AddParagraph("Induk").SetListItem(outline);
        document.AddParagraph("Anak").SetListItem(outline, 1);
        document.AddParagraph("Anak kedua").SetListItem(outline, 1);

        Assert.Equal(["1. Induk", "1.a Anak", "1.b Anak kedua"], Lines(document));
    }

    [Fact]
    public void ALabelWiderThanItsIndentPushesTheTextAlong()
    {
        using var document = WordDocument.Create();
        var roman = document.Numbering.AddNumberedList(NumberFormat.UpperRoman, startAt: 3888);
        document.AddParagraph("panjang").SetListItem(roman);

        using var pdf = document.ToPdf();
        var fragments = pdf.Pages[0].ExtractTextFragments();
        var label = fragments.Single(f => f.Text.StartsWith("MMMDCCCLXXXVIII", StringComparison.Ordinal));
        var text = fragments.Single(f => f.Text.Contains("panjang", StringComparison.Ordinal));

        Assert.True(text.X >= label.X + label.Width, $"text at {text.X} overlaps label ending at {label.X + label.Width}");
    }

    [Fact]
    public void ASymbolFontBulletIsDrawnAsABullet()
    {
        // Word's own bullet is U+F0B7 in the Symbol font: a private-use character, nothing in any
        // other font.
        using var document = WordDocument.Create();
        var list = document.Numbering.AddBulletList("");
        document.AddParagraph("butir").SetListItem(list);

        Assert.Equal(["• butir"], Lines(document));
    }

    [Fact]
    public void FormatsAndAStartingNumberAreHonoured()
    {
        using var document = WordDocument.Create();
        var roman = document.Numbering.AddNumberedList(NumberFormat.UpperRoman, startAt: 4);
        var letters = document.Numbering.AddNumberedList(NumberFormat.LowerLetter);

        document.AddParagraph("empat").SetListItem(roman);
        document.AddParagraph("lima").SetListItem(roman);
        document.AddParagraph("a").SetListItem(letters);
        document.AddParagraph("b").SetListItem(letters);

        Assert.Equal(["IV. empat", "V. lima", "a. a", "b. b"], Lines(document));
    }

    [Fact]
    public void ARestartedListCountsFromItsOwnStart()
    {
        using var document = WordDocument.Create();
        var first = document.AddList(["x", "y"], numbered: true);
        var restarted = document.Numbering.Restart(first[0].Element.Descendants(OfficeNet.Core.Xml.Ns.W + "numId")
            .Select(e => int.Parse(e.Attribute(OfficeNet.Core.Xml.Ns.W + "val")!.Value, System.Globalization.CultureInfo.InvariantCulture)).Single());

        document.AddParagraph("z").SetListItem(restarted);

        Assert.Equal(["1. x", "2. y", "1. z"], Lines(document));
    }

    [Theory]
    [InlineData(1, "lowerLetter", "a")]
    [InlineData(27, "lowerLetter", "aa")]
    [InlineData(28, "upperLetter", "BB")]
    [InlineData(4, "lowerRoman", "iv")]
    [InlineData(1994, "upperRoman", "MCMXCIV")]
    [InlineData(7, "decimalZero", "07")]
    [InlineData(12, "ordinalText", "12")]
    public void NumbersAreWrittenInWordsFormats(int value, string format, string expected) =>
        Assert.Equal(expected, NumberingDefinitions.FormatNumber(value, format));
}
