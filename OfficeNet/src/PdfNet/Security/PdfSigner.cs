// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using OfficeNet.Core;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Security;

/// <summary>What goes into a signature besides the cryptography.</summary>
public sealed class PdfSignOptions
{
    /// <summary>The name shown as the signer. Defaults to the certificate's subject.</summary>
    public string? Name { get; set; }

    /// <summary>Why the document was signed.</summary>
    public string? Reason { get; set; }

    /// <summary>Where the signer was.</summary>
    public string? Location { get; set; }

    /// <summary>How to reach the signer.</summary>
    public string? ContactInfo { get; set; }

    /// <summary>When it was signed. Defaults to now.</summary>
    public DateTime? SignedAt { get; set; }

    /// <summary>The name of the form field to create.</summary>
    public string FieldName { get; set; } = "Signature1";

    /// <summary>The digest to sign with.</summary>
    /// <remarks>
    /// SHA-256 by default. SHA-1 is not offered: it has been unsafe for signatures since 2017, and
    /// a library that made it available as an option would see it used.
    /// </remarks>
    public HashAlgorithmName DigestAlgorithm { get; set; } = HashAlgorithmName.SHA256;

    /// <summary>
    /// How many bytes to reserve for the signature.
    /// </summary>
    /// <remarks>
    /// The hole has to be reserved before the signature exists, so it is sized generously and padded
    /// with zeros. Eight kilobytes fits a certificate chain comfortably; a signature that does not
    /// fit is refused rather than truncated, because a truncated signature is a file that looks
    /// signed and is not.
    /// </remarks>
    public int ReservedBytes { get; set; } = 8192;
}

/// <summary>
/// Signs a document.
/// </summary>
/// <remarks>
/// <para>
/// The order of operations is the whole problem. A signature covers a byte range of the finished
/// file, and it cannot cover itself — so the file has to be written first, with a hole where the
/// signature will go and placeholders where the byte range will go, and only then can the hash be
/// taken and the signature spliced in.
/// </para>
/// <para>
/// That splice has one hard rule: <b>nothing may change length</b>. The byte range is written as
/// fixed-width numbers padded with spaces, because a shorter number would move every byte after it
/// and invalidate the very offsets it describes.
/// </para>
/// <para>
/// <b>What this produces.</b> An <c>adbe.pkcs7.detached</c> signature — the ordinary kind, which
/// Acrobat and every other reader understand. It does not add a trusted timestamp, a revocation
/// response, or a long-term-validation archive; those are what turn a signature into a PAdES-LTV
/// one, they need a network service, and their absence is why a signature checked years later may
/// no longer verify even though nothing was tampered with.
/// </para>
/// <example>
/// <code>
/// using var certificate = X509CertificateLoader.LoadPkcs12FromFile("signer.pfx", "password");
/// using var pdf = PdfDocument.Open("laporan.pdf");
///
/// PdfSigner.Sign(pdf, certificate, "laporan-signed.pdf", new PdfSignOptions
/// {
///     Reason = "Disetujui",
///     Location = "Jakarta",
/// });
/// </code>
/// </example>
/// </remarks>
public static class PdfSigner
{
    /// <summary>
    /// The value written into the byte range before the real offsets are known.
    /// </summary>
    /// <remarks>
    /// Ten digits, which is wider than any offset in a file this library can produce, so the real
    /// numbers always fit in the space it reserved. It doubles as the marker that says "this range
    /// has not been filled in yet".
    /// </remarks>
    private const long Sentinel = 9_999_999_999;

    /// <summary>Signs a document and writes it to a file.</summary>
    public static void Sign(PdfDocument document, X509Certificate2 certificate, string path,
        PdfSignOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var signed = Sign(document, certificate, options);

        File.WriteAllBytes(path, signed);
    }

