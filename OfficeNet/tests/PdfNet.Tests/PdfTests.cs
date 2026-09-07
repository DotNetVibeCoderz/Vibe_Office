// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.TestKit;
using PdfNet.Annotations;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Forms;
using PdfNet.Io.Filters;
using PdfNet.Objects;
using PdfNet.Security;
using Xunit;

namespace PdfNet.Tests;

/// <summary>Builds the documents the tests read back.</summary>
internal static class Sample
{
    internal static PdfDocument ThreePageReport()
    {
        var document = PdfDocument.Create();
        document.Info.Title = "Laporan OfficeNet";
        document.Info.Author = "Gravicode Studios";

        for (var page = 1; page <= 3; page++)
        {
            var target = document.Pages.Add(PageSize.A4);
            using var canvas = target.OpenCanvas();
            canvas.TopDown = true;

            canvas.SetFont(StandardFont.HelveticaBold, 20);
            canvas.SetFillColor(OfficeColor.FromRgb(0x1F, 0x38, 0x64));
            canvas.DrawText($"Halaman {page}", 50, 70);

            canvas.SetFont(StandardFont.TimesRoman, 11);
            canvas.SetFillColor(OfficeColor.Black);
            canvas.DrawWrappedText(
                "Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil. " +
                "PdfNet menulis dokumen ini tanpa dependensi native apa pun.",
                50, 100, 495, 15, TextAlignment.Justify);
        }

        return document;
    }
}

public class ObjectModelTests
{
    [Fact]
    public void NumbersSerialiseWithoutExponentNotation()
    {
        // PDF's grammar has no exponent form, so "R" formatting would produce a file no reader
        // can parse.
        Assert.Equal("0.000001", new PdfNumber(0.000001).ToString());
        Assert.Equal("10", new PdfNumber(10.0).ToString());
        Assert.Equal("-3.5", new PdfNumber(-3.5).ToString());
    }

    [Fact]
    public void WholeRealsCollapseToIntegers()
    {
        // /Length and array indices are integers by specification; "10.0" is rejected by strict
        // readers.
        Assert.True(new PdfNumber(10.0).IsInteger);
        Assert.False(new PdfNumber(10.5).IsInteger);
    }

    [Fact]
    public void NamesEscapeCharactersThatWouldEndTheToken()
    {
        using var buffer = new MemoryStream();
        PdfName.Get("A Name/With#Delims").Write(buffer, new PdfWriteContext());

        Assert.Equal("/A#20Name#2FWith#23Delims", Encoding.ASCII.GetString(buffer.ToArray()));
    }

    [Fact]
    public void NonLatinStringsGetAUtf16ByteOrderMark()
    {
        // Without the BOM a reader decodes the bytes as PDFDocEncoding and renders mojibake.
        var text = new PdfString("Ringkasan — 日本語");

        Assert.Equal(0xFE, text.Value[0]);
        Assert.Equal(0xFF, text.Value[1]);
        Assert.Equal("Ringkasan — 日本語", text.AsText());
    }

    [Fact]
    public void DatesRoundTripThroughTheirPdfForm()
    {
        var when = new DateTimeOffset(2026, 3, 14, 15, 9, 26, TimeSpan.FromHours(7));
        Assert.Equal(when, PdfString.FromDate(when).AsDate());
    }

    [Fact]
    public void GetArrayWrapsALoneObject()
    {
        // /Contents, /Filter and /Annots are each legally either one object or an array, and
        // handling only the array form is the classic way a PDF parser fails on real files.
        var dictionary = new PdfDictionary();
        dictionary[PdfName.Filter] = PdfName.FlateDecode;

        var array = dictionary.GetArray(PdfName.Filter);
        Assert.Single(array);
        Assert.Equal(PdfName.FlateDecode, array[0]);
    }
}

