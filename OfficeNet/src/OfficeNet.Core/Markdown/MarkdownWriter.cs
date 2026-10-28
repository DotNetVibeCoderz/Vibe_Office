// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Text.RegularExpressions;
using OfficeNet.Core.Html;

namespace OfficeNet.Core.Markdown;

/// <summary>How a list of blocks is written out as Markdown.</summary>
public sealed class MarkdownWriteOptions
{
    /// <summary>
    /// Whether pictures are embedded as <c>data:</c> URIs. When off, and when
    /// <see cref="ImageUrl"/> gives none, a picture is replaced by its alt text.
    /// </summary>
    public bool EmbedImages { get; set; } = true;

    /// <summary>
    /// Decides where each picture lives: given its bytes and alt text, returns the URL to write —
    /// typically after saving the bytes to a file beside the Markdown — or <c>null</c> to fall back
    /// to <see cref="EmbedImages"/>.
    /// </summary>
    public Func<byte[], string?, string?>? ImageUrl { get; set; }
}

/// <summary>
/// Writes a list of formatted blocks as GitHub-flavoured Markdown — the Markdown counterpart of
/// <see cref="HtmlWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It writes from the same blocks the HTML writer does, so a document exported to both says the
/// same thing twice. Markdown has fewer words than HTML: colour, font, size, alignment and
/// underline have no syntax and are dropped; superscript and subscript are written as the inline
/// HTML Markdown allows.
/// </para>
/// <para>
/// The output is meant to read back as what was written. Text that would otherwise be read as
/// syntax — an asterisk, a <c>#</c> or <c>1.</c> at the start of a line — is escaped; emphasis
/// markers are kept tight against the words they wrap, because <c>** bold **</c> is not bold;
/// and two lists in a row get different markers, because a blank line alone would merge them.
/// </para>
/// </remarks>
public static partial class MarkdownWriter
{
    /// <summary>Writes the blocks as Markdown.</summary>
    public static string Write(IEnumerable<HtmlBlock> blocks, MarkdownWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        options ??= new MarkdownWriteOptions();

        var output = new StringBuilder();
        var list = blocks as IReadOnlyList<HtmlBlock> ?? blocks.ToList();

        // Per level: the indentation of an item's content, which is where a nested item starts.
        var indents = new string[10];
        indents[0] = string.Empty;

        HtmlBlock? previous = null;
        var lastListId = 0;
        var lastListKind = (Numbered: false, Alternate: false);
        var alternate = false;

        for (var index = 0; index < list.Count; index++)
        {
            var block = list[index];
            string text;

            if (block.Kind == HtmlBlockKind.Code)
            {
                // Code paragraphs in a row are one listing; Word keeps a listing a line apiece.
                var lines = new List<string> { block.PlainText };

                while (index + 1 < list.Count && list[index + 1].Kind == HtmlBlockKind.Code &&
                       list[index + 1].Quote == block.Quote)
                {
                    lines.Add(list[++index].PlainText);
                }

                text = Fence(string.Join('\n', lines));
            }
            else if (block.Kind == HtmlBlockKind.ListItem)
            {
                var level = Math.Clamp(block.ListLevel, 0, 8);

                // A new list straight after another uses the other marker, or the two would merge.
                if (block.ListId != lastListId && level == 0)
                {
                    alternate = previous?.Kind == HtmlBlockKind.ListItem && lastListKind.Numbered == block.Numbered &&
                                !lastListKind.Alternate;
                    lastListId = block.ListId;
                    lastListKind = (block.Numbered, alternate);
                }

                // Markdown takes a list's start from its first number, so an item nobody counted
                // is 1 rather than 0.
                var marker = block.Numbered
                    ? $"{(block.ListNumber > 0 ? block.ListNumber : 1)}{(alternate ? ')' : '.')}"
                    : alternate ? "*" : "-";

                indents[level + 1] = indents[level] + new string(' ', marker.Length + 1);

                var content = Inline(block.Spans).Replace("\n", "\n" + indents[level + 1], StringComparison.Ordinal);
                text = indents[level] + marker + " " + content;
            }
            else
            {
                text = block.Kind switch
                {
                    HtmlBlockKind.Heading => new string('#', Math.Clamp(block.HeadingLevel, 1, 6)) + " " +
                                             Inline(block.Spans).Replace("\\\n", " ", StringComparison.Ordinal),
                    HtmlBlockKind.Paragraph => Inline(block.Spans),
                    HtmlBlockKind.Rule => "---",
                    HtmlBlockKind.Table => Table(block),
                    HtmlBlockKind.Image => Image(block, options),
                    _ => string.Empty,
                };
            }

            if (text.Trim().Length == 0)
            {
                continue;
            }

            if (block.Quote)
            {
                text = string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? ">" : "> " + line));
            }

