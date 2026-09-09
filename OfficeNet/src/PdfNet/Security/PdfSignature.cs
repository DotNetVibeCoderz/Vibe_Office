// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using OfficeNet.Core;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Security;

/// <summary>What checking a signature found.</summary>
/// <remarks>
/// The three questions a reader actually asks, kept apart because they have different answers and
/// conflating them is how a tool ends up reporting a tampered document as valid.
/// </remarks>
public sealed class SignatureVerification
{
    internal SignatureVerification(PdfSignature signature)
    {
        Signature = signature;
    }

    /// <summary>The signature this describes.</summary>
    public PdfSignature Signature { get; }

    /// <summary>
    /// Whether the signed bytes still hash to what the signature says.
    /// </summary>
    /// <remarks>
    /// This is the cryptographic question and the only one with a definite answer. False means the
    /// bytes changed after signing, or the signature was never over these bytes.
    /// </remarks>
    public bool DigestMatches { get; internal set; }

    /// <summary>
    /// Whether the signature covers the whole file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A signature can be cryptographically perfect and still not protect the document.</b> The
    /// <c>/ByteRange</c> says which parts of the file were hashed, and nothing forces it to cover
    /// all of them: a file can carry a valid signature over its first half and arbitrary unsigned
    /// content after it. That is a real attack, not a hypothetical, and it is why this is a separate
    /// answer from <see cref="DigestMatches"/>.
    /// </para>
    /// <para>
    /// A false here with a true above means: the signature is genuine, and it does not vouch for
    /// everything you are looking at.
    /// </para>
    /// </remarks>
    public bool CoversWholeDocument { get; internal set; }

    /// <summary>The certificate that signed, when one could be read.</summary>
    public X509Certificate2? Certificate { get; internal set; }

    /// <summary>Why the check failed, or <c>null</c> when it did not.</summary>
    public string? Problem { get; internal set; }

    /// <summary>
    /// Whether the signature is intact and covers everything.
    /// </summary>
    /// <remarks>
    /// This does <em>not</em> say the signer is trustworthy. Whether a certificate chains to an
    /// authority you accept, has expired, or has been revoked is a policy question with a different
    /// answer for every organisation, and it is answered with <see cref="Certificate"/> and
    /// <see cref="X509Chain"/> rather than guessed at here.
    /// </remarks>
    public bool IsIntact => DigestMatches && CoversWholeDocument;

    public override string ToString() =>
        Problem is not null ? $"invalid: {Problem}"
        : IsIntact ? $"intact, signed by {Certificate?.Subject ?? "unknown"}"
        : "intact but does not cover the whole document";
}

/// <summary>
/// A digital signature in a document.
/// </summary>
/// <remarks>
/// <para>
/// A signature lives in a form field whose <c>/V</c> is a signature dictionary. The dictionary holds
/// the signer's name and reason as plain text — which anyone can write and which proves nothing —
/// and two entries that matter: <c>/ByteRange</c>, naming the parts of the file that were hashed,
/// and <c>/Contents</c>, holding a detached CMS signature over that hash.
/// </para>
/// <para>
/// The <c>/Contents</c> string sits in a hole in the middle of the file, and the byte range is
/// exactly "everything before the hole" plus "everything after it". A signature cannot cover itself,
/// which is why the arrangement looks so odd.
/// </para>
/// </remarks>
public sealed class PdfSignature
{
    /// <summary>Wraps a signature dictionary.</summary>
    /// <param name="dictionary">The signature dictionary — a field's <c>/V</c>.</param>
    /// <param name="fieldName">The name of the field holding it.</param>
    /// <remarks>
    /// Public because <see cref="PdfSignatures.Find"/> is not the only way to reach one: a document
    /// with an unusual field tree, or one being inspected object by object, gives you the dictionary
    /// and nothing to wrap it with otherwise.
    /// </remarks>
    public PdfSignature(PdfDictionary dictionary, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(fieldName);

        Dictionary = dictionary;
        FieldName = fieldName;
    }

    /// <summary>The signature dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>The name of the form field holding it.</summary>
    public string FieldName { get; }

    /// <summary>The name the signer typed. Not evidence of anything.</summary>
    public string? Name => Dictionary.GetText(PdfName.Get("Name"));

    /// <summary>The reason given for signing.</summary>
    public string? Reason => Dictionary.GetText(PdfName.Get("Reason"));

    /// <summary>Where the signer said they were.</summary>
    public string? Location => Dictionary.GetText(PdfName.Get("Location"));

    /// <summary>Contact details the signer gave.</summary>
    public string? ContactInfo => Dictionary.GetText(PdfName.Get("ContactInfo"));