    /// <summary>Signs a document and returns the finished file.</summary>
    /// <exception cref="OfficeNetException">
    /// The certificate has no private key, or the signature does not fit the reserved space.
    /// </exception>
    public static byte[] Sign(PdfDocument document, X509Certificate2 certificate,
        PdfSignOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(certificate);

        options ??= new PdfSignOptions();

        if (!certificate.HasPrivateKey)
        {
            throw new OfficeNetException(
                "This certificate has no private key, so it can verify a signature but not make " +
                "one. Load a .pfx or .p12, not a .cer.");
        }

        AddSignatureField(document, certificate, options);

        using var buffer = new MemoryStream();
        document.Save(buffer);

        var bytes = buffer.ToArray();

        var (contentsStart, contentsEnd) = FindContentsHole(bytes);
        var ranges = RangesAround(contentsStart, contentsEnd, bytes.Length);

        WriteByteRange(bytes, ranges);

        var signature = BuildSignature(Gather(ranges, bytes), certificate, options);

        // Two hex digits per byte, and the hole was sized in bytes.
        var capacity = (contentsEnd - contentsStart - 1) / 2;

        if (signature.Length > capacity)
        {
            throw new OfficeNetException(
                $"The signature is {signature.Length} bytes and only {capacity} were reserved. " +
                $"Raise {nameof(PdfSignOptions)}.{nameof(PdfSignOptions.ReservedBytes)} — a " +
                "truncated signature would produce a file that looks signed and is not.");
        }

        WriteHex(bytes, contentsStart + 1, signature);

        return bytes;
    }

    // ---- Building the field ----------------------------------------------------------------------

    /// <summary>
    /// Adds the form field and the signature dictionary, with placeholders in place of the values
    /// that cannot be known until the file has been written.
    /// </summary>
    private static void AddSignatureField(PdfDocument document, X509Certificate2 certificate,
        PdfSignOptions options)
    {
        var signature = new PdfDictionary();

        signature.SetName(PdfName.Type, "Sig");
        signature.SetName(PdfName.Get("Filter"), "Adobe.PPKLite");
        signature.SetName(PdfName.Get("SubFilter"), "adbe.pkcs7.detached");

        signature[PdfName.Get("Name")] =
            new PdfString(options.Name ?? SubjectName(certificate));

        if (options.Reason is { Length: > 0 } reason)
        {
            signature[PdfName.Get("Reason")] = new PdfString(reason);
        }

        if (options.Location is { Length: > 0 } location)
        {
            signature[PdfName.Get("Location")] = new PdfString(location);
        }

        if (options.ContactInfo is { Length: > 0 } contact)
        {
            signature[PdfName.Get("ContactInfo")] = new PdfString(contact);
        }

        signature[PdfName.Get("M")] =
            new PdfString(PdfDates.Format(options.SignedAt ?? DateTime.Now));

        // The hole. Hex, because the signature is binary and a literal string would need escaping —
        // which changes its length, which is the one thing that must not happen here.
        signature[PdfName.Get("Contents")] =
            new PdfString(new byte[Math.Max(1024, options.ReservedBytes)]) { IsHex = true };

        // Placeholders wide enough for any offset in a file this library can write, and recognisable
        // afterwards: the splice finds them by this value rather than by position, so signing a
        // document that already has a signature fills in the new range and not the old one.
        signature[PdfName.Get("ByteRange")] = new PdfArray(
        [
            new PdfNumber(Sentinel),
            new PdfNumber(Sentinel),
            new PdfNumber(Sentinel),
            new PdfNumber(Sentinel),
        ]);

        var signatureReference = document.AddObject(signature);

        var field = new PdfDictionary();

        field.SetName(PdfName.Get("FT"), "Sig");
        field[PdfName.Get("T")] = new PdfString(options.FieldName);
        field[PdfName.Get("V")] = signatureReference;
        field.SetName(PdfName.Subtype, "Widget");
        field.SetName(PdfName.Type, "Annot");

        // A zero-size rectangle: the signature is real and invisible, which is what a signature
        // without a drawn appearance means. Omitting /Rect entirely makes some readers complain.
        field[PdfName.Get("Rect")] = new PdfArray(0, 0, 0, 0);
        field.Set(PdfName.Get("F"), 132);   // Print | NoView

        if (document.Pages.Count > 0)
        {
            field[PdfName.Get("P")] = document.ReferenceTo(document.Pages[0].Dictionary);
            AddAnnotation(document, document.Pages[0], document.AddObject(field));
        }
        else
        {
            document.AddObject(field);
        }

        AttachToForm(document, field);
    }

