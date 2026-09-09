// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using WordNet;
using WordNet.Sections;
using WordNet.Tables;
using Xunit;

namespace WordNet.Tests;

/// <summary>Structural rules a .docx must satisfy, checked without going through WordNet.</summary>
internal static class DocxValidator
{
    private static readonly string[] RunPropertyOrder =
    [
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike",
        "outline", "shadow", "emboss", "imprint", "noProof", "snapToGrid", "vanish", "webHidden",
        "color", "spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect",
        "bdr", "shd", "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout",
        "specVanish", "oMath",
    ];

    private static readonly string[] ParagraphPropertyOrder =
    [
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr",
        "suppressLineNumbers", "pBdr", "shd", "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap",
        "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd",
        "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap",
        "jc", "textDirection", "textAlignment", "textboxTightWrap", "outlineLvl", "divId",
        "cnfStyle", "rPr", "sectPr", "pPrChange",
    ];

    internal static IReadOnlyList<string> Validate(byte[] docx)
    {
        var report = OpcValidator.Validate(docx);
        var problems = new List<string>(report.Problems);

        using var stream = new MemoryStream(docx, writable: false);
        using var archive = new System.IO.Compression.ZipArchive(stream);

        var entry = archive.GetEntry("word/document.xml");

        if (entry is null)
        {
            problems.Add("word/document.xml is missing.");
            return problems;
        }

        using var entryStream = entry.Open();
        var document = XDocument.Load(entryStream);

        var body = document.Root?.Element(Ns.W + "body");

        if (body is null)
        {
            problems.Add("word/document.xml has no w:body.");
            return problems;
        }

        // The last section's properties must be the body's final child, or Word treats them as a
        // section break in the middle of the document.
        if (body.Elements().LastOrDefault()?.Name != Ns.W + "sectPr")
        {
            problems.Add("w:body does not end with w:sectPr.");
        }

        foreach (var properties in document.Descendants(Ns.W + "rPr"))
        {
            if (OpcValidator.CheckChildOrder(properties, RunPropertyOrder) is { } problem)
            {
                problems.Add(problem);
            }
        }

        foreach (var properties in document.Descendants(Ns.W + "pPr"))
        {
            if (OpcValidator.CheckChildOrder(properties, ParagraphPropertyOrder) is { } problem)
            {
                problems.Add(problem);
            }
        }

        // The table containers are ordered sequences in their own right, not just their property
        // bags. Checking only w:rPr and w:pPr let a w:trPr written after the last w:tc through —
        // a file Word opens only by repairing it.
        foreach (var table in document.Descendants(Ns.W + "tbl"))
        {
            if (OpcValidator.CheckChildOrder(table, "tblPr", "tblGrid", "tr") is { } problem)
            {
                problems.Add(problem);
            }
        }

        foreach (var row in document.Descendants(Ns.W + "tr"))
        {
            if (OpcValidator.CheckChildOrder(row, "tblPrEx", "trPr", "tc") is { } problem)
            {
                problems.Add(problem);
            }
        }

        // w:tc is not a full sequence — w:p and w:tbl interleave freely inside it — so only the
        // one real constraint is checked: w:tcPr, when present, comes first.
        foreach (var cell in document.Descendants(Ns.W + "tc"))
        {
            var children = cell.Elements().ToList();
            var propertiesIndex = children.FindIndex(e => e.Name == Ns.W + "tcPr");

            if (propertiesIndex > 0)
            {
                problems.Add($"A w:tc has w:tcPr at position {propertiesIndex}; it must come first.");
            }
        }

        // A table cell must contain at least one block and must end with a paragraph.
        foreach (var cell in document.Descendants(Ns.W + "tc"))
        {
            var blocks = cell.Elements().Where(e => e.Name != Ns.W + "tcPr").ToList();

            if (blocks.Count == 0)
            {
                problems.Add("A w:tc is empty; Word requires at least one block-level child.");
            }
            else if (blocks[^1].Name != Ns.W + "p")
            {
                problems.Add($"A w:tc ends with <{blocks[^1].Name.LocalName}>; it must end with w:p.");
            }
        }

        // Every r:id used in the document must resolve in the document part's relationships.
        var rels = archive.GetEntry("word/_rels/document.xml.rels");
        var ids = new HashSet<string>(StringComparer.Ordinal);

        if (rels is not null)
        {
            using var relsStream = rels.Open();
            var relationships = XDocument.Load(relsStream);

            foreach (var relationship in relationships.Root?.Elements() ?? [])
            {
                if (relationship.Attribute("Id")?.Value is { } id)
                {
                    ids.Add(id);
                }
            }
        }

        foreach (var attribute in document.Descendants().Attributes()
                     .Where(a => a.Name.Namespace == Ns.R))
        {
            if (!ids.Contains(attribute.Value))
            {
                problems.Add($"document.xml references relationship '{attribute.Value}', which does not exist.");
            }
        }

        return problems;
    }