    /// <summary>
    /// When the signer's own clock said it was signed.
    /// </summary>
    /// <remarks>
    /// Written by the signer, so it is a claim rather than a fact. A trusted timestamp is a separate
    /// thing, carried inside the CMS as an unsigned attribute, and is not read here.
    /// </remarks>
    public DateTime? SignedAt
    {
        get
        {
            var raw = Dictionary.GetText(PdfName.Get("M"));

            return raw is not null && PdfDates.TryParse(raw, out var value) ? value : null;
        }
    }

    /// <summary>The handler that produced it, for example <c>Adobe.PPKLite</c>.</summary>
    public string? Filter => Dictionary.GetName(PdfName.Get("Filter"));

    /// <summary>The signature format, for example <c>adbe.pkcs7.detached</c>.</summary>
    public string? SubFilter => Dictionary.GetName(PdfName.Get("SubFilter"));

    /// <summary>The byte ranges of the file that were hashed, as offset/length pairs.</summary>
    public IReadOnlyList<(int Offset, int Length)> ByteRange
    {
        get
        {
            if (Dictionary.Get(PdfName.Get("ByteRange")) is not PdfArray array)
            {
                return [];
            }

            var ranges = new List<(int, int)>();

            for (var i = 0; i + 1 < array.Count; i += 2)
            {
                if (array[i] is PdfNumber offset && array[i + 1] is PdfNumber length)
                {
                    ranges.Add(((int)offset.IntValue, (int)length.IntValue));
                }
            }

            return ranges;
        }
    }

    /// <summary>The detached CMS signature.</summary>
    public byte[] Contents =>
        Dictionary.Get(PdfName.Get("Contents")) is PdfString contents ? contents.Value : [];

    public override string ToString() =>
        $"{FieldName}: {Name ?? "unnamed"}{(SignedAt is { } when ? $" at {when:u}" : string.Empty)}";
}

/// <summary>
/// Finds and checks the digital signatures in a document.
/// </summary>
/// <remarks>
/// <para>
/// Verification needs the file's bytes, not the parsed object graph: the signature is over a byte
/// range of the file as it was saved, and reserialising the objects would produce different bytes
/// that hash to something else. So every method here takes the file.
/// </para>
/// <para>
/// <b>What this answers and what it does not.</b> It answers whether the bytes changed since signing
/// and whether the signature covers all of them. It does <em>not</em> answer whether the signer
/// should be trusted: chain validation, expiry and revocation are policy, they need a trust store
/// and usually a network, and every organisation answers them differently. The certificate is handed
/// back so that question can be asked properly with <see cref="X509Chain"/>.
/// </para>
/// </remarks>
public static class PdfSignatures
{
    /// <summary>Every signature in a document.</summary>
    public static IReadOnlyList<PdfSignature> Find(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var form = document.Catalog.Get(PdfName.Get("AcroForm"));

        if (document.Follow(form) is not PdfDictionary acroForm ||
            document.Follow(acroForm.Get(PdfName.Get("Fields"))) is not PdfArray fields)
        {
            return [];
        }

        var found = new List<PdfSignature>();

        Walk(document, fields, string.Empty, found);

        return found;
    }

    /// <summary>Walks the field tree, which can nest, gathering signature fields.</summary>
    private static void Walk(PdfDocument document, PdfArray fields, string prefix,
        List<PdfSignature> found)
    {
        foreach (var entry in fields)
        {
            if (document.Follow(entry) is not PdfDictionary field)
            {
                continue;
            }

            var partial = field.GetText(PdfName.Get("T")) ?? string.Empty;
            var name = prefix.Length == 0 ? partial : $"{prefix}.{partial}";

            if (document.Follow(field.Get(PdfName.Get("Kids"))) is PdfArray kids)
            {
                Walk(document, kids, name, found);
            }

            if (field.GetName(PdfName.Get("FT")) == "Sig" &&
                document.Follow(field.Get(PdfName.Get("V"))) is PdfDictionary value &&
                value.ContainsKey(PdfName.Get("Contents")))
            {
                found.Add(new PdfSignature(value, name));
            }
        }
    }

