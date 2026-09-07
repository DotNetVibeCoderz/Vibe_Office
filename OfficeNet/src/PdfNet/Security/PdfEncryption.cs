// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Security.Cryptography;
using System.Text;
using OfficeNet.Core;
using PdfNet.Objects;

namespace PdfNet.Security;

/// <summary>What a user is allowed to do with an encrypted document.</summary>
/// <remarks>
/// These are advisory. The permission bits live inside the encrypted file and any reader can
/// ignore them — they express the author's intent, they do not enforce it. Only the requirement to
/// know a password to decrypt at all is cryptographic.
/// </remarks>
[Flags]
public enum PdfPermissions
{
    /// <summary>Nothing is permitted beyond opening the document.</summary>
    None = 0,

    /// <summary>Print at normal resolution (bit 3).</summary>
    Print = 1 << 2,

    /// <summary>Modify contents (bit 4).</summary>
    Modify = 1 << 3,

    /// <summary>Copy text and graphics (bit 5).</summary>
    Copy = 1 << 4,

    /// <summary>Add or modify annotations and fill form fields (bit 6).</summary>
    Annotate = 1 << 5,

    /// <summary>Fill existing form fields even when <see cref="Modify"/> is denied (bit 9).</summary>
    FillForms = 1 << 8,

    /// <summary>Extract text and graphics for accessibility (bit 10).</summary>
    ExtractForAccessibility = 1 << 9,

    /// <summary>Assemble the document — insert, rotate, delete pages (bit 11).</summary>
    Assemble = 1 << 10,

    /// <summary>Print at high resolution (bit 12).</summary>
    PrintHighResolution = 1 << 11,

    /// <summary>Everything.</summary>
    All = Print | Modify | Copy | Annotate | FillForms | ExtractForAccessibility |
          Assemble | PrintHighResolution,
}

/// <summary>Encrypts and decrypts the strings and streams of one document.</summary>
public interface IPdfEncryption
{
    /// <summary>Decrypts a payload belonging to a numbered object.</summary>
    byte[] Decrypt(byte[] data, int objectNumber, int generationNumber);

    /// <summary>Encrypts a payload belonging to a numbered object.</summary>
    byte[] Encrypt(byte[] data, int objectNumber, int generationNumber);
}

/// <summary>The cipher a document's standard security handler uses.</summary>
public enum PdfCipher
{
    /// <summary>RC4 with a 40-bit key (revision 2).</summary>
    Rc4_40,

    /// <summary>RC4 with a 128-bit key (revision 3).</summary>
    Rc4_128,

    /// <summary>AES-128 in CBC mode (revision 4, /AESV2).</summary>
    Aes128,

    /// <summary>AES-256 in CBC mode (revision 6, /AESV3).</summary>
    Aes256,
}

/// <summary>
/// The standard security handler (ISO 32000-1 §7.6.3 and ISO 32000-2 §7.6.4) — password-based
/// encryption as every PDF producer implements it.
/// </summary>
/// <remarks>
/// <para>
/// Revisions 2 through 4 are built on MD5 and RC4. Both are broken as general-purpose primitives
/// and neither is a choice this library made: they are what the file format specifies, and a
/// reader that refuses them cannot open the majority of existing encrypted PDFs. New documents
/// default to <see cref="PdfCipher.Aes256"/>, which is the revision 6 handler and is sound.
/// </para>
/// <para>
/// A PDF has two passwords. The <em>user</em> password is required to open the document; the
/// <em>owner</em> password additionally lifts the permission restrictions. A document with an empty
/// user password opens without prompting and is still encrypted — which is the usual shape of a
/// "print but do not copy" file.
/// </para>
/// </remarks>
public sealed class StandardSecurityHandler : IPdfEncryption
{
    private static ReadOnlySpan<byte> PadBytes =>
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56,
        0xFF, 0xFA, 0x01, 0x08, 0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    private static ReadOnlySpan<byte> AesSalt => [0x73, 0x41, 0x6C, 0x54];