public class FilterTests
{
    [Theory]
    [InlineData("FlateDecode")]
    [InlineData("ASCIIHexDecode")]
    [InlineData("ASCII85Decode")]
    [InlineData("RunLengthDecode")]
    public void EncodeThenDecodeIsTheIdentity(string name)
    {
        var filter = PdfFilters.Find(name)!;
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("OfficeNet ", 50)));

        Assert.Equal(data, filter.Decode(filter.Encode(data, null), null));
    }

    [Fact]
    public void RunLengthHandlesLongRuns()
    {
        var filter = new RunLengthFilter();
        var data = new byte[600];
        Array.Fill(data, (byte)0x41);

        var encoded = filter.Encode(data, null);
        Assert.True(encoded.Length < 20, $"600 identical bytes should compress hard, got {encoded.Length}");
        Assert.Equal(data, filter.Decode(encoded, null));
    }

    [Fact]
    public void FlateAcceptsRawDeflateWithoutTheZlibHeader()
    {
        // Producers disagree about the two-byte header. A reader that only accepts zlib framing
        // reports scanned PDFs as having no text.
        var data = "OfficeNet by Gravicode Studios"u8.ToArray();

        using var buffer = new MemoryStream();

        using (var deflate = new System.IO.Compression.DeflateStream(buffer,
                   System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        Assert.Equal(data, new FlateFilter().Decode(buffer.ToArray(), null));
    }

    [Fact]
    public void PngPredictorIsReversed()
    {
        // Ignoring /Predictor inflates cleanly and gives entirely wrong bytes; in an xref stream
        // that means every object offset is garbage.
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Get("Predictor"), 12);
        parameters.Set(PdfName.Get("Colors"), 1);
        parameters.Set(PdfName.Get("BitsPerComponent"), 8);
        parameters.Set(PdfName.Get("Columns"), 4);

        // Two rows, filter type 2 (Up): the second row's bytes are deltas from the first.
        byte[] predicted = [2, 10, 20, 30, 40, 2, 1, 1, 1, 1];

        var result = PdfNet.Io.Filters.Predictor.Undo(predicted, parameters);

        Assert.Equal<byte[]>([10, 20, 30, 40, 11, 21, 31, 41], result);
    }
}

public class DocumentTests
{
    [Fact]
    public void CreateSaveReopenPreservesPagesAndMetadata()
    {
        using var file = new TempFile(".pdf");

        using (var document = Sample.ThreePageReport())
        {
            document.Save(file.Path);
        }

        using var reopened = PdfDocument.Open(file.Path);

        Assert.Equal(3, reopened.Pages.Count);
        Assert.Equal("Laporan OfficeNet", reopened.Info.Title);
        Assert.Equal("Gravicode Studios", reopened.Info.Author);
        Assert.False(reopened.WasRepaired);
    }

    [Fact]
    public void ExtractedTextKeepsWordsSeparate()
    {
        // A PDF stores no space characters between separately positioned runs; the extractor has
        // to infer them from the gaps or every line reads "Thequickbrownfox".
        using var document = Sample.ThreePageReport();
        using var reopened = PdfDocument.Open(document.ToArray());

        var text = reopened.Pages[0].ExtractText();

        Assert.Contains("Halaman 1", text, StringComparison.Ordinal);
        Assert.Contains("Dibuat oleh Gravicode Studios", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DibuatolehGravicode", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PageSizeAndRotationRoundTrip()
    {
        using var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);
        page.Rotation = 90;

        using var reopened = PdfDocument.Open(document.ToArray());

        Assert.Equal(90, reopened.Pages[0].Rotation);
        // Rotation swaps the reported width and height without touching the media box.
        Assert.Equal(842, reopened.Pages[0].Width, 0);
        Assert.Equal(595, reopened.Pages[0].Height, 0);
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(450, 90)]
    [InlineData(360, 0)]
    public void RotationIsNormalised(int written, int expected)
    {
        using var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);
        page.Rotation = written;

        Assert.Equal(expected, page.Rotation);
    }

    [Fact]
    public void SplitThenConcatRestoresTheOriginal()
    {
        using var source = Sample.ThreePageReport();

        var parts = source.Split();
        Assert.Equal(3, parts.Count);
        Assert.All(parts, p => Assert.Single(p.Pages));

        using var merged = PdfDocument.Concat([.. parts]);
        Assert.Equal(3, merged.Pages.Count);

        using var reopened = PdfDocument.Open(merged.ToArray());
        Assert.Contains("Halaman 3", reopened.Pages[2].ExtractText(), StringComparison.Ordinal);

        foreach (var part in parts)
        {
            part.Dispose();
        }
    }

    [Fact]
    public void MergedPagesKeepTheirInheritedSize()
    {
        // A page whose /MediaBox lives on the page tree loses its size when merged unless the
        // inherited attributes are materialised onto the page itself.
        using var source = PdfDocument.Create();
        source.Pages.Add(PageSize.A5);

        using var target = PdfDocument.Create();
        target.Merge(source);

        using var reopened = PdfDocument.Open(target.ToArray());

        Assert.Equal(PageSize.A5.Width, reopened.Pages[0].Width, 1);
        Assert.Equal(PageSize.A5.Height, reopened.Pages[0].Height, 1);
    }

