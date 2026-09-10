// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using OfficeNet.Core.Drawing;

namespace OfficeNet.Core.Html;

/// <summary>How a list of blocks is written out as HTML.</summary>
public sealed class HtmlWriteOptions
{
    /// <summary>
    /// Whether to write a complete document — doctype, head, title and body — or only the body's
    /// content, for pasting into a page that already has its own.
    /// </summary>
    public bool FullDocument { get; set; } = true;

    /// <summary>The page title. Defaults to the text of the first heading.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Whether pictures are embedded as <c>data:</c> URIs. When off, a picture is replaced by its
    /// alt text, which keeps the output a single self-contained file either way.
    /// </summary>
    public bool EmbedImages { get; set; } = true;

    /// <summary>
    /// The stylesheet written into the head. <c>null</c> writes a small default that gives tables
    /// visible cell borders; an empty string writes none.
    /// </summary>
    public string? Stylesheet { get; set; }
}

/// <summary>
/// Writes a list of formatted blocks as HTML — the other half of <see cref="HtmlFlattener"/>.
/// </summary>
/// <remarks>
/// <para>
/// The flattener turns HTML into blocks; this turns blocks into HTML. Every converter that exports
/// to HTML maps its own model onto the same blocks and hands them here, which is what keeps a Word
/// document and a deck that say the same thing from coming out as two different dialects of HTML.
/// </para>
/// <para>
/// The output is deliberately plain: semantic elements for structure, inline elements for bold and
/// its siblings, and a <c>style</c> attribute only for what has no element — colour, font, size,
/// alignment. It is meant to be read and restyled, not to reproduce a page pixel for pixel.
/// </para>
/// <para>
/// Hyperlinks are filtered. A document can carry a <c>javascript:</c> link, and HTML written from
/// it may be served; a scheme outside a short allow-list keeps its text and loses its link.
/// </para>
/// </remarks>
public static class HtmlWriter
{
    private const string DefaultStylesheet =
        "body{font-family:Calibri,Arial,sans-serif;line-height:1.4;max-width:48em;margin:2em auto;padding:0 1em}" +
        "table{border-collapse:collapse;margin:1em 0}" +
        "th,td{border:1px solid #999;padding:4px 8px;vertical-align:top}" +
        "img{max-width:100%}" +
        "pre{background:#f5f5f5;padding:8px;overflow-x:auto}";

    /// <summary>Writes the blocks as HTML.</summary>
    public static string Write(IEnumerable<HtmlBlock> blocks, HtmlWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        options ??= new HtmlWriteOptions();

        var list = blocks as IReadOnlyList<HtmlBlock> ?? blocks.ToList();
        var body = new StringBuilder();
        var lists = new ListWriter(body);

        foreach (var block in list)
        {
            if (block.Kind == HtmlBlockKind.ListItem)
            {
                lists.Item(block);
                AppendSpans(body, block.Spans);
                continue;
            }

            lists.CloseAll();
            WriteBlock(body, block, options);
        }

        lists.CloseAll();

        if (!options.FullDocument)
        {
            return body.ToString();
        }

        var title = options.Title
                    ?? list.FirstOrDefault(b => b.Kind == HtmlBlockKind.Heading)?.PlainText.Trim()
                    ?? "Document";

        var page = new StringBuilder(body.Length + 512);

        page.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n");
        page.Append("<title>").Append(Escape(title.Length > 0 ? title : "Document")).Append("</title>\n");

        var stylesheet = options.Stylesheet ?? DefaultStylesheet;

        if (stylesheet.Length > 0)
        {
            page.Append("<style>").Append(stylesheet).Append("</style>\n");
        }

        page.Append("</head>\n<body>\n").Append(body).Append("</body>\n</html>\n");

        return page.ToString();
    }

    private static void WriteBlock(StringBuilder html, HtmlBlock block, HtmlWriteOptions options)
    {
        switch (block.Kind)
        {
            case HtmlBlockKind.Heading:
            {
                var level = Math.Clamp(block.HeadingLevel, 1, 6);

                html.Append("<h").Append(level).Append(BlockStyle(block.Style)).Append('>');
                AppendSpans(html, block.Spans);
                html.Append("</h").Append(level).Append(">\n");
                break;
            }

            case HtmlBlockKind.Paragraph:
                html.Append("<p").Append(BlockStyle(block.Style)).Append('>');
                AppendSpans(html, block.Spans);
                html.Append("</p>\n");
                break;

            case HtmlBlockKind.Code:
                html.Append("<pre>").Append(Escape(block.PlainText)).Append("</pre>\n");
                break;

            case HtmlBlockKind.Rule:
                html.Append("<hr>\n");
                break;

            case HtmlBlockKind.Table:
                WriteTable(html, block);
                break;

            case HtmlBlockKind.Image:
                WriteImage(html, block, options);
                break;
        }
    }

