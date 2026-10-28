// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using WordNet.Sections;
using Xunit;

namespace WordNet.Tests;

public class SplitTests
{
    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static int MediaCount(byte[] docx)
    {
        using var archive = new ZipArchive(new MemoryStream(docx, writable: false));
        return archive.Entries.Count(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));
    }

    /// <summary>Three sections, each with its own header, picture and footnote.</summary>
    private static WordDocument ThreeSections()
    {
        var document = WordDocument.Create();

        for (var i = 1; i <= 3; i++)
        {
            if (i > 1)
            {
                document.AddSection();
            }

            document.Section.GetHeader().AddParagraph($"Kop {i}");
            document.AddHeading($"Bab {i}", 1);
            var paragraph = document.AddParagraph($"Isi bab {i}");
            paragraph.AddFootnote($"Catatan {i}");
            document.AddParagraph("gambar").AddPicture(TestImages.SolidPng(4 + i, 4, (byte)(i * 40), 0, 0));
        }

        return document;
    }

    [Fact]
    public void EachSectionBecomesAValidDocumentWithOnlyItsOwnThings()
    {
        using var source = ThreeSections();
        var parts = source.SplitBySection();

        Assert.Equal(3, parts.Count);

        for (var i = 0; i < 3; i++)
        {
            using var part = parts[i];
            var bytes = Save(part);
            DocxValidator.AssertValid(bytes);

            var text = part.ExtractText();
            Assert.Contains($"Bab {i + 1}", text);
            Assert.DoesNotContain($"Bab {(i + 1) % 3 + 1}", text);

            using var reopened = WordDocument.Open(bytes);
            Assert.Single(reopened.Sections);
            Assert.Equal($"Kop {i + 1}", reopened.Sections[0].GetHeader().Text);

            // Only this section's footnote and picture remain; the others were pruned.
            Assert.Equal([$"Catatan {i + 1}"], reopened.Footnotes.All.Select(n => n.Text.Trim()));
            Assert.Equal(1, MediaCount(bytes));
        }
    }

    [Fact]
    public void ANewSectionsHeaderIsItsOwn()
    {
        // AddSection used to copy the header reference, so both sections pointed at one part and
        // writing the second section's header rewrote the first's.
        using var document = WordDocument.Create();
        document.Section.GetHeader().AddParagraph("Kop satu");
        document.AddParagraph("a");
        document.AddSection();
        document.Section.GetHeader().AddParagraph("Kop dua");

        Assert.Equal("Kop satu", document.Sections[0].GetHeader().Text);
        Assert.Equal("Kop dua", document.Sections[1].GetHeader().Text);
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void AnUntouchedNewSectionInheritsTheHeaderEvenInThePdf()
    {
        using var document = WordDocument.Create();
        document.Section.GetHeader().AddParagraph("Kop bersama");
        document.AddParagraph("bagian satu");
        document.AddSection();
        document.AddParagraph("bagian dua");

        // No reference of its own: Word shows the previous section's header, and so must the PDF,
        // which lays out with the last section.
        Assert.Empty(document.Body.Elements(Ns.W + "sectPr").Single().Elements(Ns.W + "headerReference"));

        using var pdf = document.ToPdf();
        Assert.Contains("Kop bersama", pdf.ExtractText());
    }

    [Fact]
    public void ASplitPartKeepsAnInheritedHeader()
    {
        using var document = WordDocument.Create();
        document.Section.GetHeader().AddParagraph("Kop warisan");
        document.AddParagraph("satu");
        document.AddSection();
        document.AddParagraph("dua");

        var parts = document.SplitBySection();
        using var second = WordDocument.Open(Save(parts[1]));

        Assert.Equal("Kop warisan", second.Sections[0].GetHeader().Text);

        foreach (var part in parts)
        {
            part.Dispose();
        }
    }

    [Fact]
    public void ThePartsKeepTheirSectionsPageSetup()
    {
        using var source = WordDocument.Create();
        source.AddParagraph("potret");
        var landscape = source.AddSection();
        landscape.Orientation = PageOrientation.Landscape;
        source.AddParagraph("lanskap");

        var parts = source.SplitBySection();

        Assert.Equal(PageOrientation.Portrait, parts[0].Section.Orientation);
        Assert.Equal(PageOrientation.Landscape, parts[1].Section.Orientation);

        // The first section of a part starts the document; a break type there means nothing.
        Assert.Null(parts[1].Body.Element(Ns.W + "sectPr")!.Element(Ns.W + "type"));

        foreach (var part in parts)
        {
            part.Dispose();
        }
    }

    [Fact]
    public void SplittingByHeadingMakesOneDocumentPerChapter()
    {
        using var source = WordDocument.Create();
        source.AddParagraph("Pengantar");
        source.AddHeading("Bab 1", 1);
        source.AddHeading("1.1", 2);
        source.AddParagraph("isi 1");
        source.AddHeading("Bab 2", 1);
        source.AddParagraph("isi 2");

        var chapters = source.SplitByHeading(1);
        Assert.Equal(["Pengantar", "Bab 1\n1.1\nisi 1", "Bab 2\nisi 2"], chapters.Select(c => c.ExtractText()));

        var sections = source.SplitByHeading(2);
        Assert.Equal(4, sections.Count);

        foreach (var part in chapters.Concat(sections))
        {
            DocxValidator.AssertValid(Save(part));
            part.Dispose();
        }
    }

    [Fact]
    public void SplittingAtPageBreaksDropsTheBreaksAndSplitsMidParagraph()
    {
        using var source = WordDocument.Create();
        source.AddParagraph("halaman satu");
        source.AddPageBreak();
        var mixed = source.AddParagraph("akhir dua");
        mixed.AddRun().Element.Add(new System.Xml.Linq.XElement(Ns.W + "br",
            new System.Xml.Linq.XAttribute(Ns.W + "type", "page")));
        mixed.AddRun("awal tiga");

        var pages = source.SplitAtPageBreaks();

        Assert.Equal(3, pages.Count);
        Assert.Equal("halaman satu", pages[0].ExtractText().Trim());
        Assert.Equal("akhir dua", pages[1].ExtractText().Trim());
        Assert.Equal("awal tiga", pages[2].ExtractText().Trim());

        foreach (var page in pages)
        {
            Assert.DoesNotContain(page.Body.Descendants(Ns.W + "br"), b => b.Attr(Ns.W + "type") == "page");
            DocxValidator.AssertValid(Save(page));
            page.Dispose();
        }
    }

    [Fact]
    public void ABookmarkCutInHalfIsRemoved()
    {
        using var source = WordDocument.Create();
        var first = source.AddParagraph("awal");
        source.AddSection();
        var second = source.AddParagraph("akhir");

        // A bookmark spanning the section break: start in the first part, end in the second.
        first.Element.Add(new System.Xml.Linq.XElement(Ns.W + "bookmarkStart",
            new System.Xml.Linq.XAttribute(Ns.W + "id", "90"), new System.Xml.Linq.XAttribute(Ns.W + "name", "lintas")));
        second.Element.Add(new System.Xml.Linq.XElement(Ns.W + "bookmarkEnd",
            new System.Xml.Linq.XAttribute(Ns.W + "id", "90")));

        foreach (var part in source.SplitBySection())
        {
            Assert.DoesNotContain(part.Body.Descendants(), e => e.Attr(Ns.W + "id") == "90");
            part.Dispose();
        }
    }

    [Fact]
    public void TheSourceIsUnchanged()
    {
        using var source = ThreeSections();
        var before = source.Body.ToString();

        foreach (var part in source.SplitBySection())
        {
            part.Dispose();
        }

        Assert.Equal(before, source.Body.ToString());
    }

    [Fact]
    public void SplitThenAppendRoundTrips()
    {
        using var source = ThreeSections();
        var parts = source.SplitBySection();

        using var rebuilt = parts[0];
        rebuilt.AppendDocument(parts[1]);
        rebuilt.AppendDocument(parts[2]);

        Assert.Equal(source.ExtractText(), rebuilt.ExtractText());
        Assert.Equal(3, rebuilt.Sections.Count);
        DocxValidator.AssertValid(Save(rebuilt));

        parts[1].Dispose();
        parts[2].Dispose();
    }
}
