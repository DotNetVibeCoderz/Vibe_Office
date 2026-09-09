// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OfficeNet.Core;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Security;
using Xunit;

namespace PdfNet.Tests;

public class SignatureTests
{
    /// <summary>
    /// A self-signed certificate, made here rather than shipped.
    /// </summary>
    /// <remarks>
    /// A checked-in .pfx would expire, and a certificate from the machine's store would make the
    /// test depend on what happens to be installed. Generating one costs a few milliseconds and the
    /// test then says exactly what it means.
    /// </remarks>
    private static X509Certificate2 Certificate(string subject = "CN=Kang Fadhil, O=Gravicode Studios")
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        // Round-tripped through PKCS#12 so the private key is usable for signing on every platform;
        // a certificate straight out of CreateSelfSigned is not, on Windows.
        var exported = certificate.Export(X509ContentType.Pkcs12);

        return X509CertificateLoader.LoadPkcs12(exported, password: null);
    }

    private static PdfDocument Document(string text = "Laporan yang akan ditandatangani.")
    {
        var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            canvas.SetFont(StandardFont.Helvetica, 12);
            canvas.DrawText(text, 72, 700);
        }

        return pdf;
    }

    [Fact]
    public void ASignedDocumentVerifies()
    {
        using var certificate = Certificate();
        using var pdf = Document();

        var signed = PdfSigner.Sign(pdf, certificate, new PdfSignOptions
        {
            Reason = "Disetujui",
            Location = "Jakarta",
        });

        var verification = Assert.Single(PdfSignatures.VerifyAll(signed));

        Assert.Null(verification.Problem);
        Assert.True(verification.DigestMatches);
        Assert.True(verification.CoversWholeDocument);
        Assert.True(verification.IsIntact);
    }

    [Fact]
    public void TheSignatureCarriesWhatTheSignerSaid()
    {
        using var certificate = Certificate();
        using var pdf = Document();

        var when = new DateTime(2026, 3, 17, 9, 30, 0, DateTimeKind.Local);

        var signed = PdfSigner.Sign(pdf, certificate, new PdfSignOptions
        {
            Name = "Kang Fadhil",
            Reason = "Disetujui",
            Location = "Jakarta",
            ContactInfo = "gravicode@example.com",
            SignedAt = when,
        });

        using var reopened = PdfDocument.Open(new MemoryStream(signed, writable: false));
        var signature = Assert.Single(PdfSignatures.Find(reopened));

        Assert.Equal("Kang Fadhil", signature.Name);
        Assert.Equal("Disetujui", signature.Reason);
        Assert.Equal("Jakarta", signature.Location);
        Assert.Equal("gravicode@example.com", signature.ContactInfo);
        Assert.Equal(when, signature.SignedAt);
        Assert.Equal("adbe.pkcs7.detached", signature.SubFilter);
        Assert.Equal("Signature1", signature.FieldName);
    }

    [Fact]
    public void TheSignersCertificateComesBack()
    {
        using var certificate = Certificate("CN=Gravicode Studios");
        using var pdf = Document();

        var signed = PdfSigner.Sign(pdf, certificate, new PdfSignOptions());
        var verification = Assert.Single(PdfSignatures.VerifyAll(signed));

        Assert.NotNull(verification.Certificate);
        Assert.Equal(certificate.Thumbprint, verification.Certificate.Thumbprint);
    }

    [Fact]
    public void ChangingOneByteBreaksTheSignature()
    {
        // The whole point. If this passes with a tampered file, the verification is decorative.
        using var certificate = Certificate();
        using var pdf = Document();

        var signed = PdfSigner.Sign(pdf, certificate, new PdfSignOptions());

        // A byte inside the signed range, well away from the signature itself.
        var tampered = (byte[])signed.Clone();
        tampered[200] = (byte)(tampered[200] ^ 0xFF);

        var verification = Assert.Single(PdfSignatures.VerifyAll(tampered));

        Assert.False(verification.DigestMatches);
        Assert.False(verification.IsIntact);
    }

    [Fact]
    public void ContentAppendedAfterSigningIsReportedAsUncovered()
    {
        // A signature can be cryptographically perfect and still not vouch for the whole file. The
        // ByteRange says what it covers, and nothing forces that to be everything — appending
        // unsigned content after a signed file is a real attack, and it is why "the digest matches"
        // and "it covers the document" are two separate answers.
        using var certificate = Certificate();
        using var pdf = Document();

        var signed = PdfSigner.Sign(pdf, certificate, new PdfSignOptions());

        var appended = new byte[signed.Length + 64];
        signed.CopyTo(appended, 0);
        "% appended after signing"u8.ToArray().CopyTo(appended, signed.Length);

        var verification = Assert.Single(PdfSignatures.VerifyAll(appended));

        // The cryptography is still fine — those bytes were never part of the hash.
        Assert.True(verification.DigestMatches);

        // And that is exactly why the second question has to be asked.
        Assert.False(verification.CoversWholeDocument);
        Assert.False(verification.IsIntact);
    }

    [Fact]
    public void TheByteRangeCoversEverythingExceptTheSignatureItself()
    {
        using var certificate = Certificate();
        using var pdf = Document();

        var signed = PdfSigner.Sign(pdf, certificate, new PdfSignOptions());

        using var reopened = PdfDocument.Open(new MemoryStream(signed, writable: false));
        var signature = Assert.Single(PdfSignatures.Find(reopened));

        var ranges = signature.ByteRange;

        Assert.Equal(2, ranges.Count);
        Assert.Equal(0, ranges[0].Offset);

        // The gap between them is the /Contents hole, and the second range runs to the end.
        var gapStart = ranges[0].Offset + ranges[0].Length;

        Assert.True(ranges[1].Offset > gapStart, "There is no hole for the signature to sit in.");
        Assert.Equal(signed.Length, ranges[1].Offset + ranges[1].Length);
    }

    [Fact]
    public void ACertificateWithNoPrivateKeyIsRefusedWithTheReason()
    {
        using var full = Certificate();
        using var publicOnly = X509CertificateLoader.LoadCertificate(full.Export(X509ContentType.Cert));
        using var pdf = Document();

        var exception = Assert.Throws<OfficeNetException>(() =>
            PdfSigner.Sign(pdf, publicOnly, new PdfSignOptions()));

        Assert.Contains("no private key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASignatureThatDoesNotFitIsRefusedRatherThanTruncated()
    {
        // A truncated signature produces a file that looks signed and is not, which is worse than
        // an exception at the call site by a wide margin.
        using var certificate = Certificate();
        using var pdf = Document();

        var exception = Assert.Throws<OfficeNetException>(() =>
            PdfSigner.Sign(pdf, certificate, new PdfSignOptions { ReservedBytes = 1024 }));

        Assert.Contains("ReservedBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnsignedDocumentHasNoSignatures()
    {
        using var pdf = Document();

        Assert.Empty(PdfSignatures.Find(pdf));
    }

    [Fact]
    public void TheDocumentIsStillReadableAfterSigning()
    {
        // Signing must not damage what it signs: the splice writes into a reserved hole and nowhere
        // else, and the file has to parse and extract exactly as before.
        using var certificate = Certificate();
        using var pdf = Document("Pendapatan naik 32 persen.");

        var signed = PdfSigner.Sign(pdf, certificate, new PdfSignOptions());

        using var reopened = PdfDocument.Open(new MemoryStream(signed, writable: false));

        Assert.Single(reopened.Pages);
        Assert.Contains("Pendapatan naik 32 persen.",
            PdfNet.Text.TextExtractor.Extract(reopened.Pages[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFormIsMarkedAsHoldingASignature()
    {
        // Without /SigFlags a reader may not offer to check the signature at all.
        using var certificate = Certificate();
        using var pdf = Document();

        var signed = PdfSigner.Sign(pdf, certificate, new PdfSignOptions());

        using var reopened = PdfDocument.Open(new MemoryStream(signed, writable: false));

        var form = reopened.Follow(reopened.Catalog.Get(Objects.PdfName.Get("AcroForm")))
            as Objects.PdfDictionary;

        Assert.NotNull(form);
        Assert.Equal(3, form.GetInt(Objects.PdfName.Get("SigFlags")));
    }

    [Theory]
    // The full form, as Acrobat writes it.
    [InlineData("D:20260317093000+07'00'", 2026, 3, 17, 9, 30, 0)]
    // Every field after the year is optional, and the prefix is too.
    [InlineData("D:20260317093000", 2026, 3, 17, 9, 30, 0)]
    [InlineData("D:2026031709", 2026, 3, 17, 9, 0, 0)]
    [InlineData("D:202603", 2026, 3, 1, 0, 0, 0)]
    [InlineData("20260317", 2026, 3, 17, 0, 0, 0)]
    [InlineData("D:2026", 2026, 1, 1, 0, 0, 0)]
    public void PdfDatesAreParsedIncludingTheOddOnes(string text, int year, int month, int day,
        int hour, int minute, int second)
    {
        // D:YYYYMMDDHHmmSSOHH'mm. DateTime.TryParse fails on the prefix and on the apostrophe,
        // and a file from another producer may carry any of these forms.
        var dictionary = new Objects.PdfDictionary();
        dictionary[Objects.PdfName.Get("M")] = new Objects.PdfString(text);
        dictionary[Objects.PdfName.Get("Contents")] = new Objects.PdfString(new byte[8]);

        var signature = new PdfSignature(dictionary, "Signature1");

        Assert.Equal(new DateTime(year, month, day, hour, minute, second), signature.SignedAt);
    }

    [Fact]
    public void ADateThisCannotReadComesBackAsNothingRatherThanAGuess()
    {
        var dictionary = new Objects.PdfDictionary();
        dictionary[Objects.PdfName.Get("M")] = new Objects.PdfString("beberapa waktu lalu");

        Assert.Null(new PdfSignature(dictionary, "Signature1").SignedAt);
    }

    [Fact]
    public void TheDateWrittenIsTheDateReadBack()
    {
        using var certificate = Certificate();
        using var pdf = Document();

        var when = new DateTime(2026, 3, 17, 9, 30, 0, DateTimeKind.Local);
        var signed = PdfSigner.Sign(pdf, certificate, new PdfSignOptions { SignedAt = when });

        using var reopened = PdfDocument.Open(new MemoryStream(signed, writable: false));

        Assert.Equal(when, Assert.Single(PdfSignatures.Find(reopened)).SignedAt);
    }

    [Fact]
    public void TwoSignaturesOnOneDocumentAreBothFound()
    {
        // Signing twice through this API produces two fields. The first signature no longer covers
        // the whole file once the second was added, which is the honest report rather than a
        // failure: that is what an incremental signature looks like.
        using var certificate = Certificate();
        using var pdf = Document();

        var once = PdfSigner.Sign(pdf, certificate, new PdfSignOptions { FieldName = "Signature1" });

        using var reopened = PdfDocument.Open(new MemoryStream(once, writable: false));
        var twice = PdfSigner.Sign(reopened, certificate,
            new PdfSignOptions { FieldName = "Signature2" });

        using var final = PdfDocument.Open(new MemoryStream(twice, writable: false));

        Assert.Equal(2, PdfSignatures.Find(final).Count);
    }
}
