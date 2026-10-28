// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Xml.Linq;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using WordNet.Merging;
using Xunit;

namespace WordNet.Tests;

/// <summary>
/// <see cref="WordDocument.AppendDocument"/>: the content arrives, and so does everything it refers
/// to, renumbered so nothing collides.
/// </summary>
public class MergeTests
{
    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static XElement PartRoot(byte[] docx, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(docx, writable: false));
        using var stream = archive.GetEntry(name)!.Open();
        return XDocument.Load(stream).Root!;
    }

    private static WordDocument Target()
    {
        var target = WordDocument.Create();
        target.AddHeading("Surat utama", 1);
        target.AddList(["satu", "dua"], numbered: true);
        target.AddParagraph("Lihat ").AddBookmark("Lampiran");
        target.AddParagraph("logo").AddPicture(TestImages.SolidPng(10, 10, 255, 0, 0));
        target.Section.GetHeader().AddParagraph("Kop surat utama");
        return target;
    }

    private static WordDocument Source()
    {
        var source = WordDocument.Create();
        source.AddHeading("Lampiran", 1);
        source.AddList(["tiga", "empat"], numbered: true);

        var paragraph = source.AddParagraph("Rincian ");
        paragraph.AddBookmark("Lampiran");
        paragraph.AddFootnote("Catatan lampiran.");
        paragraph.AddComment("Periksa lagi.", "Ani");
        paragraph.AddHyperlink("situs", "https://example.org/lampiran");
        paragraph.AddInternalHyperlink(" ke atas", "Lampiran");
        paragraph.AddTrackedRun(" tambahan", "Budi");

        source.AddParagraph("gambar").AddPicture(TestImages.SolidPng(12, 12, 0, 0, 255));
        source.Section.GetHeader().AddParagraph("Kop lampiran").AddPicture(TestImages.SolidPng(8, 8, 0, 255, 0));
        return source;
    }

    [Fact]
    public void AnAppendedDocumentIsValidAndComplete()
    {
        using var target = Target();
        using var source = Source();

        target.AppendDocument(source);

        var bytes = Save(target);
        DocxValidator.AssertValid(bytes);

        var text = target.ExtractText();
        Assert.Contains("Surat utama", text);
        Assert.Contains("Lampiran", text);
        Assert.Contains("Rincian situs ke atas tambahan", text);

        // Two sections, each with its own header; the appended one shows the source's.
        using var reopened = WordDocument.Open(bytes);
        Assert.Equal(2, reopened.Sections.Count);
        Assert.Equal("Kop surat utama", reopened.Sections[0].GetHeader().Text);
        Assert.Equal("Kop lampiran", reopened.Sections[1].GetHeader().Text);
    }

    [Fact]
    public void TheSourceIsLeftUntouched()
    {
        using var target = Target();
        using var source = Source();
        var before = source.Body.ToString();

        target.AppendDocument(source);

        Assert.Equal(before, source.Body.ToString());
    }

    [Fact]
    public void ListsKeepTheirOwnNumbering()
    {
        // Both documents have list 1. Without renumbering, and without a fresh nsid on the copied
        // abstract list, the appended "tiga" is numbered 3 — a continuation of the target's list.
        using var target = Target();
        using var source = Source();
        target.AppendDocument(source);

        var numbering = PartRoot(Save(target), "word/numbering.xml");
        var listIds = target.Body.Descendants(Ns.W + "numPr")
            .Select(n => n.Element(Ns.W + "numId").Attr(Ns.W + "val"))
            .Distinct()
            .ToList();

        Assert.Equal(2, listIds.Count);

        var nsids = numbering.Elements(Ns.W + "abstractNum")
            .Select(a => a.Element(Ns.W + "nsid")?.Attr(Ns.W + "val"))
            .Where(v => v is not null)
            .ToList();
        Assert.Equal(nsids.Count, nsids.Distinct().Count());

        // Schema order: every abstractNum before the first num.
        var names = numbering.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.True(names.LastIndexOf("abstractNum") < names.IndexOf("num"));
    }

    [Fact]
    public void ACopiedListGetsItsOwnNsidEvenWhenBothDocumentsShareOne()
    {
        // Two documents made from the same Normal.dotm routinely carry the same w:nsid, and Word
        // decides that two lists are one list by nsid, not by id. WordNet's own lists carry none,
        // so the shared value is planted the way Word writes it.
        using var target = Target();
        using var source = Source();

        foreach (var document in new[] { target, source })
        {
            var root = document.DocumentPart.RelatedPartByType(RelationshipTypes.Numbering)!.Xml.Root!;
            foreach (var abstractNum in root.Elements(Ns.W + "abstractNum"))
            {
                abstractNum.AddFirst(new XElement(Ns.W + "nsid", new XAttribute(Ns.W + "val", "1A2B3C4D")));
            }
        }

        target.AppendDocument(source);

        var nsids = target.DocumentPart.RelatedPartByType(RelationshipTypes.Numbering)!.Xml.Root!
            .Elements(Ns.W + "abstractNum")
            .Select(a => a.Element(Ns.W + "nsid").Attr(Ns.W + "val"))
            .ToList();

        Assert.True(nsids.Count >= 2);
        Assert.Equal(nsids.Count, nsids.Distinct().Count());
    }

    [Fact]
    public void FootnotesAndCommentsComeAlongUnderNewIds()
    {
        using var target = Target();
        target.Paragraphs[0].AddFootnote("Catatan utama.");
        target.Paragraphs[0].AddComment("Komentar utama.", "Citra");

        using var source = Source();
        target.AppendDocument(source);

        var notes = target.Footnotes.All.Select(n => n.Text.Trim()).ToList();
        Assert.Contains("Catatan utama.", notes);
        Assert.Contains("Catatan lampiran.", notes);
        Assert.Equal(notes.Count, target.Footnotes.All.Select(n => n.Id).Distinct().Count());

        Assert.Equal(2, target.Comments.Count);
        Assert.Equal(2, target.Comments.All.Select(c => c.Id).Distinct().Count());

        // Every reference resolves to a note that exists.
        var noteIds = target.Footnotes.All.Select(n => (string?)n.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        Assert.All(target.Body.Descendants(Ns.W + "footnoteReference"),
            r => Assert.Contains(r.Attr(Ns.W + "id"), noteIds));

        DocxValidator.AssertValid(Save(target));
    }

    [Fact]
    public void BookmarksAreRenamedAndTheirLinksFollow()
    {
        using var target = Target();
        using var source = Source();
        target.AppendDocument(source);

        var names = target.Body.Descendants(Ns.W + "bookmarkStart").Select(b => b.Attr(Ns.W + "name")).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains("Lampiran_2", names);

        // The appended internal link pointed at the source's bookmark; it must now point at the
        // renamed copy, not at the target's bookmark of the same old name.
        var anchors = target.Body.Descendants(Ns.W + "hyperlink").Select(h => h.Attr(Ns.W + "anchor")).OfType<string>();
        Assert.Contains("Lampiran_2", anchors);
    }

    [Fact]
    public void IdsThatMustBeUniqueAre()
    {
        using var target = Target();
        target.Paragraphs[0].AddTrackedRun(" draf", "Ani");

        using var source = Source();
        target.AppendDocument(source);
        target.AppendDocument(source);   // twice: the mail-merge pattern of one source, many copies

        var bytes = Save(target);
        DocxValidator.AssertValid(bytes);

        var revisionIds = target.Revisions.Select(r => r.Id).ToList();
        Assert.Equal(revisionIds.Count, revisionIds.Distinct().Count());
    }

    [Fact]
    public void ImagesAreDeduplicatedAndHeaderImagesResolveInTheirOwnPart()
    {
        using var target = Target();
        using var first = Source();
        using var second = Source();

        target.AppendDocument(first);
        target.AppendDocument(second);

        var bytes = Save(target);
        DocxValidator.AssertValid(bytes);

        using var archive = new ZipArchive(new MemoryStream(bytes, writable: false));
        // Red logo, blue picture, green header picture: three images however many copies.
        Assert.Equal(3, archive.Entries.Count(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)));

        // Each copied header's r:embed resolves against that header's own relationships.
        foreach (var header in archive.Entries.Where(e => e.FullName.StartsWith("word/header", StringComparison.Ordinal)))
        {
            var root = PartRoot(bytes, header.FullName);
            var rels = archive.GetEntry($"word/_rels/{header.Name}.rels");
            var ids = rels is null
                ? new HashSet<string?>()
                : PartRoot(bytes, rels.FullName).Elements().Select(r => (string?)r.Attribute("Id")!.Value).ToHashSet();

            foreach (var attribute in root.Descendants().Attributes().Where(a => a.Name.Namespace == Ns.R))
            {
                Assert.Contains(attribute.Value, ids);
            }
        }
    }

    [Fact]
    public void StylesUseTheTargetsDefinitionByDefault()
    {
        using var target = Target();
        target.Styles["Heading1"]!.RunFormat.Color = OfficeNet.Core.Drawing.OfficeColor.FromRgb(0xC0, 0x00, 0x00);

        using var source = Source();
        target.AppendDocument(source);

        Assert.All(target.Body.Descendants(Ns.W + "pStyle"),
            s => Assert.NotEqual("Heading1_1", s.Attr(Ns.W + "val")));
    }

    [Fact]
    public void KeepSourceCopiesAConflictingStyleUnderANewId()
    {
        using var target = Target();
        target.Styles["Heading1"]!.RunFormat.Color = OfficeNet.Core.Drawing.OfficeColor.FromRgb(0xC0, 0x00, 0x00);

        using var source = Source();
        target.AppendDocument(source, new AppendOptions { Styles = StyleConflict.KeepSource });

        var copied = target.Styles["Heading1_1"];
        Assert.NotNull(copied);
        Assert.False(copied.IsDefault);
        Assert.NotEqual(target.Styles["Heading1"]!.Name, copied.Name);

        var appendedHeading = target.Paragraphs.First(p => p.Text == "Lampiran");
        Assert.Equal("Heading1_1", appendedHeading.StyleId);
        Assert.Equal("Heading1", target.Paragraphs[0].StyleId);

        DocxValidator.AssertValid(Save(target));
    }

    [Fact]
    public void ACopiedDefaultStyleIsNotADefaultInTheTarget()
    {
        // Heading 1 is based on Normal. When both differ, the copy of the source's Normal comes
        // along under a new id — and must not keep w:default, or the target has two default
        // paragraph styles and Word picks one.
        using var target = Target();
        target.Styles["Heading1"]!.RunFormat.Color = OfficeNet.Core.Drawing.OfficeColor.FromRgb(0xC0, 0x00, 0x00);

        using var source = Source();
        source.Styles["Normal"]!.RunFormat.FontName = "Georgia";

        target.AppendDocument(source, new AppendOptions { Styles = StyleConflict.KeepSource });

        Assert.Equal("Normal_1", target.Styles["Heading1_1"]!.BasedOn);
        Assert.Single(target.Styles, s => s.Type == Styles.StyleType.Paragraph && s.IsDefault);
    }

    [Fact]
    public void IdenticalStylesAreSharedEvenWhenKeepingSource()
    {
        using var target = Target();
        using var source = Source();
        target.AppendDocument(source, new AppendOptions { Styles = StyleConflict.KeepSource });

        Assert.Null(target.Styles["Heading1_1"]);
    }

    [Fact]
    public void WithoutABreakTheContentFlowsIntoOneSection()
    {
        using var target = Target();
        using var source = Source();

        // A section break inside the source too: None must drop those as well, not only the last.
        source.AddSection();
        source.AddParagraph("bagian kedua lampiran");
        target.AppendDocument(source, new AppendOptions { SectionBreak = SectionJoin.None });

        var bytes = Save(target);
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);
        Assert.Single(reopened.Sections);
        Assert.Equal("Kop surat utama", reopened.Sections[0].GetHeader().Text);

        // The source's header was not referenced, so it was not copied.
        using var archive = new ZipArchive(new MemoryStream(bytes, writable: false));
        Assert.Single(archive.Entries, e => e.FullName.StartsWith("word/header", StringComparison.Ordinal));
    }

    [Fact]
    public void AContinuousBreakIsMarkedOnTheAppendedSection()
    {
        using var target = Target();
        using var source = Source();
        target.AppendDocument(source, new AppendOptions { SectionBreak = SectionJoin.Continuous });

        var final = target.Body.Element(Ns.W + "sectPr")!;
        Assert.Equal("continuous", final.Element(Ns.W + "type").Attr(Ns.W + "val"));

        // The target's section ends in its own last paragraph, not in a blank one added for it.
        var carrier = target.Body.Descendants(Ns.W + "sectPr").First(s => s != final).Parent!.Parent!;
        Assert.Contains(target.Paragraphs, p => p.Element == carrier && p.Text.Length > 0);

        DocxValidator.AssertValid(Save(target));
    }

    [Fact]
    public void APictureAddedAfterAMergeGetsAFreshDrawingId()
    {
        using var target = Target();
        using var source = Source();
        target.AppendDocument(source);

        // The merge handed out docPr ids; the document's own counter must have moved past them.
        target.AddParagraph("sesudah").AddPicture(TestImages.SolidPng(5, 5, 9, 9, 9));
        DocxValidator.AssertValid(Save(target));
    }

    [Fact]
    public void HeaderPicturesAndLinksResolveInTheHeader()
    {
        // Not a merge test as such: a picture or link added to a header used to put its
        // relationship on the document part, so the header pointed at nothing. Found because the
        // merge tests checked the header's relationships and the source already failed.
        using var document = WordDocument.Create();
        document.AddParagraph("isi");
        var header = document.Section.GetHeader().AddParagraph("Kop ");
        header.AddPicture(TestImages.SolidPng(6, 6, 1, 2, 3));
        header.AddHyperlink("situs", "https://example.org/");
        document.Paragraphs[0].AddFootnote("catatan").AddParagraph("tautan").AddHyperlink(" di sini", "https://example.org/n");

        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void AppendingADocumentToItselfIsRefused()
    {
        using var document = WordDocument.Create();
        Assert.Throws<ArgumentException>(() => document.AppendDocument(document));
    }
}
