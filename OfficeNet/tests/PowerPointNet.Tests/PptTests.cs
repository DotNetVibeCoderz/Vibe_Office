// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using PowerPointNet;
using PowerPointNet.Shapes;
using Xunit;

namespace PowerPointNet.Tests;

/// <summary>Structural rules a .pptx must satisfy, checked without going through PowerPointNet.</summary>
internal static class PptxValidator
{
    internal static void AssertValid(byte[] pptx)
    {
        var report = OpcValidator.Validate(pptx);
        var problems = new List<string>(report.Problems);

        using var stream = new MemoryStream(pptx, writable: false);
        using var archive = new System.IO.Compression.ZipArchive(stream);

        var presentation = Load(archive, "ppt/presentation.xml");

        if (presentation is null)
        {
            problems.Add("ppt/presentation.xml is missing.");
            Assert.Fail(string.Join("\n  - ", problems));
            return;
        }

        var relationships = LoadRelationshipIds(archive, "ppt/presentation.xml");

        var masters = presentation.Root?.Element(Ns.P + "sldMasterIdLst");

        if (masters is null || !masters.Elements().Any())
        {
            problems.Add("The presentation registers no slide master; PowerPoint refuses such a file.");
        }

        // Slide ids must be at least 256 and unique. PowerPoint rejects anything lower.
        var ids = new HashSet<int>();

        foreach (var slide in presentation.Root?.Element(Ns.P + "sldIdLst")?.Elements() ?? [])
        {
            var id = int.Parse(slide.Attribute("id")!.Value);

            if (id < 256)
            {
                problems.Add($"Slide id {id} is below the 256 minimum.");
            }

            if (!ids.Add(id))
            {
                problems.Add($"Slide id {id} is used more than once.");
            }

            var relationshipId = slide.Attribute(Ns.R + "id")?.Value;

            if (relationshipId is null || !relationships.Contains(relationshipId))
            {
                problems.Add($"sldId references relationship '{relationshipId}', which does not exist.");
            }
        }

        if (presentation.Root?.Element(Ns.P + "sldSz") is null)
        {
            problems.Add("The presentation has no p:sldSz, so the slide size is undefined.");
        }

        foreach (var entry in archive.Entries.Where(e =>
                     e.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) &&
                     e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            var slide = Load(archive, entry.FullName)!;
            var tree = slide.Root?.Element(Ns.P + "cSld")?.Element(Ns.P + "spTree");

            if (tree is null)
            {
                problems.Add($"{entry.FullName} has no p:spTree.");
                continue;
            }

            // Every shape tree must open with these two; PowerPoint rejects a slide missing either.
            var first = tree.Elements().Take(2).Select(e => e.Name.LocalName).ToList();

            if (first is not ["nvGrpSpPr", "grpSpPr"])
            {
                problems.Add(
                    $"{entry.FullName}: p:spTree must begin with nvGrpSpPr then grpSpPr, got " +
                    $"[{string.Join(", ", first)}].");
            }

            var shapeIds = tree.Descendants(Ns.P + "cNvPr")
                .Select(e => e.Attribute("id")!.Value)
                .ToList();

            if (shapeIds.Count != shapeIds.Distinct().Count())
            {
                problems.Add($"{entry.FullName}: duplicate shape ids.");
            }

            if (!LoadRelationshipTypes(archive, entry.FullName).Contains("slideLayout"))
            {
                problems.Add($"{entry.FullName}: no slideLayout relationship.");
            }
        }

        foreach (var entry in archive.Entries.Where(e =>
                     e.FullName.StartsWith("ppt/slideLayouts/slideLayout", StringComparison.Ordinal) &&
                     e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            if (!LoadRelationshipTypes(archive, entry.FullName).Contains("slideMaster"))
            {
                problems.Add($"{entry.FullName}: no slideMaster relationship.");
            }
        }

        var masterTypes = LoadRelationshipTypes(archive, "ppt/slideMasters/slideMaster1.xml");

        if (!masterTypes.Contains("theme"))
        {
            problems.Add("The slide master has no theme relationship.");
        }

        if (!masterTypes.Contains("slideLayout"))
        {
            problems.Add("The slide master registers no layouts.");
        }

        var theme = Load(archive, "ppt/theme/theme1.xml");
        var scheme = theme?.Root?.Element(Ns.A + "themeElements")?.Element(Ns.A + "clrScheme");

        string[] required =
        [
            "dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5",
            "accent6", "hlink", "folHlink",
        ];

        var present = scheme?.Elements().Select(e => e.Name.LocalName).ToHashSet() ?? [];

        foreach (var slot in required)
        {
            if (!present.Contains(slot))
            {
                problems.Add($"The theme has no '{slot}' colour slot; the master's clrMap names it.");
            }
        }

        // Each format list must hold exactly three entries — a shape's idx is a one-based index
        // into them, and a short list makes shapes using index 3 vanish.
        var format = theme?.Root?.Element(Ns.A + "themeElements")?.Element(Ns.A + "fmtScheme");

        foreach (var list in (string[])["fillStyleLst", "lnStyleLst", "effectStyleLst", "bgFillStyleLst"])
        {
            var count = format?.Element(Ns.A + list)?.Elements().Count() ?? 0;

            if (count != 3)
            {
                problems.Add($"The theme's {list} holds {count} entries; it must hold exactly 3.");
            }
        }

        Assert.True(problems.Count == 0,
            "The .pptx is structurally invalid:\n  - " + string.Join("\n  - ", problems));
    }

    private static XDocument? Load(System.IO.Compression.ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);

        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static HashSet<string> LoadRelationshipIds(System.IO.Compression.ZipArchive archive,
        string part)
    {
        var document = LoadRelationships(archive, part);

        return document?.Root?.Elements()
            .Select(e => e.Attribute("Id")!.Value)
            .ToHashSet(StringComparer.Ordinal) ?? [];
    }

    private static HashSet<string> LoadRelationshipTypes(System.IO.Compression.ZipArchive archive,
        string part)
    {
        var document = LoadRelationships(archive, part);

        return document?.Root?.Elements()
            .Select(e => e.Attribute("Type")!.Value.Split('/')[^1])
            .ToHashSet(StringComparer.Ordinal) ?? [];
    }

    private static XDocument? LoadRelationships(System.IO.Compression.ZipArchive archive, string part)
    {
        var slash = part.LastIndexOf('/');
        var folder = slash < 0 ? string.Empty : part[..slash];
        var file = part[(slash + 1)..];

        return Load(archive, $"{folder}/_rels/{file}.rels");
    }
}

public class PresentationTests
{
    [Fact]
    public void ANewPresentationHasEverythingASlideNeedsToInheritFrom()
    {
        using var presentation = Presentation.Create();

        Assert.NotNull(presentation.Master);
        Assert.Equal(6, presentation.Layouts.Count);
        Assert.Equal(13.333, presentation.SlideWidth.Inches, 2);
        Assert.Equal(7.5, presentation.SlideHeight.Inches, 2);

        presentation.AddSlide();
        PptxValidator.AssertValid(presentation.ToArray());
    }

