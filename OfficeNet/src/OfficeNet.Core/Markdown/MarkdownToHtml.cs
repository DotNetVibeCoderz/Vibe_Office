// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;

namespace OfficeNet.Core.Markdown;

/// <summary>Which Markdown dialect is read.</summary>
/// <remarks>
/// The defaults are GitHub-flavoured: CommonMark plus tables, strikethrough and bare URLs. Turning
/// the three extensions off gives strict CommonMark.
/// </remarks>
public sealed class MarkdownOptions
{
    /// <summary>GFM pipe tables.</summary>
    public bool Tables { get; set; } = true;

    /// <summary>GFM <c>~~strikethrough~~</c>.</summary>
    public bool Strikethrough { get; set; } = true;

    /// <summary>GFM extended autolinks: a bare <c>https://…</c> or <c>www.…</c> becomes a link.</summary>
    public bool Autolinks { get; set; } = true;

    /// <summary>
    /// Whether raw HTML in the source passes through. When off, it is shown as the text it is.
    /// </summary>
    public bool AllowHtml { get; set; } = true;
}

/// <summary>
/// Converts Markdown to HTML — CommonMark, with GitHub's tables, strikethrough and autolinks.
/// </summary>
/// <remarks>
/// <para>
/// Written here rather than taken as a dependency, for the reason everything in OfficeNet is: the
/// core packages have none beyond the runtime. It follows the reference implementation's
/// algorithm and is tested against the CommonMark specification's own examples.
/// </para>
/// <para>
/// Markdown becomes a Word document by way of this HTML and the HTML importer, so Markdown and
/// HTML agree about what a heading, a list and a table are by construction rather than by care.
/// </para>
/// </remarks>
public static class MarkdownToHtml
{
    /// <summary>Converts a Markdown document to an HTML fragment.</summary>
    public static string Convert(string markdown, MarkdownOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var document = new BlockParser(options ?? new MarkdownOptions()).Parse(markdown);
        var renderer = new Renderer();
        renderer.Render(document);
        return renderer.ToString();
    }

    /// <summary>
    /// Writes the tree as HTML, in the reference implementation's exact layout so the
    /// specification's expected output can be compared directly.
    /// </summary>
    private sealed class Renderer
    {
        private readonly StringBuilder _out = new();
        private int _disableTags;

        public override string ToString() => _out.ToString();

        private void Out(string text) => _out.Append(text);

        private void Tag(string text)
        {
            if (_disableTags == 0)
            {
                _out.Append(text);
            }
        }

        private void Cr()
        {
            if (_out.Length > 0 && _out[^1] != '\n')
            {
                _out.Append('\n');
            }
        }

        private void Children(MarkdownNode node)
        {
            foreach (var child in node.Children)
            {
                Render(child);
            }
        }

