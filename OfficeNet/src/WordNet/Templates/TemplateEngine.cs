// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Xml;

namespace WordNet.Templates;

/// <summary>What to do with a <c>{{field}}</c> the data has no value for.</summary>
public enum MissingValueBehavior
{
    /// <summary>Write nothing.</summary>
    Empty,

    /// <summary>Leave the token as it is, so the gap is visible in the result.</summary>
    Keep,

    /// <summary>Throw <see cref="OfficeNetException"/> naming the field.</summary>
    Throw,
}

/// <summary>Options for <see cref="WordDocument.FillTemplate"/>.</summary>
public sealed record TemplateOptions
{
    /// <summary>
    /// The culture numbers and dates are formatted in. Defaults to the invariant culture, so the
    /// same data fills the same template identically on every machine; pass
    /// <c>CultureInfo.GetCultureInfo("id-ID")</c> for <c>1.234.567,50</c>.
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>What a field with no value becomes. Defaults to nothing.</summary>
    public MissingValueBehavior MissingValue { get; init; } = MissingValueBehavior.Empty;
}

/// <summary>
/// Fills a template written in Word with <c>{{field}}</c>, <c>{{#section}}…{{/section}}</c> and
/// <c>{{^section}}…{{/section}}</c> tokens.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tokens are cut out of their runs, not flattened with them.</b> Word splits typed text into
/// runs at will — a spelling mark, an edit session, a formatting change — so <c>{{nama}}</c> often
/// spans three runs. Every token is first isolated into a run of its own by splitting runs at the
/// token's edges, which keeps every character's formatting where it was. The older
/// <see cref="WordDocument.MailMerge"/> rewrote the whole paragraph as one run instead.
/// </para>
/// <para>
/// <b>Where a section's tags sit decides what repeats</b>, so a template needs no extra syntax:
/// </para>
/// <list type="bullet">
/// <item>Both tags in one paragraph: the text between them repeats inline.</item>
/// <item>Both tags in rows of one table: those rows repeat — one row per invoice line.</item>
/// <item>Otherwise: the blocks from the opening tag's paragraph to the closing tag's repeat, and a
/// paragraph holding nothing but a tag is dropped rather than left blank.</item>
/// </list>
/// <para>
/// A section's value decides how often: a list repeats once per item with the item as the context,
/// <c>false</c>, <c>null</c>, an empty string or an empty list renders nothing, and anything else
/// renders once with that value as the context. <c>{{^x}}</c> renders exactly when <c>{{#x}}</c>
/// would not. Zero is a value, not an absence, and renders.
/// </para>
/// </remarks>
internal sealed partial class TemplateEngine
{
    private static readonly XNamespace Marker = "urn:officenet:template";
    private static readonly XName TokenAttribute = Marker + "token";

    [GeneratedRegex(@"\{\{\s*(?<kind>[#^/]?)\s*(?<name>[^{}#^/][^{}]*?)\s*\}\}")]
    private static partial Regex TokenPattern();

    private readonly WordDocument _document;
    private readonly TemplateOptions _options;
    private int _replaced;

    internal TemplateEngine(WordDocument document, TemplateOptions options)
    {
        _document = document;
        _options = options;
    }

    /// <summary>Fills every part of the document; returns how many fields were written.</summary>
    internal int Fill(object? data)
    {
        foreach (var root in Revisions.RevisionCollection.Roots(_document).ToList())
        {
            foreach (var paragraph in root.Descendants(Ns.W + "p").ToList())
            {
                IsolateTokens(paragraph);
            }

            Render(root, [data]);
            root.DescendantsAndSelf().Attributes(TokenAttribute).Remove();
        }

        _document.Touch();
        return _replaced;
    }

    // ---- Isolating tokens ----------------------------------------------------------------------

