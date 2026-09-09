// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.Rendering;
using OfficeNet.TestKit;
using PowerPointNet;
using Xunit;

namespace OfficeNet.Rendering.Tests;

public class SlideImageTests
{
    private static Presentation Deck(int slides = 3)
    {
        var deck = Presentation.Create();

        for (var i = 1; i <= slides; i++)
        {
            deck.AddSlide(2).SetTitle($"Slide {i}");
        }

        return deck;
    }

    [Fact]
    public void EverySlideBecomesAnImage()
    {
        using var deck = Deck(4);

        var images = DocumentRenderer.RenderPowerPoint(deck, new RenderOptions { Dpi = 72 });

        Assert.Equal(4, images.Count);
        Assert.All(images, image => Assert.True(image.Length > 0));
    }

    [Fact]
    public void OneSlideCanBeRenderedOnItsOwn()
    {
        // Rendering a whole deck to get one thumbnail is most of a second per slide on a long deck.
        using var deck = Deck(5);

        var options = new RenderOptions { Dpi = 72 };
        var single = DocumentRenderer.RenderSlide(deck, 2, options);
        var all = DocumentRenderer.RenderPowerPoint(deck, options);

        // Byte for byte the same page, so the shortcut is a shortcut and not a different renderer.
        Assert.Equal(all[2], single);
    }

    [Fact]
    public void AskingForASlideThatIsNotThereSaysSo()
    {
        using var deck = Deck(2);

        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentRenderer.RenderSlide(deck, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentRenderer.RenderSlide(deck, -1));
    }

    [Fact]
    public void APageRangeRendersOnlyThosePages()
    {
        using var deck = Deck(6);
        using var pdf = deck.ToPdf();

        var options = new RenderOptions { Dpi = 72 };
        var middle = DocumentRenderer.RenderPdf(pdf, 2..4, options);
        var all = DocumentRenderer.RenderPdf(pdf, options);

        Assert.Equal(2, middle.Count);
        Assert.Equal(all[2], middle[0]);
        Assert.Equal(all[3], middle[1]);
    }

    [Fact]
    public void ARangeFromTheEndWorksTheWayCSharpMeansIt()
    {
        using var deck = Deck(4);
        using var pdf = deck.ToPdf();

        var options = new RenderOptions { Dpi = 72 };

        Assert.Single(DocumentRenderer.RenderPdf(pdf, ^1.., options));
        Assert.Equal(2, DocumentRenderer.RenderPdf(pdf, ^2.., options).Count);
    }

    [Fact]
    public void ALiveDeckCanBeWrittenToFilesWithoutSavingItFirst()
    {
        // The path-based overload forced a temporary file into every pipeline that generated a deck
        // and wanted thumbnails of it.
        using var directory = new TempDirectory();
        using var deck = Deck(3);

        var written = DocumentRenderer.RenderToFiles(deck, directory.Path,
            new RenderOptions { Dpi = 72 });

        Assert.Equal(3, written.Count);
        Assert.All(written, path => Assert.True(new FileInfo(path).Length > 0));

        // Padded, so a directory listing sorts the way the deck reads.
        Assert.Equal(["slide-01.png", "slide-02.png", "slide-03.png"],
            written.Select(Path.GetFileName));
    }

    [Fact]
    public void ASingleImageIsNotNumbered()
    {
        using var directory = new TempDirectory();
        using var deck = Deck(1);

        var written = DocumentRenderer.RenderToFiles(deck, directory.Path,
            new RenderOptions { Dpi = 72 }, namePrefix: "sampul");

        Assert.Equal("sampul.png", Path.GetFileName(Assert.Single(written)));
    }

    [Fact]
    public void TheFormatDecidesTheExtension()
    {
        using var directory = new TempDirectory();
        using var deck = Deck(2);

        var written = DocumentRenderer.RenderToFiles(deck, directory.Path,
            new RenderOptions { Dpi = 72, Format = RenderFormat.Jpeg, Quality = 70 });

        Assert.All(written, path => Assert.EndsWith(".jpg", path, StringComparison.Ordinal));

        // A JPEG of a mostly-white slide should be well under the PNG of the same thing.
        var jpeg = new FileInfo(written[0]).Length;
        var png = DocumentRenderer.RenderSlide(deck, 0, new RenderOptions { Dpi = 72 }).Length;

        Assert.True(jpeg < png, $"JPEG was {jpeg} bytes against PNG's {png}.");
    }

    [Fact]
    public void AWordDocumentAndAWorkbookWriteFilesToo()
    {
        using var directory = new TempDirectory();

        using var document = WordNet.WordDocument.Create();
        document.AddParagraph("Halaman satu.");

        using var workbook = ExcelNet.Workbook.Create("Data");
        workbook["Data"]["A1"].Set("Nilai");

        var pages = DocumentRenderer.RenderToFiles(document, directory.Path,
            new RenderOptions { Dpi = 72 }, namePrefix: "surat");

        var sheets = DocumentRenderer.RenderToFiles(workbook, directory.Path,
            new RenderOptions { Dpi = 72 }, namePrefix: "lembar");

        Assert.NotEmpty(pages);
        Assert.NotEmpty(sheets);
        Assert.Equal("surat.png", Path.GetFileName(pages[0]));
        Assert.Equal("lembar.png", Path.GetFileName(sheets[0]));
    }

    [Fact]
    public void DpiDecidesTheSize()
    {
        using var deck = Deck(1);

        var small = DocumentRenderer.RenderSlide(deck, 0, new RenderOptions { Dpi = 48 });
        var large = DocumentRenderer.RenderSlide(deck, 0, new RenderOptions { Dpi = 144 });

        Assert.True(large.Length > small.Length,
            "A slide rendered at three times the resolution should not be smaller.");
    }
}
