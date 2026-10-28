// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet;
using OfficeNet;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.TestKit;
using PowerPointNet;
using WordNet;
using Xunit;

namespace OfficeNet.Docs.Tests;

/// <summary>
/// Password-encrypted documents through the public entry points, against files Office encrypted.
/// </summary>
public class EncryptedDocumentTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Encryption", name);

    [Theory]
    [InlineData("excel-encrypted.xlsx", "kata sandi Excel", typeof(Workbook), "Dari Excel")]
    [InlineData("word-encrypted.docx", "sandi Word", typeof(WordDocument), "Dokumen dari Word")]
    [InlineData("powerpoint-encrypted.pptx", "sandi PowerPoint", typeof(Presentation), "Slide dari PowerPoint")]
    public void OfficeOpensWhatOfficeEncryptedAsTheRightType(string file, string password, Type type, string text)
    {
        var path = Fixture(file);

        // Not "legacy binary", which is what the first bytes alone would say.
        Assert.Equal(OfficeFormat.Encrypted, Office.DetectFormat(path));
        Assert.Throws<OfficeNetPasswordException>(() => Office.Open(path));
        Assert.Throws<OfficeNetPasswordException>(() => Office.Open(path, "salah"));
        Assert.Throws<OfficeNetPasswordException>(() => Office.ConvertToPdf(path));

        using var document = Office.Open(path, password);
        Assert.IsType(type, document);
        Assert.Contains(text, document.ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AWorkbookSavedWithAPasswordIsEncryptedAndStaysSo()
    {
        using var folder = new TempDirectory();
        var path = Path.Combine(folder.Path, "gaji.xlsx");

        using (var workbook = Workbook.Create("Gaji"))
        {
            workbook.Worksheets[0]["A1"].Set("Rahasia");
            workbook.Password = "Sandi-Ku 123";
            workbook.Save(path);
        }

        Assert.Equal(OfficeFormat.Encrypted, Office.DetectFormat(path));
        Assert.Throws<OfficeNetPasswordException>(() => Workbook.Open(path));

        using (var reopened = Workbook.Open(path, "Sandi-Ku 123"))
        {
            Assert.Equal("Rahasia", reopened.Worksheets[0]["A1"].Value.ToString());
            Assert.Equal("Sandi-Ku 123", reopened.Password);

            // Saving over it keeps the protection, as Office does.
            reopened.Worksheets[0]["B1"].Set(42);
            reopened.Save();
        }

        Assert.True(PackageEncryption.IsEncrypted(File.ReadAllBytes(path)));

        using (var again = Workbook.Open(File.ReadAllBytes(path), "Sandi-Ku 123"))
        {
            again.Password = null;
            again.Save(path);
        }

        Assert.Equal(OfficeFormat.Excel, Office.DetectFormat(path));
    }

    [Fact]
    public void WordAndPowerPointSaveEncryptedToo()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Surat rahasia");
        document.Password = "a";

        using (var back = WordDocument.Open(new MemoryStream(document.ToArray()), "a"))
        {
            Assert.Contains("Surat rahasia", back.ExtractText(), StringComparison.Ordinal);
        }

        using var deck = Presentation.Create();
        deck.Password = "b";
        Assert.True(PackageEncryption.IsEncrypted(deck.ToArray()));
        Assert.Throws<OfficeNetPasswordException>(() => Presentation.Open(deck.ToArray(), "a"));
    }
}