    private readonly byte[] _fileKey;

    private StandardSecurityHandler(byte[] fileKey, PdfCipher cipher, int revision, PdfPermissions permissions,
        bool encryptMetadata, bool ownerAccess)
    {
        _fileKey = fileKey;
        Cipher = cipher;
        Revision = revision;
        Permissions = permissions;
        EncryptMetadata = encryptMetadata;
        HasOwnerAccess = ownerAccess;
    }

    /// <summary>The cipher in use.</summary>
    public PdfCipher Cipher { get; }

    /// <summary>The handler revision (2, 3, 4 or 6).</summary>
    public int Revision { get; }

    /// <summary>The permissions the document declares.</summary>
    public PdfPermissions Permissions { get; }

    /// <summary>True when the metadata stream is encrypted along with everything else.</summary>
    public bool EncryptMetadata { get; }

    /// <summary>True when the password supplied was the owner password.</summary>
    public bool HasOwnerAccess { get; }

    private bool IsAes => Cipher is PdfCipher.Aes128 or PdfCipher.Aes256;

    // ---- Opening an encrypted document ---------------------------------------------------------

    /// <summary>
    /// Recovers the file key from an <c>/Encrypt</c> dictionary and a password.
    /// </summary>
    /// <param name="encryptDictionary">The document's encryption dictionary.</param>
    /// <param name="firstFileId">The first element of the trailer's <c>/ID</c> array.</param>
    /// <param name="password">The user or owner password; empty for an unprompted document.</param>
    /// <exception cref="OfficeNetPasswordException">Neither password matches.</exception>
    /// <exception cref="OfficeNetNotSupportedException">The handler is not the standard one.</exception>
    public static StandardSecurityHandler Open(PdfDictionary encryptDictionary, byte[] firstFileId, string password)
    {
        ArgumentNullException.ThrowIfNull(encryptDictionary);

        var filter = encryptDictionary.GetName(PdfName.Get("Filter"));
        if (filter is not null && filter != "Standard")
        {
            throw new OfficeNetNotSupportedException(
                $"The document uses the '{filter}' security handler. Only the standard " +
                "password-based handler is implemented.");
        }

        var revision = encryptDictionary.GetInt(PdfName.Get("R"), 2);
        var version = encryptDictionary.GetInt(PdfName.Get("V"), 1);
        // /P carries the reserved high bits set to 1, so the raw value is a large negative number.
        // The RAW value is what the legacy key derivation hashes and must not be masked; the masked
        // form is only what the Permissions property reports. Masking before ComputeLegacyKey
        // produces a key that is wrong for every RC4 and AES-128 document.
        var rawPermissions = encryptDictionary.GetInt(PdfName.Get("P"), -1);
        var permissions = (PdfPermissions)rawPermissions & PdfPermissions.All;
        var encryptMetadata = encryptDictionary.GetBool(PdfName.Get("EncryptMetadata"), true);

        var ownerEntry = (encryptDictionary.Get(PdfName.Get("O")) as PdfString)?.Value ?? [];
        var userEntry = (encryptDictionary.Get(PdfName.Get("U")) as PdfString)?.Value ?? [];

        var cipher = ResolveCipher(encryptDictionary, version, revision, out var keyLengthBytes);

        if (revision >= 5)
        {
            return OpenRevision6(encryptDictionary, password, cipher, revision, permissions,
                encryptMetadata, ownerEntry, userEntry);
        }

        var passwordBytes = EncodeLegacyPassword(password);

        // Try the user password first: it is the common case, and an owner-password check is a
        // superset of the work.
        var userKey = ComputeLegacyKey(passwordBytes, ownerEntry, rawPermissions, firstFileId,
            revision, keyLengthBytes, encryptMetadata);

        if (VerifyUserPassword(userKey, userEntry, firstFileId, revision))
        {
            return new StandardSecurityHandler(userKey, cipher, revision, permissions, encryptMetadata, false);
        }

        // As the owner password: decrypt /O to recover the user password, then verify that.
        var recovered = RecoverUserPassword(passwordBytes, ownerEntry, revision, keyLengthBytes);
        var ownerDerivedKey = ComputeLegacyKey(recovered, ownerEntry, rawPermissions, firstFileId,
            revision, keyLengthBytes, encryptMetadata);

        if (VerifyUserPassword(ownerDerivedKey, userEntry, firstFileId, revision))
        {
            return new StandardSecurityHandler(ownerDerivedKey, cipher, revision, permissions,
                encryptMetadata, true);
        }

        throw new OfficeNetPasswordException(password.Length == 0
            ? "The document is encrypted and needs a password."
            : "The password is not the document's user or owner password.");
    }

