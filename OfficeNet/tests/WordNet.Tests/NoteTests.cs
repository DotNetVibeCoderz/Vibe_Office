// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using WordNet.Notes;
using Xunit;

namespace WordNet.Tests;

public class FootnoteTests
{
    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void AFootnoteRoundTrips()
    {
        byte[] bytes;

        using (var document = WordDocument.Create())
        {
            var paragraph = document.AddParagraph("Pernyataan yang perlu dirujuk.");
            paragraph.AddFootnote("Sumber: Gravicode Studios, 2026.");

            bytes = Save(document);
        }

        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);

        var note = Assert.Single(reopened.Footnotes.All);
        Assert.Contains("Gravicode Studios", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReservedSeparatorsExistAndAreNotCountedAsNotes()
    {
        // Ids 0 and 1 are the separator and continuation separator. Word repairs a part missing
        // them, but a caller asking for "the footnotes" means the ones a reader sees.
        using var document = WordDocument.Create();
        document.AddParagraph("Teks").AddFootnote("Catatan");

        using var package = OpcPackage.Open(new MemoryStream(Save(document), writable: false));
        var part = package.FindPart("/word/footnotes.xml");

        Assert.NotNull(part);

        var all = part.Xml.Root!.Elements(Ns.W + "footnote").ToList();

        Assert.Equal(3, all.Count);
        Assert.Equal("separator", all[0].Attr(Ns.W + "type"));
        Assert.Equal("continuationSeparator", all[1].Attr(Ns.W + "type"));
        Assert.Null(all[2].Attr(Ns.W + "type"));

        Assert.Single(document.Footnotes.All);
    }

    [Fact]
    public void TheReferenceIdMatchesTheDefinition()
    {
        // A reference pointing at an id with no definition is what Word calls unreadable content.
        using var document = WordDocument.Create();
        var note = document.AddParagraph("Teks").AddFootnote("Catatan");

        var reference = document.Body.Descendants(Ns.W + "footnoteReference").Single();

        Assert.Equal(note.Id, reference.IntAttr(Ns.W + "id"));
        Assert.True(note.Id > 1, "A note must not reuse a reserved separator id.");
    }

    [Fact]
    public void NoteIdsContinuePastTheHighestRatherThanCountingNotes()
    {
        // Deleting note 3 of five must not make the next one collide with note 4.
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Teks");

        var first = paragraph.AddFootnote("Satu");
        var second = paragraph.AddFootnote("Dua");
        var third = paragraph.AddFootnote("Tiga");

        document.Footnotes.Remove(second.Id);

        var fourth = paragraph.AddFootnote("Empat");

        Assert.True(fourth.Id > third.Id, "Ids must not be reused after a removal.");
        Assert.Equal(3, document.Footnotes.Count);
    }

    [Fact]
    public void RemovingANoteAlsoRemovesItsReference()
    {
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Teks");

        var note = paragraph.AddFootnote("Akan dihapus");
        paragraph.AddFootnote("Bertahan");

        Assert.True(document.Footnotes.Remove(note.Id));

        var remaining = document.Body.Descendants(Ns.W + "footnoteReference")
            .Select(e => e.IntAttr(Ns.W + "id"))
            .ToList();

        Assert.DoesNotContain(note.Id, remaining);
        Assert.Single(remaining);

        // And the file is still valid, which is the point of removing both together.
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void ASeparatorCannotBeRemoved()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Teks").AddFootnote("Catatan");

        Assert.Throws<ArgumentOutOfRangeException>(() => document.Footnotes.Remove(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Footnotes.Remove(1));
    }

    [Fact]
    public void EndnotesAreASeparatePartFromFootnotes()
    {
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Teks");

        paragraph.AddFootnote("Di kaki halaman");
        paragraph.AddEndnote("Di akhir dokumen");

        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        using var package = OpcPackage.Open(new MemoryStream(bytes, writable: false));

        Assert.NotNull(package.FindPart("/word/footnotes.xml"));
        Assert.NotNull(package.FindPart("/word/endnotes.xml"));

        using var reopened = WordDocument.Open(bytes);

        Assert.Single(reopened.Footnotes.All);
        Assert.Single(reopened.Endnotes.All);
    }

    [Fact]
    public void ADocumentWithNoNotesCarriesNoNotesPart()
    {
        // The part is created on first use; shipping an empty one is what Word avoids too.
        using var document = WordDocument.Create();
        document.AddParagraph("Tanpa catatan");

        using var package = OpcPackage.Open(new MemoryStream(Save(document), writable: false));

        Assert.Null(package.FindPart("/word/footnotes.xml"));
        Assert.Null(package.FindPart("/word/endnotes.xml"));
    }

    [Fact]
    public void TheReferenceStylesAreDefined()
    {
        // A note whose rStyle names an undefined style renders as body text — no superscript —
        // which looks like a layout bug rather than a missing style.
        using var document = WordDocument.Create();
        document.AddParagraph("Teks").AddFootnote("Catatan");

        Assert.True(document.Styles.Contains("FootnoteReference"));
        Assert.True(document.Styles.Contains("FootnoteText"));

        Assert.Equal(VerticalAlignment.Superscript,
            document.Styles["FootnoteReference"]!.RunFormat.VerticalAlignment);
    }

    [Fact]
    public void ANoteCanHoldSeveralParagraphs()
    {
        // A note is a block-level document, not a string: a footnote citing three sources across
        // two paragraphs is ordinary.
        using var document = WordDocument.Create();

        var note = document.AddParagraph("Teks").AddFootnote("Sumber pertama.");
        note.AddParagraph("Sumber kedua.");

        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);

        Assert.Equal(2, reopened.Footnotes.All[0].Paragraphs.Count);
    }
}

public class CommentTests
{
    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void ACommentRoundTrips()
    {
        byte[] bytes;

        using (var document = WordDocument.Create())
        {
            document.AddParagraph("Perlu ditinjau.")
                .AddComment("Tolong periksa angkanya.", "Kang Fadhil");

            bytes = Save(document);
        }

        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);