    [Fact]
    public void CreateSaveReopenPreservesSlidesAndMetadata()
    {
        using var file = new TempFile(".pptx");

        using (var presentation = Presentation.Create())
        {
            presentation.Properties.Title = "Deck";
            presentation.Properties.Creator = "Kang Fadhil";

            presentation.AddTitleSlide("OfficeNet", "Empat library dalam satu");
            presentation.AddBulletSlide("Komponen", ["WordNet", "ExcelNet", "PdfNet"]);

            presentation.Save(file.Path);
        }

        PptxValidator.AssertValid(file.ReadAllBytes());

        using var reopened = Presentation.Open(file.Path);

        Assert.Equal("Deck", reopened.Properties.Title);
        Assert.Equal(2, reopened.SlideCount);
        Assert.Equal("OfficeNet", reopened[0].Title!.Text);
        Assert.Equal(3, reopened[1].Body!.TextFrame!.Paragraphs.Count);
    }

    [Fact]
    public void RejectsAPackageThatIsNotPresentationMl()
    {
        var package = OfficeNet.Core.Packaging.OpcPackage.Create();

        var main = package.AddXmlPart("/word/document.xml",
            OfficeNet.Core.Packaging.ContentTypes.WordDocument,
            XmlUtil.NewDocument(new XElement(Ns.W + "document")));

        package.AddRootRelationship(main, OfficeNet.Core.Packaging.RelationshipTypes.OfficeDocument);

        var ex = Assert.Throws<OfficeNetException>(() => Presentation.Open(package.ToArray()));
        Assert.Contains("PresentationML", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlideOrderComesFromTheIdListNotThePartNames()
    {
        // A deck reordered in PowerPoint keeps slide7.xml in third position.
        using var presentation = Presentation.Create();

        presentation.AddBulletSlide("Satu", ["a"]);
        presentation.AddBulletSlide("Dua", ["b"]);
        presentation.AddBulletSlide("Tiga", ["c"]);

        presentation.MoveSlide(2, 0);

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);

        Assert.Equal(["Tiga", "Satu", "Dua"], reopened.Slides.Select(s => s.Title!.Text));
    }

    [Fact]
    public void RemovingASlideLeavesNoDanglingRelationship()
    {
        using var presentation = Presentation.Create();

        presentation.AddBulletSlide("Satu", ["a"]);
        presentation.AddBulletSlide("Dua", ["b"]);
        presentation.RemoveSlide(0);

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);

        Assert.Equal(1, reopened.SlideCount);
        Assert.Equal("Dua", reopened[0].Title!.Text);
    }