    private static PdfCipher ResolveCipher(PdfDictionary dictionary, int version, int revision,
        out int keyLengthBytes)
    {
        keyLengthBytes = Math.Max(5, dictionary.GetInt(PdfName.Get("Length"), 40) / 8);

        if (version >= 5 || revision >= 5)
        {
            keyLengthBytes = 32;
            return PdfCipher.Aes256;
        }

        if (version == 4)
        {
            // V4 names a crypt filter rather than fixing the cipher. /StmF names which one applies
            // to streams; a document whose /StmF is /Identity is structurally encrypted and
            // actually is not, which the CFM lookup handles by falling through to RC4.
            var cryptFilters = dictionary.Get<PdfDictionary>(PdfName.Get("CF"));
            var streamFilterName = dictionary.GetName(PdfName.Get("StmF")) ?? "Identity";
            var filter = cryptFilters?.Get<PdfDictionary>(PdfName.Get(streamFilterName));
            var method = filter?.GetName(PdfName.Get("CFM"));

            if (filter is not null)
            {
                var filterLength = filter.GetInt(PdfName.Get("Length"), 0);
                if (filterLength > 0)
                {
                    // /Length in a crypt filter is in bytes in most producers and in bits in some.
                    keyLengthBytes = filterLength > 40 ? filterLength / 8 : filterLength;
                }
            }

            return method switch
            {
                "AESV2" => PdfCipher.Aes128,
                "AESV3" => PdfCipher.Aes256,
                "V2" => PdfCipher.Rc4_128,
                _ => keyLengthBytes <= 5 ? PdfCipher.Rc4_40 : PdfCipher.Rc4_128,
            };
        }

        return revision <= 2 || keyLengthBytes <= 5 ? PdfCipher.Rc4_40 : PdfCipher.Rc4_128;
    }

    private static byte[] EncodeLegacyPassword(string password)
    {
        // Revisions up to 4 take the password as PDFDocEncoded bytes, truncated to 32.
        var bytes = new List<byte>(32);
        foreach (var c in password)
        {
            if (bytes.Count == 32)
            {
                break;
            }

            bytes.Add((byte)(c <= 0xFF ? c : '?'));
        }

        return [.. bytes];
    }

    private static byte[] Pad(byte[] password)
    {
        var padded = new byte[32];
        var take = Math.Min(password.Length, 32);
        Array.Copy(password, padded, take);
        PadBytes[..(32 - take)].CopyTo(padded.AsSpan(take));
        return padded;
    }