    internal static void AssertValid(byte[] docx)
    {
        var problems = Validate(docx);

        Assert.True(problems.Count == 0,
            "The .docx is structurally invalid:\n  - " + string.Join("\n  - ", problems));
    }
}

public class DocumentTests
{
    [Fact]
    public void CreateSaveReopenPreservesContentAndMetadata()
    {
        using var file = new TempFile(".docx");

        using (var document = WordDocument.Create())
        {
            document.Properties.Title = "Laporan";
            document.Properties.Creator = "Kang Fadhil";
            document.Custom["Departemen"] = "Riset";

            document.AddHeading("Judul Utama", 1);
            document.AddParagraph("Isi paragraf pertama.");

            document.Save(file.Path);
        }

        DocxValidator.AssertValid(file.ReadAllBytes());

        using var reopened = WordDocument.Open(file.Path);

        Assert.Equal("Laporan", reopened.Properties.Title);
        Assert.Equal("Riset", reopened.Custom["Departemen"]);
        Assert.Equal(2, reopened.Paragraphs.Count);
        Assert.Equal("Judul Utama", reopened.Paragraphs[0].Text);
        Assert.Equal(1, reopened.Paragraphs[0].HeadingLevel);
    }

    [Fact]
    public void RejectsAPackageThatIsNotWordprocessingMl()
    {
        // A .docx, .xlsx and .pptx share a container, so the content type is the only thing that
        // tells them apart.
        var workbook = ExcelLikePackage();

        var ex = Assert.Throws<OfficeNetException>(() => WordDocument.Open(workbook));
        Assert.Contains("WordprocessingML", ex.Message, StringComparison.Ordinal);
    }

    private static byte[] ExcelLikePackage()
    {
        var package = OfficeNet.Core.Packaging.OpcPackage.Create();

        var main = package.AddXmlPart("/xl/workbook.xml",
            OfficeNet.Core.Packaging.ContentTypes.ExcelWorkbook,
            XmlUtil.NewDocument(new XElement(Ns.S + "workbook")));

        package.AddRootRelationship(main, OfficeNet.Core.Packaging.RelationshipTypes.OfficeDocument);
        return package.ToArray();
    }

    [Fact]
    public void FromTemplateKeepsStylesAndPageSetupButDropsContent()
    {
        using var template = new TempFile(".docx");

        using (var document = WordDocument.Create())
        {
            document.Styles.Add("Merek", "Merek Perusahaan").RunFormat.Color =
                OfficeColor.FromRgb(0x1F, 0x38, 0x64);

            document.Section.SetPageSize("F4");
            document.AddParagraph("Konten yang harus hilang.");
            document.Save(template.Path);
        }

        using var generated = WordDocument.FromTemplate(template.Path);

        Assert.Empty(generated.Paragraphs);
        Assert.NotNull(generated.Styles["Merek"]);
        Assert.Equal(215, generated.Section.PageWidth.Millimeters, 0);
        Assert.Equal(330, generated.Section.PageHeight.Millimeters, 0);
    }
}

public class FormattingTests
{
    [Fact]
    public void RunFormattingRoundTrips()
    {
        using var document = WordDocument.Create();

        var run = document.AddParagraph().AddRun("Teks");
        run.Format.Bold = true;
        run.Format.Italic = true;
        run.Format.FontSize = Units.Pt(14);
        run.Format.FontName = "Georgia";
        run.Format.Color = OfficeColor.FromRgb(0xC0, 0x00, 0x00);
        run.Format.Underline = UnderlineStyle.Double;

        using var reopened = WordDocument.Open(document.ToArray());
        var read = reopened.Paragraphs[0].Runs[0].Format;

        Assert.True(read.Bold);
        Assert.True(read.Italic);
        Assert.Equal(14, read.FontSize!.Value.Points, 6);
        Assert.Equal("Georgia", read.FontName);
        Assert.Equal(OfficeColor.FromRgb(0xC0, 0x00, 0x00), read.Color);
        Assert.Equal(UnderlineStyle.Double, read.Underline);
    }