    [Fact]
    public void DuplicatingASlideCopiesItsShapesAndItsImages()
    {
        var png = TestImages.SolidPng(48, 48, 0x2E, 0x54, 0x96);

        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(2);
        slide.SetTitle("Asli");
        slide.AddPicture(png, Units.Inches(1), Units.Inches(2));

        var copy = presentation.DuplicateSlide(0);

        Assert.Equal(2, presentation.SlideCount);
        Assert.Equal("Asli", copy.Title!.Text);
        Assert.NotNull(copy.Pictures.First().GetImageBytes());

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);

        // The image is stored once and referenced from both slides.
        var media = reopened.Package.Parts
            .Count(p => p.Name.Value.StartsWith("/ppt/media/", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, media);
        Assert.NotNull(reopened[1].Pictures.First().GetImageBytes());
    }
}

public class SlideContentTests
{
    [Fact]
    public void PlaceholdersAreCreatedFromTheLayoutOnDemand()
    {
        // A new slide inherits its placeholders' appearance but holds no shapes; text can only be
        // set on a shape that exists.
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(1);
        Assert.Empty(slide.Shapes);

        slide.SetTitle("Judul");
        slide.SetBody(["Satu", "Dua"]);

        Assert.Equal(2, slide.Shapes.Count);
        Assert.Equal("Judul", slide.Title!.Text);
    }

    [Fact]
    public void LayoutPromptTextIsNotCopiedOntoTheSlide()
    {
        // Copying the layout's paragraphs shows "Click to edit Master title style" as real text.
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(1);
        slide.SetTitle("Judul Sebenarnya");

        Assert.DoesNotContain("Click to edit", slide.ExtractText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Judul Sebenarnya", slide.Title!.Text);
    }

    [Fact]
    public void ASubtitleGetsNoBullets()
    {
        // A subtitle is prose. Bulleting it is what makes a generated title slide look wrong.
        using var presentation = Presentation.Create();

        var slide = presentation.AddTitleSlide("Judul", "Sebuah subjudul");
        var subtitle = slide.Shapes.First(s => s.PlaceholderType == "subTitle");

        Assert.False(subtitle.TextFrame!.Paragraphs[0].HasBullet);
    }

    [Fact]
    public void TextRunFormattingRoundTrips()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        var box = slide.AddTextBox("Terima kasih", Units.Inches(1), Units.Inches(1),
            Units.Inches(6), Units.Inches(1.5));

        var run = box.TextFrame!.Paragraphs[0].Runs[0];
        run.WithBold().WithItalic().WithSize(40).WithColor(OfficeColor.FromRgb(0xC5, 0x5A, 0x11))
            .WithFont("Georgia");

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var read = reopened[0].Shapes[0].TextFrame!.Paragraphs[0].Runs[0];

        Assert.True(read.Bold);
        Assert.True(read.Italic);
        Assert.Equal(40, read.FontSize!.Value.Points, 3);
        Assert.Equal(OfficeColor.FromRgb(0xC5, 0x5A, 0x11), read.Color);
        Assert.Equal("Georgia", read.FontName);
    }

    [Fact]
    public void FontSizeIsHundredthsOfAPointNotHalfPoints()
    {
        // DrawingML's a:rPr/@sz differs from WordprocessingML's w:sz, which is half-points.
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        var box = slide.AddTextBox("x", Units.Inches(1), Units.Inches(1),
            Units.Inches(2), Units.Inches(1));

        box.TextFrame!.Paragraphs[0].Runs[0].FontSize = Units.Pt(18);

        var size = box.Element.Descendants(Ns.A + "rPr").First().Attribute("sz")!.Value;
        Assert.Equal("1800", size);
    }

    [Fact]
    public void ShapePositionAndSizeRoundTrip()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        var shape = slide.AddShape(ShapeGeometry.RoundedRectangle,
            Units.Inches(2), Units.Inches(3), Units.Inches(4), Units.Inches(1),
            "Tombol", OfficeColor.FromRgb(0x54, 0x82, 0x35));

        using var reopened = Presentation.Open(presentation.ToArray());
        var read = reopened[0].Shapes[0];