    private static byte[] ComputeLegacyKey(byte[] password, byte[] ownerEntry, int permissions,
        byte[] firstFileId, int revision, int keyLengthBytes, bool encryptMetadata)
    {
        using var md5 = MD5.Create();
        using var buffer = new MemoryStream();

        buffer.Write(Pad(password));
        buffer.Write(ownerEntry, 0, Math.Min(ownerEntry.Length, 32));

        // P is a signed 32-bit value written little-endian. Writing it big-endian, or as unsigned,
        // produces a key that is wrong for every document with restrictive permissions and right
        // for the permissive ones — a bug that passes casual testing.
        Span<byte> p = stackalloc byte[4];
        BitConverter.TryWriteBytes(p, permissions);
        if (!BitConverter.IsLittleEndian)
        {
            p.Reverse();
        }

        buffer.Write(p);
        buffer.Write(firstFileId);

        if (revision >= 4 && !encryptMetadata)
        {
            buffer.Write([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        var hash = md5.ComputeHash(buffer.ToArray());

        if (revision >= 3)
        {
            // 50 further MD5 rounds over the first n bytes — deliberate key stretching, and
            // skipping it yields a key that decrypts revision-2 files and garbage for revision 3.
            for (var i = 0; i < 50; i++)
            {
                hash = md5.ComputeHash(hash, 0, keyLengthBytes);
            }
        }

        return hash[..keyLengthBytes];
    }

    private static bool VerifyUserPassword(byte[] key, byte[] userEntry, byte[] firstFileId, int revision)
    {
        if (userEntry.Length < 16)
        {
            return false;
        }

        if (revision == 2)
        {
            var expected = Rc4.Transform(key, Pad([]));
            return expected.AsSpan(0, 32).SequenceEqual(userEntry.AsSpan(0, Math.Min(32, userEntry.Length)));
        }

        using var md5 = MD5.Create();
        using var buffer = new MemoryStream();
        buffer.Write(PadBytes);
        buffer.Write(firstFileId);
        var hash = md5.ComputeHash(buffer.ToArray());

        var value = Rc4.Transform(key, hash);

        // 19 further RC4 passes with the key XORed by the round number.
        for (var round = 1; round <= 19; round++)
        {
            var roundKey = new byte[key.Length];
            for (var i = 0; i < key.Length; i++)
            {
                roundKey[i] = (byte)(key[i] ^ round);
            }

            value = Rc4.Transform(roundKey, value);
        }

        // Only the first 16 bytes are meaningful; the rest of /U is arbitrary padding.
        return value.AsSpan(0, 16).SequenceEqual(userEntry.AsSpan(0, 16));
    }

    private static byte[] RecoverUserPassword(byte[] ownerPassword, byte[] ownerEntry, int revision,
        int keyLengthBytes)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Pad(ownerPassword));

        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = md5.ComputeHash(hash);
            }
        }

        var key = hash[..keyLengthBytes];

        if (revision == 2)
        {
            return Rc4.Transform(key, ownerEntry);
        }

        var value = ownerEntry;
        for (var round = 19; round >= 0; round--)
        {
            var roundKey = new byte[key.Length];
            for (var i = 0; i < key.Length; i++)
            {
                roundKey[i] = (byte)(key[i] ^ round);
            }

            value = Rc4.Transform(roundKey, value);
        }