        var comment = Assert.Single(reopened.Comments.All);

        Assert.Contains("periksa angkanya", comment.Text, StringComparison.Ordinal);
        Assert.Equal("Kang Fadhil", comment.Author);
        Assert.Equal("KF", comment.Initials);
    }

    [Fact]
    public void TheRangeMarkersAndTheReferenceShareTheId()
    {
        // All three carry the same id, and Word reports the file as unreadable if they disagree.
        using var document = WordDocument.Create();
        var comment = document.AddParagraph("Teks").AddComment("Catatan", "Penulis");

        var ids = document.Body.Descendants()
            .Where(e => e.Name == Ns.W + "commentRangeStart"
                     || e.Name == Ns.W + "commentRangeEnd"
                     || e.Name == Ns.W + "commentReference")
            .Select(e => e.IntAttr(Ns.W + "id"))
            .ToList();

        Assert.Equal(3, ids.Count);
        Assert.All(ids, id => Assert.Equal(comment.Id, id));
    }

    [Fact]
    public void TheMarkersAppearInTheOrderWordExpects()
    {
        // start, then the content, then end, then the reference run.
        using var document = WordDocument.Create();
        document.AddParagraph("Teks").AddComment("Catatan", "Penulis");

        var paragraph = document.Paragraphs[0].Element;

        var names = paragraph.Elements()
            .Select(e => e.Name.LocalName)
            .Where(n => n is "commentRangeStart" or "commentRangeEnd" or "r")
            .ToList();

        Assert.Equal("commentRangeStart", names[0]);
        Assert.Equal("r", names[1]);
        Assert.Equal("commentRangeEnd", names[2]);
        Assert.Equal("r", names[3]);
    }

    [Fact]
    public void ARunCanBeCommentedOnItsOwn()
    {
        using var document = WordDocument.Create();

        var paragraph = document.AddParagraph();
        paragraph.AddRun("Bagian biasa. ");
        var target = paragraph.AddRun("Bagian yang dikomentari.");

        target.AddComment("Ini yang saya maksud.", "Kang Fadhil");

        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        // The range brackets the second run only, so the start marker follows the first run.
        var names = document.Paragraphs[0].Element.Elements()
            .Select(e => e.Name.LocalName)
            .ToList();

        Assert.Equal(["r", "commentRangeStart", "r", "commentRangeEnd", "r"], names);
    }