    private static void AddAnnotation(PdfDocument document, PdfPage page, PdfObject field)
    {
        if (document.Follow(page.Dictionary.Get(PdfName.Get("Annots"))) is PdfArray existing)
        {
            existing.Add(field);
            return;
        }

        page.Dictionary[PdfName.Get("Annots")] = new PdfArray([field]);
    }

    /// <summary>
    /// Puts the field in the document's form, creating the form if there is none.
    /// </summary>
    /// <remarks>
    /// <c>/SigFlags</c> matters: bit 1 says the document contains a signature, and bit 2 says
    /// appending to it would invalidate one. Without them a reader may not offer to check the
    /// signature at all.
    /// </remarks>
    private static void AttachToForm(PdfDocument document, PdfDictionary field)
    {
        var reference = document.ReferenceTo(field);

        if (document.Follow(document.Catalog.Get(PdfName.Get("AcroForm"))) is PdfDictionary form)
        {
            if (document.Follow(form.Get(PdfName.Get("Fields"))) is PdfArray fields)
            {
                fields.Add(reference);
            }
            else
            {
                form[PdfName.Get("Fields")] = new PdfArray([reference]);
            }

            form.Set(PdfName.Get("SigFlags"), 3);
            return;
        }

        var created = new PdfDictionary();

        created[PdfName.Get("Fields")] = new PdfArray([reference]);
        created.Set(PdfName.Get("SigFlags"), 3);

        document.Catalog[PdfName.Get("AcroForm")] = document.AddObject(created);
    }

    private static string SubjectName(X509Certificate2 certificate)
    {
        var common = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

        return common is { Length: > 0 } ? common : certificate.Subject;
    }

    // ---- Splicing ------------------------------------------------------------------------------

    /// <summary>
    /// Finds the reserved hole in the written file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Located by scanning the bytes rather than by tracking an offset while writing, because the
    /// writer does not report where it put things.
    /// </para>
    /// <para>
    /// It is the hole that is <em>still all zeros</em>, not simply the first one. Signing a document
    /// that already carries a signature would otherwise splice into the existing one and destroy it,
    /// and the file would still look signed.
    /// </para>
    /// </remarks>
    private static (int Start, int End) FindContentsHole(byte[] bytes)
    {
        var marker = "/Contents"u8;

        for (var i = 0; i + marker.Length < bytes.Length; i++)
        {
            if (!bytes.AsSpan(i, marker.Length).SequenceEqual(marker))
            {
                continue;
            }

            var cursor = i + marker.Length;

            while (cursor < bytes.Length && bytes[cursor] is (byte)' ' or (byte)'\r' or (byte)'\n')
            {
                cursor++;
            }

            if (cursor >= bytes.Length || bytes[cursor] != (byte)'<')
            {
                continue;
            }

            var close = Array.IndexOf(bytes, (byte)'>', cursor);

            if (close <= cursor)
            {
                continue;
            }

            var empty = true;

            for (var at = cursor + 1; at < close && empty; at++)
            {
                empty = bytes[at] == (byte)'0';
            }

            if (empty)
            {
                return (cursor, close);
            }
        }

        throw new OfficeNetException(
            "The reserved signature space was not found in the written file, which means the " +
            "signature dictionary was not written as expected.");
    }

    /// <summary>The two ranges either side of the hole, which is what a signature always covers.</summary>
    private static List<(int Offset, int Length)> RangesAround(int start, int end, int fileLength) =>
    [
        (0, start),
        (end + 1, fileLength - end - 1),
    ];