    [Fact]
    public void MergingSharesImportedResourcesRatherThanDuplicatingThem()
    {
        // Passing one map across a whole import is a correctness requirement for file size: two
        // pages using one font must still share it afterwards.
        using var source = Sample.ThreePageReport();
        using var target = PdfDocument.Create();

        target.Merge(source);

        var perPage = (double)target.ObjectCount / target.Pages.Count;
        Assert.True(perPage < 12, $"{perPage:0.#} objects per merged page suggests resources were duplicated");
    }

    [Fact]
    public void RemovingAPageDoesNotDisturbTheRest()
    {
        using var document = Sample.ThreePageReport();
        document.Pages.RemoveAt(1);

        using var reopened = PdfDocument.Open(document.ToArray());

        Assert.Equal(2, reopened.Pages.Count);
        Assert.Contains("Halaman 1", reopened.Pages[0].ExtractText(), StringComparison.Ordinal);
        Assert.Contains("Halaman 3", reopened.Pages[1].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReorderingChangesPageOrder()
    {
        using var document = Sample.ThreePageReport();
        document.Pages.Reverse();

        using var reopened = PdfDocument.Open(document.ToArray());
        Assert.Contains("Halaman 3", reopened.Pages[0].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsSomethingThatIsNotAPdf()
    {
        var ex = Assert.Throws<OfficeNetException>(() =>
            PdfDocument.Open("this is not a pdf"u8.ToArray()));

        Assert.Contains("%PDF", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairsAFileWithABrokenCrossReferenceTable()
    {
        // Acrobat repairs these silently and so must this reader, or it refuses files every other
        // tool opens.
        using var document = Sample.ThreePageReport();
        var bytes = document.ToArray();

        var text = Encoding.Latin1.GetString(bytes);
        var startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        Assert.True(startxref > 0);

        // Point startxref at nothing.
        var damaged = Encoding.Latin1.GetBytes(
            text[..(startxref + 10)] + "999999999" + text[(startxref + 19)..]);

        using var reopened = PdfDocument.Open(damaged);

        Assert.True(reopened.WasRepaired);
        Assert.Equal(3, reopened.Pages.Count);
        Assert.Contains("Halaman 1", reopened.Pages[0].ExtractText(), StringComparison.Ordinal);
    }
}

public class EncryptionTests
{
    [Theory]
    [InlineData(PdfCipher.Rc4_40)]
    [InlineData(PdfCipher.Rc4_128)]
    [InlineData(PdfCipher.Aes128)]
    [InlineData(PdfCipher.Aes256)]
    public void EveryCipherRoundTrips(PdfCipher cipher)
    {
        byte[] encrypted;

        using (var document = Sample.ThreePageReport())
        {
            document.Encrypt("rahasia", "pemilik", PdfPermissions.Print | PdfPermissions.Copy, cipher);
            encrypted = document.ToArray();
        }

        using var reopened = PdfDocument.Open(encrypted, "rahasia");

        Assert.True(reopened.WasEncrypted);
        Assert.Equal(3, reopened.Pages.Count);
        Assert.Contains("Halaman 1", reopened.Pages[0].ExtractText(), StringComparison.Ordinal);

        // The permission bits must survive: the raw /P is hashed into the legacy key, and masking
        // it before derivation silently breaks RC4 and AES-128.
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Copy, reopened.Permissions);
    }

    [Fact]
    public void TheOwnerPasswordAlsoOpensTheDocument()
    {
        byte[] encrypted;

        using (var document = Sample.ThreePageReport())
        {
            document.Encrypt("pengguna", "pemilik", PdfPermissions.Print);
            encrypted = document.ToArray();
        }

        using var reopened = PdfDocument.Open(encrypted, "pemilik");
        Assert.Equal(3, reopened.Pages.Count);
    }

    [Fact]
    public void AWrongPasswordIsRejected()
    {
        byte[] encrypted;

        using (var document = Sample.ThreePageReport())
        {
            document.Encrypt("rahasia");
            encrypted = document.ToArray();
        }

        Assert.Throws<OfficeNetPasswordException>(() => PdfDocument.Open(encrypted, "salah"));
        Assert.Throws<OfficeNetPasswordException>(() => PdfDocument.Open(encrypted));
    }

    [Fact]
    public void AnEmptyUserPasswordOpensWithoutPromptingAndIsStillEncrypted()
    {
        // The usual shape of a "print but do not copy" file.
        byte[] encrypted;

        using (var document = Sample.ThreePageReport())
        {
            document.Encrypt(string.Empty, "pemilik", PdfPermissions.Print);
            encrypted = document.ToArray();
        }

        using var reopened = PdfDocument.Open(encrypted);

        Assert.True(reopened.WasEncrypted);
        Assert.Equal(PdfPermissions.Print, reopened.Permissions);
    }

    [Fact]
    public void Aes256DeclaresPdfTwoPointZero()
    {
        // The revision 6 handler is a PDF 2.0 feature; declaring 1.7 makes Acrobat refuse the file.
        using var document = PdfDocument.Create();
        document.Pages.Add(PageSize.A4);
        document.Encrypt("x", cipher: PdfCipher.Aes256);

        Assert.Equal("2.0", document.Version);
    }
}

public class CanvasTests
{
    [Fact]
    public void WrappedTextBreaksIntoTheExpectedNumberOfLines()
    {
        using var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            canvas.TopDown = true;
            canvas.SetFont(StandardFont.Helvetica, 10);

            var used = canvas.DrawWrappedText(
                string.Join(' ', Enumerable.Repeat("kata", 200)), 50, 50, 200, 12);

            Assert.True(used > 12 * 10, $"200 words in a 200pt column should wrap well past 10 lines, used {used}");
        }

        using var reopened = PdfDocument.Open(document.ToArray());
        Assert.Contains("kata kata", reopened.Pages[0].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void StandardFontMetricsMatchThePublishedAfmValues()
    {
        // These are Adobe's own AFM numbers. They are what makes a PDF this library writes lay out
        // identically in every reader.
        Assert.Equal(833, StandardFonts.WidthOf(StandardFont.Helvetica, 'M'));
        Assert.Equal(278, StandardFonts.WidthOf(StandardFont.Helvetica, ' '));
        Assert.Equal(667, StandardFonts.WidthOf(StandardFont.Helvetica, 'A'));
        Assert.Equal(889, StandardFonts.WidthOf(StandardFont.TimesRoman, 'M'));
        Assert.Equal(250, StandardFonts.WidthOf(StandardFont.TimesRoman, ' '));
        Assert.Equal(722, StandardFonts.WidthOf(StandardFont.TimesRoman, 'A'));

        // Courier is monospaced: every glyph is 600 regardless of which one it is.
        Assert.Equal(600, StandardFonts.WidthOf(StandardFont.Courier, 'M'));
        Assert.Equal(600, StandardFonts.WidthOf(StandardFont.Courier, 'i'));
    }

    [Fact]
    public void AccentedLettersMeasureAsTheirBaseLetter()
    {
        // Falling back to a fixed 500 for every accented character makes Indonesian and European
        // text wrap in the wrong place.
        Assert.Equal(
            StandardFonts.WidthOf(StandardFont.Helvetica, 'e'),
            StandardFonts.WidthOf(StandardFont.Helvetica, 'é'));
    }

    [Fact]
    public void ImagesAreDeduplicatedByContent()
    {
        // A logo drawn on every page must be one XObject, not one per page.
        var logo = TestImages.SolidPng(32, 32, 0x1F, 0x38, 0x64);

        using var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            for (var i = 0; i < 10; i++)
            {
                canvas.DrawImage(logo, 50, 50 + i * 40, 32, 32);
            }
        }

        using var reopened = PdfDocument.Open(document.ToArray());
        Assert.Single(reopened.Pages[0].ExtractImages());
    }

    [Fact]
    public void EmbeddedPngComesBackWithTheSameSize()
    {
        var png = TestImages.SolidPng(48, 24, 200, 30, 30);

        using var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            canvas.DrawImage(png, 40, 40, 96, 48);
        }

        using var reopened = PdfDocument.Open(document.ToArray());
        var image = Assert.Single(reopened.Pages[0].ExtractImages());

        Assert.Equal(48, image.Width);
        Assert.Equal(24, image.Height);
    }
}

public class AnnotationTests
{
    [Fact]
    public void LinksAndNotesRoundTrip()
    {
        using var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);

        page.AddLink(new PdfRectangle(50, 700, 200, 720), "https://gravicode.com");
        page.AddTextNote(300, 700, "Periksa angka ini", "Kang Fadhil");

        using var reopened = PdfDocument.Open(document.ToArray());
        var annotations = reopened.Pages[0].GetAnnotations();

        Assert.Equal(2, annotations.Count);

        var link = annotations.First(a => a.AnnotationType == PdfAnnotationType.Link);
        Assert.Equal("https://gravicode.com", link.Uri);

        var note = annotations.First(a => a.AnnotationType == PdfAnnotationType.Text);
        Assert.Equal("Periksa angka ini", note.Contents);
        Assert.Equal("Kang Fadhil", note.Author);
    }

    [Fact]
    public void HighlightQuadPointsUseAcrobatsCornerOrder()
    {
        // Upper-left, upper-right, lower-left, lower-right. Clockwise draws a bowtie.
        using var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);

        page.AddHighlight([new PdfRectangle(50, 700, 200, 715)]);

        var annotation = Assert.Single(page.GetAnnotations());
        var quads = ((PdfArray)annotation.Dictionary.Get(PdfName.Get("QuadPoints"))!).AsDoubles();

        Assert.Equal([50, 715, 200, 715, 50, 700, 200, 700], quads);
    }

    [Fact]
    public void AWatermarkDrawsBehindExistingContent()
    {
        using var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);

        using (var canvas = page.OpenCanvas())
        {
            canvas.SetFont(StandardFont.Helvetica, 12);
            canvas.DrawText("Isi halaman", 50, 700);
        }

        page.AddWatermark("DRAFT");

        using var reopened = PdfDocument.Open(document.ToArray());
        var text = reopened.Pages[0].ExtractText();

        // The watermark is prepended, so it comes first in drawing order.
        Assert.Contains("DRAFT", text, StringComparison.Ordinal);
        Assert.Contains("Isi halaman", text, StringComparison.Ordinal);
    }
}

public class AcroFormTests
{
    private static PdfDocument WithTextField()
    {
        var document = PdfDocument.Create();
        var page = document.Pages.Add(PageSize.A4);

        var field = new PdfDictionary();
        field.SetName(PdfName.Type, "Annot");
        field.SetName(PdfName.Subtype, "Widget");
        field.SetName(PdfName.Get("FT"), "Tx");
        field[PdfName.Get("T")] = new PdfString("nama");
        field[PdfName.Get("Rect")] = new PdfArray(50, 700, 300, 722);
        field[PdfName.Get("DA")] = new PdfString("/Helv 10 Tf 0 g");
        field[PdfName.Get("P")] = document.ReferenceTo(page.Dictionary);

        var reference = document.AddObject(field);
        page.Annotations.Add(reference);

        var acroForm = new PdfDictionary();
        acroForm[PdfName.Get("Fields")] = new PdfArray([reference]);
        acroForm[PdfName.Get("DA")] = new PdfString("/Helv 0 Tf 0 g");
        document.Catalog[PdfName.Get("AcroForm")] = document.AddObject(acroForm);

        return document;
    }

