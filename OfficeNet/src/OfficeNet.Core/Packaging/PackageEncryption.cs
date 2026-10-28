// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace OfficeNet.Core.Packaging;

/// <summary>
/// Password encryption of a whole .docx, .xlsx or .pptx — ECMA-376 Agile Encryption, what Office
/// writes for "Encrypt with Password".
/// </summary>
/// <remarks>
/// <para>
/// An encrypted package is not a zip. It is a compound file (<see cref="CompoundFile"/>) holding the
/// encrypted zip as <c>EncryptedPackage</c>, the parameters needed to decrypt it as
/// <c>EncryptionInfo</c>, and the <c>\u0006DataSpaces</c> storage that tells Office which transform
/// was applied. This is why a password-protected .xlsx looks like a legacy .xls to anything that
/// only checks the first bytes.
/// </para>
/// <para>
/// Writing uses AES-256 in CBC mode with SHA-512 and 100,000 hash iterations, the parameters Office
/// itself uses. Reading takes any Agile parameters (AES-128/192/256; SHA-1, SHA-256, SHA-384,
/// SHA-512). The older "Standard" encryption of Office 2007 is refused by name.
/// </para>
/// <para>
/// The password is checked against the stored verifier before anything is decrypted, so a wrong
/// one is an <see cref="OfficeNetPasswordException"/> rather than garbage; and the package's HMAC is
/// checked after, so a file altered on the way is refused rather than opened half-corrupt.
/// </para>
/// <para>
/// This is <b>not</b> the "protect sheet" or "restrict editing" setting, which stores a hash in the
/// document and keeps it readable by everyone. This one makes the file unreadable without the password.
/// </para>
/// </remarks>
public static class PackageEncryption
{
    private const string EncryptionNamespace = "http://schemas.microsoft.com/office/2006/encryption";
    private const string PasswordNamespace = "http://schemas.microsoft.com/office/2006/keyEncryptor/password";
    private const string CertificateNamespace = "http://schemas.microsoft.com/office/2006/keyEncryptor/certificate";

    private const string InfoStream = "EncryptionInfo";
    private const string PackageStream = "EncryptedPackage";
    private const int SegmentSize = 4096;