    /// <summary>
    /// Overwrites the placeholder byte range with the real one, in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Space-padded to exactly the width the placeholder occupied. Writing a shorter number would
    /// move every byte after it, which would invalidate the offsets being written — the snake eating
    /// its own tail, and the reason the placeholder is so wide to begin with.
    /// </para>
    /// <para>
    /// The placeholder is found by its sentinel value rather than by position, for the same reason
    /// the contents hole is: a document that already carries a signature has a filled-in byte range
    /// earlier in the file, and its padding has already been collapsed to fit the numbers in it.
    /// </para>
    /// </remarks>
    private static void WriteByteRange(byte[] bytes, List<(int Offset, int Length)> ranges)
    {
        var placeholder = Sentinel.ToString(CultureInfo.InvariantCulture);
        var marker = "/ByteRange"u8;

        for (var at = IndexOf(bytes, marker, 0); at >= 0; at = IndexOf(bytes, marker, at + 1))
        {
            var open = Array.IndexOf(bytes, (byte)'[', at);
            var close = open < 0 ? -1 : Array.IndexOf(bytes, (byte)']', open);

            if (open < 0 || close < 0)
            {
                continue;
            }

            var body = Encoding.ASCII.GetString(bytes, open + 1, close - open - 1);

            if (!body.Contains(placeholder, StringComparison.Ordinal))
            {
                // A byte range that has already been filled in: another signature's.
                continue;
            }

            var text = string.Create(CultureInfo.InvariantCulture,
                $"{ranges[0].Offset} {ranges[0].Length} {ranges[1].Offset} {ranges[1].Length}");

            var room = close - open - 1;

            if (text.Length > room)
            {
                throw new OfficeNetException(
                    $"The byte range needs {text.Length} characters and the placeholder reserved " +
                    $"{room}.");
            }

            Encoding.ASCII.GetBytes(text.PadRight(room)).CopyTo(bytes, open + 1);
            return;
        }

        throw new OfficeNetException("The signature has no /ByteRange placeholder to fill in.");
    }

    private static byte[] BuildSignature(byte[] content, X509Certificate2 certificate,
        PdfSignOptions options)
    {
        var signer = new CmsSigner(certificate)
        {
            DigestAlgorithm = new Oid(OidFor(options.DigestAlgorithm)),

            // The whole chain, so a verifier can build a path without fetching intermediates.
            IncludeOption = X509IncludeOption.WholeChain,
        };

        // Detached: the CMS carries the signature and not the content, which is the arrangement a
        // PDF needs because the content is the file itself.
        var cms = new SignedCms(new ContentInfo(content), detached: true);

        cms.ComputeSignature(signer);

        return cms.Encode();
    }

    private static string OidFor(HashAlgorithmName algorithm) => algorithm.Name switch
    {
        "SHA384" => "2.16.840.1.101.3.4.2.2",
        "SHA512" => "2.16.840.1.101.3.4.2.3",
        _ => "2.16.840.1.101.3.4.2.1",
    };

    private static byte[] Gather(List<(int Offset, int Length)> ranges, byte[] file)
    {
        var buffer = new byte[ranges.Sum(r => r.Length)];
        var cursor = 0;

        foreach (var (offset, length) in ranges)
        {
            Array.Copy(file, offset, buffer, cursor, length);
            cursor += length;
        }

        return buffer;
    }

    /// <summary>Writes bytes as hex into the reserved hole, leaving the rest of it as zeros.</summary>
    private static void WriteHex(byte[] bytes, int start, byte[] signature)
    {
        const string Digits = "0123456789ABCDEF";

        for (var i = 0; i < signature.Length; i++)
        {
            bytes[start + (i * 2)] = (byte)Digits[signature[i] >> 4];
            bytes[start + (i * 2) + 1] = (byte)Digits[signature[i] & 0x0F];
        }
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int from = 0)
    {
        for (var i = Math.Max(0, from); i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