        Assert.Equal(2, read.Left.Inches, 3);
        Assert.Equal(3, read.Top.Inches, 3);
        Assert.Equal(4, read.Width.Inches, 3);
        Assert.Equal(OfficeColor.FromRgb(0x54, 0x82, 0x35), read.FillColor);
        Assert.Equal("Tombol", read.Text);
    }

    [Fact]
    public void TablesAreRecognisedAgainWhenTheFileIsReopened()
    {
        // A table is a p:graphicFrame, the same element a chart uses; only the a:tbl inside makes
        // it a table.
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(2);

        var table = slide.AddTable(3, 2, Units.Inches(1), Units.Inches(2),
            Units.Inches(8), Units.Inches(2));

        table.SetData(new[]
        {
            new[] { "Komponen", "Status" },
            new[] { "WordNet", "Selesai" },
            new[] { "PdfNet", "Selesai" },
        });

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var read = Assert.Single(reopened[0].Tables);

        Assert.Equal(3, read.Count);
        Assert.Equal(2, read.ColumnCount);
        Assert.Equal("Komponen", read[0, 0].Text);
        Assert.Equal("Selesai", read[2, 1].Text);
    }

    [Fact]
    public void TableColumnWidthsStillSumToTheFrameWidth()
    {
        // If the grid stops summing to the frame width PowerPoint rescales every column and the
        // caller's sizing is lost.
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(2);
        var width = Units.Inches(8);

        var table = slide.AddTable(2, 3, Units.Inches(1), Units.Inches(2), width, Units.Inches(2));
        table.SetColumnWidth(0, Units.Inches(4));

        var total = table.Element.Descendants(Ns.A + "gridCol")
            .Sum(c => long.Parse(c.Attribute("w")!.Value));

        Assert.Equal(width.Emu, total);
    }

    [Fact]
    public void PicturesUseTheirNaturalSizeWhenNoneIsGiven()
    {
        var png = TestImages.SolidPng(192, 96, 1, 2, 3);

        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        var picture = slide.AddPicture(png, Units.Inches(1), Units.Inches(1));

        Assert.Equal(Units.Px(192).Emu, picture.Width.Emu);
        Assert.Equal(Units.Px(96).Emu, picture.Height.Emu);
    }

    [Fact]
    public void GivingOneDimensionKeepsTheAspectRatio()
    {
        var png = TestImages.SolidPng(200, 100, 1, 2, 3);

        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(3);
        var picture = slide.AddPicture(png, Units.Inches(1), Units.Inches(1), Units.Inches(4));

        Assert.Equal(4, picture.Width.Inches, 3);
        Assert.Equal(2, picture.Height.Inches, 3);
    }

    [Fact]
    public void NotesRoundTrip()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddBulletSlide("Judul", ["poin"]);
        slide.Notes = "Ingatkan tentang tenggat.\nSebutkan Gravicode Studios.";

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);

        Assert.Equal("Ingatkan tentang tenggat.\nSebutkan Gravicode Studios.", reopened[0].Notes);
    }

    [Fact]
    public void TransitionsAndAnimationsProduceValidMarkup()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddBulletSlide("Judul", ["satu", "dua"]);
        slide.SetTransition(SlideTransition.Fade, TimeSpan.FromMilliseconds(700));
        slide.AnimateOnClick(AnimationEffect.Fade, slide.Body!);

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);
        var root = reopened[0].Root;

        Assert.NotNull(root.Element(Ns.P + "transition"));
        Assert.NotNull(root.Element(Ns.P + "timing"));
    }

    [Fact]
    public void HiddenSlidesAreMarkedAndSkippedInExport()
    {
        using var presentation = Presentation.Create();

        presentation.AddBulletSlide("Terlihat", ["a"]);
        presentation.AddBulletSlide("Tersembunyi", ["b"]).IsHidden = true;

        using var reopened = Presentation.Open(presentation.ToArray());

        Assert.True(reopened[1].IsHidden);

        using var pdf = reopened.ToPdf();
        Assert.Single(pdf.Pages);
    }
}

public class ThemeTests
{
    [Fact]
    public void ThemeColoursRoundTrip()
    {
        using var presentation = Presentation.Create();

        presentation.Master!.SetThemeColor("accent1", OfficeColor.FromRgb(0x1F, 0x38, 0x64));

        var bytes = presentation.ToArray();
        PptxValidator.AssertValid(bytes);

        using var reopened = Presentation.Open(bytes);

        Assert.Equal(OfficeColor.FromRgb(0x1F, 0x38, 0x64),
            reopened.Master!.ThemeColors["accent1"]);
    }

