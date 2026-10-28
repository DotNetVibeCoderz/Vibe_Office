// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Text;
using OfficeNet.Core;
using OfficeNet.TestKit;
using PdfNet.Annotations;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Forms;
using PdfNet.Objects;
using Xunit;

namespace PdfNet.Tests;

/// <summary>
/// PDF/A: what a save writes, and what it refuses to claim.
/// </summary>
/// <remarks>
/// The structural tests always run. The last one hands the files to veraPDF, the reference
/// validator, when <c>VERAPDF</c> names its launcher — the only test that can say a file conforms,
/// as opposed to looking as if it should.
/// </remarks>
public class PdfATests
{
    private static PdfDocument Document(PdfConformance conformance, string text = "Arsip ini tetap sama")
    {
        var pdf = PdfDocument.Create();
        pdf.Info.Title = "Uji PDF A";
        pdf.Info.Author = "Kang Fadhil";
        pdf.Conformance = conformance;

        var font = pdf.EmbedFont(SyntheticFont.Build());
        var page = pdf.Pages.Add(PageSize.A4);

        using var canvas = page.OpenCanvas();
        canvas.SetFont(font, 14);
        canvas.DrawText(text, 72, 700);
        canvas.SetFillColor(OfficeNet.Core.Drawing.OfficeColor.FromRgb(200, 30, 30));
        canvas.Rectangle(72, 600, 100, 50).Fill();

        return pdf;
    }

    [Fact]
    public void APdfA2bFileCarriesItsIdentificationAndAnSrgbOutputIntent()
    {
        using var pdf = Document(PdfConformance.PdfA2B);
        var bytes = pdf.ToArray();

        Assert.StartsWith("%PDF-1.7", Encoding.ASCII.GetString(bytes, 0, 8), StringComparison.Ordinal);

        using var reopened = PdfDocument.Open(bytes);
        var xmp = reopened.XmpMetadata!;
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", xmp, StringComparison.Ordinal);

        var intent = Assert.IsType<PdfDictionary>(reopened.Follow(Assert.Single(reopened.Catalog.GetArray(PdfName.Get("OutputIntents")))));
        Assert.Equal("GTS_PDFA1", intent.GetName(PdfName.Get("S")));
        Assert.Equal("sRGB IEC61966-2.1", intent.GetText(PdfName.Get("OutputConditionIdentifier")));

        var profile = Assert.IsType<PdfStream>(intent.Get(PdfName.Get("DestOutputProfile")));
        Assert.Equal(3, profile.GetInt(PdfName.Get("N")));

        // The ICC header says what the profile is; the size field must be the size.
        var icc = profile.Decoded;
        Assert.Equal((uint)icc.Length, BinaryPrimitives.ReadUInt32BigEndian(icc));
        Assert.Equal(0x02100000u, BinaryPrimitives.ReadUInt32BigEndian(icc.AsSpan(8)));
        Assert.Equal("mntrRGB XYZ ", Encoding.ASCII.GetString(icc, 12, 12));
        Assert.Equal("acsp", Encoding.ASCII.GetString(icc, 36, 4));
    }

    [Fact]
    public void APdfA3bFileIsPart3AndMayCarryAnyAttachment()
    {
        using var pdf = Document(PdfConformance.PdfA3B);
        pdf.AddAttachment("factur-x.xml", "<Invoice/>"u8.ToArray(), "text/xml", "Faktur", "Alternative");

        using var reopened = PdfDocument.Open(pdf.ToArray());

        Assert.Contains("<pdfaid:part>3</pdfaid:part>", reopened.XmpMetadata, StringComparison.Ordinal);
        Assert.Equal("factur-x.xml", Assert.Single(reopened.Attachments).Name);
    }

