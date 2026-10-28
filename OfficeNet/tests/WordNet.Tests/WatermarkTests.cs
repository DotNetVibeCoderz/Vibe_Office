// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using Xunit;

namespace WordNet.Tests;

public class WatermarkTests
{
    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static int WatermarkCount(WordDocument document) =>
        document.DocumentPart.RelatedPartsByType(RelationshipTypes.Header)
            .SelectMany(p => p.Xml.Root!.Descendants(Ns.Wp + "docPr"))
            .Count(d => d.Attr("name") == "OfficeNet Watermark");

    [Fact]
    public void AWatermarkIsABehindTextShapeInTheHeader()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Isi kontrak.");
        document.AddWatermark("DRAF");

        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);
        Assert.Equal("DRAF", reopened.Watermark);

        var header = reopened.DocumentPart.RelatedPartByType(RelationshipTypes.Header)!.Xml.Root!;
        var anchor = header.Descendants(Ns.Wp + "anchor").Single();
        Assert.Equal("1", anchor.Attr("behindDoc"));
        Assert.Equal((315L * 60000).ToString(), header.Descendants(Ns.A + "xfrm").First().Attr("rot"));

        // The body is untouched: a watermark is not content.
        Assert.Equal("Isi kontrak.", reopened.ExtractText());
    }

    [Fact]
    public void EverySectionWithItsOwnHeaderGetsOneAndInheritingSectionsAreLeftAlone()
    {
        using var document = WordDocument.Create();
        document.Section.GetHeader().AddParagraph("Kop satu");
        document.AddParagraph("a");
        document.AddSection();
        document.Section.GetHeader().AddParagraph("Kop dua");
        document.AddParagraph("b");
        document.AddSection();                 // inherits "Kop dua"
        document.AddParagraph("c");

        document.AddWatermark("RAHASIA");

        Assert.Equal(2, WatermarkCount(document));

        // The third section still has no header of its own: giving it one would have broken the
        // inheritance and left it with an empty header and nothing else.
        Assert.Empty(document.Sections[2].Properties.Elements(Ns.W + "headerReference"));
        Assert.Contains("Kop dua", document.Sections[1].GetHeader().Text);
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void AHeaderSharedByTwoSectionsGetsOneWatermark()
    {
        // Two sections referencing one header part explicitly — what AddSection wrote before it
        // learned to inherit, so every document made with an older version looks like this.
        using var document = WordDocument.Create();
        document.Section.GetHeader().AddParagraph("Kop bersama");
        document.AddParagraph("a");
        document.AddSection();
        document.AddParagraph("b");

        var shared = document.Sections[0].Properties.Element(Ns.W + "headerReference")!;
        document.Sections[1].Properties.AddFirst(new System.Xml.Linq.XElement(shared));

        document.AddWatermark("DRAF");

        Assert.Equal(1, WatermarkCount(document));
    }

    [Fact]
    public void AddingAgainReplacesAndRemovingRemoves()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("x");
        document.AddWatermark("SATU");
        document.AddWatermark("DUA");

        Assert.Equal(1, WatermarkCount(document));
        Assert.Equal("DUA", document.Watermark);

        Assert.True(document.RemoveWatermark());
        Assert.Null(document.Watermark);
        Assert.False(document.RemoveWatermark());
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void ThePdfExportDrawsTheDocumentsWatermark()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("isi");
        document.AddWatermark("SALINAN");

        using var pdf = document.ToPdf();
        Assert.Contains("SALINAN", pdf.ExtractText());

        using var overridden = document.ToPdf(new Export.PdfExportOptions { Watermark = "LAIN" });
        Assert.Contains("LAIN", overridden.ExtractText());
        Assert.DoesNotContain("SALINAN", overridden.ExtractText());
    }

    [Fact]
    public void TheWatermarkFitsALongWord()
    {
        using var document = WordDocument.Create();
        document.AddWatermark("TIDAK UNTUK DIEDARKAN");

        var size = document.DocumentPart.RelatedPartByType(RelationshipTypes.Header)!.Xml.Root!
            .Descendants(Ns.W + "sz").First().IntAttr(Ns.W + "val");

        // Half-points. A short word gets a big face; a long phrase is sized down to fit the box
        // instead of running off the page.
        Assert.InRange(size / 2.0, 12, 60);
    }
}
