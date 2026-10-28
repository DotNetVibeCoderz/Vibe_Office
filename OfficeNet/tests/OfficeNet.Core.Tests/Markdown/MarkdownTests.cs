// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;
using OfficeNet.Core.Html;
using OfficeNet.Core.Markdown;
using Xunit;

namespace OfficeNet.Core.Tests.Markdown;

/// <summary>
/// The Markdown reader against the CommonMark specification, and the writer against the reader.
/// </summary>
/// <remarks>
/// <c>commonmark-spec-0.31.2.json</c> is the specification's own example set, from
/// https://spec.commonmark.org/0.31.2/spec.json — © John MacFarlane, CC BY-SA 4.0. Every example
/// is a test: the Markdown, and the exact HTML the reference implementation produces.
/// </remarks>
public class MarkdownTests
{
    private static readonly MarkdownOptions Strict = new() { Tables = false, Strikethrough = false, Autolinks = false };

    /// <summary>
    /// The examples that are known not to pass, each for a stated reason.
    /// </summary>
    private static readonly Dictionary<int, string> KnownFailures = new()
    {
        // HTML5 named entities beyond HTML 4's set (&HilbertSpace;, &ngE; …). The runtime's decoder
        // knows the HTML 4 names; the full table is two thousand entries for characters nobody
        // types as entities.
        [25] = "HTML5-only named entities",
    };

    public static TheoryData<int> SpecExamples()
    {
        var data = new TheoryData<int>();

        foreach (var example in Spec())
        {
            data.Add(example.GetProperty("example").GetInt32());
        }

        return data;
    }