        public void Render(MarkdownNode node)
        {
            switch (node.Type)
            {
                case MarkdownNodeType.Document:
                    Children(node);
                    break;

                case MarkdownNodeType.Paragraph:
                    if (node.Parent?.Parent is { Type: MarkdownNodeType.List, List.Tight: true })
                    {
                        Children(node);
                        break;
                    }

                    Cr();
                    Tag("<p>");
                    Children(node);
                    Tag("</p>");
                    Cr();
                    break;

                case MarkdownNodeType.BlockQuote:
                    Cr();
                    Tag("<blockquote>");
                    Cr();
                    Children(node);
                    Cr();
                    Tag("</blockquote>");
                    Cr();
                    break;

                case MarkdownNodeType.List:
                    var list = node.List!;
                    var open = !list.Ordered ? "<ul>" :
                        list.Start != 1 ? $"<ol start=\"{list.Start}\">" : "<ol>";

                    Cr();
                    Tag(open);
                    Cr();
                    Children(node);
                    Cr();
                    Tag(list.Ordered ? "</ol>" : "</ul>");
                    Cr();
                    break;

                case MarkdownNodeType.Item:
                    Tag("<li>");
                    Children(node);
                    Tag("</li>");
                    Cr();
                    break;

                case MarkdownNodeType.Heading:
                    Cr();
                    Tag($"<h{node.Level}>");
                    Children(node);
                    Tag($"</h{node.Level}>");
                    Cr();
                    break;

                case MarkdownNodeType.CodeBlock:
                    var language = node.Info.Split([' ', '\t'], 2)[0];

                    Cr();
                    Out(language.Length > 0 ? $"<pre><code class=\"language-{Escape(language)}\">" : "<pre><code>");
                    Out(Escape(node.Literal));
                    Out("</code></pre>");
                    Cr();
                    break;

                case MarkdownNodeType.HtmlBlock:
                    Cr();
                    Out(node.Literal);
                    Cr();
                    break;

                case MarkdownNodeType.ThematicBreak:
                    Cr();
                    Tag("<hr />");
                    Cr();
                    break;

                case MarkdownNodeType.Table:
                    Cr();
                    Out("<table>\n<thead>\n");
                    Render(node.FirstChild!);
                    Out("</thead>\n");

                    if (node.FirstChild!.Next is not null)
                    {
                        Out("<tbody>\n");

                        for (var row = node.FirstChild.Next; row is not null; row = row.Next)
                        {
                            Render(row);
                        }

                        Out("</tbody>\n");
                    }

                    Out("</table>");
                    Cr();
                    break;

                case MarkdownNodeType.TableRow:
                    Out("<tr>\n");
                    Children(node);
                    Out("</tr>\n");
                    break;

                case MarkdownNodeType.TableCell:
                    var cell = node.IsHeader ? "th" : "td";
                    Out(node.Align is null ? $"<{cell}>" : $"<{cell} align=\"{node.Align}\">");
                    Children(node);
                    Out($"</{cell}>\n");
                    break;

                case MarkdownNodeType.Text:
                    Out(Escape(node.Literal));
                    break;

                case MarkdownNodeType.SoftBreak:
                    Out("\n");
                    break;

                case MarkdownNodeType.LineBreak:
                    Tag("<br />");
                    Cr();
                    break;

                case MarkdownNodeType.Code:
                    Tag("<code>");
                    Out(Escape(node.Literal));
                    Tag("</code>");
                    break;

                case MarkdownNodeType.HtmlInline:
                    Out(node.Literal);
                    break;

                case MarkdownNodeType.Emphasis:
                    Wrap(node, "em");
                    break;

                case MarkdownNodeType.Strong:
                    Wrap(node, "strong");
                    break;

                case MarkdownNodeType.Strikethrough:
                    Wrap(node, "del");
                    break;

                case MarkdownNodeType.Link:
                    Tag($"<a href=\"{Escape(node.Destination)}\"" +
                        (node.Title.Length > 0 ? $" title=\"{Escape(node.Title)}\"" : string.Empty) + ">");
                    Children(node);
                    Tag("</a>");
                    break;

                case MarkdownNodeType.Image:
                    // The alt text is the image's content as plain text, with no tags inside.
                    if (_disableTags == 0)
                    {
                        Out($"<img src=\"{Escape(node.Destination)}\" alt=\"");
                    }

                    _disableTags++;
                    Children(node);
                    _disableTags--;

                    if (_disableTags == 0)
                    {
                        if (node.Title.Length > 0)
                        {
                            Out($"\" title=\"{Escape(node.Title)}");
                        }

                        Out("\" />");
                    }

                    break;
            }
        }

        private void Wrap(MarkdownNode node, string tag)
        {
            Tag($"<{tag}>");
            Children(node);
            Tag($"</{tag}>");
        }

        private static string Escape(string text)
        {
            if (text.AsSpan().IndexOfAny("&<>\"") < 0)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length + 16);

            foreach (var c in text)
            {
                builder.Append(c switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' => "&quot;",
                    _ => c.ToString(),
                });
            }

            return builder.ToString();
        }
    }
}
