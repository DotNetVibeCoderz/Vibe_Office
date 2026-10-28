// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using OfficeNet.Core.Xml;

namespace WordNet.Protection;

/// <summary>What a protected document still lets a reader do.</summary>
public enum DocumentEditRestriction
{
    /// <summary>Nothing but reading, except inside ranges opened with <c>AllowEditing</c>.</summary>
    ReadOnly,

    /// <summary>Adding comments only.</summary>
    Comments,

    /// <summary>Any edit, but every edit is tracked and cannot be accepted or rejected.</summary>
    TrackedChanges,

    /// <summary>Filling in form fields only.</summary>
    Forms,
}

/// <summary>
/// The document's editing restriction (<c>w:documentProtection</c> in <c>settings.xml</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not security</b>. The document is not encrypted, the restriction is a flag any
/// editor can clear by editing the XML, and the password only decides whether Word's own "Stop
/// Protection" button works. It exists to stop accidental edits to a template or a contract.
/// </para>
/// <para>
/// The password hash is not the one Excel uses for sheets. Word first reduces the password to the
/// 32-bit key of its 1997-era scheme (high word from the encryption matrix, low word from the XOR
/// verifier), writes that key's bytes reversed as eight hex digits, and only then runs the salted,
/// iterated SHA-512 over the UTF-16 of those digits. Hashing the password directly gives a file
/// Word protects correctly and then refuses to unprotect with the right password.
/// </para>
/// </remarks>
public sealed class DocumentProtection
{
    private readonly XElement _element;

    internal DocumentProtection(XElement element) => _element = element;

    /// <summary>The restriction in force.</summary>
    public DocumentEditRestriction Restriction => _element.Attr(Ns.W + "edit") switch
    {
        "comments" => DocumentEditRestriction.Comments,
        "trackedChanges" => DocumentEditRestriction.TrackedChanges,
        "forms" => DocumentEditRestriction.Forms,
        _ => DocumentEditRestriction.ReadOnly,
    };

    /// <summary>Whether the restriction is enforced; a present but unenforced one does nothing.</summary>
    public bool IsEnforced => XmlUtil.OoxmlBool(_element.Attr(Ns.W + "enforcement")) == true;

    /// <summary>Whether a password is needed to lift the restriction in Word.</summary>
    public bool HasPassword => _element.Attr(Ns.W + "hash") is not null ||
                               _element.Attr(Ns.W + "hashValue") is not null;

    /// <summary>
    /// Checks a password against the stored hash.
    /// </summary>
    /// <returns>
    /// <c>true</c> or <c>false</c>, or <c>null</c> when the hash uses a scheme this library does
    /// not implement — ISO-style <c>algorithmName</c> attributes, or an algorithm id outside SHA-1
    /// and the SHA-2 family. Answering <c>false</c> there would claim a mismatch nobody checked.
    /// </returns>
    public bool? VerifyPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var hash = _element.Attr(Ns.W + "hash");
        var salt = _element.Attr(Ns.W + "salt");

