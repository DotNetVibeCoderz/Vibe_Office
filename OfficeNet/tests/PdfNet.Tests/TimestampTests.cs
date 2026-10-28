// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using OfficeNet.Core;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Security;
using Xunit;

namespace PdfNet.Tests;

/// <summary>
/// RFC 3161 timestamps. The authority here is built in the test — CI has no network to reach a real
/// one — and OpenSSL's own <c>ts -reply</c>, used as the authority by hand, produced a response the
/// signer accepted and a token <c>ts -verify</c> passed.
/// </summary>
public class TimestampTests
{
    private static readonly DateTimeOffset StampedAt = new(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);

    private static X509Certificate2 Certificate(string subject, bool timestamping = false)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        if (timestamping)
        {
            // A timestamp authority's certificate must say so, critically, and say nothing else.
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") }, critical: true));
        }

        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), password: null);
    }

    private static PdfDocument Document()
    {
        var pdf = PdfDocument.Create();

        using var canvas = pdf.Pages.Add(PageSize.A4).OpenCanvas();
        canvas.SetFont(StandardFont.Helvetica, 12);
        canvas.DrawText("Ditandatangani dengan timestamp.", 72, 700);

        return pdf;
    }

    /// <summary>A timestamp authority: answers a request with a token over its hash, at a fixed time.</summary>
    private static Func<byte[], byte[]> Authority(X509Certificate2 tsa, int status = 0, bool wrongHash = false) => requestBytes =>
    {
        Assert.True(Rfc3161TimestampRequest.TryDecode(requestBytes, out var request, out _));

        var hash = request!.GetMessageHash().ToArray();

        if (wrongHash)
        {
            hash[0] ^= 0xFF;
        }

        var info = new Rfc3161TimestampTokenInfo(new Oid("1.2.3.4.1"), request.HashAlgorithmId, hash,
            serialNumber: new byte[] { 0x01, 0x02 }, timestamp: StampedAt, nonce: request.GetNonce());

        var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), info.Encode()));
        var signer = new CmsSigner(tsa) { IncludeOption = X509IncludeOption.EndCertOnly };

        // RFC 5816: the token names its signer's certificate by hash, in signing-certificate-v2.
        var essWriter = new AsnWriter(AsnEncodingRules.DER);
        using (essWriter.PushSequence())
        using (essWriter.PushSequence())
        using (essWriter.PushSequence())
        {
            essWriter.WriteOctetString(SHA256.HashData(tsa.RawData));
        }

        signer.SignedAttributes.Add(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.47"), essWriter.Encode()));
        cms.ComputeSignature(signer);

        // TimeStampResp: the status, then the token when it was granted.
        var response = new AsnWriter(AsnEncodingRules.DER);
        using (response.PushSequence())
        {
            using (response.PushSequence())
            {
                response.WriteInteger(status);
            }

            if (status == 0)
            {
                response.WriteEncodedValue(cms.Encode());
            }
        }

        return response.Encode();
    };

    [Fact]
    public void ATimestampedSignatureCarriesTheAuthoritysTime()
    {
        using var signer = Certificate("CN=Kang Fadhil");
        using var tsa = Certificate("CN=Uji TSA", timestamping: true);
        using var pdf = Document();

        var signed = PdfSigner.Sign(pdf, signer, new PdfSignOptions { TimestampAuthority = Authority(tsa) });
        var verification = Assert.Single(PdfSignatures.VerifyAll(signed));

        Assert.True(verification.IsIntact);
        Assert.Equal(StampedAt, verification.Timestamp);
        Assert.Equal("CN=Uji TSA", verification.TimestampCertificate?.Subject);
        Assert.Null(verification.TimestampProblem);
    }

    [Fact]
    public void ASignatureWithoutATimestampSaysNothingAboutOne()
    {
        using var signer = Certificate("CN=Kang Fadhil");
        using var pdf = Document();

        var verification = Assert.Single(PdfSignatures.VerifyAll(PdfSigner.Sign(pdf, signer)));

        Assert.Null(verification.Timestamp);
        Assert.Null(verification.TimestampProblem);
    }

    [Fact]
    public void AResponseThatDoesNotAnswerTheRequestIsRefused()
    {
        using var signer = Certificate("CN=Kang Fadhil");
        using var tsa = Certificate("CN=Uji TSA", timestamping: true);

        using (var pdf = Document())
        {
            var wrong = Assert.Throws<OfficeNetException>(() =>
                PdfSigner.Sign(pdf, signer, new PdfSignOptions { TimestampAuthority = Authority(tsa, wrongHash: true) }));
            Assert.Contains("does not answer", wrong.Message, StringComparison.Ordinal);
        }

        using (var pdf = Document())
        {
            // PKIStatus 2: rejection.
            Assert.Throws<OfficeNetException>(() =>
                PdfSigner.Sign(pdf, signer, new PdfSignOptions { TimestampAuthority = Authority(tsa, status: 2) }));
        }

        using (var pdf = Document())
        {
            var offline = Assert.Throws<OfficeNetException>(() =>
                PdfSigner.Sign(pdf, signer, new PdfSignOptions { TimestampAuthority = _ => throw new HttpRequestException("tidak ada jaringan") }));
            Assert.Contains("could not be reached", offline.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ATokenTakenFromAnotherSignatureIsReportedAndTheSignatureStillHolds()
    {
        using var signer = Certificate("CN=Kang Fadhil");
        using var tsa = Certificate("CN=Uji TSA", timestamping: true);

        byte[] first;
        byte[] second;

        using (var pdf = Document())
        {
            first = PdfSigner.Sign(pdf, signer, new PdfSignOptions { TimestampAuthority = Authority(tsa) });
        }

        using (var pdf = Document())
        {
            second = PdfSigner.Sign(pdf, signer, new PdfSignOptions { TimestampAuthority = Authority(tsa) });
        }

        // Move the second signature's token into the first. The token sits outside the bytes the
        // signature covers, so this is exactly the substitution a verifier has to catch.
        var original = Cms(first);
        var other = new SignedCms();
        other.Decode(Cms(second));
        var foreign = other.SignerInfos[0].UnsignedAttributes[0];

        var cms = new SignedCms();
        cms.Decode(original);
        cms.SignerInfos[0].RemoveUnsignedAttribute(cms.SignerInfos[0].UnsignedAttributes[0].Values[0]);
        cms.SignerInfos[0].AddUnsignedAttribute(foreign.Values[0]);

        // The whole reserved hole, from the signature's first hex digit to the closing '>'. The
        // re-encoded signature can be a byte longer than the original — a nonce's DER length varies
        // with its leading bits — so it is padded to the hole, not to the old signature.
        var forged = (byte[])first.Clone();
        var at = forged.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(original)));
        Assert.True(at > 0);
        var hole = forged.AsSpan(at).IndexOf((byte)'>');

        var replacement = Convert.ToHexString(cms.Encode()).PadRight(hole, '0');
        Assert.True(replacement.Length == hole, "the forged signature must fit the same hole");
        System.Text.Encoding.ASCII.GetBytes(replacement).CopyTo(forged, at);

        var verification = Assert.Single(PdfSignatures.VerifyAll(forged));

        Assert.True(verification.IsIntact);
        Assert.Null(verification.Timestamp);
        Assert.NotNull(verification.TimestampProblem);

        static byte[] Cms(byte[] file)
        {
            using var document = PdfDocument.Open(new MemoryStream(file, writable: false));
            var contents = Assert.Single(PdfSignatures.Find(document)).Contents;
            AsnDecoder.ReadEncodedValue(contents, AsnEncodingRules.BER, out _, out _, out var length);
            return contents[..length];
        }
    }

    [Fact]
    public void ATimestampMakesRoomForItselfUnlessTheCallerSaidHowMuch()
    {
        using var signer = Certificate("CN=Kang Fadhil");
        using var tsa = Certificate("CN=Uji TSA", timestamping: true);

        Assert.Equal(16384, Hole(new PdfSignOptions { TimestampAuthority = Authority(tsa) }));
        Assert.Equal(8192, Hole(new PdfSignOptions()));
        Assert.Equal(30000, Hole(new PdfSignOptions { TimestampAuthority = Authority(tsa), ReservedBytes = 30000 }));

        int Hole(PdfSignOptions options)
        {
            using var pdf = Document();
            using var signed = PdfDocument.Open(new MemoryStream(PdfSigner.Sign(pdf, signer, options), writable: false));
            return Assert.Single(PdfSignatures.Find(signed)).Contents.Length;
        }
    }
}
