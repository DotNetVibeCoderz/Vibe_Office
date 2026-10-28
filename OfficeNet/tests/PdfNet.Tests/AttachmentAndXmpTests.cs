// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using PdfNet.Document;
using PdfNet.Objects;
using Xunit;

namespace PdfNet.Tests;

public class AttachmentAndXmpTests
{
    private static readonly byte[] InvoiceXml = Encoding.UTF8.GetBytes("<Invoice><Total>10000000</Total></Invoice>");

    [Fact]
    public void AnAttachmentIsEmbeddedAndReadBack()
    {
        byte[] bytes;

        using (var pdf = PdfDocument.Create())
        {
            pdf.Pages.Add();
            pdf.AddAttachment("factur-x.xml", InvoiceXml, "text/xml", "Faktur elektronik", "Alternative");
            pdf.AddAttachment("lampiran.csv", "a,b\n1,2"u8.ToArray(), "text/csv");
            bytes = pdf.ToArray();
        }

        using var reopened = PdfDocument.Open(bytes);
        var attachments = reopened.Attachments;

        Assert.Equal(["factur-x.xml", "lampiran.csv"], attachments.Select(a => a.Name));

        var invoice = attachments[0];
        Assert.Equal(InvoiceXml, invoice.Data);
        Assert.Equal("text/xml", invoice.MimeType);
        Assert.Equal("Faktur elektronik", invoice.Description);
        Assert.Equal("Alternative", invoice.Relationship);
        Assert.NotNull(invoice.Modified);

        // PDF/A-3 lists associated files in the catalog's /AF too.
        Assert.Equal(2, (reopened.Catalog[PdfName.Get("AF")] as PdfArray)!.Count);
    }

    [Fact]
    public void TheNameTreeStaysSortedAndANameIsReplacedNotDuplicated()
    {
        using var pdf = PdfDocument.Create();
        pdf.Pages.Add();
        pdf.AddAttachment("zeta.txt", [1]);
        pdf.AddAttachment("alfa.txt", [2]);
        pdf.AddAttachment("zeta.txt", [3]);

        Assert.Equal(["alfa.txt", "zeta.txt"], pdf.Attachments.Select(a => a.Name));
        Assert.Equal([3], pdf.Attachments.Single(a => a.Name == "zeta.txt").Data);
        Assert.Equal(2, (pdf.Catalog[PdfName.Get("AF")] as PdfArray)!.Count);

        Assert.True(pdf.RemoveAttachment("alfa.txt"));
        Assert.False(pdf.RemoveAttachment("tidak-ada"));
        Assert.Single(pdf.Attachments);
    }

    [Fact]
    public void XmpMirrorsTheInformationDictionary()
    {
        byte[] bytes;

        using (var pdf = PdfDocument.Create())
        {
            pdf.Pages.Add();
            pdf.Info.Title = "Laporan & Ringkasan";
            pdf.Info.Author = "Kang Fadhil";
            pdf.Info.Subject = "Kuartal III";
            pdf.Info.Keywords = "laporan, 2026";
            pdf.WriteXmpMetadata = true;
            bytes = pdf.ToArray();
        }

        using var reopened = PdfDocument.Open(bytes);
        var xmp = reopened.XmpMetadata!;

        // The packet must be well-formed XML once the xpacket wrapper is set aside.
        var start = xmp.IndexOf("<x:xmpmeta", StringComparison.Ordinal);
        var end = xmp.IndexOf("</x:xmpmeta>", StringComparison.Ordinal) + "</x:xmpmeta>".Length;
        var document = XDocument.Parse(xmp[start..end]);

        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace xmpNs = "http://ns.adobe.com/xap/1.0/";
        Assert.Equal("Laporan & Ringkasan", document.Descendants(dc + "title").Single().Value);
        Assert.Equal("Kang Fadhil", document.Descendants(dc + "creator").Single().Value);

        // The dates agree with /Info to the second, time zone included — what PDF/A checks.
        var created = reopened.Info.CreationDate!.Value;
        var xmpCreated = DateTimeOffset.Parse(document.Descendants(xmpNs + "CreateDate").Single().Value);
        Assert.Equal(created, xmpCreated);

        // Not compressed: PDF/A wants the packet findable by tools that do not parse PDF.
        var stream = (PdfStream)reopened.Follow(reopened.Catalog[PdfName.Get("Metadata")])!;
        Assert.Null(stream[PdfName.Filter]);
    }

    [Fact]
    public void SavingTwiceReusesTheMetadataStream()
    {
        using var pdf = PdfDocument.Create();
        pdf.Pages.Add();
        pdf.WriteXmpMetadata = true;
        pdf.ToArray();
        var count = pdf.ObjectCount;
        pdf.ToArray();

        Assert.Equal(count, pdf.ObjectCount);
    }

    [Theory]
    [InlineData("D:20261028153000+07'00'", 2026, 10, 28, 15, 30, 0, 7)]
    [InlineData("D:20261028080000Z", 2026, 10, 28, 8, 0, 0, 0)]
    [InlineData("D:20261028080000-05'30'", 2026, 10, 28, 8, 0, 0, -5.5)]
    public void PdfDatesRoundTrip(string text, int y, int mo, int d, int h, int mi, int s, double offsetHours)
    {
        var expected = new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.FromHours(offsetHours));
        Assert.Equal(expected, PdfDocument.ParsePdfDate(text));

        if (offsetHours != 0)
        {
            Assert.Equal(text, PdfDocument.PdfDate(expected));
        }
    }
}
