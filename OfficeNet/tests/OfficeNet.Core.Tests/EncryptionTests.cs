// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Text;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using Xunit;

namespace OfficeNet.Core.Tests;

/// <summary>
/// Agile encryption, checked against files the reference implementations wrote.
/// </summary>
/// <remarks>
/// The fixtures in <c>Encryption/</c> were encrypted by Excel, Word and PowerPoint themselves (through
/// COM, "Encrypt with Password") and by msoffcrypto-tool. The other direction was checked by hand
/// the same way: Excel opened a workbook encrypted here, with the password and not without it, and
/// msoffcrypto decrypted one with its integrity check on.
/// </remarks>
public class EncryptionTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Encryption", name));

    private static string PackageText(byte[] package)
    {
        using var opened = OpcPackage.Open(new MemoryStream(package, writable: false));
        return string.Concat(opened.Parts.Select(p => Encoding.UTF8.GetString(p.GetBytes())));
    }

    [Theory]
    [InlineData("excel-encrypted.xlsx", "kata sandi Excel", "Dari Excel")]
    [InlineData("word-encrypted.docx", "sandi Word", "Dokumen dari Word")]
    [InlineData("powerpoint-encrypted.pptx", "sandi PowerPoint", "Slide dari PowerPoint")]
    [InlineData("msoffcrypto-encrypted.xlsx", "rahasia ü", "Dari Python")]
    public void FilesOfficeEncryptedOpenWithTheirPassword(string file, string password, string text)
    {
        var bytes = Fixture(file);
        Assert.True(PackageEncryption.IsEncrypted(bytes));

        Assert.Contains(text, PackageText(PackageEncryption.Decrypt(bytes, password)), StringComparison.Ordinal);

        // Through the package, which remembers the password so a save stays encrypted.
        using var package = OpcPackage.Open(new MemoryStream(bytes), password);
        Assert.Equal(password, package.Password);
    }

    [Fact]
    public void AWrongOrMissingPasswordIsRefusedAsSuch()
    {
        var bytes = Fixture("excel-encrypted.xlsx");

        Assert.Throws<OfficeNetPasswordException>(() => PackageEncryption.Decrypt(bytes, "salah"));
        Assert.Throws<OfficeNetPasswordException>(() => OpcPackage.Open(new MemoryStream(bytes)));

        // A legacy binary file is a compound file too, and is not mistaken for an encrypted one.
        var legacy = new CompoundFile();
        legacy.Streams["Workbook"] = new byte[100];
        var error = Assert.Throws<OfficeNetException>(() => OpcPackage.Open(new MemoryStream(legacy.ToArray()), "x"));
        Assert.IsNotType<OfficeNetPasswordException>(error);
        Assert.Contains("legacy", error.Message, StringComparison.Ordinal);
    }

    private static byte[] SamplePackage(out string marker)
    {
        marker = "Isi rahasia " + Guid.NewGuid();
        using var package = OpcPackage.Create();

        // Big enough to span several 4096-byte segments, each with its own IV.
        var body = string.Concat(Enumerable.Repeat(marker + " ", 2000));
        package.AddPart(new OpcPartName("/data/secret.xml"), "application/xml", Encoding.UTF8.GetBytes($"<s>{body}</s>"));
        return package.ToArray();
    }

    [Fact]
    public void APackageSavedWithAPasswordComesBackAndStaysEncrypted()
    {
        var plain = SamplePackage(out var marker);

        using var package = OpcPackage.Open(new MemoryStream(plain));
        package.Password = "Sandi-Ku 123";
        var encrypted = package.ToArray();

        Assert.True(CompoundFile.IsCompoundFile(encrypted));
        Assert.True(PackageEncryption.IsEncrypted(encrypted));
        Assert.Equal(-1, encrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker)));

        using var reopened = OpcPackage.Open(new MemoryStream(encrypted), "Sandi-Ku 123");
        Assert.Contains(marker, Encoding.UTF8.GetString(reopened.GetPart("/data/secret.xml").GetBytes()), StringComparison.Ordinal);

        // Saved again: still encrypted. Password removed: a zip again.
        Assert.True(PackageEncryption.IsEncrypted(reopened.ToArray()));
        reopened.Password = null;
        Assert.Equal((byte)'P', reopened.ToArray()[0]);

        // A password given for a plain package changes nothing on its own.
        using var plainOpened = OpcPackage.Open(new MemoryStream(plain), "tidak dipakai");
        Assert.Null(plainOpened.Password);
    }

    [Fact]
    public void EveryEncryptionIsFreshAndTheParametersAreOfficesOwn()
    {
        var plain = SamplePackage(out _);
        var first = PackageEncryption.Encrypt(plain, "sama");
        var second = PackageEncryption.Encrypt(plain, "sama");

        // Random salts and key: the same document and password never encrypt alike.
        var a = CompoundFile.Read(first).Streams["EncryptedPackage"];
        var b = CompoundFile.Read(second).Streams["EncryptedPackage"];
        Assert.False(a.AsSpan(8, 64).SequenceEqual(b.AsSpan(8, 64)));
        Assert.Equal((ulong)plain.Length, BinaryPrimitives.ReadUInt64LittleEndian(a));

        var info = CompoundFile.Read(first).Streams["EncryptionInfo"];
        Assert.Equal([4, 0, 4, 0, 0x40, 0, 0, 0], info[..8]);

        var xml = Encoding.UTF8.GetString(info, 8, info.Length - 8);
        Assert.Contains("""keyBits="256" hashSize="64" cipherAlgorithm="AES" cipherChaining="ChainingModeCBC" hashAlgorithm="SHA512" """, xml, StringComparison.Ordinal);
        Assert.Contains("""spinCount="100000" """, xml, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDataSpacesStorageIsByteForByteWhatOfficeExpects()
    {
        // Reference bytes from herumi/msoffice's resource.hpp, as msoffcrypto-tool also carries them.
        var streams = CompoundFile.Read(PackageEncryption.Encrypt([1, 2, 3], "x", spinCount: 1)).Streams;

        Assert.Equal(Convert.FromHexString(
            "3C0000004D006900630072006F0073006F00660074002E0043006F006E007400610069006E00650072002E004400610074006100530070006100630065007300010000000100000001000000"),
            streams["\u0006DataSpaces/Version"]);
        Assert.Equal(Convert.FromHexString(
            "08000000010000006800000001000000000000002000000045006E0063007200790070007400650064005000610063006B00610067006500320000005300740072006F006E00670045006E006300720079007000740069006F006E004400610074006100530070006100630065000000"),
            streams["\u0006DataSpaces/DataSpaceMap"]);
        Assert.Equal(Convert.FromHexString(
            "0800000001000000320000005300740072006F006E00670045006E006300720079007000740069006F006E005400720061006E00730066006F0072006D000000"),
            streams["\u0006DataSpaces/DataSpaceInfo/StrongEncryptionDataSpace"]);
        Assert.Equal(Convert.FromHexString(
            "58000000010000004C0000007B00460046003900410033004600300033002D0035003600450046002D0034003600310033002D0042004400440035002D003500410034003100430031004400300037003200340036007D004E0000004D006900630072006F0073006F00660074002E0043006F006E007400610069006E00650072002E0045006E006300720079007000740069006F006E005400720061006E00730066006F0072006D00000001000000010000000100000000000000000000000000000004000000"),
            streams["\u0006DataSpaces/TransformInfo/StrongEncryptionTransform/\u0006Primary"]);
    }

    [Fact]
    public void AnAlteredPackageFailsItsIntegrityCheck()
    {
        var encrypted = PackageEncryption.Encrypt(SamplePackage(out _), "sandi", spinCount: 10);
        var file = CompoundFile.Read(encrypted);
        var stored = file.Streams["EncryptedPackage"];
        stored[stored.Length / 2] ^= 0x01;

        var error = Assert.Throws<OfficeNetException>(() => PackageEncryption.Decrypt(file.ToArray(), "sandi"));
        Assert.IsNotType<OfficeNetPasswordException>(error);
        Assert.Contains("integrity", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardEncryptionIsRefusedByName()
    {
        var file = new CompoundFile();
        file.Streams["EncryptionInfo"] = [3, 0, 2, 0, 0x24, 0, 0, 0];
        file.Streams["EncryptedPackage"] = new byte[16];

        var error = Assert.Throws<OfficeNetNotSupportedException>(() => PackageEncryption.Decrypt(file.ToArray(), "x"));
        Assert.Contains("Standard", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordsAreLimitedAsInOffice()
    {
        Assert.Throws<ArgumentException>(() => PackageEncryption.Encrypt([1], ""));
        Assert.ThrowsAny<ArgumentException>(() => PackageEncryption.Encrypt([1], new string('a', 256)));

        using var package = OpcPackage.Create();
        Assert.ThrowsAny<ArgumentException>(() => package.Password = new string('a', 256));
    }
}

public class CompoundFileTests
{
    [Fact]
    public void StreamsInNestedStoragesRoundTrip()
    {
        var big = new byte[9_000_000];
        new Random(5).NextBytes(big);

        var file = new CompoundFile { RootClassId = Guid.Parse("0003000c-0000-0000-c000-000000000046") };
        file.Streams["Big"] = big;
        file.Streams["Dir/Small"] = [1, 2, 3];
        file.Streams["Dir/Empty"] = [];
        file.Streams["Dir/Deeper/Exactly4096"] = new byte[4096];
        file.StorageClassIds["Dir"] = Guid.Parse("11111111-2222-3333-4444-555555555555");

        for (var i = 0; i < 40; i++)
        {
            file.Streams[$"Many/S{i}"] = Enumerable.Repeat((byte)i, i * 97).ToArray();
        }

        var bytes = file.ToArray();
        var back = CompoundFile.Read(bytes);

        // 9 MB needs more FAT sectors than the header lists: a DIFAT sector.
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(72)));
        Assert.Equal(file.Streams.Count, back.Streams.Count);

        foreach (var (path, data) in file.Streams)
        {
            Assert.True(data.AsSpan().SequenceEqual(back.Streams[path]), path);
        }

        Assert.Equal(file.RootClassId, back.RootClassId);
        Assert.Equal(file.StorageClassIds["Dir"], back.StorageClassIds["Dir"]);
    }

    [Fact]
    public void SiblingsAreATreeOrderedByLengthThenName()
    {
        // MS-CFB 2.6.4: shorter names first, then case-insensitively. A reader that looks a name up
        // by walking the tree finds nothing in a tree built in insertion order.
        var file = new CompoundFile();

        foreach (var name in new[] { "zz", "B", "a", "Ccc", "bb" })
        {
            file.Streams[name] = [1];
        }

        var bytes = file.ToArray();
        var directory = (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(48)) + 1) * 512;

        string Name(int id)
        {
            var entry = bytes.AsSpan(directory + id * 128, 128);
            return Encoding.Unicode.GetString(entry[..(BinaryPrimitives.ReadUInt16LittleEndian(entry[64..]) - 2)]);
        }

        uint Link(int id, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(directory + id * 128 + offset));

        var inOrder = new List<string>();

        void Walk(uint id)
        {
            if (id == uint.MaxValue)
            {
                return;
            }

            Walk(Link((int)id, 68));
            inOrder.Add(Name((int)id));
            Walk(Link((int)id, 72));
        }

        Walk(Link(0, 76));
        Assert.Equal(["a", "B", "bb", "zz", "Ccc"], inOrder);
    }

    [Fact]
    public void ADamagedFileIsRefusedNotLoopedOver()
    {
        var file = new CompoundFile();
        file.Streams["Big"] = new byte[10_000];
        var bytes = file.ToArray();

        // "Big" occupies sectors 0, 1, 2. Point sector 1 back at sector 0.
        var fat = (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(76)) + 1) * 512;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(fat + 4), 0);

        Assert.Throws<OfficeNetException>(() => CompoundFile.Read(bytes));
        Assert.Throws<OfficeNetException>(() => CompoundFile.Read(new byte[600]));
    }

    [Fact]
    public void NamesFollowTheFormatsLimits()
    {
        var file = new CompoundFile();
        file.Streams[new string('n', 32)] = [1];
        Assert.Throws<ArgumentException>(file.ToArray);

        var clash = new CompoundFile();
        clash.Streams["A"] = [1];
        clash.Streams["a/B"] = [1];
        Assert.Throws<ArgumentException>(clash.ToArray);
    }
}