    /// <summary>Checks a signature against the file it was made over.</summary>
    /// <param name="signature">The signature to check.</param>
    /// <param name="fileBytes">The file exactly as it was saved.</param>
    public static SignatureVerification Verify(PdfSignature signature, byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(fileBytes);

        var result = new SignatureVerification(signature);
        var ranges = signature.ByteRange;

        if (ranges.Count == 0)
        {
            result.Problem = "The signature has no /ByteRange, so there is nothing to check it over.";
            return result;
        }

        foreach (var (offset, length) in ranges)
        {
            if (offset < 0 || length < 0 || (long)offset + length > fileBytes.Length)
            {
                result.Problem =
                    $"The /ByteRange names bytes {offset}..{offset + length} of a {fileBytes.Length}-byte " +
                    "file, so it does not describe this file at all.";

                return result;
            }
        }

        result.CoversWholeDocument = CoversEverything(ranges, fileBytes.Length);

        var signed = Gather(ranges, fileBytes);

        try
        {
            var cms = new SignedCms();

            // The signature is detached: the CMS holds no content of its own, and the bytes it
            // covers are supplied here. Decoding first and then attaching is the order the API
            // wants; doing it the other way round silently verifies an empty document.
            cms.Decode(Trim(signature.Contents));

            var detached = new SignedCms(new ContentInfo(signed), detached: true);
            detached.Decode(Trim(signature.Contents));

            // No chain building: that is a policy question this class deliberately does not answer.
            // The check here is that the bytes hash to what the signer signed.
            detached.CheckSignature(verifySignatureOnly: true);

            result.DigestMatches = true;
            result.Certificate = detached.SignerInfos.Count > 0
                ? detached.SignerInfos[0].Certificate
                : null;
        }
        catch (CryptographicException ex)
        {
            result.DigestMatches = false;
            result.Problem = ex.Message;
        }

        return result;
    }

    /// <summary>Checks every signature in a file.</summary>
    public static IReadOnlyList<SignatureVerification> VerifyAll(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);

        using var document = PdfDocument.Open(new MemoryStream(fileBytes, writable: false));

        return [.. Find(document).Select(signature => Verify(signature, fileBytes))];
    }

    /// <summary>Checks every signature in a file on disk.</summary>
    public static IReadOnlyList<SignatureVerification> VerifyAll(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return VerifyAll(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Whether the byte ranges leave nothing out.
    /// </summary>
    /// <remarks>
    /// The hole between the ranges is where <c>/Contents</c> sits, and that is the only gap allowed.
    /// Anything after the last range is unsigned content appended to a signed file — the attack this
    /// exists to catch. A few trailing bytes of whitespace after <c>%%EOF</c> are tolerated because
    /// some writers add them and they cannot carry meaning.
    /// </remarks>
    private static bool CoversEverything(IReadOnlyList<(int Offset, int Length)> ranges, int fileLength)
    {
        if (ranges[0].Offset != 0)
        {
            return false;
        }

        var end = ranges[^1].Offset + ranges[^1].Length;

        return fileLength - end <= 2;
    }

    private static byte[] Gather(IReadOnlyList<(int Offset, int Length)> ranges, byte[] file)
    {
        var total = ranges.Sum(r => r.Length);
        var buffer = new byte[total];
        var cursor = 0;

        foreach (var (offset, length) in ranges)
        {
            Array.Copy(file, offset, buffer, cursor, length);
            cursor += length;
        }

        return buffer;
    }

    /// <summary>
    /// Removes the zero padding from a <c>/Contents</c> string.
    /// </summary>
    /// <remarks>
    /// The hole is reserved before the signature exists, so it is always larger than the signature
    /// that goes into it, and the remainder is zeros. A DER parser reads the structure's own length
    /// and stops, but some reject trailing bytes, so they are cut here.
    /// </remarks>
    private static byte[] Trim(byte[] contents)
    {
        var end = contents.Length;

        while (end > 0 && contents[end - 1] == 0)
        {
            end--;
        }

        return end == contents.Length ? contents : contents[..end];
    }
}

/// <summary>Reads and writes PDF's own date format.</summary>
/// <remarks>
/// <c>D:YYYYMMDDHHmmSSOHH'mm</c>, where the offset uses an apostrophe and every field after the year
/// is optional. Parsing it with <see cref="DateTime.TryParse(string, out DateTime)"/> fails on the
/// prefix and on the apostrophe, which is why this exists.
/// </remarks>
internal static class PdfDates
{
    internal static bool TryParse(string text, out DateTime value)
    {
        value = default;

        var trimmed = text.Trim();

        if (trimmed.StartsWith("D:", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        var digits = new string([.. trimmed.TakeWhile(char.IsDigit)]);

        if (digits.Length < 4)
        {
            return false;
        }

        // Every field after the year is optional, and the defaults are not all zero: an omitted
        // month or day is 01, because there is no month zero. Padding the whole thing with zeros —
        // the obvious thing to do — turns "D:2026" into the zeroth day of the zeroth month.
        var padded = digits.Length > 14 ? digits[..14] : digits;

        padded = padded.Length switch
        {
            4 => padded + "0101000000",
            6 => padded + "01000000",
            8 => padded + "000000",
            10 => padded + "0000",
            12 => padded + "00",
            _ => padded.PadRight(14, '0'),
        };

        return DateTime.TryParseExact(padded, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out value);
    }

    internal static string Format(DateTime value)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(value);
        var sign = offset < TimeSpan.Zero ? '-' : '+';

        return string.Create(CultureInfo.InvariantCulture,
            $"D:{value:yyyyMMddHHmmss}{sign}{Math.Abs(offset.Hours):00}'{Math.Abs(offset.Minutes):00}'");
    }
}