    [Fact]
    public void AStandardFontIsRefusedByName()
    {
        using var pdf = PdfDocument.Create();
        pdf.Conformance = PdfConformance.PdfA2B;

        using (var canvas = pdf.Pages.Add(PageSize.A4).OpenCanvas())
        {
            canvas.SetFont(StandardFont.TimesRoman, 12);
            canvas.DrawText("x", 72, 700);
        }

        var error = Assert.Throws<OfficeNetException>(() => pdf.ToArray());
        Assert.Contains("Times-Roman", error.Message, StringComparison.Ordinal);
        Assert.Contains("EmbedFont", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACharacterTheFontLacksIsRefusedWithItsCodePoint()
    {
        using var pdf = Document(PdfConformance.PdfA2B, "Harga \u20AC 10");

        var error = Assert.Throws<OfficeNetException>(() => pdf.ToArray());
        Assert.Contains("U+20AC", error.Message, StringComparison.Ordinal);
        Assert.Contains(".notdef", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EncryptionAndForeignAttachmentsAreRefusedUnderPdfA2()
    {
        using var encrypted = Document(PdfConformance.PdfA2B);
        encrypted.Encrypt("rahasia");
        Assert.Contains("encrypted", Assert.Throws<OfficeNetException>(() => encrypted.ToArray()).Message, StringComparison.Ordinal);

        using var attached = Document(PdfConformance.PdfA2B);
        attached.AddAttachment("data.xml", [1], "text/xml");
        var error = Assert.Throws<OfficeNetException>(() => attached.ToArray());
        Assert.Contains("data.xml", error.Message, StringComparison.Ordinal);
        Assert.Contains("PdfA3B", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFontListedButNeverDrawnDoesNotBlock()
    {
        // A check box's appearance lists Helvetica and draws its tick with paths; a form's /DR lists
        // it for viewers. Neither draws a glyph, so neither stops the file being PDF/A.
        using var pdf = Document(PdfConformance.PdfA2B);
        var page = pdf.Pages[0];
        AcroForm.OpenOrCreate(pdf).AddCheckBox(page, "setuju", new PdfRectangle(72, 470, 86, 484), isChecked: true);

        // A page whose canvas selected Helvetica and then drew only a rectangle lists the font and
        // never draws a glyph in it.
        using (var canvas = page.OpenCanvas())
        {
            canvas.SetFont(StandardFont.Helvetica, 12);
            canvas.Rectangle(300, 300, 20, 20).Fill();
        }

        var bytes = pdf.ToArray();

        // But a text field does draw with it, and says so.
        using var withText = Document(PdfConformance.PdfA2B);
        AcroForm.OpenOrCreate(withText).AddTextField(withText.Pages[0], "nama", new PdfRectangle(72, 500, 250, 520), "Fadhil");
        var error = Assert.Throws<OfficeNetException>(() => withText.ToArray());
        Assert.Contains("text fields", error.Message, StringComparison.Ordinal);

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void AnnotationsPrintAndANoteHasItsOwnAppearance()
    {
        using var pdf = Document(PdfConformance.PdfA2B);
        var page = pdf.Pages[0];
        page.AddTextNote(300, 700, "catatan");
        var link = page.AddLink(new PdfRectangle(72, 690, 200, 720), "https://gravicode.com");
        link.Dictionary[PdfName.Get("F")] = new PdfNumber(2);   // hidden — PDF/A forbids it

        using var reopened = PdfDocument.Open(pdf.ToArray());
        var annotations = reopened.Pages[0].Dictionary.GetArray(PdfName.Get("Annots"))
            .Select(reopened.Follow).OfType<PdfDictionary>().ToList();

        Assert.All(annotations, a => Assert.Equal(4, a.GetInt(PdfName.Get("F"))));
        Assert.NotNull(annotations.Single(a => a.GetName(PdfName.Subtype) == "Text").Get<PdfDictionary>(PdfName.Get("AP")));

        // An action's type is /S; /Subtype is not where a viewer looks.
        var action = annotations.Single(a => a.GetName(PdfName.Subtype) == "Link").Get<PdfDictionary>(PdfName.Get("A"))!;
        Assert.Equal("URI", action.GetName(PdfName.Get("S")));
        Assert.Null(action.Get(PdfName.Subtype));
    }

    [Fact]
    public void GlyphsDrawnAfterAnEarlierCanvasClosedAreInTheSubset()
    {
        // The subset used to be built when the first canvas closed, so page two's letters — and a
        // watermark drawn last of all — were missing from the font and extracted as noise.
        using var pdf = PdfDocument.Create();
        var font = pdf.EmbedFont(SyntheticFont.Build());

        using (var first = pdf.Pages.Add(PageSize.A4).OpenCanvas())
        {
            first.SetFont(font, 12);
            first.DrawText("AB", 72, 700);
        }

        using (var second = pdf.Pages.Add(PageSize.A4).OpenCanvas())
        {
            second.SetFont(font, 12);
            second.DrawText("XYZ", 72, 700);
        }

        var once = pdf.ToArray();

        using (var third = pdf.Pages.Add(PageSize.A4).OpenCanvas())
        {
            third.SetFont(font, 12);
            third.DrawText("qrs", 72, 700);
        }

        // A save after more drawing builds the subset again.
        var twice = pdf.ToArray();

        using var reopenedOnce = PdfDocument.Open(once);
        Assert.Equal("XYZ", reopenedOnce.Pages[1].ExtractText().Trim());

        using var reopened = PdfDocument.Open(twice);
        Assert.Equal(["AB", "XYZ", "qrs"], reopened.Pages.Select(p => p.ExtractText().Trim()));

        // Rebuilding replaces the old subset rather than leaving it in the file beside the new one.
        Assert.Equal(1, Encoding.Latin1.GetString(twice).Split("/Length1").Length - 1);
        Assert.Equal(8, font.UsedGlyphCount);   // A B X Y Z q r s
    }

    [Fact]
    public void AWatermarkInAnEmbeddedFontGoesUnderneathAndReadsBack()
    {
        using var pdf = Document(PdfConformance.PdfA2B);
        var font = pdf.EmbedFont(SyntheticFont.Build());
        pdf.Pages[0].AddWatermark("ARSIP", font, opacity: 0.2);

        using var reopened = PdfDocument.Open(pdf.ToArray());
        Assert.Contains("ARSIP", reopened.Pages[0].ExtractText(), StringComparison.Ordinal);

        // Underneath: the watermark is the first content stream, the page's own drawing after it.
        var streams = reopened.Pages[0].Dictionary.GetArray(PdfName.Contents).Select(reopened.Follow).OfType<PdfStream>().ToList();
        Assert.Equal(2, streams.Count);
        Assert.Contains(" Tj", streams[0].DecodedText, StringComparison.Ordinal);
        Assert.Contains(" gs", streams[0].DecodedText, StringComparison.Ordinal);
        Assert.Contains(" re", streams[1].DecodedText, StringComparison.Ordinal);
    }

    [Fact]
    public void VeraPdfFindsTheFilesConforming()
    {
        if (VeraPdf.Launcher is null)
        {
            Assert.Skip(VeraPdf.SkipReason);
        }

        using var folder = new TempDirectory();

        using (var a2 = Document(PdfConformance.PdfA2B))
        {
            var page = a2.Pages[0];
            page.AddTextNote(300, 700, "catatan");
            page.AddLink(new PdfRectangle(72, 690, 200, 720), "https://gravicode.com");
            page.AddHighlight([new PdfRectangle(72, 695, 180, 715)]);
            AcroForm.OpenOrCreate(a2).AddCheckBox(page, "setuju", new PdfRectangle(72, 470, 86, 484), isChecked: true);
            page.AddWatermark("ARSIP", a2.EmbedFont(SyntheticFont.Build()));
            a2.Save(Path.Combine(folder.Path, "a2b.pdf"));
        }

        using (var a3 = Document(PdfConformance.PdfA3B))
        {
            a3.AddAttachment("factur-x.xml", "<Invoice/>"u8.ToArray(), "text/xml", "Faktur", "Alternative");
            a3.Save(Path.Combine(folder.Path, "a3b.pdf"));
        }

        var token = TestContext.Current.CancellationToken;
        Assert.Equal("true", VeraPdf.Validate(Path.Combine(folder.Path, "a2b.pdf"), "2b", token));
        Assert.Equal("true", VeraPdf.Validate(Path.Combine(folder.Path, "a3b.pdf"), "3b", token));
    }
}
