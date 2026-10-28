// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using PowerPointNet.Shapes;
using Xunit;

namespace PowerPointNet.Tests;

/// <summary>
/// Files embedded as OLE objects.
/// </summary>
/// <remarks>
/// Checked by hand in PowerPoint through COM: it reads every object as type 7 with its ProgID,
/// activating the workbook starts Excel inside the deck and reads its cells, the document likewise in
/// Word, and a re-save keeps the packaged PDF with its name. python-pptx reads the same ProgIDs and
/// blobs, and openpyxl and python-docx open the blobs.
/// </remarks>
public class EmbeddedObjectTests
{
    private static Length Pt(double value) => Length.FromPoints(value);

    /// <summary>An Office file to embed: a deck, since this project does not reference ExcelNet.</summary>
    private static byte[] Workbook()
    {
        using var inner = Presentation.Create();
        inner.AddSlide().AddTextBox("Tertanam", Pt(10), Pt(10), Pt(200), Pt(40));
        return inner.ToArray();
    }

    [Fact]
    public void AnOfficeFileIsStoredAsItselfUnderItsApplicationsProgId()
    {
        var xlsx = Workbook();

        using var deck = Presentation.Create();
        var embedded = deck.AddSlide().AddEmbeddedObject(xlsx, "Anggaran.xlsx", Pt(40), Pt(60), Pt(260), Pt(160));

        Assert.Equal("Excel.Sheet.12", embedded.ProgId);

        var bytes = deck.ToArray();
        PptxValidator.AssertValid(bytes);

        // A package part, related by the package relationship, with the workbook's content type.
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            var entry = Assert.Single(zip.Entries, e => e.FullName.StartsWith("ppt/embeddings/", StringComparison.Ordinal));
            Assert.Equal("ppt/embeddings/Microsoft_Excel_Worksheet1.xlsx", entry.FullName);

            using var types = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
            Assert.Contains("Extension=\"xlsx\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet\"", types.ReadToEnd(), StringComparison.Ordinal);

            using var rels = new StreamReader(zip.GetEntry("ppt/slides/_rels/slide1.xml.rels")!.Open());
            Assert.Contains("relationships/package\" Target=\"../embeddings/Microsoft_Excel_Worksheet1.xlsx", rels.ReadToEnd(), StringComparison.Ordinal);
        }

        using var reopened = Presentation.Open(bytes);
        var read = Assert.Single(reopened.Slides[0].EmbeddedObjects);

        Assert.Equal("Excel.Sheet.12", read.ProgId);
        Assert.Equal(xlsx, read.GetData());
        Assert.False(read.ShowAsIcon);

        using var inner = Presentation.Open(read.GetData()!);
        Assert.Contains("Tertanam", inner.ExtractText(), StringComparison.Ordinal);

        // The generated preview is a real PNG.
        Assert.Equal((byte)0x89, read.GetPreviewImage()![0]);
    }

    [Theory]
    [InlineData("Surat.docx", "Word.Document.12")]
    [InlineData("Deck.pptx", "PowerPoint.Show.12")]
    [InlineData("Makro.xlsm", "Excel.SheetMacroEnabled.12")]
    public void EachOfficeFormatGetsItsOwnProgId(string name, string progId)
    {
        using var deck = Presentation.Create();
        var embedded = deck.AddSlide().AddEmbeddedObject(Workbook(), name, Pt(10), Pt(10), Pt(100), Pt(100));
        Assert.Equal(progId, embedded.ProgId);
    }

    [Fact]
    public void AnythingElseIsAPackageObjectAsPowerPointWritesIt()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 not really, but bytes are bytes");

        using var deck = Presentation.Create();
        deck.AddSlide().AddEmbeddedObject(pdf, "Laporan Ω Ü.pdf", Pt(500), Pt(60), Pt(80), Pt(80), showAsIcon: true);
        var bytes = deck.ToArray();
        PptxValidator.AssertValid(bytes);

        byte[] bin;

        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            var entry = zip.GetEntry("ppt/embeddings/oleObject1.bin")!;
            using var buffer = new MemoryStream();
            entry.Open().CopyTo(buffer);
            bin = buffer.ToArray();
        }

        var file = CompoundFile.Read(bin);
        Assert.Equal(Guid.Parse("0003000c-0000-0000-c000-000000000046"), file.RootClassId);

        // Byte for byte the CompObj stream PowerPoint wrote when it embedded a PDF itself.
        Assert.Equal(Convert.FromHexString(
                "0100FEFF030A0000FFFFFFFF0C00030000000000C0000000000000460C0000004F4C45205061636B6167650000000000080000005061636B61676500F439B271000000000000000000000000"),
            file.Streams["\u0001CompObj"]);

        // Ole10Native: its size, type 2, the name, ..., the data.
        var native = file.Streams["\u0001Ole10Native"];
        Assert.Equal((uint)native.Length - 4, BinaryPrimitives.ReadUInt32LittleEndian(native));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(4)));
        Assert.True(native.AsSpan().IndexOf(pdf) > 0);

        using var reopened = Presentation.Open(bytes);
        var read = Assert.Single(reopened.Slides[0].EmbeddedObjects);

        // Ω is not in the ANSI copy of the name; only the UTF-16 copy that follows the data has it.
        Assert.Equal("Package", read.ProgId);
        Assert.Equal("Laporan Ω Ü.pdf", read.FileName);
        Assert.Equal(pdf, read.GetData());
        Assert.True(read.ShowAsIcon);
    }

    [Fact]
    public void PowerPointsOwnMarkupReadsBack()
    {
        // PowerPoint wraps the object in mc:AlternateContent: a VML choice without a picture, and a
        // fallback with one. This is its markup, with the relationship ids of this deck.
        using var deck = Presentation.Create();
        var slide = deck.AddSlide();
        var made = slide.AddEmbeddedObject(Workbook(), "a.xlsx", Pt(10), Pt(10), Pt(100), Pt(100));

        var oleObj = made.Element.Descendants().First(e => e.Name.LocalName == "oleObj");
        var data = oleObj.Parent!;
        XNamespace mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        var choice = new XElement(oleObj);
        choice.Elements().Where(e => e.Name.LocalName == "pic").Remove();
        oleObj.Remove();
        data.Add(new XElement(mc + "AlternateContent",
            new XElement(mc + "Choice", new XAttribute("Requires", "v"), choice),
            new XElement(mc + "Fallback", oleObj)));

        var read = Assert.Single(slide.EmbeddedObjects);
        Assert.Equal("Excel.Sheet.12", read.ProgId);
        Assert.NotNull(read.GetData());
        Assert.NotNull(read.GetPreviewImage());
    }

    [Fact]
    public void AFileNamedAsOfficeButNotAZipIsRefused()
    {
        using var deck = Presentation.Create();
        Assert.Throws<ArgumentException>(() =>
            deck.AddSlide().AddEmbeddedObject("bukan zip"u8.ToArray(), "palsu.xlsx", Pt(0), Pt(0), Pt(10), Pt(10)));
    }

    [Fact]
    public void ThePdfExportDrawsThePreview()
    {
        using var deck = Presentation.Create();
        var preview = OfficeNet.TestKit.TestImages.SolidPng(40, 30, 200, 30, 30);
        deck.AddSlide().AddEmbeddedObject(Workbook(), "a.xlsx", Pt(10), Pt(10), Pt(200), Pt(150), preview);

        using var stream = new MemoryStream();
        deck.SaveAsPdf(stream);
        using var pdf = PdfNet.Document.PdfDocument.Open(new MemoryStream(stream.ToArray()));
        Assert.Single(pdf.Pages[0].ExtractImages());
    }
}