    private static void WriteTable(StringBuilder html, HtmlBlock block)
    {
        var rows = block.TableRows ?? [];

        if (rows.Count == 0)
        {
            return;
        }

        html.Append("<table>\n");

        var start = 0;

        if (block.TableHasHeader)
        {
            html.Append("<thead>\n<tr>");

            foreach (var cell in rows[0])
            {
                html.Append("<th>").Append(Escape(cell).Replace("\n", "<br>", StringComparison.Ordinal))
                    .Append("</th>");
            }

            html.Append("</tr>\n</thead>\n");
            start = 1;
        }

        html.Append("<tbody>\n");

        for (var r = start; r < rows.Count; r++)
        {
            html.Append("<tr>");

            foreach (var cell in rows[r])
            {
                html.Append("<td>").Append(Escape(cell).Replace("\n", "<br>", StringComparison.Ordinal))
                    .Append("</td>");
            }

            html.Append("</tr>\n");
        }

        html.Append("</tbody>\n</table>\n");
    }

    private static void WriteImage(StringBuilder html, HtmlBlock block, HtmlWriteOptions options)
    {
        var alt = block.AltText ?? string.Empty;

        if (options.EmbedImages && block.Image is { Length: > 0 } bytes &&
            ImageInfo.TryRead(bytes, out var info) && MimeType(info.Format) is { } mime)
        {
            html.Append("<p><img src=\"data:").Append(mime).Append(";base64,")
                .Append(Convert.ToBase64String(bytes))
                .Append("\" alt=\"").Append(EscapeAttribute(alt)).Append("\"></p>\n");
            return;
        }

        // A format a browser cannot display, or embedding switched off: the alt text is what a
        // reader would have been given anyway, and a broken image icon is worse than nothing.
        if (alt.Length > 0)
        {
            html.Append("<p><em>").Append(Escape(alt)).Append("</em></p>\n");
        }
    }

    /// <summary>The MIME type a browser will display, or <c>null</c> for one it will not.</summary>
    private static string? MimeType(ImageFormat format) => format switch
    {
        ImageFormat.Png => "image/png",
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Gif => "image/gif",
        ImageFormat.Bmp => "image/bmp",
        ImageFormat.Webp => "image/webp",
        ImageFormat.Svg => "image/svg+xml",
        _ => null,
    };

    private static void AppendSpans(StringBuilder html, IEnumerable<HtmlSpan> spans)
    {
        foreach (var span in spans)
        {
            if (span.Text.Length == 0)
            {
                continue;
            }

            var href = SafeHref(span.Hyperlink);

            if (href is not null)
            {
                html.Append("<a href=\"").Append(EscapeAttribute(href)).Append("\">");
            }

            var style = span.Style;
            var closers = new Stack<string>();

            void Open(string tag)
            {
                html.Append('<').Append(tag).Append('>');
                closers.Push("</" + tag + ">");
            }

            if (style.Bold == true)
            {
                Open("strong");
            }

            if (style.Italic == true)
            {
                Open("em");
            }

            if (style.Underline == true && href is null)
            {
                Open("u");
            }

            if (style.Strike == true)
            {
                Open("s");
            }

            if (style.Position == TextPosition.Superscript)
            {
                Open("sup");
            }
            else if (style.Position == TextPosition.Subscript)
            {
                Open("sub");
            }

            var inline = InlineStyle(style);

            if (inline.Length > 0)
            {
                html.Append("<span style=\"").Append(inline).Append("\">");
                closers.Push("</span>");
            }

            html.Append(EscapeText(span.Text));

            while (closers.Count > 0)
            {
                html.Append(closers.Pop());
            }

            if (href is not null)
            {
                html.Append("</a>");
            }
        }
    }

