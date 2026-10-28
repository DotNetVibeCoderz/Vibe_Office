// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Security.Cryptography;
using System.Xml.Linq;
using OfficeNet.Core.Xml;
using WordNet.Protection;
using Xunit;

namespace WordNet.Tests;

public class ProtectionTests
{
    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static XElement Settings(byte[] docx)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(docx, writable: false));
        using var stream = archive.GetEntry("word/settings.xml")!.Open();
        return XDocument.Load(stream).Root!;
    }

    [Fact]
    public void TheLegacyKeyMatchesTheSpecificationsExample()
    {
        // ECMA-376 Part 4 §2.15.1.28 works this one through: "Example" gives 0x64CEED7E, and the
        // reversed byte order hashed afterwards is 7EEDCE64. This is the stage that is easy to get
        // wrong, and getting it wrong is invisible until Word refuses the right password.
        Assert.Equal(0x64CEED7Eu, DocumentProtection.LegacyKey("Example"));
        Assert.Equal("7EEDCE64", DocumentProtection.LegacyKeyReversedHex("Example"));
    }

    [Fact]
    public void OnlyTheFirstFifteenCharactersCount()
    {
        Assert.Equal(DocumentProtection.LegacyKey("123456789012345"),
            DocumentProtection.LegacyKey("123456789012345 and the rest"));
        Assert.Equal(0u, DocumentProtection.LegacyKey(""));
    }

    [Fact]
    public void TheIteratedHashMatchesAnIndependentComputation()
    {
        // Computed by tools/word_protection_hash.py, written from the spec rather than from this
        // code: salt 00..0F, password "Rahasia", 100000 rounds of SHA-512.
        var salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var hash = DocumentProtection.Hash("Rahasia", salt, 100_000, HashAlgorithmName.SHA512);

        Assert.Equal(ExpectedRahasiaHash, Convert.ToBase64String(hash));
    }

    internal const string ExpectedRahasiaHash = "7OeCazxxyt4B+1GImDPUN5akD3k2wbn+UjV3UOuBeC6FeEJ4lHnOowW0etFe2WT+FGJtEMGYJ1D9BU47YCWrJg==";

    [Fact]
    public void ProtectingWritesAnEnforcedRestrictionInSchemaOrder()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Kontrak");
        document.AddTableOfContents();
        document.Protect(DocumentEditRestriction.ReadOnly, "rahasia");

        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        var protection = Settings(bytes).Element(Ns.W + "documentProtection")!;
        Assert.Equal("readOnly", protection.Attr(Ns.W + "edit"));
        Assert.Equal("1", protection.Attr(Ns.W + "enforcement"));
        Assert.Equal("14", protection.Attr(Ns.W + "cryptAlgorithmSid"));
        Assert.Equal(16, Convert.FromBase64String(protection.Attr(Ns.W + "salt")!).Length);
        Assert.Equal(64, Convert.FromBase64String(protection.Attr(Ns.W + "hash")!).Length);
    }

    [Fact]
    public void ThePasswordVerifiesAfterAReopen()
    {
        using var document = WordDocument.Create();
        document.Protect(DocumentEditRestriction.Comments, "rahasia");

        using var reopened = WordDocument.Open(Save(document));
        var protection = reopened.Protection!;

        Assert.Equal(DocumentEditRestriction.Comments, protection.Restriction);
        Assert.True(protection.IsEnforced);
        Assert.True(protection.HasPassword);
        Assert.True(protection.VerifyPassword("rahasia"));
        Assert.False(protection.VerifyPassword("Rahasia"));
    }

    [Fact]
    public void AnUnknownSchemeIsNotReportedAsAMismatch()
    {
        using var document = WordDocument.Create();
        document.Protect(DocumentEditRestriction.ReadOnly, "x");
        var element = document.DocumentPart
            .RelatedPartByType(OfficeNet.Core.Packaging.RelationshipTypes.Settings)!
            .Xml.Root!.Element(Ns.W + "documentProtection")!;
        element.SetAttributeValue(Ns.W + "cryptAlgorithmSid", "3");   // MD5

        Assert.Null(document.Protection!.VerifyPassword("x"));
    }

    [Fact]
    public void ProtectingWithoutAPasswordWritesNoHash()
    {
        using var document = WordDocument.Create();
        document.Protect(DocumentEditRestriction.Forms);

        Assert.False(document.Protection!.HasPassword);
        Assert.Null(document.Protection.VerifyPassword("apa saja"));
    }

    [Fact]
    public void TrackedChangesProtectionTurnsTrackingOn()
    {
        using var document = WordDocument.Create();
        document.Protect(DocumentEditRestriction.TrackedChanges);

        Assert.True(document.TrackRevisions);
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void UnprotectRemovesTheRestriction()
    {
        using var document = WordDocument.Create();
        document.Protect(DocumentEditRestriction.ReadOnly, "x");
        document.Unprotect();

        Assert.Null(document.Protection);
        Assert.Null(Settings(Save(document)).Element(Ns.W + "documentProtection"));
    }

    [Fact]
    public void EditableRangesBracketTheirParagraphs()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Pasal 1 — tetap.");
        var name = document.AddParagraph("Nama: ");
        name.Alignment = ParagraphAlignment.Left;
        var address = document.AddParagraph("Alamat: ");
        var note = document.AddParagraph("Catatan khusus");

        document.AllowEditing(name, address);
        document.AllowEditing(note, editor: "ani@example.org");
        document.Protect(DocumentEditRestriction.ReadOnly, "x");

        var bytes = Save(document);
        DocxValidator.AssertValid(bytes);

        using var reopened = WordDocument.Open(bytes);
        var ranges = reopened.EditableRanges;
        Assert.Equal(2, ranges.Count);
        Assert.Null(ranges[0].Editor);
        Assert.Equal("ani@example.org", ranges[1].Editor);
        Assert.NotEqual(ranges[0].Id, ranges[1].Id);

        // pPr stays first; the start marker follows it; the end closes the last paragraph.
        var body = reopened.Body;
        var paragraphs = body.Elements(Ns.W + "p").ToList();
        Assert.Equal(Ns.W + "pPr", paragraphs[1].Elements().First().Name);
        Assert.Equal(Ns.W + "permStart", paragraphs[1].Elements().ElementAt(1).Name);
        Assert.Equal(Ns.W + "permEnd", paragraphs[2].Elements().Last().Name);

        // The markers carry no text.
        Assert.Equal("Nama: ", reopened.Paragraphs[1].Text);
    }
}
