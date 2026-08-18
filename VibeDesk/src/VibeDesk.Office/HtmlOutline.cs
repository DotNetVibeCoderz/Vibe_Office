using System.Net;
using System.Text.RegularExpressions;

namespace VibeDesk.Office;

/// <summary>One block of a document: a heading, paragraph, list item, or a table row set.</summary>
internal sealed record HtmlBlock(string Tag, List<HtmlSpan> Spans, List<List<string>>? Rows = null);

/// <summary>A run of text with the marks that apply to it.</summary>
internal sealed record HtmlSpan(string Text, bool Bold, bool Italic, bool Underline, bool Strike);

/// <summary>
/// Flattens the HTML VibeDesk stores into the block-and-run shape every Office writer needs.
/// </summary>
/// <remarks>
/// A regex tokenizer rather than an HTML parser, because the input is not arbitrary web HTML: it is
/// what the editors produce, and the writers only care about a dozen tags. Anything unrecognised
/// contributes its text and loses its markup, which is the right failure for a lossy export.
/// </remarks>
internal static partial class HtmlOutline
{
    private static readonly string[] BlockTags =
        ["h1", "h2", "h3", "h4", "h5", "h6", "p", "li", "blockquote", "pre"];

    public static List<HtmlBlock> Parse(string? html)
    {
        var blocks = new List<HtmlBlock>();
        if (string.IsNullOrWhiteSpace(html)) return blocks;

        // Items of an ordered list are retagged before matching, because the writer needs to know
        // whether a list item is a bullet or a number and the item itself does not say.
        var normalised = OrderedListPattern().Replace(
            html,
            m => m.Value.Replace("<li", "<li-ol", StringComparison.OrdinalIgnoreCase)
                        .Replace("</li>", "</li-ol>", StringComparison.OrdinalIgnoreCase));

        foreach (Match match in BlockPattern().Matches(normalised))
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            var inner = match.Groups["inner"].Value;

            blocks.Add(tag == "table"
                ? new HtmlBlock("table", [], TableRows(inner))
                : new HtmlBlock(tag, Spans(inner)));
        }

        return blocks;
    }

    private static List<List<string>> TableRows(string html)
    {
        var rows = new List<List<string>>();

        foreach (Match row in RowPattern().Matches(html))
        {
            var cells = CellPattern()
                .Matches(row.Groups["inner"].Value)
                .Select(c => Text(c.Groups["inner"].Value))
                .ToList();

            if (cells.Count > 0) rows.Add(cells);
        }

        return rows;
    }

    /// <summary>
    /// Splits inline HTML into runs, tracking which marks are open. Nesting is handled by counting,
    /// so <c>&lt;b&gt;a&lt;i&gt;b&lt;/i&gt;c&lt;/b&gt;</c> gives bold, bold+italic, bold.
    /// </summary>
    public static List<HtmlSpan> Spans(string html)
    {
        var spans = new List<HtmlSpan>();
        int bold = 0, italic = 0, underline = 0, strike = 0;
        var position = 0;

        void Emit(string raw)
        {
            var text = Decode(raw);
            if (text.Length == 0) return;

            spans.Add(new HtmlSpan(text, bold > 0, italic > 0, underline > 0, strike > 0));
        }

        foreach (Match tag in InlineTagPattern().Matches(html))
        {
            Emit(html[position..tag.Index]);
            position = tag.Index + tag.Length;

            var name = tag.Groups["name"].Value.ToLowerInvariant();
            var closing = tag.Groups["slash"].Success;
            var delta = closing ? -1 : 1;

            switch (name)
            {
                case "b" or "strong": bold = Math.Max(0, bold + delta); break;
                case "i" or "em": italic = Math.Max(0, italic + delta); break;
                case "u": underline = Math.Max(0, underline + delta); break;
                case "s" or "strike" or "del": strike = Math.Max(0, strike + delta); break;
                case "br": Emit("\n"); break;
            }
        }

        Emit(html[position..]);

        return spans;
    }

    public static string Text(string html) => Decode(TagPattern().Replace(html, string.Empty));

    private static string Decode(string raw) =>
        WebUtility.HtmlDecode(raw.Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"<ol\b[^>]*>.*?</ol>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OrderedListPattern();

    [GeneratedRegex(
        @"<(?<tag>h[1-6]|p|li-ol|li|blockquote|pre|table)\b[^>]*>(?<inner>.*?)</\k<tag>>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"<tr\b[^>]*>(?<inner>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"<t[dh]\b[^>]*>(?<inner>.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellPattern();

    [GeneratedRegex(@"<(?<slash>/)?(?<name>[a-zA-Z][a-zA-Z0-9-]*)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineTagPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}