            if (previous is not null)
            {
                var sameList = previous.Kind == HtmlBlockKind.ListItem && block.Kind == HtmlBlockKind.ListItem &&
                               (previous.ListId == block.ListId || block.ListLevel > 0);

                output.Append(sameList ? "\n" : previous.Quote && block.Quote ? "\n>\n" : "\n\n");
            }

            output.Append(text);
            previous = block;
        }

        return output.Length == 0 ? string.Empty : output.Append('\n').ToString();
    }

    /// <summary>A fenced code block, with a fence longer than any run of backticks inside.</summary>
    private static string Fence(string code)
    {
        var longest = BacktickRun().Matches(code).Select(m => m.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, longest + 1));

        return fence + "\n" + code.TrimEnd('\n') + "\n" + fence;
    }

    private static string Image(HtmlBlock block, MarkdownWriteOptions options)
    {
        var alt = EscapeText(block.AltText ?? string.Empty, lineStart: false);

        if (block.Image is not { Length: > 0 } bytes)
        {
            return alt;
        }

        var url = options.ImageUrl?.Invoke(bytes, block.AltText);

        if (url is null && options.EmbedImages && Drawing.ImageInfo.TryRead(bytes, out var info) &&
            HtmlWriter.MimeType(info.Format) is { } mime)
        {
            url = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }

        return url is null ? alt : $"![{alt}]({Destination(url)})";
    }

    private static string Table(HtmlBlock block)
    {
        var rows = block.TableRows ?? [];

        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var columns = rows.Max(r => r.Count);

        // GFM tables always have a header row; a table without one uses its first row.
        var lines = new List<string> { Row(rows[0]), "|" + string.Concat(Enumerable.Repeat(" --- |", columns)) };
        lines.AddRange(rows.Skip(1).Select(Row));
        return string.Join('\n', lines);

        string Row(List<string> cells) =>
            "|" + string.Concat(Enumerable.Range(0, columns).Select(i =>
                " " + (i < cells.Count ? Cell(cells[i]) : string.Empty) + " |"));

        // EscapeText already escapes the pipe; a line break has no Markdown form inside a cell.
        static string Cell(string text) =>
            EscapeText(text.Trim(), lineStart: false).Replace("\n", "<br>", StringComparison.Ordinal);
    }

    /// <summary>One block's spans as inline Markdown.</summary>
    private static string Inline(List<HtmlSpan> spans)
    {
        var builder = new StringBuilder();

        foreach (var span in Coalesce(spans))
        {
            // Leading spaces are trimmed below, so text after them still starts the line.
            var written = builder.ToString();
            var lineStart = written[(written.LastIndexOf('\n') + 1)..].Trim().Length == 0;

            builder.Append(Span(span, lineStart));
        }

        // A line break inside a paragraph is a backslash at the end of the line; leading spaces on
        // the next line would be read as indentation.
        var text = builder.ToString().TrimEnd();
        var lines = text.Split('\n').Select((line, i) => i == 0 ? line.TrimStart() : line.TrimStart(' ', '\t'));

        return string.Join("\\\n", lines);
    }

    /// <summary>
    /// Merges neighbours that Markdown cannot tell apart, so one bold phrase in two colours is
    /// written as one bold phrase rather than <c>**a****b**</c>.
    /// </summary>
    private static List<HtmlSpan> Coalesce(List<HtmlSpan> spans)
    {
        var result = new List<HtmlSpan>();

        foreach (var span in spans)
        {
            if (result.Count > 0 && Key(result[^1]) == Key(span))
            {
                result[^1] = result[^1] with { Text = result[^1].Text + span.Text };
            }
            else
            {
                result.Add(span);
            }
        }

        return result;

        static (bool, bool, bool, bool, TextPosition?, string?) Key(HtmlSpan span) =>
            (span.Style.Bold == true, span.Style.Italic == true, span.Style.Strike == true,
                CssStyle.IsMonospace(span.Style.FontFamily), span.Style.Position, span.Hyperlink);
    }

    private static string Span(HtmlSpan span, bool atLineStart)
    {
        var style = span.Style;
        var raw = span.Text;

        // Whitespace stays outside the markers: "**bold **" is not bold.
        var core = raw.Trim(' ', '\t');

        if (core.Length == 0)
        {
            return raw;
        }

        var lead = raw[..raw.IndexOf(core, StringComparison.Ordinal)];
        var trail = raw[(lead.Length + core.Length)..];

        string text;

        if (CssStyle.IsMonospace(style.FontFamily) && !core.Contains('\n', StringComparison.Ordinal))
        {
            text = CodeSpan(core);
        }
        else
        {
            text = EscapeText(core, atLineStart);

            if (style.Strike == true)
            {
                text = "~~" + text + "~~";
            }

            if (style.Italic == true)
            {
                text = "*" + text + "*";
            }

            if (style.Bold == true)
            {
                text = "**" + text + "**";
            }
        }

        if (style.Position == TextPosition.Superscript)
        {
            text = "<sup>" + text + "</sup>";
        }
        else if (style.Position == TextPosition.Subscript)
        {
            text = "<sub>" + text + "</sub>";
        }

        if (span.Hyperlink is { Length: > 0 } href)
        {
            // A link that reads as its own address is written as an autolink.
            text = core == href && AutolinkSafe().IsMatch(href)
                ? "<" + href + ">"
                : "[" + text + "](" + Destination(href) + ")";
        }

        return lead + text + trail;
    }

    private static string CodeSpan(string code)
    {
        var longest = BacktickRun().Matches(code).Select(m => m.Length).DefaultIfEmpty(0).Max();
        var ticks = new string('`', longest + 1);

        // A span that starts or ends with a backtick needs a space between it and the fence.
        var pad = code.StartsWith('`') || code.EndsWith('`') ? " " : string.Empty;
        return ticks + pad + code + pad + ticks;
    }

    /// <summary>A link destination: plain when it can be, in angle brackets when it has spaces.</summary>
    private static string Destination(string url)
    {
        var escaped = url.Replace("<", "%3C", StringComparison.Ordinal).Replace(">", "%3E", StringComparison.Ordinal);

        return escaped.Any(c => c is ' ' or '(' or ')' || char.IsControl(c))
            ? "<" + escaped.Replace("\n", "%0A", StringComparison.Ordinal) + ">"
            : escaped;
    }

    /// <summary>
    /// Escapes what would be read as syntax. Inline markers are escaped everywhere; block markers
    /// only where they would start a block.
    /// </summary>
    private static string EscapeText(string text, bool lineStart)
    {
        var builder = new StringBuilder(text.Length + 8);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // An underscore inside a word cannot open or close emphasis, so snake_case stays
            // readable; anywhere else it could.
            var intraword = c == '_' && i > 0 && i + 1 < text.Length &&
                            char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]);

            if (c is '\\' or '`' or '*' or '[' or ']' or '<' or '>' or '~' or '|' || (c == '_' && !intraword))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        var escaped = EntityLike().Replace(builder.ToString(), "\\&");

        // Each line of a paragraph could start a block.
        var lines = escaped.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (i == 0 && !lineStart)
            {
                continue;
            }

            lines[i] = BlockStart().Replace(lines[i], m => m.Groups[1].Success
                ? m.Groups[1].Value + "\\" + m.Groups[2].Value
                : m.Groups[3].Value + "\\" + m.Groups[4].Value, 1);
        }

        return string.Join('\n', lines);
    }

    [GeneratedRegex("`+")]
    private static partial Regex BacktickRun();

    [GeneratedRegex(@"^(?:https?|mailto|ftp):[^<>\s]*$", RegexOptions.IgnoreCase)]
    private static partial Regex AutolinkSafe();

    [GeneratedRegex(@"&(?=#?[A-Za-z0-9]+;)")]
    private static partial Regex EntityLike();

    // "1. item" and "1) item"; "# heading", "- item", "+ item", "= underline".
    [GeneratedRegex(@"^(\s*\d{1,9})([.)])|^(\s*)([#+=-])")]
    private static partial Regex BlockStart();
}