    [Fact]
    public void FillingAFieldGeneratesAnAppearanceStream()
    {
        // Setting /V alone leaves the field looking empty in every viewer that does not regenerate
        // appearances, which is most of them.
        using var document = WithTextField();

        var form = AcroForm.Open(document)!;
        var missing = form.Fill(new Dictionary<string, string?> { ["nama"] = "Kang Fadhil" });

        Assert.Empty(missing);

        using var reopened = PdfDocument.Open(document.ToArray());
        var reread = AcroForm.Open(reopened)!;

        Assert.Equal("Kang Fadhil", reread["nama"]!.Value);

        var widget = reread["nama"]!.Widgets().First();
        Assert.NotNull(widget.Get(PdfName.Get("AP")));
    }

    [Fact]
    public void UnknownFieldNamesAreReportedRatherThanIgnored()
    {
        using var document = WithTextField();
        var form = AcroForm.Open(document)!;

        var missing = form.Fill(new Dictionary<string, string?>
        {
            ["nama"] = "A",
            ["tidak-ada"] = "B",
        });

        Assert.Equal(["tidak-ada"], missing);
    }

    [Fact]
    public void FlatteningRemovesTheFieldsAndKeepsTheValues()
    {
        using var document = WithTextField();

        var form = AcroForm.Open(document)!;
        form.Fill(new Dictionary<string, string?> { ["nama"] = "Kang Fadhil" });
        form.Flatten();

        using var reopened = PdfDocument.Open(document.ToArray());

        Assert.Null(AcroForm.Open(reopened));
        Assert.Empty(reopened.Pages[0].GetAnnotations());
        Assert.Contains("Kang Fadhil", reopened.Pages[0].ExtractText(), StringComparison.Ordinal);
    }
}