    // The block keys of MS-OFFCRYPTO 2.3.4.11–14: each derives a different key or IV from one secret.
    private static readonly byte[] VerifierInputBlock = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] VerifierValueBlock = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] KeyValueBlock = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];
    private static readonly byte[] HmacKeyBlock = [0x5F, 0xB2, 0xAD, 0x01, 0x0C, 0xB9, 0xE1, 0xF6];
    private static readonly byte[] HmacValueBlock = [0xA0, 0x67, 0x7F, 0x02, 0xB2, 0x2C, 0x84, 0x33];

    /// <summary>Whether bytes are an encrypted OOXML package.</summary>
    public static bool IsEncrypted(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!CompoundFile.IsCompoundFile(file))
        {
            return false;
        }

        try
        {
            var streams = CompoundFile.Read(file).Streams;
            return streams.ContainsKey(InfoStream) && streams.ContainsKey(PackageStream);
        }
        catch (OfficeNetException)
        {
            return false;
        }
    }

    /// <summary>Encrypts a package (the bytes of a .docx, .xlsx or .pptx) with a password.</summary>
    /// <param name="package">The zip.</param>
    /// <param name="password">At most 255 characters, as in Office.</param>
    /// <param name="spinCount">
    /// How many times the password hash is iterated; 100,000 as in Office. Lower only for tests.
    /// </param>
    public static byte[] Encrypt(byte[] package, string password, int spinCount = 100_000)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidatePassword(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(spinCount, 1);

        var parameters = new Parameters(HashAlgorithmName.SHA512, 64, 256, 16, RandomNumberGenerator.GetBytes(16));
        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var secretKey = RandomNumberGenerator.GetBytes(parameters.KeyBytes);

        // The package: its length, then 4096-byte segments, each with its own IV.
        var encrypted = EncryptPackage(package, secretKey, parameters);

        // Integrity: an HMAC of the stored stream, under a random key, both stored encrypted.
        var hmacKey = RandomNumberGenerator.GetBytes(parameters.HashSize);
        var hmacValue = Hmac(parameters, hmacKey, encrypted);
        var encryptedHmacKey = Aes(secretKey, parameters.Iv(HmacKeyBlock), hmacKey, encrypt: true);
        var encryptedHmacValue = Aes(secretKey, parameters.Iv(HmacValueBlock), hmacValue, encrypt: true);

        // The password: a verifier to recognise it by, and the secret key it unlocks.
        var passwordHash = IteratedHash(parameters.Hash, passwordSalt, password, spinCount);
        var verifierInput = RandomNumberGenerator.GetBytes(16);
        var verifierHash = Hash(parameters.Hash, verifierInput);

        var encryptedVerifierInput = Aes(Key(parameters, passwordHash, VerifierInputBlock), passwordSalt, verifierInput, encrypt: true);
        var encryptedVerifierHash = Aes(Key(parameters, passwordHash, VerifierValueBlock), passwordSalt, verifierHash, encrypt: true);
        var encryptedKey = Aes(Key(parameters, passwordHash, KeyValueBlock), passwordSalt, secretKey, encrypt: true);

        XNamespace e = EncryptionNamespace;
        XNamespace p = PasswordNamespace;

        XAttribute[] Cipher(byte[] salt) =>
        [
            new("saltSize", salt.Length), new("blockSize", 16), new("keyBits", parameters.KeyBits),
            new("hashSize", parameters.HashSize), new("cipherAlgorithm", "AES"),
            new("cipherChaining", "ChainingModeCBC"), new("hashAlgorithm", "SHA512"),
            new("saltValue", Convert.ToBase64String(salt)),
        ];

        var xml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(e + "encryption",
                new XAttribute("xmlns", EncryptionNamespace),
                new XAttribute(XNamespace.Xmlns + "p", PasswordNamespace),
                new XAttribute(XNamespace.Xmlns + "c", CertificateNamespace),
                new XElement(e + "keyData", Cipher(parameters.Salt)),
                new XElement(e + "dataIntegrity",
                    new XAttribute("encryptedHmacKey", Convert.ToBase64String(encryptedHmacKey)),
                    new XAttribute("encryptedHmacValue", Convert.ToBase64String(encryptedHmacValue))),
                new XElement(e + "keyEncryptors",
                    new XElement(e + "keyEncryptor", new XAttribute("uri", PasswordNamespace),
                        new XElement(p + "encryptedKey",
                            new XAttribute("spinCount", spinCount),
                            Cipher(passwordSalt),
                            new XAttribute("encryptedVerifierHashInput", Convert.ToBase64String(encryptedVerifierInput)),
                            new XAttribute("encryptedVerifierHashValue", Convert.ToBase64String(encryptedVerifierHash)),
                            new XAttribute("encryptedKeyValue", Convert.ToBase64String(encryptedKey)))))));

        // EncryptionInfo: version 4.4 (Agile), the reserved flag 0x40, then the XML.
        var info = new MemoryStream();
        Span<byte> prefix = stackalloc byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(prefix, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix[2..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix[4..], 0x40);
        info.Write(prefix);

        using (var writer = new StreamWriter(info, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.Write(xml.Declaration + "\r\n" + xml.Root!.ToString(SaveOptions.DisableFormatting));
        }

        var file = new CompoundFile();
        file.Streams[InfoStream] = info.ToArray();
        file.Streams[PackageStream] = encrypted;

        foreach (var (path, data) in DataSpaces.Streams())
        {
            file.Streams[path] = data;
        }

        return file.ToArray();
    }

    /// <summary>Decrypts an encrypted package, returning the zip.</summary>
    /// <exception cref="OfficeNetPasswordException">The password is wrong.</exception>
    /// <exception cref="OfficeNetException">
    /// The file is not an encrypted package, uses encryption other than Agile, or was altered.
    /// </exception>
    public static byte[] Decrypt(byte[] file, string password)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(password);

        var streams = CompoundFile.Read(file).Streams;

        if (!streams.TryGetValue(InfoStream, out var info) || !streams.TryGetValue(PackageStream, out var encrypted))
        {
            throw new OfficeNetException(
                "This compound file is not an encrypted Office document. A legacy .doc, .xls or .ppt is " +
                "also a compound file — OfficeNet reads the XML formats only.");
        }

        if (info.Length < 8)
        {
            throw new OfficeNetException("The EncryptionInfo stream is truncated.");
        }

        var major = BinaryPrimitives.ReadUInt16LittleEndian(info);
        var minor = BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(2));

        if ((major, minor) != (4, 4))
        {
            throw new OfficeNetNotSupportedException(
                $"The document uses encryption version {major}.{minor}. OfficeNet reads Agile encryption " +
                "(4.4), which Office has written since 2010; Standard encryption (Office 2007) is not supported.");
        }

        XDocument xml;

        try
        {
            xml = XDocument.Parse(Encoding.UTF8.GetString(info, 8, info.Length - 8).TrimStart('﻿'));
        }
        catch (System.Xml.XmlException ex)
        {
            throw new OfficeNetException("The EncryptionInfo XML cannot be read: " + ex.Message, ex);
        }

        XNamespace e = EncryptionNamespace;
        XNamespace p = PasswordNamespace;

        var keyData = xml.Root?.Element(e + "keyData") ?? throw Missing("keyData");
        var integrity = xml.Root.Element(e + "dataIntegrity");
        var encryptedKey = xml.Root.Element(e + "keyEncryptors")?.Elements(e + "keyEncryptor")
            .Select(k => k.Element(p + "encryptedKey")).FirstOrDefault(k => k is not null)
            ?? throw new OfficeNetNotSupportedException(
                "The document is encrypted for a certificate, not a password; OfficeNet opens password-encrypted documents only.");

        var parameters = Parameters.Read(keyData);
        var keyParameters = Parameters.Read(encryptedKey);
        var passwordSalt = keyParameters.Salt;
        var spinCount = int.Parse(Attribute(encryptedKey, "spinCount"), CultureInfo.InvariantCulture);

        if (spinCount is < 0 or > 10_000_000)
        {
            throw new OfficeNetException($"The document's spin count {spinCount} is outside what Office allows.");
        }

        var passwordHash = IteratedHash(keyParameters.Hash, passwordSalt, password, spinCount);

        // Is it the password? Decrypt the verifier and its hash, hash one, compare with the other.
        var verifierInput = Aes(Key(keyParameters, passwordHash, VerifierInputBlock), passwordSalt,
            Base64(encryptedKey, "encryptedVerifierHashInput"), encrypt: false);
        var verifierHash = Aes(Key(keyParameters, passwordHash, VerifierValueBlock), passwordSalt,
            Base64(encryptedKey, "encryptedVerifierHashValue"), encrypt: false);

        var expected = Hash(keyParameters.Hash, verifierInput.AsSpan(0, Math.Min(verifierInput.Length, passwordSalt.Length)).ToArray());

        if (verifierHash.Length < keyParameters.HashSize ||
            !CryptographicOperations.FixedTimeEquals(expected, verifierHash.AsSpan(0, keyParameters.HashSize)))
        {
            throw new OfficeNetPasswordException("The password is wrong.");
        }

        var secretKey = Aes(Key(keyParameters, passwordHash, KeyValueBlock), passwordSalt,
            Base64(encryptedKey, "encryptedKeyValue"), encrypt: false)[..parameters.KeyBytes];

        // Was it altered? The HMAC covers the stored stream, length prefix included.
        if (integrity is not null)
        {
            var hmacKey = Aes(secretKey, parameters.Iv(HmacKeyBlock), Base64(integrity, "encryptedHmacKey"), encrypt: false)[..parameters.HashSize];
            var hmacValue = Aes(secretKey, parameters.Iv(HmacValueBlock), Base64(integrity, "encryptedHmacValue"), encrypt: false)[..parameters.HashSize];

            if (!CryptographicOperations.FixedTimeEquals(Hmac(parameters, hmacKey, encrypted), hmacValue))
            {
                throw new OfficeNetException(
                    "The encrypted document fails its integrity check: it was altered or damaged after it was encrypted.");
            }
        }

        return DecryptPackage(encrypted, secretKey, parameters);
    }

    // ---- The package stream ----------------------------------------------------------------

    private static byte[] EncryptPackage(byte[] package, byte[] key, Parameters parameters)
    {
        var output = new MemoryStream(package.Length + 8 + 16);
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(length, (ulong)package.Length);
        output.Write(length);

        for (var (offset, segment) = (0, 0u); offset < package.Length; offset += SegmentSize, segment++)
        {
            var chunk = package.AsSpan(offset, Math.Min(SegmentSize, package.Length - offset));
            var padded = new byte[(chunk.Length + 15) / 16 * 16];
            chunk.CopyTo(padded);
            output.Write(Aes(key, parameters.SegmentIv(segment), padded, encrypt: true));
        }

        return output.ToArray();
    }

    private static byte[] DecryptPackage(byte[] encrypted, byte[] key, Parameters parameters)
    {
        if (encrypted.Length < 8)
        {
            throw new OfficeNetException("The EncryptedPackage stream is truncated.");
        }

        var size = BinaryPrimitives.ReadUInt64LittleEndian(encrypted);

        if (size > (ulong)(encrypted.Length - 8))
        {
            throw new OfficeNetException("The EncryptedPackage stream is shorter than the size it declares.");
        }

        var output = new byte[size];
        var data = encrypted.AsSpan(8);

        for (var (offset, segment) = (0, 0u); offset < (int)size; offset += SegmentSize, segment++)
        {
            var length = Math.Min(SegmentSize, data.Length - offset);
            length -= length % 16;

            var plain = Aes(key, parameters.SegmentIv(segment), data.Slice(offset, length).ToArray(), encrypt: false);
            plain.AsSpan(0, Math.Min(plain.Length, (int)size - offset)).CopyTo(output.AsSpan(offset));
        }

        return output;
    }

    // ---- Keys --------------------------------------------------------------------------------

    /// <summary>
    /// H0 = H(salt + password); Hn = H(iterator + Hn-1) — MS-OFFCRYPTO 2.3.4.11. Most of the time
    /// spent opening an encrypted file is here, on purpose.
    /// </summary>
    private static byte[] IteratedHash(HashAlgorithmName algorithm, byte[] salt, string password, int spinCount)
    {
        var hash = Hash(algorithm, [.. salt, .. Encoding.Unicode.GetBytes(password)]);
        var buffer = new byte[4 + hash.Length];

        for (var i = 0; i < spinCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, i);
            hash.CopyTo(buffer, 4);
            hash = Hash(algorithm, buffer);
        }

        return hash;
    }

    /// <summary>The key for one purpose: H(Hn + blockKey), cut or padded with 0x36 to the key size.</summary>
    private static byte[] Key(Parameters parameters, byte[] passwordHash, byte[] block) =>
        Fit(Hash(parameters.Hash, [.. passwordHash, .. block]), parameters.KeyBytes);

    private static byte[] Fit(byte[] value, int length)
    {
        if (value.Length == length)
        {
            return value;
        }

        var fitted = new byte[length];
        fitted.AsSpan().Fill(0x36);
        value.AsSpan(0, Math.Min(value.Length, length)).CopyTo(fitted);
        return fitted;
    }

    private static byte[] Hash(HashAlgorithmName algorithm, byte[] data) => algorithm.Name switch
    {
        "SHA1" => SHA1.HashData(data),
        "SHA256" => SHA256.HashData(data),
        "SHA384" => SHA384.HashData(data),
        _ => SHA512.HashData(data),
    };

    private static byte[] Hmac(Parameters parameters, byte[] key, byte[] data) => parameters.Hash.Name switch
    {
        "SHA1" => HMACSHA1.HashData(key, data),
        "SHA256" => HMACSHA256.HashData(key, data),
        "SHA384" => HMACSHA384.HashData(key, data),
        _ => HMACSHA512.HashData(key, data),
    };

    private static byte[] Aes(byte[] key, byte[] iv, byte[] data, bool encrypt)
    {
        if (data.Length % 16 != 0)
        {
            // Values to encrypt are padded with zeros to the block; a stored value that is not a
            // whole number of blocks is damaged.
            if (!encrypt)
            {
                throw new OfficeNetException("An encrypted value is not a whole number of AES blocks.");
            }

            data = [.. data, .. new byte[16 - data.Length % 16]];
        }

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;

        return encrypt
            ? aes.EncryptCbc(data, iv, PaddingMode.None)
            : aes.DecryptCbc(data, iv, PaddingMode.None);
    }

    // ---- Parameters --------------------------------------------------------------------------

    private sealed record Parameters(HashAlgorithmName Hash, int HashSize, int KeyBits, int BlockSize, byte[] Salt)
    {
        public int KeyBytes => KeyBits / 8;

        /// <summary>An IV for one purpose: H(salt + blockKey), cut or padded to the block size.</summary>
        public byte[] Iv(byte[] block) => Fit(PackageEncryption.Hash(Hash, [.. Salt, .. block]), BlockSize);

        /// <summary>A segment's IV: H(salt + segment index).</summary>
        public byte[] SegmentIv(uint segment)
        {
            var index = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(index, segment);
            return Iv(index);
        }

        public static Parameters Read(XElement element)
        {
            var cipher = Attribute(element, "cipherAlgorithm");
            var chaining = Attribute(element, "cipherChaining");

            if (cipher != "AES" || chaining != "ChainingModeCBC")
            {
                throw new OfficeNetNotSupportedException(
                    $"The document is encrypted with {cipher} in {chaining}; OfficeNet reads AES in CBC mode, which is what Office writes.");
            }

            var hash = Attribute(element, "hashAlgorithm") switch
            {
                "SHA1" => HashAlgorithmName.SHA1,
                "SHA256" => HashAlgorithmName.SHA256,
                "SHA384" => HashAlgorithmName.SHA384,
                "SHA512" => HashAlgorithmName.SHA512,
                var other => throw new OfficeNetNotSupportedException($"The document's hash algorithm {other} is not supported."),
            };

            var keyBits = int.Parse(Attribute(element, "keyBits"), CultureInfo.InvariantCulture);

            if (keyBits is not (128 or 192 or 256))
            {
                throw new OfficeNetException($"An AES key of {keyBits} bits is not valid.");
            }

            return new Parameters(hash,
                int.Parse(Attribute(element, "hashSize"), CultureInfo.InvariantCulture),
                keyBits,
                int.Parse(Attribute(element, "blockSize"), CultureInfo.InvariantCulture) is 16
                    ? 16
                    : throw new OfficeNetException("AES has a 16-byte block; the document says otherwise."),
                Base64(element, "saltValue"));
        }
    }

    private static string Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value ?? throw Missing($"{element.Name.LocalName}/@{name}");

    private static byte[] Base64(XElement element, string name)
    {
        try
        {
            return Convert.FromBase64String(Attribute(element, name));
        }
        catch (FormatException ex)
        {
            throw new OfficeNetException($"The EncryptionInfo value {name} is not base64.", ex);
        }
    }

    private static OfficeNetException Missing(string what) =>
        new($"The EncryptionInfo XML has no {what}, so the document cannot be decrypted.");

    private static void ValidatePassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        if (password.Length > 255)
        {
            throw new ArgumentException("Office accepts passwords of at most 255 characters.", nameof(password));
        }
    }

    /// <summary>
    /// The <c>\u0006DataSpaces</c> storage (MS-OFFCRYPTO 2.2): it names the transform applied to
    /// <c>EncryptedPackage</c>. Office will not open an encrypted package without it.
    /// </summary>
    private static class DataSpaces
    {
        private const string Root = "\u0006DataSpaces/";
        private const string DataSpaceName = "StrongEncryptionDataSpace";
        private const string TransformName = "StrongEncryptionTransform";

        public static IEnumerable<(string Path, byte[] Data)> Streams()
        {
            yield return (Root + "Version", Bytes(w =>
            {
                w.Text("Microsoft.Container.DataSpaces");
                w.Versions();
            }));

            yield return (Root + "DataSpaceMap", Bytes(w =>
            {
                w.UInt(8);   // header length
                w.UInt(1);   // one entry

                var entry = Bytes(m =>
                {
                    m.UInt(1);   // one reference component
                    m.UInt(0);   // ... which is a stream
                    m.Text(PackageStream);
                    m.Text(DataSpaceName);
                });

                w.UInt((uint)entry.Length + 4);
                w.Raw(entry);
            }));

            yield return (Root + "DataSpaceInfo/" + DataSpaceName, Bytes(w =>
            {
                w.UInt(8);
                w.UInt(1);
                w.Text(TransformName);
            }));

            yield return (Root + "TransformInfo/" + TransformName + "/\u0006Primary", Bytes(w =>
            {
                var id = Bytes(h =>
                {
                    h.UInt(1);   // a password transform
                    h.Text("{FF9A3F03-56EF-4613-BDD5-5A41C1D07246}");
                });

                w.UInt((uint)id.Length + 4);
                w.Raw(id);
                w.Text("Microsoft.Container.EncryptionTransform");
                w.Versions();

                // EncryptionTransformInfo: no name, no block size, no mode — the details are in
                // EncryptionInfo — and the reserved 4.
                w.UInt(0);
                w.UInt(0);
                w.UInt(0);
                w.UInt(4);
            }));
        }

        private static byte[] Bytes(Action<Writer> write)
        {
            var writer = new Writer();
            write(writer);
            return writer.Stream.ToArray();
        }

        private sealed class Writer
        {
            public MemoryStream Stream { get; } = new();

            public void UInt(uint value)
            {
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
                Stream.Write(buffer);
            }

            public void Raw(byte[] data) => Stream.Write(data);

            /// <summary>UNICODE-LP-P4: a byte length, UTF-16, padded to four bytes.</summary>
            public void Text(string value)
            {
                var bytes = Encoding.Unicode.GetBytes(value);
                UInt((uint)bytes.Length);
                Stream.Write(bytes);
                Stream.Write(new byte[(4 - bytes.Length % 4) % 4]);
            }

            /// <summary>Reader, updater and writer versions, each 1.0.</summary>
            public void Versions()
            {
                for (var i = 0; i < 3; i++)
                {
                    UInt(1);
                }
            }
        }
    }
}