    [Fact]
    public void ExplicitlyOffIsNotTheSameAsInherit()
    {
        // A run with Bold = false inside a bold heading must render unbolded; a run with
        // Bold = null must inherit. Collapsing them loses the override.
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph(styleId: "Heading1");
        var inherited = paragraph.AddRun("mewarisi");
        var overridden = paragraph.AddRun("dipaksa");
        overridden.Format.Bold = false;

        using var reopened = WordDocument.Open(document.ToArray());
        var runs = reopened.Paragraphs[0].Runs;

        Assert.Null(runs[0].Format.Bold);
        Assert.False(runs[1].Format.Bold);
    }

    [Fact]
    public void PropertiesEndUpInSchemaOrderWhateverOrderTheyAreSet()
    {
        using var document = WordDocument.Create();

        var run = document.AddParagraph().AddRun("Teks");

        // Deliberately set last-in-schema first.
        run.Format.FontSize = Units.Pt(12);
        run.Format.Color = OfficeColor.Red;
        run.Format.Bold = true;
        run.Format.StyleId = "Hyperlink";

        DocxValidator.AssertValid(document.ToArray());
    }

    [Fact]
    public void ParagraphFormattingRoundTrips()
    {
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph("Isi");
        paragraph.Format.Alignment = ParagraphAlignment.Justify;
        paragraph.Format.LeftIndent = Units.Cm(1.5);
        paragraph.Format.SpaceAfter = Units.Pt(12);
        paragraph.Format.LineSpacing = 1.5;
        paragraph.Format.KeepWithNext = true;

        using var reopened = WordDocument.Open(document.ToArray());
        var read = reopened.Paragraphs[0].Format;

        Assert.Equal(ParagraphAlignment.Justify, read.Alignment);
        Assert.Equal(12, read.SpaceAfter!.Value.Points, 3);
        Assert.Equal(1.5, read.LineSpacing!.Value, 3);
        Assert.True(read.KeepWithNext);

        // WordprocessingML stores lengths as whole twips, so 1.5 cm is not exactly representable:
        // it lands on 850 twips, which reads back as 1.4993 cm. One twip (1/1440 inch, about
        // 0.0018 cm) is the format's resolution and the right tolerance to assert against.
        var oneTwip = Units.Twips(1).Centimeters;
        Assert.InRange(read.LeftIndent!.Value.Centimeters, 1.5 - oneTwip, 1.5 + oneTwip);
    }

    [Fact]
    public void HangingIndentComesBackNegative()
    {
        // A hanging indent is stored as a positive w:hanging; reporting it unsigned makes a
        // hanging list look like an indented one.
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph("Isi");
        paragraph.Format.FirstLineIndent = -Units.Cm(0.75);

        using var reopened = WordDocument.Open(document.ToArray());

        Assert.Equal(-0.75, reopened.Paragraphs[0].Format.FirstLineIndent!.Value.Centimeters, 3);
    }

    [Fact]
    public void JustifyIsWrittenAsBoth()
    {
        // "justify" is not a legal w:jc value; Word drops it silently.
        using var document = WordDocument.Create();
        document.AddParagraph("Isi").Format.Alignment = ParagraphAlignment.Justify;

        var xml = document.DocumentPart.Xml.ToString();
        Assert.Contains("w:val=\"both\"", xml, StringComparison.Ordinal);
    }
}

public class TextTests
{
    [Fact]
    public void NewlinesInRunTextBecomeBreaksNotLiteralCharacters()
    {
        // WordprocessingML has no newline inside w:t; a literal one collapses to a space.
        using var document = WordDocument.Create();
        document.AddParagraph().AddRun("baris satu\nbaris dua");

        using var reopened = WordDocument.Open(document.ToArray());
        var run = reopened.Paragraphs[0].Runs[0];

        Assert.Equal("baris satu\nbaris dua", run.Text);
        Assert.Single(run.Element.Elements(Ns.W + "br"));
    }

