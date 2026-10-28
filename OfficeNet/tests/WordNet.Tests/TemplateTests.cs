// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Xml;
using WordNet.Templates;
using Xunit;

namespace WordNet.Tests;

public class TemplateTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static byte[] Save(WordDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static WordDocument WithBody(string xml)
    {
        var document = WordDocument.Create();
        var wrapper = XElement.Parse($"<root xmlns:w=\"{W}\">{xml}</root>");
        var sectPr = document.Body.Element(Ns.W + "sectPr")!;
        document.Body.Elements().Where(e => e != sectPr).Remove();
        sectPr.AddBeforeSelf(wrapper.Elements());
        return document;
    }

    // ---- Fields --------------------------------------------------------------------------------

    [Fact]
    public void ATokenSplitAcrossRunsIsFilledAndTheOtherFormattingStays()
    {
        // How Word actually stores a typed {{nama}} after a spell-check pass: three runs. Around it,
        // a bold word that the old MailMerge would have flattened into the token's formatting.
        using var document = WithBody(
            """
            <w:p><w:r><w:t xml:space="preserve">Yth. </w:t></w:r><w:r><w:t>{{na</w:t></w:r><w:r><w:rPr><w:i/></w:rPr><w:t>ma}}</w:t></w:r><w:r><w:t xml:space="preserve">, </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>harap segera</w:t></w:r></w:p>
            """);

        Assert.Equal(1, document.FillTemplate(new { nama = "Kang Fadhil" }));

        var paragraph = document.Paragraphs[0];
        Assert.Equal("Yth. Kang Fadhil, harap segera", paragraph.Text);
        Assert.True(paragraph.Runs.Single(r => r.Text == "harap segera").Format.Bold);
        Assert.Null(paragraph.Runs.Single(r => r.Text == "Yth. ").Format.Bold);
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void ATokenInTheMiddleOfARunKeepsTheTextAroundIt()
    {
        using var document = WordDocument.Create();
        document.AddParagraph().AddRun("Nomor: {{nomor}} / 2026", bold: true);

        document.FillTemplate(new Dictionary<string, object?> { ["nomor"] = 17 });

        var runs = document.Paragraphs[0].Runs;
        Assert.Equal("Nomor: 17 / 2026", document.Paragraphs[0].Text);
        Assert.All(runs, r => Assert.True(r.Format.Bold));
    }

    [Fact]
    public void PathsFormatsAndCultures()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("{{pelanggan.kota}} {{total:N2}} {{tanggal:dd MMMM yyyy}}");

        document.FillTemplate(
            new
            {
                pelanggan = new { kota = "Bandung" },
                total = 1234567.5m,
                tanggal = new DateTime(2026, 10, 28),
            },
            new TemplateOptions { Culture = CultureInfo.GetCultureInfo("id-ID") });

        Assert.Equal("Bandung 1.234.567,50 28 Oktober 2026", document.ExtractText());
    }

    [Fact]
    public void AMultiLineValueBecomesLineBreaks()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("{{alamat}}");
        document.FillTemplate(new { alamat = "Jl. Merdeka 1\nBandung" });

        Assert.Single(document.Paragraphs[0].Element.Descendants(Ns.W + "br"));
        Assert.Equal("Jl. Merdeka 1\nBandung", document.Paragraphs[0].Text);
    }

    [Fact]
    public void MissingValuesFollowTheOption()
    {
        static string Fill(MissingValueBehavior behavior)
        {
            using var document = WordDocument.Create();
            document.AddParagraph("a{{tidakAda}}b");
            document.FillTemplate(new { }, new TemplateOptions { MissingValue = behavior });
            return document.ExtractText();
        }

        Assert.Equal("ab", Fill(MissingValueBehavior.Empty));
        Assert.Equal("a{{tidakAda}}b", Fill(MissingValueBehavior.Keep));
        Assert.Throws<OfficeNetException>(() => Fill(MissingValueBehavior.Throw));
    }

    // ---- Sections ------------------------------------------------------------------------------

    [Fact]
    public void TableRowsRepeatOncePerItem()
    {
        using var document = WordDocument.Create();
        var table = document.AddTable(3, 3);
        table[0, 0].Text = "Barang"; table[0, 1].Text = "Qty"; table[0, 2].Text = "Harga";
        table[1, 0].Text = "{{#baris}}{{nama}}"; table[1, 1].Text = "{{qty}}"; table[1, 2].Text = "{{harga:N0}}{{/baris}}";
        table[2, 0].Text = "Total"; table[2, 2].Text = "{{total:N0}}";

        document.FillTemplate(new
        {
            baris = new[]
            {
                new { nama = "Kertas", qty = 2, harga = 50000 },
                new { nama = "Tinta", qty = 1, harga = 125000 },
                new { nama = "Map", qty = 10, harga = 3000 },
            },
            total = 255000,
        });

        var rows = document.Tables[0].Rows.Select(r => string.Join("|", r.Cells.Select(c => c.Text))).ToList();
        Assert.Equal(
            ["Barang|Qty|Harga", "Kertas|2|50,000", "Tinta|1|125,000", "Map|10|3,000", "Total||255,000"],
            rows);
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void AnEmptyListRemovesItsRowsAndAnEmptiedTableGoes()
    {
        using var document = WordDocument.Create();
        // One column: the closing tag goes in a second paragraph of the cell. With both tags in
        // one paragraph the section would repeat inline, inside the cell, by design.
        var table = document.AddTable(1, 1);
        table[0, 0].Text = "{{#baris}}{{nama}}";
        table[0, 0].AddParagraph("{{/baris}}");
        document.AddParagraph("sesudah");

        document.FillTemplate(new { baris = Array.Empty<object>() });

        Assert.Empty(document.Tables);
        Assert.Equal("sesudah", document.ExtractText().Trim());
        DocxValidator.AssertValid(Save(document));
    }

    [Fact]
    public void BlocksRepeatAndTagOnlyParagraphsDisappear()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Daftar:");
        document.AddParagraph("{{#orang}}");
        document.AddHeading("{{nama}}", 2);
        document.AddParagraph("Kota: {{kota}}");
        document.AddParagraph("{{/orang}}");
        document.AddParagraph("Selesai.");

        document.FillTemplate(new
        {
            orang = new[] { new { nama = "Ani", kota = "Bandung" }, new { nama = "Budi", kota = "Medan" } },
        });

        Assert.Equal("Daftar:\nAni\nKota: Bandung\nBudi\nKota: Medan\nSelesai.", document.ExtractText());
        Assert.Equal(2, document.Paragraphs.Count(p => p.StyleId == "Heading2"));
    }

    [Fact]
    public void InlineSectionsRepeatWithinAParagraph()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Tim: {{#anggota}}{{.}}, {{/anggota}}selesai");

        document.FillTemplate(new { anggota = new[] { "Ani", "Budi", "Citra" } });

        Assert.Equal("Tim: Ani, Budi, Citra, selesai", document.ExtractText());
    }

    [Fact]
    public void ConditionalsAndInvertedSections()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("{{#lunas}}LUNAS{{/lunas}}{{^lunas}}BELUM LUNAS{{/lunas}}");
        document.AddParagraph("{{^catatan}}Tanpa catatan.{{/catatan}}");
        document.AddParagraph("{{#nol}}nol tetap nilai{{/nol}}");

        document.FillTemplate(new { lunas = false, catatan = "", nol = 0 });

        Assert.Equal("BELUM LUNAS\nTanpa catatan.\nnol tetap nilai", document.ExtractText());
    }

    [Fact]
    public void NestedSectionsSeeOuterValues()
    {
        // An inner lookup that misses falls back to the enclosing contexts: {{kode}} inside each
        // line resolves against its order.
        using var document = WordDocument.Create();
        document.AddParagraph("{{#pesanan}}");
        document.AddParagraph("Pesanan {{kode}}: {{#baris}}{{kode}}-{{barang}} {{/baris}}");
        document.AddParagraph("{{/pesanan}}");

        document.FillTemplate(new
        {
            pesanan = new object[]
            {
                new { kode = "A1", baris = new[] { new { barang = "x" }, new { barang = "y" } } },
                new { kode = "B2", baris = new[] { new { barang = "z" } } },
            },
        });

        Assert.Equal("Pesanan A1: A1-x A1-y \nPesanan B2: B2-z ", document.ExtractText());
    }

    [Fact]
    public void HeadersAndFootersAreFilledToo()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("isi");
        document.Section.GetHeader().AddParagraph("Kepada {{nama}}");
        document.Section.GetFooter().AddParagraph("No. {{nomor}}");

        Assert.Equal(2, document.FillTemplate(new { nama = "Ani", nomor = "INV-7" }));
        Assert.Equal("Kepada Ani", document.Sections[0].GetHeader().Text);
        Assert.Equal("No. INV-7", document.Sections[0].GetFooter().Text);
    }

    [Fact]
    public void UnbalancedSectionsAreReported()
    {
        using var open = WordDocument.Create();
        open.AddParagraph("{{#a}} tanpa penutup");
        Assert.Throws<OfficeNetException>(() => open.FillTemplate(new { a = true }));

        using var close = WordDocument.Create();
        close.AddParagraph("tanpa pembuka {{/a}}");
        Assert.Throws<OfficeNetException>(() => close.FillTemplate(new { a = true }));
    }

    [Fact]
    public void NoMarkerAttributesAreLeftBehind()
    {
        using var document = WordDocument.Create();
        document.AddParagraph("{{a}} {{#b}}{{c}}{{/b}} {{tidakAda}}");
        document.FillTemplate(new { a = 1, b = new[] { new { c = 2 } } },
            new TemplateOptions { MissingValue = MissingValueBehavior.Keep });

        Assert.DoesNotContain(document.Body.DescendantsAndSelf().Attributes(),
            a => a.Name.NamespaceName == "urn:officenet:template");
        Assert.DoesNotContain(document.Body.Descendants(), e => e.Name.NamespaceName == "urn:officenet:template");
    }
}