    /// <summary>Cuts every token in a paragraph into a run of its own.</summary>
    private static void IsolateTokens(XElement paragraph)
    {
        // Working from the last token back keeps earlier offsets valid while runs are split.
        var matches = TokenPattern().Matches(TextOf(paragraph));

        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            SplitAt(paragraph, match.Index + match.Length);
            SplitAt(paragraph, match.Index);

            var runs = TextRuns(paragraph)
                .Where(t => t.Start >= match.Index && t.Start < match.Index + match.Length)
                .Select(t => t.Run)
                .Distinct()
                .ToList();

            var keep = runs[0];

            foreach (var extra in runs.Skip(1))
            {
                extra.Remove();
            }

            foreach (var t in keep.Elements(Ns.W + "t").Skip(1).ToList())
            {
                t.Remove();
            }

            keep.Element(Ns.W + "t")!.Value = match.Value;
            keep.SetAttributeValue(TokenAttribute, "1");
        }
    }

    private static string TextOf(XElement paragraph) =>
        string.Concat(TextRuns(paragraph).Select(t => t.Text.Value));

    /// <summary>Every <c>w:t</c> the paragraph shows, with its offset into the paragraph's text.</summary>
    private static List<(XElement Run, XElement Text, int Start)> TextRuns(XElement paragraph)
    {
        var result = new List<(XElement, XElement, int)>();
        var offset = 0;

        foreach (var (run, _) in ParagraphContent.Runs(paragraph))
        {
            foreach (var t in run.Elements(Ns.W + "t"))
            {
                result.Add((run, t, offset));
                offset += t.Value.Length;
            }
        }

        return result;
    }

    /// <summary>Makes <paramref name="position"/> fall between two runs.</summary>
    private static void SplitAt(XElement paragraph, int position)
    {
        foreach (var (run, t, start) in TextRuns(paragraph))
        {
            var end = start + t.Value.Length;

            if (position < start || position > end)
            {
                continue;
            }

            // At a run's very edge there is nothing to split.
            var before = t.ElementsBeforeSelf().Any(e => e.Name != Ns.W + "rPr");
            var after = t.ElementsAfterSelf().Any();

            if ((position == start && !before) || (position == end && !after))
            {
                if (position == start && !before)
                {
                    return;
                }

                continue;
            }

            var tail = new XElement(Ns.W + "r", run.Attributes().Where(a => a.Name != TokenAttribute));

            if (run.Element(Ns.W + "rPr") is { } properties)
            {
                tail.Add(new XElement(properties));
            }

            if (position == start)
            {
                // Split before this w:t: it and everything after move.
                foreach (var element in new[] { t }.Concat(t.ElementsAfterSelf()).ToList())
                {
                    element.Remove();
                    tail.Add(element);
                }
            }
            else if (position == end)
            {
                foreach (var element in t.ElementsAfterSelf().ToList())
                {
                    element.Remove();
                    tail.Add(element);
                }
            }
            else
            {
                var cut = position - start;
                var rest = XmlUtil.TextElement(Ns.W + "t", t.Value[cut..]);
                t.Value = t.Value[..cut];
                t.SetAttributeValue(XNamespace.Xml + "space", "preserve");

                var following = t.ElementsAfterSelf().ToList();
                following.ForEach(e => e.Remove());
                tail.Add(rest);
                tail.Add(following);
            }

            run.AddAfterSelf(tail);
            return;
        }
    }

    // ---- Rendering -----------------------------------------------------------------------------

    private sealed record Token(XElement Run, string Kind, string Name, string? Format);

    private static IEnumerable<Token> Tokens(XElement container) =>
        container.Descendants(Ns.W + "r")
            .Where(r => r.Attribute(TokenAttribute) is not null)
            .Select(Parse)
            .OfType<Token>()
            .ToList();

    private static Token? Parse(XElement run)
    {
        var match = TokenPattern().Match(run.Element(Ns.W + "t")?.Value ?? "");

        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();
        string? format = null;
        var colon = name.IndexOf(':');

        if (colon > 0)
        {
            format = name[(colon + 1)..];
            name = name[..colon].Trim();
        }

        return new Token(run, match.Groups["kind"].Value, name, format);
    }

    /// <summary>Expands every section in <paramref name="container"/>, then fills its fields.</summary>
    private void Render(XElement container, List<object?> contexts)
    {
        while (Tokens(container).FirstOrDefault(t => t.Kind is "#" or "^") is { } open)
        {
            var close = FindClose(container, open)
                ?? throw new OfficeNetException(
                    $"The template opens {{{{{open.Kind}{open.Name}}}}} but never closes it with {{{{/{open.Name}}}}}.");

            ExpandSection(open, close, contexts);
        }

        if (Tokens(container).FirstOrDefault(t => t.Kind == "/") is { } stray)
        {
            throw new OfficeNetException(
                $"The template closes {{{{/{stray.Name}}}}} without opening it.");
        }

        foreach (var field in Tokens(container))
        {
            FillField(field, contexts);
        }
    }

    private static Token? FindClose(XElement container, Token open)
    {
        var depth = 0;

        foreach (var token in Tokens(container).SkipWhile(t => t.Run != open.Run).Skip(1))
        {
            if (token.Name != open.Name)
            {
                continue;
            }

            if (token.Kind is "#" or "^")
            {
                depth++;
            }
            else if (token.Kind == "/")
            {
                if (depth == 0)
                {
                    return token;
                }

                depth--;
            }
        }

        return null;
    }

    private void ExpandSection(Token open, Token close, List<object?> contexts)
    {
        var items = Items(Lookup(open.Name, contexts), inverted: open.Kind == "^");

        var openParagraph = open.Run.Ancestors(Ns.W + "p").First();
        var closeParagraph = close.Run.Ancestors(Ns.W + "p").First();

        List<XNode> region;
        XNode anchor;

        if (openParagraph == closeParagraph)
        {
            // Inline: the runs between the two tags.
            if (open.Run.Parent != close.Run.Parent)
            {
                throw new OfficeNetException(
                    $"The tags of {{{{#{open.Name}}}}} sit in different parts of one paragraph " +
                    "(one inside a hyperlink, say); put both at the same level.");
            }

            region = [.. open.Run.NodesAfterSelf().TakeWhile(n => n != close.Run)];
            anchor = open.Run;
            Emit(region, items, contexts, anchor);
            open.Run.Remove();
            close.Run.Remove();
            return;
        }

        var openRow = open.Run.Ancestors(Ns.W + "tr").FirstOrDefault();
        var closeRow = close.Run.Ancestors(Ns.W + "tr").FirstOrDefault();

        if (openRow is not null && closeRow is not null && openRow.Parent == closeRow.Parent)
        {
            // Rows: from the opening tag's row to the closing tag's, as a unit.
            open.Run.Remove();
            close.Run.Remove();

            region = [openRow, .. openRow.ElementsAfterSelf().TakeWhile(r => r != closeRow), closeRow];
            if (openRow == closeRow)
            {
                region = [openRow];
            }

            var table = openRow.Parent!;
            Emit(region, items, contexts, openRow);
            DropEmptyTable(table);
            return;
        }

        // Blocks: from the opening paragraph to the closing one, as siblings.
        var (first, last) = SiblingBlocks(openParagraph, closeParagraph, open.Name);

        open.Run.Remove();
        close.Run.Remove();

        var blocks = new List<XElement> { first };
        blocks.AddRange(first.ElementsAfterSelf().TakeWhile(e => e != last));

        if (last != first)
        {
            blocks.Add(last);
        }

        // A paragraph that held nothing but the tag is scaffolding; repeating it would put a
        // blank line around every item.
        var scaffolding = new List<XElement>();

        foreach (var paragraph in new[] { openParagraph, closeParagraph }.Distinct())
        {
            if (blocks.Contains(paragraph) && IsEmpty(paragraph))
            {
                scaffolding.Add(paragraph);
            }
        }

        region = [.. blocks.Where(b => !scaffolding.Contains(b))];
        Emit(region, items, contexts, first);

        foreach (var paragraph in scaffolding)
        {
            paragraph.Remove();
        }
    }

    /// <summary>
    /// Writes one rendered copy of <paramref name="region"/> per item before
    /// <paramref name="anchor"/>, then removes the original region.
    /// </summary>
    private void Emit(List<XNode> region, List<object?> items, List<object?> contexts, XNode anchor)
    {
        var template = region.Select(n => n is XElement e ? (XNode)new XElement(e) : n).ToList();

        // A placeholder holds the spot: the anchor itself may be part of the region and go.
        var placeholder = new XElement(Marker + "here");
        anchor.AddBeforeSelf(placeholder);

        foreach (var node in region)
        {
            node.Remove();
        }

        foreach (var item in items)
        {
            var wrapper = new XElement(Marker + "fragment",
                template.Select(n => n is XElement e ? (XNode)new XElement(e) : CloneNode(n)));

            Render(wrapper, [.. contexts, item]);
            placeholder.AddBeforeSelf(wrapper.Nodes());
        }

        placeholder.Remove();
    }

    private static XNode CloneNode(XNode node) => node switch
    {
        XText text => new XText(text),
        XComment comment => new XComment(comment),
        _ => node,
    };

    private static (XElement First, XElement Last) SiblingBlocks(XElement open, XElement close, string name)
    {
        // Walk both paragraphs up until they share a parent; those ancestors are the blocks.
        var openChain = open.AncestorsAndSelf().ToList();
        var closeChain = close.AncestorsAndSelf().ToList();

        foreach (var a in openChain)
        {
            var b = closeChain.FirstOrDefault(c => c.Parent == a.Parent && c.Parent is not null);

            if (b is not null && a.Parent is { } parent && parent.Name != Ns.W + "p" &&
                parent.Elements().ToList().IndexOf(a) <= parent.Elements().ToList().IndexOf(b))
            {
                return (a, b);
            }
        }

        throw new OfficeNetException(
            $"The tags of {{{{#{name}}}}} cannot be paired: the closing tag comes before the opening one, " +
            "or they are in different parts of the document.");
    }

    private static bool IsEmpty(XElement paragraph) =>
        TextOf(paragraph).Trim().Length == 0 &&
        !paragraph.Descendants().Any(e => e.Name == Ns.W + "drawing" || e.Name == Ns.W + "object" ||
                                          e.Name == Ns.W + "footnoteReference" ||
                                          e.Name == Ns.W + "sectPr");

    private static void DropEmptyTable(XElement table)
    {
        if (table.Elements(Ns.W + "tr").Any())
        {
            return;
        }

        // A table with no rows is invalid. A cell must still end in a paragraph if it held one.
        var container = table.Parent;
        table.Remove();

        if (container is not null && container.Name == Ns.W + "tc" && !container.Elements(Ns.W + "p").Any())
        {
            container.Add(new XElement(Ns.W + "p"));
        }
    }

    // ---- Fields --------------------------------------------------------------------------------

    private void FillField(Token field, List<object?> contexts)
    {
        var value = Lookup(field.Name, contexts, out var found);

        if (!found)
        {
            switch (_options.MissingValue)
            {
                case MissingValueBehavior.Keep:
                    // Left marked: an enclosing level may look at it again, but its contexts are
                    // a subset of these, so it cannot find a value this lookup missed.
                    return;

                case MissingValueBehavior.Throw:
                    throw new OfficeNetException($"The data has no value for {{{{{field.Name}}}}}.");
            }
        }

        var text = Format(value, field.Format);
        var run = field.Run;

        run.Elements().Where(e => e.Name != Ns.W + "rPr").Remove();
        new Run(_document, run).AppendText(text);
        _replaced++;
    }

    private string Format(object? value, string? format) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(format, _options.Culture),
        _ => value.ToString() ?? string.Empty,
    };

    // ---- Data ----------------------------------------------------------------------------------

    private static List<object?> Items(object? value, bool inverted)
    {
        var present = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            IEnumerable e => e.Cast<object?>().Any(),
            _ => true,
        };

        if (inverted)
        {
            return present ? [] : [null];
        }

        if (!present)
        {
            return [];
        }

        return value is IEnumerable list and not string and not IDictionary
            ? [.. list.Cast<object?>()]
            : [value];
    }

    private static object? Lookup(string path, List<object?> contexts) => Lookup(path, contexts, out _);

    /// <summary>
    /// Resolves a dotted path: the first segment against the innermost context that has it, the
    /// rest member by member. <c>.</c> is the current item.
    /// </summary>
    private static object? Lookup(string path, List<object?> contexts, out bool found)
    {
        if (path == ".")
        {
            found = true;
            return contexts[^1];
        }

        var segments = path.Split('.');

        for (var i = contexts.Count - 1; i >= 0; i--)
        {
            if (!TryMember(contexts[i], segments[0], out var value))
            {
                continue;
            }

            foreach (var segment in segments.Skip(1))
            {
                if (!TryMember(value, segment, out value))
                {
                    found = false;
                    return null;
                }
            }

            found = true;
            return value;
        }

        found = false;
        return null;
    }

    private static bool TryMember(object? target, string name, out object? value)
    {
        value = null;

        switch (target)
        {
            case null:
                return false;

            case IDictionary<string, object?> dictionary:
                return TryKey(dictionary, name, out value);

            case IReadOnlyDictionary<string, object?> readOnly:
                foreach (var (key, item) in readOnly)
                {
                    if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = item;
                        return true;
                    }
                }

                return false;

            case IDictionary<string, string> strings:
                foreach (var (key, item) in strings)
                {
                    if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = item;
                        return true;
                    }
                }

                return false;

            case IDictionary legacy:
                foreach (DictionaryEntry entry in legacy)
                {
                    if (string.Equals(entry.Key?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = entry.Value;
                        return true;
                    }
                }

                return false;
        }

        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        if (type.GetProperty(name, flags) is { } property && property.GetIndexParameters().Length == 0)
        {
            value = property.GetValue(target);
            return true;
        }

        if (type.GetField(name, flags) is { } field)
        {
            value = field.GetValue(target);
            return true;
        }

        return false;
    }

    private static bool TryKey(IDictionary<string, object?> dictionary, string name, out object? value)
    {
        if (dictionary.TryGetValue(name, out value))
        {
            return true;
        }

        foreach (var (key, item) in dictionary)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = item;
                return true;
            }
        }

        return false;
    }
}