    /// <summary>What a span needs that no element expresses.</summary>
    /// <remarks>
    /// An explicit <c>false</c> is written too. A run that is deliberately not bold inside a bold
    /// heading is information, and dropping it makes the word bold again.
    /// </remarks>
    private static string InlineStyle(CssStyle style)
    {
        var css = new StringBuilder();

        if (style.Color is { } color && !color.IsAutomatic)
        {
            css.Append("color:").Append(color.ToCssHex()).Append(';');
        }

        if (style.BackgroundColor is { } background && !background.IsAutomatic)
        {
            css.Append("background-color:").Append(background.ToCssHex()).Append(';');
        }

        if (style.FontFamily is { Length: > 0 } family)
        {
            css.Append("font-family:").Append(CssString(family)).Append(';');
        }

        if (style.FontSize is { } size && size.Points > 0)
        {
            css.Append("font-size:").Append(size.Points.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("pt;");
        }

        if (style.Bold == false)
        {
            css.Append("font-weight:normal;");
        }

        if (style.Italic == false)
        {
            css.Append("font-style:normal;");
        }

        return css.Length > 0 ? EscapeAttribute(css.ToString().TrimEnd(';')) : string.Empty;
    }

    private static string BlockStyle(CssStyle style) => style.Alignment switch
    {
        TextAlign.Center => " style=\"text-align:center\"",
        TextAlign.Right => " style=\"text-align:right\"",
        TextAlign.Justify => " style=\"text-align:justify\"",
        _ => string.Empty,
    };

    /// <summary>
    /// Keeps a link only when its scheme is one a reader would expect to follow.
    /// </summary>
    private static string? SafeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var trimmed = href.Trim();

        if (trimmed.StartsWith('#') || trimmed.StartsWith('/') || trimmed.StartsWith("./", StringComparison.Ordinal) ||
            trimmed.StartsWith("../", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var colon = trimmed.IndexOf(':');

        if (colon < 0)
        {
            // No scheme at all: a relative path, which cannot run anything.
            return trimmed;
        }

        var scheme = trimmed[..colon].ToLowerInvariant();

        return scheme is "http" or "https" or "mailto" or "tel" or "ftp" ? trimmed : null;
    }

    private static string CssString(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "'" + value.Replace("'", string.Empty, StringComparison.Ordinal) + "'" : value;

    /// <summary>Escapes text for element content, keeping tabs and line breaks visible.</summary>
    private static string EscapeText(string text) =>
        Escape(text)
            .Replace("\n", "<br>", StringComparison.Ordinal)
            .Replace("\t", "&emsp;", StringComparison.Ordinal);

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EscapeAttribute(string text) =>
        Escape(text).Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>
    /// Opens and closes nested <c>ul</c> and <c>ol</c> elements as list items arrive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HTML nests a list <em>inside</em> the item it hangs from, so the item cannot be closed until
    /// it is known whether a deeper list follows. Each open level therefore remembers whether it has
    /// an item still open.
    /// </para>
    /// <para>
    /// A level that jumps by more than one — level 0 straight to level 2 — has no item to hang the
    /// middle list from, and <c>&lt;ul&gt;&lt;ul&gt;</c> is not valid HTML. An unmarked item is opened
    /// to hold it, which renders the same and keeps the markup valid.
    /// </para>
    /// </remarks>
    private sealed class ListWriter(StringBuilder html)
    {
        private readonly List<(string Tag, bool ItemOpen)> _open = [];
        private int _listId = -1;

        public void Item(HtmlBlock block)
        {
            var tag = block.Numbered ? "ol" : "ul";
            var depth = Math.Clamp(block.ListLevel, 0, 8) + 1;

            if (block.ListId != _listId)
            {
                CloseAll();
                _listId = block.ListId;
            }

            while (_open.Count > depth)
            {
                Close();
            }

            if (_open.Count == depth)
            {
                // Same level: finish the previous item, and restart the list if its kind changed.
                if (_open[^1].Tag != tag)
                {
                    Close();
                }
                else if (_open[^1].ItemOpen)
                {
                    html.Append("</li>\n");
                    _open[^1] = (_open[^1].Tag, false);
                }
            }

            while (_open.Count < depth)
            {
                if (_open.Count > 0 && !_open[^1].ItemOpen)
                {
                    html.Append("<li style=\"list-style:none\">");
                    _open[^1] = (_open[^1].Tag, true);
                }

                var opening = _open.Count == depth - 1 ? tag : "ul";
                html.Append('<').Append(opening);

                if (opening == "ol" && _open.Count == depth - 1 && block.ListNumber > 1)
                {
                    html.Append(" start=\"").Append(block.ListNumber.ToString(CultureInfo.InvariantCulture))
                        .Append('"');
                }

                html.Append(">\n");
                _open.Add((opening, false));
            }

            html.Append("<li>");
            _open[^1] = (_open[^1].Tag, true);
        }

        public void CloseAll()
        {
            while (_open.Count > 0)
            {
                Close();
            }

            _listId = -1;
        }

        private void Close()
        {
            var (tag, itemOpen) = _open[^1];

            if (itemOpen)
            {
                html.Append("</li>\n");
            }

            html.Append("</").Append(tag).Append(">\n");
            _open.RemoveAt(_open.Count - 1);
        }
    }
}