        if (hash is null || salt is null ||
            !int.TryParse(_element.Attr(Ns.W + "cryptSpinCount"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var spinCount) ||
            AlgorithmFor(_element.Attr(Ns.W + "cryptAlgorithmSid")) is not { } algorithm)
        {
            return null;
        }

        // A file can claim any spin count; Word itself caps it at ten million.
        if (spinCount is < 0 or > 10_000_000)
        {
            return null;
        }

        byte[] expected, saltBytes;

        try
        {
            expected = Convert.FromBase64String(hash);
            saltBytes = Convert.FromBase64String(salt);
        }
        catch (FormatException)
        {
            return null;
        }

        var actual = Hash(password, saltBytes, spinCount, algorithm);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // ---- Writing -------------------------------------------------------------------------------

    internal const int DefaultSpinCount = 100_000;

    /// <summary>Builds the element for a restriction, hashing the password with SHA-512.</summary>
    internal static XElement Build(DocumentEditRestriction restriction, string? password,
        int spinCount = DefaultSpinCount)
    {
        var element = new XElement(Ns.W + "documentProtection",
            new XAttribute(Ns.W + "edit", restriction switch
            {
                DocumentEditRestriction.Comments => "comments",
                DocumentEditRestriction.TrackedChanges => "trackedChanges",
                DocumentEditRestriction.Forms => "forms",
                _ => "readOnly",
            }),
            new XAttribute(Ns.W + "enforcement", "1"));

        if (string.IsNullOrEmpty(password))
        {
            return element;
        }

        var salt = RandomNumberGenerator.GetBytes(16);

        element.Add(
            new XAttribute(Ns.W + "cryptProviderType", "rsaAES"),
            new XAttribute(Ns.W + "cryptAlgorithmClass", "hash"),
            new XAttribute(Ns.W + "cryptAlgorithmType", "typeAny"),
            new XAttribute(Ns.W + "cryptAlgorithmSid", "14"),
            new XAttribute(Ns.W + "cryptSpinCount", spinCount.ToString(CultureInfo.InvariantCulture)),
            new XAttribute(Ns.W + "hash",
                Convert.ToBase64String(Hash(password, salt, spinCount, HashAlgorithmName.SHA512))),
            new XAttribute(Ns.W + "salt", Convert.ToBase64String(salt)));

        return element;
    }

    // ---- The hash ------------------------------------------------------------------------------

    /// <summary>The salted, iterated hash of the legacy key's reversed hex form.</summary>
    /// <remarks>Public so another producer's file, or another implementation, can be checked against it.</remarks>
    public static byte[] Hash(string password, byte[] salt, int spinCount, HashAlgorithmName algorithm)
    {
        var input = Encoding.Unicode.GetBytes(LegacyKeyReversedHex(password));

        using var hasher = IncrementalHash.CreateHash(algorithm);
        hasher.AppendData(salt);
        hasher.AppendData(input);
        var hash = hasher.GetHashAndReset();

        Span<byte> iterator = stackalloc byte[4];

        for (var i = 0; i < spinCount; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(iterator, i);
            hasher.AppendData(hash);
            hasher.AppendData(iterator);

            if (!hasher.TryGetHashAndReset(hash, out _))
            {
                throw new CryptographicException("The hash buffer is the wrong size.");
            }
        }

        return hash;
    }

    /// <summary>
    /// The 32-bit legacy key as eight hex digits, low byte first: "Example" is 0x64CEED7E, written
    /// "7EEDCE64" (ECMA-376 Part 4, §2.15.1.28).
    /// </summary>
    public static string LegacyKeyReversedHex(string password)
    {
        var key = LegacyKey(password);

        return string.Create(CultureInfo.InvariantCulture,
            $"{key & 0xFF:X2}{(key >> 8) & 0xFF:X2}{(key >> 16) & 0xFF:X2}{(key >> 24) & 0xFF:X2}");
    }

    /// <summary>The legacy key: high word from the encryption matrix, low word the XOR verifier.</summary>
    public static uint LegacyKey(string password)
    {
        if (password.Length == 0)
        {
            return 0;
        }

        // Only the first fifteen characters count; each is reduced to one byte, its low byte unless
        // that is zero.
        var bytes = password.Length > 15 ? password[..15] : password;
        var chars = new byte[bytes.Length];

        for (var i = 0; i < bytes.Length; i++)
        {
            var low = (byte)(bytes[i] & 0xFF);
            chars[i] = low != 0 ? low : (byte)(bytes[i] >> 8);
        }

        int high = InitialCode[chars.Length - 1];
        var line = 15 - chars.Length;

        foreach (var c in chars)
        {
            var ch = c;

            foreach (var xor in Matrix[line++])
            {
                if ((ch & 1) == 1)
                {
                    high ^= xor;
                }

                ch >>= 1;
            }
        }

        // Bytes are unsigned here, as the spec defines them. Apache POI XORs Java's signed bytes
        // into a short, which sign-extends any character above 0x7F and gives a different key.
        int verifier = 0;

        for (var i = chars.Length - 1; i >= 0; i--)
        {
            verifier = Rotate(verifier) ^ chars[i];
        }

        verifier = Rotate(verifier) ^ chars.Length;
        verifier ^= 0xCE4B;

        return (uint)(verifier & 0xFFFF) | ((uint)(high & 0xFFFF) << 16);

        static int Rotate(int value) => ((value & 0x4000) == 0 ? 0 : 1) | ((value << 1) & 0x7FFF);
    }

    private static HashAlgorithmName? AlgorithmFor(string? sid) => sid switch
    {
        "4" => HashAlgorithmName.SHA1,
        "12" => HashAlgorithmName.SHA256,
        "13" => HashAlgorithmName.SHA384,
        "14" => HashAlgorithmName.SHA512,
        _ => null,
    };

    private static readonly int[] InitialCode =
    [
        0xE1F0, 0x1D0F, 0xCC9C, 0x84C0, 0x110C, 0x0E10, 0xF1CE, 0x313E, 0x1872, 0xE139, 0xD40F,
        0x84F9, 0x280C, 0xA96A, 0x4EC3,
    ];

    private static readonly int[][] Matrix =
    [
        [0xAEFC, 0x4DD9, 0x9BB2, 0x2745, 0x4E8A, 0x9D14, 0x2A09],
        [0x7B61, 0xF6C2, 0xFDA5, 0xEB6B, 0xC6F7, 0x9DCF, 0x2BBF],
        [0x4563, 0x8AC6, 0x05AD, 0x0B5A, 0x16B4, 0x2D68, 0x5AD0],
        [0x0375, 0x06EA, 0x0DD4, 0x1BA8, 0x3750, 0x6EA0, 0xDD40],
        [0xD849, 0xA0B3, 0x5147, 0xA28E, 0x553D, 0xAA7A, 0x44D5],
        [0x6F45, 0xDE8A, 0xAD35, 0x4A4B, 0x9496, 0x390D, 0x721A],
        [0xEB23, 0xC667, 0x9CEF, 0x29FF, 0x53FE, 0xA7FC, 0x5FD9],
        [0x47D3, 0x8FA6, 0x0F6D, 0x1EDA, 0x3DB4, 0x7B68, 0xF6D0],
        [0xB861, 0x60E3, 0xC1C6, 0x93AD, 0x377B, 0x6EF6, 0xDDEC],
        [0x45A0, 0x8B40, 0x06A1, 0x0D42, 0x1A84, 0x3508, 0x6A10],
        [0xAA51, 0x4483, 0x8906, 0x022D, 0x045A, 0x08B4, 0x1168],
        [0x76B4, 0xED68, 0xCAF1, 0x85C3, 0x1BA7, 0x374E, 0x6E9C],
        [0x3730, 0x6E60, 0xDCC0, 0xA9A1, 0x4363, 0x86C6, 0x1DAD],
        [0x3331, 0x6662, 0xCCC4, 0x89A9, 0x0373, 0x06E6, 0x0DCC],
        [0x1021, 0x2042, 0x4084, 0x8108, 0x1231, 0x2462, 0x48C4],
    ];
}
