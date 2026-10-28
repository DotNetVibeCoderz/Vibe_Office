// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core.Xml;
using WordNet.Revisions;
using Xunit;

namespace WordNet.Tests;

/// <summary>
/// Tracked changes: reading them, accepting and rejecting them, and writing new ones.
/// </summary>
/// <remarks>
/// The fixtures are written as raw WordprocessingML, the way Word writes it, rather than through
/// the authoring API — the reader must not depend on this library having produced the file.
/// </remarks>
public class RevisionTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>A document whose body is the given paragraphs, inserted before the final sectPr.</summary>
    private static WordDocument WithBody(string xml)
    {
        var document = WordDocument.Create();
        var wrapper = XElement.Parse($"<root xmlns:w=\"{W}\">{xml}</root>");
        var sectPr = document.Body.Element(Ns.W + "sectPr")!;

        document.Body.Elements().Where(e => e != sectPr).Remove();
        sectPr.AddBeforeSelf(wrapper.Elements());
        return document;
    }

    private const string Tracked =
        """
        <w:p><w:r><w:t xml:space="preserve">Harga </w:t></w:r><w:del w:id="1" w:author="Ani" w:date="2026-10-01T09:00:00Z"><w:r><w:delText>lama </w:delText></w:r></w:del><w:ins w:id="2" w:author="Budi" w:date="2026-10-02T10:30:00Z"><w:r><w:t xml:space="preserve">baru </w:t></w:r></w:ins><w:r><w:t>berlaku.</w:t></w:r></w:p>
        """;

    // ---- The final view ------------------------------------------------------------------------

    [Fact]
    public void InsertedTextIsReadAndDeletedTextIsNot()
    {
        // Before ParagraphContent, runs inside w:ins were invisible to Runs, Text, the PDF and the
        // HTML alike, so every tracked insertion vanished from every output.
        using var document = WithBody(Tracked);

        Assert.Equal("Harga baru berlaku.", document.Paragraphs[0].Text);
        Assert.Equal("Harga baru berlaku.", document.ExtractText());
        Assert.Equal(3, document.Paragraphs[0].Runs.Count);
    }

    [Fact]
    public void ThePdfShowsTheFinalView()
    {
        using var document = WithBody(Tracked);
        using var pdf = document.ToPdf();
        var text = pdf.ExtractText();

        Assert.Contains("baru", text);
        Assert.DoesNotContain("lama", text);
    }

    [Fact]
    public void TheHtmlShowsTheFinalView()
    {
        using var document = WithBody(Tracked);
        var html = Export.WordToHtml.Convert(document);

        Assert.Contains("baru", html);
        Assert.DoesNotContain("lama", html);
    }

    [Fact]
    public void ContentControlsAndSimpleFieldsAreRead()
    {
        using var document = WithBody(
            """
            <w:p><w:sdt><w:sdtPr><w:alias w:val="Nama"/></w:sdtPr><w:sdtContent><w:r><w:t>Kang Fadhil</w:t></w:r></w:sdtContent></w:sdt><w:fldSimple w:instr=" DATE "><w:r><w:t xml:space="preserve"> 28/10</w:t></w:r></w:fldSimple></w:p>
            """);

        Assert.Equal("Kang Fadhil 28/10", document.Paragraphs[0].Text);
    }

    // ---- Reading revisions ---------------------------------------------------------------------

    [Fact]
    public void RevisionsReportKindAuthorDateAndText()
    {
        using var document = WithBody(Tracked);
        var revisions = document.Revisions;

        Assert.Equal(2, revisions.Count);

        Assert.Equal(RevisionKind.Deletion, revisions[0].Kind);
        Assert.Equal("Ani", revisions[0].Author);
        Assert.Equal("lama ", revisions[0].Text);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), revisions[0].Date);

        Assert.Equal(RevisionKind.Insertion, revisions[1].Kind);
        Assert.Equal("Budi", revisions[1].Author);
        Assert.Equal("baru ", revisions[1].Text);

        Assert.Single(revisions.ByAuthor("Budi"));
    }

    [Fact]
    public void MarkersAreClassifiedByWhereTheySit()
    {
        // The same element name means three things. Reading w:ins in a paragraph mark as inserted
        // text would accept a paragraph split as if it were a word.
        using var document = WithBody(
            """
            <w:p><w:pPr><w:rPr><w:ins w:id="5" w:author="A" w:date="2026-10-01T00:00:00Z"/></w:rPr></w:pPr><w:r><w:t>satu</w:t></w:r></w:p>
            <w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="2000"/></w:tblGrid><w:tr><w:trPr><w:del w:id="6" w:author="A" w:date="2026-10-01T00:00:00Z"/></w:trPr><w:tc><w:p><w:r><w:t>sel</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            <w:p><w:r><w:rPr><w:b/><w:rPrChange w:id="7" w:author="A" w:date="2026-10-01T00:00:00Z"><w:rPr/></w:rPrChange></w:rPr><w:t>tebal</w:t></w:r></w:p>
            """);

        Assert.Equal(
            [RevisionKind.ParagraphInserted, RevisionKind.RowDeleted, RevisionKind.RunFormat],
            document.Revisions.Select(r => r.Kind));
    }

    // ---- Accepting and rejecting ---------------------------------------------------------------

    [Fact]
    public void AcceptAllKeepsInsertionsAndDropsDeletions()
    {
        using var document = WithBody(Tracked);

        Assert.Equal(2, document.Revisions.AcceptAll());
        Assert.Empty(document.Revisions);
        Assert.Equal("Harga baru berlaku.", document.ExtractText());

        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);
        Assert.DoesNotContain("lama", System.Text.Encoding.UTF8.GetString(Unzip(bytes, "word/document.xml")));
    }

    [Fact]
    public void RejectAllRestoresTheOriginal()
    {
        using var document = WithBody(Tracked);

        Assert.Equal(2, document.Revisions.RejectAll());
        Assert.Empty(document.Revisions);
        Assert.Equal("Harga lama berlaku.", document.ExtractText());

        // The restored text must be w:t again; w:delText outside a deletion is damage.
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void AcceptingADeletedParagraphMarkJoinsTheParagraphs()
    {
        using var document = WithBody(
            """
            <w:p><w:pPr><w:jc w:val="left"/><w:rPr><w:del w:id="1" w:author="A" w:date="2026-10-01T00:00:00Z"/></w:rPr></w:pPr><w:r><w:t xml:space="preserve">Awal </w:t></w:r></w:p>
            <w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:t>akhir</w:t></w:r></w:p>
            """);

        document.Revisions.AcceptAll();

        // One paragraph, carrying the second paragraph's properties: deleting a mark joins its
        // text into the paragraph that follows, as Word does.
        Assert.Single(document.Paragraphs);
        Assert.Equal("Awal akhir", document.Paragraphs[0].Text);
        Assert.Equal(ParagraphAlignment.Center, document.Paragraphs[0].Alignment);
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void RejectingAnInsertedParagraphMarkJoinsTheParagraphs()
    {
        using var document = WithBody(
            """
            <w:p><w:pPr><w:rPr><w:ins w:id="1" w:author="A" w:date="2026-10-01T00:00:00Z"/></w:rPr></w:pPr><w:r><w:t xml:space="preserve">Satu </w:t></w:r></w:p>
            <w:p><w:r><w:t>kalimat</w:t></w:r></w:p>
            """);

        document.Revisions.RejectAll();

        Assert.Single(document.Paragraphs);
        Assert.Equal("Satu kalimat", document.Paragraphs[0].Text);
    }

    [Fact]
    public void AdjacentDeletedMarksAllJoin()
    {
        // The second mark's revision is pending inside content the first join moves. It stays
        // valid only because the join moves nodes; a join that cloned them would leave it stale.
        using var document = WithBody(
            """
            <w:p><w:pPr><w:rPr><w:del w:id="1" w:author="A" w:date="2026-10-01T00:00:00Z"/></w:rPr></w:pPr><w:r><w:t xml:space="preserve">a </w:t></w:r></w:p>
            <w:p><w:pPr><w:rPr><w:del w:id="2" w:author="A" w:date="2026-10-01T00:00:00Z"/></w:rPr></w:pPr><w:r><w:t xml:space="preserve">b </w:t></w:r></w:p>
            <w:p><w:r><w:t>c</w:t></w:r></w:p>
            """);

        document.Revisions.AcceptAll();

        Assert.Single(document.Paragraphs);
        Assert.Equal("a b c", document.Paragraphs[0].Text);
    }

    [Fact]
    public void ARevisionInsideJoinedContentCanStillBeResolved()
    {
        // Accepting the mark first joins the paragraph's runs, insertion included, into the next
        // paragraph. The insertion's Revision still holds its element; if the join had cloned the
        // content, accepting it would change a detached copy and leave w:ins in the document.
        using var document = WithBody(
            """
            <w:p><w:pPr><w:rPr><w:del w:id="1" w:author="A" w:date="2026-10-01T00:00:00Z"/></w:rPr></w:pPr><w:ins w:id="2" w:author="A" w:date="2026-10-01T00:00:00Z"><w:r><w:t xml:space="preserve">baru </w:t></w:r></w:ins></w:p>
            <w:p><w:r><w:t>lama</w:t></w:r></w:p>
            """);

        var revisions = document.Revisions;
        revisions[0].Accept();
        revisions[1].Accept();

        Assert.Equal("baru lama", document.ExtractText());
        Assert.DoesNotContain(document.Body.Descendants(), e => e.Name == Ns.W + "ins");
    }

    [Fact]
    public void RowRevisionsRemoveTheRightRows()
    {
        const string table =
            """
            <w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="2000"/></w:tblGrid>
            <w:tr><w:tc><w:p><w:r><w:t>tetap</w:t></w:r></w:p></w:tc></w:tr>
            <w:tr><w:trPr><w:ins w:id="1" w:author="A" w:date="2026-10-01T00:00:00Z"/></w:trPr><w:tc><w:p><w:r><w:t>baru</w:t></w:r></w:p></w:tc></w:tr>
            <w:tr><w:trPr><w:del w:id="2" w:author="A" w:date="2026-10-01T00:00:00Z"/></w:trPr><w:tc><w:p><w:r><w:t>lama</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl><w:p/>
            """;

        using (var accepted = WithBody(table))
        {
            accepted.Revisions.AcceptAll();
            Assert.Equal("tetap|baru", string.Join("|", accepted.Tables[0].Rows.Select(r => r.Cells[0].Text)));
            DocxValidator.AssertValid(Save(accepted));
        }

        using (var rejected = WithBody(table))
        {
            rejected.Revisions.RejectAll();
            Assert.Equal("tetap|lama", string.Join("|", rejected.Tables[0].Rows.Select(r => r.Cells[0].Text)));
            DocxValidator.AssertValid(Save(rejected));
        }
    }

    [Fact]
    public void RejectingAFormatChangeRestoresTheOldFormatting()
    {
        using var document = WithBody(
            """
            <w:p><w:pPr><w:jc w:val="center"/><w:rPr><w:b/></w:rPr><w:pPrChange w:id="1" w:author="A" w:date="2026-10-01T00:00:00Z"><w:pPr><w:jc w:val="right"/></w:pPr></w:pPrChange></w:pPr><w:r><w:rPr><w:b/><w:rPrChange w:id="2" w:author="A" w:date="2026-10-01T00:00:00Z"><w:rPr><w:i/></w:rPr></w:rPrChange></w:rPr><w:t>teks</w:t></w:r></w:p>
            """);

        document.Revisions.RejectAll();

        var paragraph = document.Paragraphs[0];
        Assert.Equal(ParagraphAlignment.Right, paragraph.Alignment);

        // The paragraph mark's own run properties are not part of a pPrChange and must survive it.
        Assert.NotNull(paragraph.Element.Element(Ns.W + "pPr")!.Element(Ns.W + "rPr")?.Element(Ns.W + "b"));
        Assert.Null(paragraph.Runs[0].Format.Bold);
        Assert.True(paragraph.Runs[0].Format.Italic);
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void AcceptingAFormatChangeKeepsTheNewFormatting()
    {
        using var document = WithBody(
            """
            <w:p><w:r><w:rPr><w:b/><w:rPrChange w:id="2" w:author="A" w:date="2026-10-01T00:00:00Z"><w:rPr><w:i/></w:rPr></w:rPrChange></w:rPr><w:t>teks</w:t></w:r></w:p>
            """);

        document.Revisions.AcceptAll();

        Assert.True(document.Paragraphs[0].Runs[0].Format.Bold);
        Assert.Null(document.Paragraphs[0].Runs[0].Format.Italic);
    }

    [Fact]
    public void AcceptingAMoveRemovesItsRangeMarkers()
    {
        using var document = WithBody(
            """
            <w:p><w:moveFromRangeStart w:id="1" w:author="A" w:date="2026-10-01T00:00:00Z" w:name="m1"/><w:moveFrom w:id="2" w:author="A" w:date="2026-10-01T00:00:00Z"><w:r><w:delText>pindah</w:delText></w:r></w:moveFrom><w:moveFromRangeEnd w:id="1"/></w:p>
            <w:p><w:moveToRangeStart w:id="3" w:author="A" w:date="2026-10-01T00:00:00Z" w:name="m1"/><w:moveTo w:id="4" w:author="A" w:date="2026-10-01T00:00:00Z"><w:r><w:t>pindah</w:t></w:r></w:moveTo><w:moveToRangeEnd w:id="3"/></w:p>
            """);

        Assert.Equal(2, document.Revisions.Count);
        document.Revisions.AcceptAll();

        Assert.Equal("\npindah", document.ExtractText());
        Assert.DoesNotContain(document.Body.Descendants(), e => e.Name.LocalName.StartsWith("move", StringComparison.Ordinal));
    }

    [Fact]
    public void RevisionsInHeadersAreFound()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("isi");
        var header = document.Section.GetHeader();
        header.AddParagraph("Kop").AddTrackedRun(" draf", "Ani");

        Assert.Single(document.Revisions);
        document.Revisions.RejectAll();
        Assert.Empty(document.Revisions);
    }

    [Fact]
    public void ARevisionCanBeResolvedOnlyOnce()
    {
        using var document = WithBody(Tracked);
        var deletion = document.Revisions[0];

        deletion.Reject();
        Assert.True(deletion.IsResolved);
        deletion.Accept();

        Assert.Equal("Harga lama baru berlaku.", document.ExtractText());
    }

    // ---- Writing revisions ---------------------------------------------------------------------

    [Fact]
    public void TrackedEditsProduceAValidDocumentWordCanReview()
    {
        var when = new DateTimeOffset(2026, 10, 28, 8, 15, 0, TimeSpan.FromHours(7));

        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph("Kontrak berlaku ");
        paragraph.AddTrackedRun("mulai hari ini", "Kang Fadhil", when);
        var doomed = paragraph.AddRun(" kecuali disebut lain.");
        doomed.TrackDeletion("Kang Fadhil", when);

        document.AddParagraph("Pasal yang dihapus.").TrackDeletion("Ani", when);
        document.AddParagraph("Penutup.");
        document.TrackRevisions = true;

        Assert.Equal("Kontrak berlaku mulai hari ini\nPenutup.",
            document.ExtractText().Replace("\n\n", "\n"));

        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        var xml = XDocument.Load(new MemoryStream(Unzip(bytes, "word/document.xml")));
        var insertion = xml.Descendants(Ns.W + "ins").First();
        Assert.Equal("Kang Fadhil", insertion.Attr(Ns.W + "author"));
        Assert.Equal("2026-10-28T01:15:00Z", insertion.Attr(Ns.W + "date"));

        using var reopened = WordDocument.Open(bytes);
        Assert.True(reopened.TrackRevisions);

        var kinds = reopened.Revisions.Select(r => r.Kind).ToList();
        Assert.Equal(RevisionKind.Insertion, kinds[0]);
        Assert.Equal(RevisionKind.Deletion, kinds[1]);
        Assert.Contains(RevisionKind.ParagraphDeleted, kinds);

        // Accepting the deleted paragraph removes it entirely, mark and all.
        reopened.Revisions.AcceptAll();
        Assert.Equal("Kontrak berlaku mulai hari ini\nPenutup.", reopened.ExtractText());
        DocxValidator.AssertValid(Save(reopened));
    }

    [Fact]
    public void NewRevisionIdsDoNotCollideWithExistingOnes()
    {
        // The fixture already uses ids 1 and 2; three new revisions numbered from zero would
        // collide with both.
        using var document = WithBody(Tracked);
        document.Paragraphs[0].AddTrackedRun(" satu", "Citra");
        document.Paragraphs[0].AddTrackedRun(" dua", "Citra");
        document.Paragraphs[0].AddTrackedRun(" tiga", "Citra");

        var ids = document.Revisions.Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void TrackingAParagraphWithOnlyAnInsertionKeepsPropertiesFirst()
    {
        // A paragraph whose only content is w:ins used to get its w:pPr appended after it, because
        // the successor list that places w:pPr did not name w:ins.
        using var document = WordDocument.Create();
        var paragraph = document.AddParagraph();
        paragraph.AddTrackedRun("hanya sisipan", "Ani");
        paragraph.Alignment = ParagraphAlignment.Center;

        DocxValidator.AssertValid(Save(document));
        Assert.Equal(Ns.W + "pPr", paragraph.Element.Elements().First().Name);
    }

    [Fact]
    public void SettingsStayInSchemaOrder()
    {
        // updateFields and evenAndOddHeaders used to be added with AddFirst, ahead of zoom.
        using var document = WordDocument.Create();
        document.AddParagraph("x");
        document.AddTableOfContents();
        document.TrackRevisions = true;

        DocxValidator.AssertValid(Save(document));

        document.TrackRevisions = false;
        Assert.False(document.TrackRevisions);
    }

    [Fact]
    public void DeletingTwiceIsRefused()
    {
        using var document = WordDocument.Create();
        var run = document.AddParagraph().AddRun("x");
        run.TrackDeletion("A");

        Assert.Throws<InvalidOperationException>(() => run.TrackDeletion("A"));
    }

    private static byte[] Unzip(byte[] package, string name)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(package, writable: false));
        using var stream = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