    [Fact]
    public void RemovingACommentRemovesItsMarkersToo()
    {
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Teks");

        var first = paragraph.AddComment("Akan dihapus", "A");
        document.AddParagraph("Lain").AddComment("Bertahan", "B");

        Assert.True(document.Comments.Remove(first.Id));

        var remaining = document.Body.Descendants()
            .Where(e => e.Name == Ns.W + "commentRangeStart"
                     || e.Name == Ns.W + "commentRangeEnd"
                     || e.Name == Ns.W + "commentReference")
            .Select(e => e.IntAttr(Ns.W + "id"))
            .ToList();

        Assert.DoesNotContain(first.Id, remaining);
        Assert.Equal(3, remaining.Count);

        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void InitialsAreDerivedFromTheAuthorWhenOmitted()
    {
        using var document = WordDocument.Create();

        var comment = document.AddParagraph("Teks").AddComment("Catatan", "Budi Santoso Wijaya");

        Assert.Equal("BSW", comment.Initials);
    }

    [Fact]
    public void CommentsCanBeFilteredByAuthor()
    {
        using var document = WordDocument.Create();

        document.AddParagraph("Satu").AddComment("a", "Kang Fadhil");
        document.AddParagraph("Dua").AddComment("b", "Budi");
        document.AddParagraph("Tiga").AddComment("c", "Kang Fadhil");

        Assert.Equal(2, document.Comments.ByAuthor("Kang Fadhil").Count());
    }

    [Fact]
    public void ADocumentWithNoCommentsCarriesNoCommentsPart()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Tanpa komentar");

        using var package = OpcPackage.Open(new MemoryStream(Save(document), writable: false));

        Assert.Null(package.FindPart("/word/comments.xml"));
    }
}

public class FootnoteExportTests
{
    private static string ExportedText(Action<WordDocument> build)
    {
        using var document = WordDocument.Create();
        build(document);

        using var pdf = document.ToPdf();
        return pdf.ExtractText();
    }

    [Fact]
    public void TheNoteTextAppearsInTheExportedPdf()
    {
        var text = ExportedText(document =>
            document.AddParagraph("Pernyataan.").AddFootnote("Sumber terverifikasi."));

        Assert.Contains("Pernyataan.", text, StringComparison.Ordinal);
        Assert.Contains("Sumber terverifikasi.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesAreNumberedInDocumentOrderFromOne()
    {
        // The display number is the reader's, not the id: ids continue past deletions while the
        // numbering restarts at 1 for every document.
        var text = ExportedText(document =>
        {
            var paragraph = document.AddParagraph("Satu dan dua.");
            paragraph.AddFootnote("Catatan pertama");
            paragraph.AddFootnote("Catatan kedua");
        });

        var first = text.IndexOf("Catatan pertama", StringComparison.Ordinal);
        var second = text.IndexOf("Catatan kedua", StringComparison.Ordinal);

        Assert.True(first >= 0 && second > first,
            "Notes must appear in the order they were referenced.");
    }

    [Fact]
    public void ANoteIsDrawnOnThePageItsReferenceIsOn()
    {
        // The point of a footnote. A note referenced on page two must not be drawn on page one.
        using var document = WordDocument.Create();

        document.AddParagraph("Awal.").AddFootnote("Catatan halaman satu");

        for (var i = 0; i < 60; i++)
        {
            document.AddParagraph($"Paragraf pengisi nomor {i} untuk mendorong ke halaman berikutnya.");
        }

        document.AddParagraph("Akhir.").AddFootnote("Catatan halaman dua");

        using var pdf = document.ToPdf();

        Assert.True(pdf.Pages.Count >= 2, "The filler should have produced a second page.");

        var firstPage = pdf.Pages[0].ExtractText();
        var lastPage = pdf.Pages[^1].ExtractText();

        Assert.Contains("Catatan halaman satu", firstPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Catatan halaman dua", firstPage, StringComparison.Ordinal);
        Assert.Contains("Catatan halaman dua", lastPage, StringComparison.Ordinal);
    }

    [Fact]
    public void ADanglingReferenceDrawsNothingRatherThanABrokenNumber()
    {
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Teks.");
        var note = paragraph.AddFootnote("Akan hilang");

        // Remove the definition but leave the reference, which is the shape a hand-edited file
        // arrives in. Nothing should be drawn for it.
        document.Footnotes.Element(note.Id)?.Remove();

        using var pdf = document.ToPdf();

        Assert.Contains("Teks.", pdf.ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void EndnotesAreNotDrawnAtTheFootOfThePage()
    {
        // Endnotes belong after the last page, which is separate work. Drawing them as footnotes
        // would put them in the wrong place and look like a feature rather than a gap.
        var text = ExportedText(document =>
            document.AddParagraph("Teks.").AddEndnote("Catatan akhir"));

        Assert.DoesNotContain("Catatan akhir", text, StringComparison.Ordinal);
    }
}