    [Fact]
    public void SignificantWhitespaceSurvivesTheRoundTrip()
    {
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph();
        paragraph.AddRun("Halo ");
        paragraph.AddRun("dunia");

        using var reopened = WordDocument.Open(document.ToArray());

        Assert.Equal("Halo dunia", reopened.Paragraphs[0].Text);
    }

    [Fact]
    public void HyperlinkTextIsPartOfTheParagraph()
    {
        // A hyperlink wraps its runs in w:hyperlink, so a paragraph's direct w:r children are not
        // all of its runs — which is why link text vanishes from naive extraction.
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph("Kunjungi ");
        paragraph.AddHyperlink("situs kami", "https://gravicode.com");

        using var reopened = WordDocument.Open(document.ToArray());

        Assert.Equal("Kunjungi situs kami", reopened.Paragraphs[0].Text);
        Assert.Contains("situs kami", reopened.ExtractText(), StringComparison.Ordinal);
        DocxValidator.AssertValid(document.ToArray());
    }

    [Fact]
    public void ReplaceTextAcrossRunsFindsASplitPlaceholder()
    {
        // Word splits runs freely, so "{{nama}}" typed by a person is routinely three runs.
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph();
        paragraph.AddRun("Halo {{");
        paragraph.AddRun("nama");
        paragraph.AddRun("}}, selamat datang.");

        Assert.Equal(0, document.ReplaceText("{{nama}}", "Kang Fadhil"));
        Assert.Equal(1, document.ReplaceTextAcrossRuns("{{nama}}", "Kang Fadhil"));

        Assert.Equal("Halo Kang Fadhil, selamat datang.", document.Paragraphs[0].Text);
    }

    [Fact]
    public void MailMergeFillsEveryPlaceholder()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Kepada {{nama}} di {{kota}}.");

        var replaced = document.MailMerge(new Dictionary<string, string>
        {
            ["nama"] = "Kang Fadhil",
            ["kota"] = "Bandung",
        });

        Assert.Equal(2, replaced);
        Assert.Equal("Kepada Kang Fadhil di Bandung.", document.Paragraphs[0].Text);
    }
}

public class TableTests
{
    [Fact]
    public void TableRoundTripsWithItsData()
    {
        using var document = WordDocument.Create();

        document.AddTable(new[]
        {
            new[] { "Komponen", "Status" },
            new[] { "WordNet", "Selesai" },
            new[] { "ExcelNet", "Selesai" },
        });

        DocxValidator.AssertValid(document.ToArray());

        using var reopened = WordDocument.Open(document.ToArray());
        var table = Assert.Single(reopened.Tables);

        Assert.Equal(3, table.Count);
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal("Komponen", table[0, 0].Text);
        Assert.Equal("Selesai", table[2, 1].Text);
    }

    [Fact]
    public void HorizontalMergeWidensTheFirstCellAndRemovesTheRest()
    {
        using var document = WordDocument.Create();

        var table = document.AddTable(2, 3);
        table.MergeCells(0, 0, 0, 2);

        Assert.Equal(3, table[0].Cells[0].GridSpan);
        Assert.Single(table[0].Cells);
        Assert.Equal(3, table[1].Cells.Count);

        DocxValidator.AssertValid(document.ToArray());
    }

    [Fact]
    public void VerticalMergeKeepsEveryCellAndMarksThem()
    {
        // Deleting the continuation cells makes the rows shorter than the grid, and Word repairs
        // the table by dropping the merge.
        using var document = WordDocument.Create();

        var table = document.AddTable(3, 2);
        table.MergeCells(0, 0, 2, 0);

        Assert.All(table.Rows, row => Assert.Equal(2, row.Cells.Count));

        var merges = table.Element.Descendants(Ns.W + "vMerge").ToList();
        Assert.Equal(3, merges.Count);
        Assert.Equal("restart", merges[0].Attribute(Ns.W + "val")?.Value);
        Assert.Null(merges[1].Attribute(Ns.W + "val"));

        DocxValidator.AssertValid(document.ToArray());
    }

    [Fact]
    public void ADocumentEndingInATableStillEndsWithSectPr()
    {
        using var document = WordDocument.Create();
        document.AddTable(2, 2);

        DocxValidator.AssertValid(document.ToArray());
    }