    [Fact]
    public void SystemColourSlotsAreProtected()
    {
        // dk1 and lt1 are windowText and window; replacing them loses the high-contrast behaviour.
        using var presentation = Presentation.Create();

        Assert.Throws<ArgumentException>(() =>
            presentation.Master!.SetThemeColor("dk1", OfficeColor.Red));
    }

    [Fact]
    public void SystemColoursStillReportAUsableRgb()
    {
        // A sysClr records its resolved value in lastClr, which is the only way to get RGB out.
        using var presentation = Presentation.Create();

        Assert.Equal(OfficeColor.Black, presentation.Master!.ThemeColors["dk1"]);
        Assert.Equal(OfficeColor.White, presentation.Master.ThemeColors["lt1"]);
    }

    [Fact]
    public void ThemeFontsRoundTrip()
    {
        using var presentation = Presentation.Create();
        presentation.Master!.SetThemeFonts("Georgia", "Verdana");

        using var reopened = Presentation.Open(presentation.ToArray());

        var scheme = reopened.Master!.ThemePart!.Xml.Root!
            .Element(Ns.A + "themeElements")!.Element(Ns.A + "fontScheme")!;

        Assert.Equal("Georgia",
            scheme.Element(Ns.A + "majorFont")!.Element(Ns.A + "latin")!.Attribute("typeface")!.Value);

        Assert.Equal("Verdana",
            scheme.Element(Ns.A + "minorFont")!.Element(Ns.A + "latin")!.Attribute("typeface")!.Value);
    }
}

public class PdfExportTests
{
    [Fact]
    public void OnePageIsWrittenPerSlideAtTheSlideSize()
    {
        using var presentation = Presentation.Create();

        presentation.AddTitleSlide("Judul", "Subjudul");
        presentation.AddBulletSlide("Isi", ["satu", "dua"]);

        using var pdf = presentation.ToPdf();

        Assert.Equal(2, pdf.Pages.Count);
        Assert.Equal(presentation.SlideWidth.Points, pdf.Pages[0].Width, 1);
        Assert.Equal(presentation.SlideHeight.Points, pdf.Pages[0].Height, 1);
    }

    [Fact]
    public void SlideTextSurvivesTheExport()
    {
        using var presentation = Presentation.Create();
        presentation.AddBulletSlide("Komponen", ["WordNet", "ExcelNet"]);

        using var pdf = presentation.ToPdf();
        var text = pdf.Pages[0].ExtractText();

        Assert.Contains("Komponen", text, StringComparison.Ordinal);
        Assert.Contains("WordNet", text, StringComparison.Ordinal);
        Assert.Contains("ExcelNet", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceholdersLandWhereTheLayoutPutsThemNotAtTheOrigin()
    {
        // A placeholder on a slide usually carries no transform of its own; treating that as
        // (0,0) stacks everything in the top-left corner.
        using var presentation = Presentation.Create();
        presentation.AddBulletSlide("Judul", ["isi"]);

        using var pdf = presentation.ToPdf();

        var fragments = pdf.Pages[0].ExtractTextFragments();
        var title = fragments.First(f => f.Text.Contains("Judul", StringComparison.Ordinal));

        Assert.True(title.X > 20, $"the title should be inset from the left edge, was at {title.X}");
        Assert.True(title.Y < pdf.Pages[0].Height - 20,
            "the title should be inset from the top edge");
    }

    [Fact]
    public void TablesSurviveTheExport()
    {
        using var presentation = Presentation.Create();

        var slide = presentation.AddSlide(2);
        slide.SetTitle("Tabel");

        slide.AddTable(2, 2, Units.Inches(1), Units.Inches(2), Units.Inches(8), Units.Inches(2))
            .SetData(new[]
            {
                new[] { "Kolom A", "Kolom B" },
                new[] { "Nilai 1", "Nilai 2" },
            });

        using var pdf = presentation.ToPdf();
        var text = pdf.Pages[0].ExtractText();

        Assert.Contains("Kolom A", text, StringComparison.Ordinal);
        Assert.Contains("Nilai 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SlideNumbersCanBeAdded()
    {
        using var presentation = Presentation.Create();

        presentation.AddBulletSlide("Satu", ["a"]);
        presentation.AddBulletSlide("Dua", ["b"]);

        using var pdf = presentation.ToPdf(new PowerPointNet.Export.SlidePdfOptions
        {
            ShowSlideNumbers = true,
        });

        Assert.Contains("2", pdf.Pages[1].ExtractText(), StringComparison.Ordinal);
    }
}