    private static IEnumerable<JsonElement> Spec()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Markdown", "commonmark-spec-0.31.2.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateArray();
    }

    [Theory]
    [MemberData(nameof(SpecExamples))]
    public void EverySpecificationExampleProducesTheReferenceHtml(int number)
    {
        var example = Spec().Single(e => e.GetProperty("example").GetInt32() == number);
        var markdown = example.GetProperty("markdown").GetString()!;
        var expected = example.GetProperty("html").GetString()!;

        var actual = MarkdownToHtml.Convert(markdown, Strict);

        if (KnownFailures.ContainsKey(number))
        {
            // Still recorded, so the day it starts passing the list is corrected.
            Assert.NotEqual(expected, actual);
            return;
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ATableHasAlignmentsEscapedPipesAndTheHeadersCellCount()
    {
        var html = MarkdownToHtml.Convert("""
            Sebelum tabel
            | Wilayah | Nilai | Catatan |
            | :--- | ---: | :-: |
            | Jakarta \| Raya | 1.480 |
            | Bandung | 1.150 | baik | lebih |
            """);

        Assert.Equal("""
            <p>Sebelum tabel</p>
            <table>
            <thead>
            <tr>
            <th align="left">Wilayah</th>
            <th align="right">Nilai</th>
            <th align="center">Catatan</th>
            </tr>
            </thead>
            <tbody>
            <tr>
            <td align="left">Jakarta | Raya</td>
            <td align="right">1.480</td>
            <td align="center"></td>
            </tr>
            <tr>
            <td align="left">Bandung</td>
            <td align="right">1.150</td>
            <td align="center">baik</td>
            </tr>
            </tbody>
            </table>

            """.ReplaceLineEndings("\n"), html);
    }

    [Fact]
    public void ATableNeedsADelimiterRowWithTheHeadersCellCount()
    {
        Assert.DoesNotContain("<table>", MarkdownToHtml.Convert("| a | b |\n| --- |\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("<table>", MarkdownToHtml.Convert("| a | b |\n| x | y |\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("<table>",
            MarkdownToHtml.Convert("| a | b |\n| --- | --- |\n", new MarkdownOptions { Tables = false }),
            StringComparison.Ordinal);
    }

    [Fact]
    public void StrikethroughTakesOneOrTwoTildesThatMatch()
    {
        Assert.Equal("<p><del>lama</del> dan <del>usang</del></p>\n", MarkdownToHtml.Convert("~~lama~~ dan ~usang~"));
        Assert.Equal("<p>ini ~~~bukan~~~ coret</p>\n", MarkdownToHtml.Convert("ini ~~~bukan~~~ coret"));
        Assert.Equal("<p>~~tidak~</p>\n", MarkdownToHtml.Convert("~~tidak~"));
    }

    [Fact]
    public void BareUrlsBecomeLinksWithoutTheirTrailingPunctuation()
    {
        var html = MarkdownToHtml.Convert(
            "Lihat www.example.org, atau (https://example.org/a_b_c). Kode `https://x.org` tetap.");

        Assert.Equal(
            "<p>Lihat <a href=\"http://www.example.org\">www.example.org</a>, atau " +
            "(<a href=\"https://example.org/a_b_c\">https://example.org/a_b_c</a>). " +
            "Kode <code>https://x.org</code> tetap.</p>\n", html);
    }

    [Fact]
    public void AUrlInsideALinkIsNotLinkedAgain()
    {
        var html = MarkdownToHtml.Convert("[https://example.org](https://example.org)");

        Assert.Equal("<p><a href=\"https://example.org\">https://example.org</a></p>\n", html);
    }

    [Fact]
    public void WithHtmlOffMarkupIsShownAsText()
    {
        var html = MarkdownToHtml.Convert("<script>alert(1)</script>\n\nteks <b>tebal</b>",
            new MarkdownOptions { AllowHtml = false });

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;tebal&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    public static TheoryData<string> Awkward() =>
    [
        "*bukan miring* dan **bukan tebal**",
        "# bukan judul",
        "1. bukan daftar",
        "2) juga bukan",
        "- bukan butir",
        "+ plus",
        "= garis",
        "snake_case_name dan _garis_bawah_",
        "2*3=6 dan a * b",
        "[bukan](tautan) dan ![bukan](gambar)",
        "<b>bukan tag</b> &amp; &copy; &#169;",
        "garis\\miring\\",
        "`bukan kode`",
        "pipa | di tengah",
        "> bukan kutipan",
        "~~bukan coret~~",
        "    empat spasi di depan",
    ];

    [Theory]
    [MemberData(nameof(Awkward))]
    public void TextThatLooksLikeSyntaxIsWrittenSoItReadsBackAsText(string text)
    {
        var markdown = MarkdownWriter.Write([Paragraph(new HtmlSpan(text, default, null))]);
        var html = MarkdownToHtml.Convert(markdown);

        // One paragraph, holding the text as it was — leading spaces aside, which Markdown trims.
        Assert.Equal("<p>" + Escape(text.TrimStart()) + "</p>\n", html);
    }

    [Fact]
    public void FormattingMarkersStayAgainstTheWords()
    {
        var markdown = MarkdownWriter.Write(
        [
            Paragraph(
                new HtmlSpan("Teks ", default, null),
                new HtmlSpan("tebal ", new CssStyle { Bold = true }, null),
                new HtmlSpan("lalu ", default, null),
                new HtmlSpan("miring", new CssStyle { Italic = true }, null),
                new HtmlSpan(" dan ", default, null),
                new HtmlSpan("kode", new CssStyle { FontFamily = "Consolas" }, null),
                new HtmlSpan(" serta ", default, null),
                new HtmlSpan("tautan", default, "https://example.org/a b")),
        ]);

        Assert.Equal("Teks **tebal** lalu *miring* dan `kode` serta [tautan](<https://example.org/a b>)\n", markdown);
        Assert.Equal(
            "<p>Teks <strong>tebal</strong> lalu <em>miring</em> dan <code>kode</code> serta " +
            "<a href=\"https://example.org/a%20b\">tautan</a></p>\n",
            MarkdownToHtml.Convert(markdown));
    }

    [Fact]
    public void TwoListsInARowStayTwoLists()
    {
        var markdown = MarkdownWriter.Write(
        [
            Item(1, "a"), Item(1, "b"),
            Item(2, "c"), Item(2, "d"),
        ]);

        Assert.Equal("- a\n- b\n\n* c\n* d\n", markdown);
        Assert.Equal("<ul>\n<li>a</li>\n<li>b</li>\n</ul>\n<ul>\n<li>c</li>\n<li>d</li>\n</ul>\n",
            MarkdownToHtml.Convert(markdown));

        static HtmlBlock Item(int list, string text) => new()
        {
            Kind = HtmlBlockKind.ListItem,
            ListId = list,
            Spans = [new HtmlSpan(text, default, null)],
        };
    }

    [Fact]
    public void ANumberedListFromHtmlStartsAtOneOrItsStart()
    {
        // The flattener does not count items; only <ol start> gives them a number.
        Assert.Equal("1. a\n1. b\n", MarkdownWriter.Write(HtmlFlattener.Flatten("<ol><li>a</li><li>b</li></ol>")));
        Assert.Equal("7. a\n8. b\n", MarkdownWriter.Write(HtmlFlattener.Flatten("<ol start=\"7\"><li>a</li><li>b</li></ol>")));
    }

    [Fact]
    public void ACodeBlockGetsAFenceLongerThanAnyBackticksInside()
    {
        var markdown = MarkdownWriter.Write(
        [
            new HtmlBlock { Kind = HtmlBlockKind.Code, Spans = [new HtmlSpan("a ``` b", default, null)] },
        ]);

        Assert.Equal("````\na ``` b\n````\n", markdown);
        Assert.Equal("<pre><code>a ``` b\n</code></pre>\n", MarkdownToHtml.Convert(markdown));
    }

    [Fact]
    public void QuotedBlocksShareOneQuotation()
    {
        var markdown = MarkdownWriter.Write(
        [
            new HtmlBlock { Kind = HtmlBlockKind.Paragraph, Spans = [new HtmlSpan("satu", default, null)], Quote = true },
            new HtmlBlock { Kind = HtmlBlockKind.Paragraph, Spans = [new HtmlSpan("dua", default, null)], Quote = true },
        ]);

        Assert.Equal("> satu\n>\n> dua\n", markdown);
        Assert.Equal("<blockquote>\n<p>satu</p>\n<p>dua</p>\n</blockquote>\n", MarkdownToHtml.Convert(markdown));
    }

    [Fact]
    public void ABlockquoteWithParagraphsFlattensToQuotedParagraphsWithoutTheTagsItalics()
    {
        var blocks = HtmlFlattener.Flatten("<blockquote><p>satu</p><p>dua <em>miring</em></p></blockquote><p>bukan</p>");

        Assert.Equal([true, true, false], blocks.Select(b => b.Quote));
        Assert.Equal(["satu", "dua miring", "bukan"], blocks.Select(b => b.PlainText));
        Assert.Null(blocks[0].Spans[0].Style.Italic);
        Assert.True(blocks[1].Spans[^1].Style.Italic);
    }

    [Fact]
    public void SpaceEitherSideOfALineBreakIsNotText()
    {
        // Within one span, and where the space sits in a span of its own beside the break.
        Assert.Equal("a\nb", HtmlFlattener.Flatten("<p>a <br>\n b</p>")[0].PlainText);
        Assert.Equal("a\nb c", HtmlFlattener.Flatten("<p><b>a<br></b> <i>b</i> c</p>")[0].PlainText);
        Assert.Equal("x\ny", HtmlFlattener.Flatten("<p><i>x </i><br>y</p>")[0].PlainText);
    }

    [Fact]
    public void APictureInsideAParagraphIsKept()
    {
        var png = Convert.ToBase64String(OfficeNet.TestKit.TestImages.SolidPng(2, 2, 200, 30, 30));
        var blocks = HtmlFlattener.Flatten($"<p>Gambar: <img src=\"data:image/png;base64,{png}\" alt=\"titik\"></p>");

        Assert.Equal([HtmlBlockKind.Paragraph, HtmlBlockKind.Image], blocks.Select(b => b.Kind));
        Assert.Equal("titik", blocks[1].AltText);
    }

    private static HtmlBlock Paragraph(params HtmlSpan[] spans) => new() { Kind = HtmlBlockKind.Paragraph, Spans = [.. spans] };

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