    [Fact]
    public void ColumnWidthNeedsAFixedLayoutToBeHonoured()
    {
        // With the default autofit layout Word recomputes every column and the widths are ignored.
        using var document = WordDocument.Create();

        var table = document.AddTable(2, 2);
        table.SetColumnWidth(0, Units.Cm(3));

        var layout = table.Element.Element(Ns.W + "tblPr")?.Element(Ns.W + "tblLayout");
        Assert.Equal("fixed", layout?.Attribute(Ns.W + "type")?.Value);
    }
}

public class SectionTests
{
    [Fact]
    public void PageSizeAndMarginsRoundTrip()
    {
        using var document = WordDocument.Create();

        document.Section.SetPageSize("A4");
        document.Section.SetMargins(Units.Cm(2.5), Units.Cm(2), Units.Cm(2.5), Units.Cm(3));

        using var reopened = WordDocument.Open(document.ToArray());
        var section = reopened.Section;

        Assert.Equal(210, section.PageWidth.Millimeters, 0);
        Assert.Equal(297, section.PageHeight.Millimeters, 0);
        Assert.Equal(2.5, section.TopMargin.Centimeters, 2);
        Assert.Equal(3, section.LeftMargin.Centimeters, 2);
    }

    [Fact]
    public void LandscapeSwapsTheDimensionsAsWellAsTheAttribute()
    {
        // w:orient alone only tells the printer which way to feed paper; without swapping the
        // page size the document still lays out portrait.
        using var document = WordDocument.Create();

        document.Section.SetPageSize("A4");
        document.Section.Orientation = PageOrientation.Landscape;

        Assert.Equal(297, document.Section.PageWidth.Millimeters, 0);
        Assert.Equal(210, document.Section.PageHeight.Millimeters, 0);
    }

    [Fact]
    public void HeaderAndFooterRoundTripWithoutAStrayBlankLine()
    {
        using var document = WordDocument.Create();

        document.Section.GetHeader().AddParagraph("Gravicode Studios");

        var footer = document.Section.GetFooter().AddParagraph();
        footer.AddRun("Halaman ");
        footer.AddPageNumber();

        var bytes = document.ToArray();
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);

        Assert.Equal("Gravicode Studios", reopened.Section.GetHeader().Text);
        Assert.StartsWith("Halaman", reopened.Section.GetFooter().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFirstPageHeaderTurnsOnTitlePg()
    {
        // Without w:titlePg the first-page header exists in the package and never appears.
        using var document = WordDocument.Create();
        document.Section.GetHeader(HeaderFooterKind.First).AddParagraph("Sampul");

        Assert.True(document.Section.DifferentFirstPage);
    }

    [Fact]
    public void AddingASectionMovesThePropertiesRatherThanAppendingThem()
    {
        using var document = WordDocument.Create();

        document.AddParagraph("Bagian satu");
        var second = document.AddSection();
        second.Orientation = PageOrientation.Landscape;
        document.AddParagraph("Bagian dua");

        var bytes = document.ToArray();
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);

        Assert.Equal(2, reopened.Sections.Count);
        Assert.Equal(PageOrientation.Landscape, reopened.Section.Orientation);
    }
}