        return value;
    }

    // ---- Revision 6 (AES-256) ------------------------------------------------------------------

    private static StandardSecurityHandler OpenRevision6(PdfDictionary dictionary, string password,
        PdfCipher cipher, int revision, PdfPermissions permissions, bool encryptMetadata,
        byte[] ownerEntry, byte[] userEntry)
    {
        // Revision 5/6 passwords are SASLprep-normalised UTF-8, capped at 127 bytes.
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        if (passwordBytes.Length > 127)
        {
            passwordBytes = passwordBytes[..127];
        }

        var ownerKeyEntry = (dictionary.Get(PdfName.Get("OE")) as PdfString)?.Value ?? [];
        var userKeyEntry = (dictionary.Get(PdfName.Get("UE")) as PdfString)?.Value ?? [];

        if (userEntry.Length >= 48)
        {
            var validationSalt = userEntry[32..40];
            var keySalt = userEntry[40..48];

            if (Hash2B(passwordBytes, validationSalt, [], revision).AsSpan()
                .SequenceEqual(userEntry.AsSpan(0, 32)))
            {
                var intermediate = Hash2B(passwordBytes, keySalt, [], revision);
                var fileKey = DecryptNoPadding(intermediate, userKeyEntry);
                return new StandardSecurityHandler(fileKey, cipher, revision, permissions,
                    encryptMetadata, false);
            }
        }

        if (ownerEntry.Length >= 48 && userEntry.Length >= 48)
        {
            // The owner check mixes in the whole 48-byte /U value, which is what binds the owner
            // password to this specific document rather than to the password alone.
            var validationSalt = ownerEntry[32..40];
            var keySalt = ownerEntry[40..48];
            var u48 = userEntry[..48];

            if (Hash2B(passwordBytes, validationSalt, u48, revision).AsSpan()
                .SequenceEqual(ownerEntry.AsSpan(0, 32)))
            {
                var intermediate = Hash2B(passwordBytes, keySalt, u48, revision);
                var fileKey = DecryptNoPadding(intermediate, ownerKeyEntry);
                return new StandardSecurityHandler(fileKey, cipher, revision, permissions,
                    encryptMetadata, true);
            }
        }

        throw new OfficeNetPasswordException(password.Length == 0
            ? "The document is encrypted with AES-256 and needs a password."
            : "The password is not the document's user or owner password.");
    }

    /// <summary>
    /// Algorithm 2.B: the iterated SHA-256/384/512 hash revision 6 uses.
    /// </summary>
    /// <remarks>
    /// Revision 5 was a plain single SHA-256 and was withdrawn — it allowed an offline attack far
    /// cheaper than intended. Revision 6 runs at least 64 rounds of AES-CBC-plus-hash and stops
    /// only when the last output byte permits, which is the deliberate cost that makes guessing
    /// expensive. The loop's exit condition depends on the data, so it cannot be unrolled.
    /// </remarks>
    private static byte[] Hash2B(byte[] password, byte[] salt, byte[] userData, int revision)
    {
        var k = SHA256.HashData([.. password, .. salt, .. userData]);

        if (revision == 5)
        {
            return k;
        }

        var round = 0;
        while (true)
        {
            var k1Length = (password.Length + k.Length + userData.Length) * 64;
            var k1 = new byte[k1Length];
            var single = new byte[password.Length + k.Length + userData.Length];
            password.CopyTo(single, 0);
            k.CopyTo(single, password.Length);
            userData.CopyTo(single, password.Length + k.Length);

            for (var i = 0; i < 64; i++)
            {
                single.CopyTo(k1, i * single.Length);
            }

            var e = EncryptCbcNoPadding(k[..16], k[16..32], k1);

            // The next hash is chosen by the sum of E's first 16 bytes modulo 3.
            var sum = 0;
            for (var i = 0; i < 16; i++)
            {
                sum += e[i];
            }

            k = (sum % 3) switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e),
            };

            round++;
            if (round >= 64 && e[^1] <= round - 32)
            {
                break;
            }
        }

        return k[..32];
    }

    private static byte[] EncryptCbcNoPadding(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] DecryptNoPadding(byte[] key, byte[] data)
    {
        if (data.Length == 0)
        {
            return new byte[32];
        }

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = new byte[16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    // ---- Per-object encryption -----------------------------------------------------------------

    private byte[] ObjectKey(int objectNumber, int generationNumber)
    {
        // AES-256 uses the file key directly for every object. Deriving a per-object key there,
        // as the older revisions do, decrypts nothing.
        if (Cipher == PdfCipher.Aes256)
        {
            return _fileKey;
        }

        Span<byte> extra = stackalloc byte[5];
        extra[0] = (byte)objectNumber;
        extra[1] = (byte)(objectNumber >> 8);
        extra[2] = (byte)(objectNumber >> 16);
        extra[3] = (byte)generationNumber;
        extra[4] = (byte)(generationNumber >> 8);

        using var buffer = new MemoryStream(_fileKey.Length + 9);
        buffer.Write(_fileKey);
        buffer.Write(extra);

        if (IsAes)
        {
            buffer.Write(AesSalt);
        }

        var hash = MD5.HashData(buffer.ToArray());
        var length = Math.Min(_fileKey.Length + 5, 16);
        return hash[..length];
    }

    /// <inheritdoc />
    public byte[] Decrypt(byte[] data, int objectNumber, int generationNumber)
    {
        if (data.Length == 0)
        {
            return data;
        }

        var key = ObjectKey(objectNumber, generationNumber);

        if (!IsAes)
        {
            return Rc4.Transform(key, data);
        }

        // AES payloads carry their initialisation vector as the first 16 bytes.
        if (data.Length <= 16)
        {
            return [];
        }

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = data[..16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var decryptor = aes.CreateDecryptor();
        var blocks = (data.Length - 16) / 16 * 16;
        if (blocks == 0)
        {
            return [];
        }

        var plain = decryptor.TransformFinalBlock(data, 16, blocks);

        // PKCS#7 is stripped by hand rather than by PaddingMode.PKCS7 so that a corrupt final
        // block degrades to "keep the data" instead of throwing away the whole stream.
        var padding = plain[^1];
        return padding is >= 1 and <= 16 && padding <= plain.Length ? plain[..^padding] : plain;
    }

    /// <inheritdoc />
    public byte[] Encrypt(byte[] data, int objectNumber, int generationNumber)
    {
        if (data.Length == 0)
        {
            return data;
        }

        var key = ObjectKey(objectNumber, generationNumber);

        if (!IsAes)
        {
            return Rc4.Transform(key, data);
        }

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var cipherText = encryptor.TransformFinalBlock(data, 0, data.Length);
        return [.. aes.IV, .. cipherText];
    }

    // ---- Creating an encrypted document --------------------------------------------------------

    /// <summary>
    /// Builds an encryption dictionary and handler for a new document.
    /// </summary>
    /// <param name="userPassword">Required to open the document; empty for an unprompted open.</param>
    /// <param name="ownerPassword">Lifts the permission restrictions; defaults to the user password.</param>
    /// <param name="permissions">What a user-password holder may do.</param>
    /// <param name="cipher">The cipher; AES-256 unless a legacy reader must be supported.</param>
    /// <param name="firstFileId">The trailer's first <c>/ID</c> element.</param>
    public static (StandardSecurityHandler Handler, PdfDictionary Dictionary) Create(
        string userPassword,
        string? ownerPassword,
        PdfPermissions permissions,
        PdfCipher cipher,
        byte[] firstFileId)
    {
        ownerPassword = string.IsNullOrEmpty(ownerPassword) ? userPassword : ownerPassword;

        // Bits 1, 2, 7 and 8 are reserved and must be 1; the rest of the high bits must be 1 too.
        // Writing zeros there makes Acrobat report the file as damaged.
        var p = unchecked((int)((uint)permissions | 0xFFFFF0C0));

        return cipher == PdfCipher.Aes256
            ? CreateRevision6(userPassword, ownerPassword, p, permissions)
            : CreateLegacy(userPassword, ownerPassword, p, permissions, cipher, firstFileId);
    }

    private static (StandardSecurityHandler, PdfDictionary) CreateLegacy(string userPassword,
        string ownerPassword, int p, PdfPermissions permissions, PdfCipher cipher, byte[] firstFileId)
    {
        var revision = cipher switch
        {
            PdfCipher.Rc4_40 => 2,
            PdfCipher.Rc4_128 => 3,
            _ => 4,
        };

        var keyLengthBytes = cipher == PdfCipher.Rc4_40 ? 5 : 16;

        var userBytes = EncodeLegacyPassword(userPassword);
        var ownerBytes = EncodeLegacyPassword(ownerPassword);

        // /O is the user password encrypted with a key derived from the owner password.
        using var md5 = MD5.Create();
        var ownerHash = md5.ComputeHash(Pad(ownerBytes));
        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                ownerHash = md5.ComputeHash(ownerHash);
            }
        }

        var ownerKey = ownerHash[..keyLengthBytes];
        var oValue = Pad(userBytes);

        if (revision == 2)
        {
            oValue = Rc4.Transform(ownerKey, oValue);
        }
        else
        {
            for (var round = 0; round <= 19; round++)
            {
                var roundKey = new byte[ownerKey.Length];
                for (var i = 0; i < ownerKey.Length; i++)
                {
                    roundKey[i] = (byte)(ownerKey[i] ^ round);
                }

                oValue = Rc4.Transform(roundKey, oValue);
            }
        }

        var fileKey = ComputeLegacyKey(userBytes, oValue, p, firstFileId, revision, keyLengthBytes, true);

        byte[] uValue;
        if (revision == 2)
        {
            uValue = Rc4.Transform(fileKey, Pad([]));
        }
        else
        {
            using var buffer = new MemoryStream();
            buffer.Write(PadBytes);
            buffer.Write(firstFileId);
            var hash = MD5.HashData(buffer.ToArray());

            var value = Rc4.Transform(fileKey, hash);
            for (var round = 1; round <= 19; round++)
            {
                var roundKey = new byte[fileKey.Length];
                for (var i = 0; i < fileKey.Length; i++)
                {
                    roundKey[i] = (byte)(fileKey[i] ^ round);
                }

                value = Rc4.Transform(roundKey, value);
            }

            // /U is 32 bytes: the 16 meaningful ones plus arbitrary padding.
            uValue = new byte[32];
            value.AsSpan(0, 16).CopyTo(uValue);
            RandomNumberGenerator.Fill(uValue.AsSpan(16));
        }

        var dictionary = new PdfDictionary();
        dictionary.SetName(PdfName.Get("Filter"), "Standard");
        dictionary.Set(PdfName.Get("V"), cipher == PdfCipher.Aes128 ? 4 : revision == 2 ? 1 : 2);
        dictionary.Set(PdfName.Get("R"), revision);
        dictionary.Set(PdfName.Get("Length"), keyLengthBytes * 8);
        dictionary.Set(PdfName.Get("P"), p);
        dictionary[PdfName.Get("O")] = new PdfString(oValue) { IsHex = true };
        dictionary[PdfName.Get("U")] = new PdfString(uValue) { IsHex = true };

        if (cipher == PdfCipher.Aes128)
        {
            var standardFilter = new PdfDictionary();
            standardFilter.SetName(PdfName.Get("CFM"), "AESV2");
            standardFilter.SetName(PdfName.Get("AuthEvent"), "DocOpen");
            standardFilter.Set(PdfName.Get("Length"), 16);

            var cryptFilters = new PdfDictionary();
            cryptFilters[PdfName.Get("StdCF")] = standardFilter;

            dictionary[PdfName.Get("CF")] = cryptFilters;
            dictionary.SetName(PdfName.Get("StmF"), "StdCF");
            dictionary.SetName(PdfName.Get("StrF"), "StdCF");
        }

        var handler = new StandardSecurityHandler(fileKey, cipher, revision, permissions, true, true);
        return (handler, dictionary);
    }

    private static (StandardSecurityHandler, PdfDictionary) CreateRevision6(string userPassword,
        string ownerPassword, int p, PdfPermissions permissions)
    {
        const int Revision = 6;

        var fileKey = RandomNumberGenerator.GetBytes(32);

        var userBytes = Encoding.UTF8.GetBytes(userPassword);
        var ownerBytes = Encoding.UTF8.GetBytes(ownerPassword);

        var userValidationSalt = RandomNumberGenerator.GetBytes(8);
        var userKeySalt = RandomNumberGenerator.GetBytes(8);
        var uValue = new byte[48];
        Hash2B(userBytes, userValidationSalt, [], Revision).CopyTo(uValue, 0);
        userValidationSalt.CopyTo(uValue, 32);
        userKeySalt.CopyTo(uValue, 40);

        var userIntermediate = Hash2B(userBytes, userKeySalt, [], Revision);
        var ue = EncryptCbcNoPadding(userIntermediate, new byte[16], fileKey);

        var ownerValidationSalt = RandomNumberGenerator.GetBytes(8);
        var ownerKeySalt = RandomNumberGenerator.GetBytes(8);
        var oValue = new byte[48];
        Hash2B(ownerBytes, ownerValidationSalt, uValue, Revision).CopyTo(oValue, 0);
        ownerValidationSalt.CopyTo(oValue, 32);
        ownerKeySalt.CopyTo(oValue, 40);

        var ownerIntermediate = Hash2B(ownerBytes, ownerKeySalt, uValue, Revision);
        var oe = EncryptCbcNoPadding(ownerIntermediate, new byte[16], fileKey);

        // /Perms is the permission bits encrypted with the file key in ECB mode, so a reader can
        // detect a file whose /P has been edited outside the encryption.
        var perms = new byte[16];
        BitConverter.TryWriteBytes(perms.AsSpan(0, 4), p);
        if (!BitConverter.IsLittleEndian)
        {
            perms.AsSpan(0, 4).Reverse();
        }

        perms[4] = 0xFF;
        perms[5] = 0xFF;
        perms[6] = 0xFF;
        perms[7] = 0xFF;
        perms[8] = (byte)'T';
        perms[9] = (byte)'a';
        perms[10] = (byte)'d';
        perms[11] = (byte)'b';
        RandomNumberGenerator.Fill(perms.AsSpan(12, 4));

        using var ecb = Aes.Create();
        ecb.Key = fileKey;
        ecb.Mode = CipherMode.ECB;
        ecb.Padding = PaddingMode.None;
        using var permsEncryptor = ecb.CreateEncryptor();
        var permsEncrypted = permsEncryptor.TransformFinalBlock(perms, 0, 16);

        var standardFilter = new PdfDictionary();
        standardFilter.SetName(PdfName.Get("CFM"), "AESV3");
        standardFilter.SetName(PdfName.Get("AuthEvent"), "DocOpen");
        standardFilter.Set(PdfName.Get("Length"), 32);

        var cryptFilters = new PdfDictionary();
        cryptFilters[PdfName.Get("StdCF")] = standardFilter;

        var dictionary = new PdfDictionary();
        dictionary.SetName(PdfName.Get("Filter"), "Standard");
        dictionary.Set(PdfName.Get("V"), 5);
        dictionary.Set(PdfName.Get("R"), Revision);
        dictionary.Set(PdfName.Get("Length"), 256);
        dictionary.Set(PdfName.Get("P"), p);
        dictionary[PdfName.Get("CF")] = cryptFilters;
        dictionary.SetName(PdfName.Get("StmF"), "StdCF");
        dictionary.SetName(PdfName.Get("StrF"), "StdCF");
        dictionary[PdfName.Get("U")] = new PdfString(uValue) { IsHex = true };
        dictionary[PdfName.Get("UE")] = new PdfString(ue) { IsHex = true };
        dictionary[PdfName.Get("O")] = new PdfString(oValue) { IsHex = true };
        dictionary[PdfName.Get("OE")] = new PdfString(oe) { IsHex = true };
        dictionary[PdfName.Get("Perms")] = new PdfString(permsEncrypted) { IsHex = true };

        var handler = new StandardSecurityHandler(fileKey, PdfCipher.Aes256, Revision, permissions, true, true);
        return (handler, dictionary);
    }
}

/// <summary>
/// RC4, required by PDF revisions 2 through 4.
/// </summary>
/// <remarks>
/// RC4 is cryptographically broken and is implemented here only because the file format mandates
/// it for existing documents. It is never selected for a document this library creates unless the
/// caller explicitly asks for a legacy cipher.
/// </remarks>
internal static class Rc4
{
    internal static byte[] Transform(byte[] key, byte[] data)
    {
        Span<byte> s = stackalloc byte[256];
        for (var i = 0; i < 256; i++)
        {
            s[i] = (byte)i;
        }

        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var result = new byte[data.Length];
        int x = 0, y = 0;

        for (var n = 0; n < data.Length; n++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            (s[x], s[y]) = (s[y], s[x]);
            result[n] = (byte)(data[n] ^ s[(s[x] + s[y]) & 0xFF]);
        }

        return result;
    }
}
