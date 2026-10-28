// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.RegularExpressions;

namespace OfficeNet.Core.Markdown;

/// <summary>
/// GitHub's extended autolinks: a bare <c>https://…</c> or <c>www.…</c> in text becomes a link.
/// </summary>
/// <remarks>
/// Run after emphasis is resolved, on text outside links and code. Adjacent text nodes are merged
/// first: an underscore in a URL is a delimiter run of its own until emphasis has been processed,
/// so the URL would otherwise arrive in pieces. Trailing punctuation is not part of the link, and
/// neither is a closing parenthesis that has no opening one inside the URL — the usual case being
/// a URL written in parentheses.
/// </remarks>
internal static partial class Autolinker
{
    public static void Apply(MarkdownNode node)
    {
        if (node.Type is MarkdownNodeType.Link or MarkdownNodeType.Image)
        {
            return;
        }

        MergeText(node);

        for (var child = node.FirstChild; child is not null;)
        {
            var next = child.Next;

            if (child.Type == MarkdownNodeType.Text)
            {
                Split(child);
            }
            else
            {
                Apply(child);
            }

            child = next;
        }
    }

    private static void MergeText(MarkdownNode parent)
    {
        for (var child = parent.FirstChild; child is not null;)
        {
            var next = child.Next;

            if (child.Type == MarkdownNodeType.Text && next is { Type: MarkdownNodeType.Text })
            {
                child.Literal += next.Literal;
                next.Unlink();
                continue;
            }

            child = next;
        }
    }

    private static void Split(MarkdownNode text)
    {
        var literal = text.Literal;
        var anchor = text;
        var start = 0;

        foreach (Match match in Candidate().Matches(literal))
        {
            var url = Trim(match.Value);

            if (url.Length <= (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? 4 : 8) ||
                !url.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            var before = literal[start..match.Index];

            if (before.Length > 0)
            {
                var piece = new MarkdownNode(MarkdownNodeType.Text) { Literal = before };
                anchor.InsertAfter(piece);
                anchor = piece;
            }

            var link = new MarkdownNode(MarkdownNodeType.Link)
            {
                Destination = InlineParser.NormalizeUri(
                    url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + url : url),
            };

            link.AppendChild(new MarkdownNode(MarkdownNodeType.Text) { Literal = url });
            anchor.InsertAfter(link);
            anchor = link;
            start = match.Index + url.Length;
        }

        if (anchor == text)
        {
            return;
        }

        if (start < literal.Length)
        {
            anchor.InsertAfter(new MarkdownNode(MarkdownNodeType.Text) { Literal = literal[start..] });
        }

        text.Unlink();
    }

    private static string Trim(string url)
    {
        while (url.Length > 0)
        {
            var last = url[^1];

            if ("?!.,:*_~'\"".Contains(last, StringComparison.Ordinal))
            {
                url = url[..^1];
            }
            else if (last == ')' && url.Count(c => c == ')') > url.Count(c => c == '('))
            {
                url = url[..^1];
            }
            else
            {
                break;
            }
        }

        return url;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9/])(?:https?://|www\.)[^\s<]+", RegexOptions.IgnoreCase)]
    private static partial Regex Candidate();
}