public class ListAndMediaTests
{
    [Fact]
    public void BulletListItemsCarryNumbering()
    {
        using var document = WordDocument.Create();
        var items = document.AddList(["Satu", "Dua", "Tiga"]);

        Assert.All(items, p => Assert.Equal(0, p.ListLevel));

        var bytes = document.ToArray();
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);
        Assert.All(reopened.Paragraphs, p => Assert.NotNull(p.ListLevel));
    }

    [Fact]
    public void NumberedAndBulletListsGetIndependentNumbering()
    {
        // Two w:num entries sharing one abstract definition is what makes a second list restart.
        using var document = WordDocument.Create();

        var first = document.Numbering.AddNumberedList();
        var second = document.Numbering.Restart(first);

        Assert.NotEqual(first, second);
        Assert.Contains(first, document.Numbering.NumberingIds);
        Assert.Contains(second, document.Numbering.NumberingIds);
    }

    [Fact]
    public void PictureIsEmbeddedAtItsNaturalSize()
    {
        var png = TestImages.SolidPng(96, 48, 0x1F, 0x38, 0x64);

        using var document = WordDocument.Create();
        document.AddPicture(png);

        var bytes = document.ToArray();
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);
        var run = reopened.Paragraphs[0].Runs[0];

        Assert.True(run.HasDrawing);

        var extent = run.Element.Descendants(Ns.Wp + "extent").Single();
        Assert.Equal(Units.Px(96).Emu, long.Parse(extent.Attribute("cx")!.Value));
        Assert.Equal(Units.Px(48).Emu, long.Parse(extent.Attribute("cy")!.Value));
    }

    [Fact]
    public void TheSameImageTwiceIsStoredOnce()
    {
        var png = TestImages.SolidPng(32, 32, 10, 20, 30);

        using var document = WordDocument.Create();
        document.AddPicture(png);
        document.AddPicture(png);

        using var reopened = WordDocument.Open(document.ToArray());

        var media = reopened.Package.Parts
            .Count(p => p.Name.Value.StartsWith("/word/media/", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, media);
    }

    [Fact]
    public void DrawingIdsStayUniqueAcrossAReopen()
    {
        // Restarting ids at 1 collides with what the original producer wrote, and Word reports a
        // duplicate drawing id as unreadable content.
        var png = TestImages.SolidPng(16, 16, 1, 2, 3);

        byte[] bytes;

        using (var document = WordDocument.Create())
        {
            document.AddPicture(png);
            bytes = document.ToArray();
        }

        using var reopened = WordDocument.Open(bytes);
        reopened.AddPicture(TestImages.SolidPng(16, 16, 4, 5, 6));

        var ids = reopened.DocumentPart.Xml.Descendants(Ns.Wp + "docPr")
            .Select(e => e.Attribute("id")!.Value)
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}

public class PdfExportTests
{
    [Fact]
    public void ExportedPdfContainsTheDocumentsText()
    {
        using var document = WordDocument.Create();
        document.AddHeading("Judul Laporan", 1);
        document.AddParagraph("Paragraf pertama yang cukup panjang untuk membungkus baris.");

        using var pdf = document.ToPdf();

        Assert.Single(pdf.Pages);

        var text = pdf.Pages[0].ExtractText();
        Assert.Contains("Judul Laporan", text, StringComparison.Ordinal);
        Assert.Contains("Paragraf pertama", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APageBreakProducesASecondPage()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Halaman satu");
        document.AddPageBreak();
        document.AddParagraph("Halaman dua");

        using var pdf = document.ToPdf();

        Assert.Equal(2, pdf.Pages.Count);
        Assert.Contains("Halaman dua", pdf.Pages[1].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void PageNumberFieldsAreEvaluatedPerPage()
    {
        // The document's own cached field result says "1" on every page; the exporter must
        // substitute the real number or a fifty-page PDF reads "Page 1 of 1" throughout.
        using var document = WordDocument.Create();

        var footer = document.Section.GetFooter().AddParagraph();
        footer.AddRun("Halaman ");
        footer.AddPageNumber();
        footer.AddRun(" dari ");
        footer.AddPageCount();

        document.AddParagraph("Satu");
        document.AddPageBreak();
        document.AddParagraph("Dua");

        using var pdf = document.ToPdf();

        Assert.Equal(2, pdf.Pages.Count);
        Assert.Contains("Halaman 1 dari 2", pdf.Pages[0].ExtractText(), StringComparison.Ordinal);
        Assert.Contains("Halaman 2 dari 2", pdf.Pages[1].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void HeadingsAreRenderedLargerThanBodyText()
    {
        // A converter that reads only a run's own w:rPr renders a heading as body text, because a
        // heading paragraph carries no direct formatting at all.
        using var document = WordDocument.Create();
        document.AddHeading("Judul", 1);
        document.AddParagraph("Isi");

        using var pdf = document.ToPdf();

        var fragments = pdf.Pages[0].ExtractTextFragments();

        var heading = fragments.First(f => f.Text.Contains("Judul", StringComparison.Ordinal));
        var body = fragments.First(f => f.Text.Contains("Isi", StringComparison.Ordinal));

        Assert.True(heading.FontSize > body.FontSize,
            $"heading {heading.FontSize}pt should exceed body {body.FontSize}pt");
    }

    [Fact]
    public void TablesSurviveTheExport()
    {
        using var document = WordDocument.Create();

        document.AddTable(new[]
        {
            new[] { "Kolom A", "Kolom B" },
            new[] { "Nilai 1", "Nilai 2" },
        });

        using var pdf = document.ToPdf();
        var text = pdf.Pages[0].ExtractText();

        Assert.Contains("Kolom A", text, StringComparison.Ordinal);
        Assert.Contains("Nilai 2", text, StringComparison.Ordinal);
    }
}

/// <summary>
/// Guards the cost model of building a document, not its speed.
/// </summary>
/// <remarks>
/// <para>
/// Wall-clock assertions are flaky across machines, so these compare the time for N against the
/// time for 4N. Linear work gives a ratio near 4; the quadratic insert this replaced gave 16 and
/// climbing. The threshold sits far enough above 4 that noise cannot trip it and far enough below
/// 16 that a return to O(N²) cannot hide.
/// </para>
/// <para>
/// Each size is measured several times and the <em>fastest</em> run is used. Scheduling noise only
/// ever adds time, so the minimum is the closest estimate of the real cost — and taking the mean
/// made this test fail intermittently when the whole solution's tests ran in parallel.
/// </para>
/// </remarks>
public class ScalingTests
{
    private const int Small = 4_000;
    private const int Large = 64_000;

    private const int Attempts = 3;

    /// <summary>The fastest of several runs, which is the least noisy estimate available.</summary>
    private static double Fastest(Func<double> measure)
    {
        var best = double.MaxValue;

        for (var i = 0; i < Attempts; i++)
        {
            best = Math.Min(best, measure());
        }

        return best;
    }

    private static double MillisecondsToBuild(int paragraphs)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var document = WordDocument.Create();

        for (var i = 0; i < paragraphs; i++)
        {
            document.AddParagraph("Paragraf dengan sedikit teks isian.");
        }

        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    [Fact]
    public void AppendingParagraphsScalesLinearly()
    {
        // A warm-up run: the first call pays for JIT and for the static XName tables, which on a
        // cold process is larger than the measurement itself.
        _ = MillisecondsToBuild(Small);

        var small = Fastest(() => MillisecondsToBuild(Small));
        var large = Fastest(() => MillisecondsToBuild(Large));

        // A floor on the small measurement, or a fast machine divides by something near zero and
        // produces a meaningless ratio.
        var ratio = large / Math.Max(small, 1.0);

        // Sixteen times the work. Linear costs about 16; the quadratic insert this guards against
        // cost about 256. The threshold sits far from both because the measurement is a stopwatch
        // on a machine that may be doing something else: this test failed once on a shared CI
        // runner at 16x work, where a 9 ms baseline and one GC pause were enough to read as
        // quadratic. Widening the gap between the sizes, rather than widening the tolerance alone,
        // is what makes the signal survive that — a real regression is an order of magnitude away
        // from this line, and a noisy runner is not.
        Assert.True(ratio < 60,
            $"Building {Large} paragraphs took {large:0.0} ms against {small:0.0} ms for {Small} " +
            $"— a ratio of {ratio:0.0} for 16x the work. Linear is ~16 and quadratic is ~256; " +
            "this looks quadratic again. See WordDocument.InsertBlock.");
    }

    [Fact]
    public void FillingATableDoesNotAllocatePerRowWrappers()
    {
        // Allocation rather than time, because allocation is deterministic: the regression this
        // guards was `table[r, c]` materialising a wrapper for every row and every cell on each
        // access, which allocated 55 MB filling a 500-row table. Bytes do not vary with machine
        // load, so this cannot go flaky the way a stopwatch does.
        //
        // The walk itself is still O(row) — see the note on Table's indexer — so the *time* is
        // superlinear by design and only the allocation is asserted here.
        static long BytesToFill(int rows)
        {
            Fill(rows);

            var before = GC.GetAllocatedBytesForCurrentThread();
            Fill(rows);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        static void Fill(int rows)
        {
            using var document = WordDocument.Create();
            var table = document.AddTable(rows, 4);

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < 4; c++)
                {
                    table[r, c].Text = "x";
                }
            }
        }

        var small = BytesToFill(250);
        var large = BytesToFill(1_000);

        var ratio = (double)large / Math.Max(small, 1);

        Assert.True(ratio < 8,
            $"Filling a 1000-row table allocated {large / 1048576.0:0.0} MB against " +
            $"{small / 1048576.0:0.0} MB for 250 rows — a ratio of {ratio:0.0} for 4x the work. " +
            "Linear is ~4; the wrapper-per-access version gave far more. See Table's indexer.");
    }
}
