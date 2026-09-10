// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;

namespace OfficeNet.Core.Html;

/// <summary>A node in a parsed HTML fragment: either an element or a run of text.</summary>
public sealed class HtmlNode
{
    private readonly List<HtmlNode> _children = [];

    private HtmlNode(string name, string? text)
    {
        Name = name;
        Text = text;
    }

    /// <summary>The lower-cased tag name, or <c>#text</c> for a text node.</summary>
    public string Name { get; }

    /// <summary>The text content of a text node; <c>null</c> for an element.</summary>
    public string? Text { get; }

    /// <summary>True when this is a text node rather than an element.</summary>
    public bool IsText => Name == "#text";

    /// <summary>The node's parent, or <c>null</c> at the root.</summary>
    public HtmlNode? Parent { get; private set; }

    /// <summary>The node's children, in document order.</summary>
    public IReadOnlyList<HtmlNode> Children => _children;

    /// <summary>The element's attributes, lower-cased names.</summary>
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal static HtmlNode Element(string name) => new(name.ToLowerInvariant(), null);

    internal static HtmlNode TextNode(string text) => new("#text", text);

    internal void Add(HtmlNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    /// <summary>An attribute's value, or <c>null</c>.</summary>
    public string? Attribute(string name) => Attributes.GetValueOrDefault(name);

    /// <summary>The node's text content, with every descendant's text concatenated.</summary>
    public string InnerText
    {
        get
        {
            if (IsText)
            {
                return Text ?? string.Empty;
            }

            var builder = new StringBuilder();
            Collect(this, builder);
            return builder.ToString();

            static void Collect(HtmlNode node, StringBuilder builder)
            {
                foreach (var child in node._children)
                {
                    if (child.IsText)
                    {
                        builder.Append(child.Text);
                    }
                    else if (child.Name == "br")
                    {
                        builder.Append('\n');
                    }
                    else
                    {
                        Collect(child, builder);
                    }
                }
            }
        }
    }

    /// <summary>Every descendant element with a given tag name.</summary>
    public IEnumerable<HtmlNode> Descendants(string name)
    {
        foreach (var child in _children)
        {
            if (!child.IsText && child.Name == name)
            {
                yield return child;
            }

            foreach (var nested in child.Descendants(name))
            {
                yield return nested;
            }
        }
    }

    /// <summary>The direct child elements with a given tag name.</summary>
    public IEnumerable<HtmlNode> Elements(string name) =>
        _children.Where(c => !c.IsText && c.Name == name);

    /// <summary>The direct child elements.</summary>
    public IEnumerable<HtmlNode> Elements() => _children.Where(c => !c.IsText);

    public override string ToString() =>
        IsText ? $"\"{Text}\"" : $"<{Name}> ({_children.Count} children)";
}

/// <summary>
/// A tolerant HTML parser covering the subset a document-to-slides conversion needs.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not an HTML5 parser. The full algorithm is thousands of lines of error
/// recovery for cases a hand-written or exported fragment never produces, and pulling in a browser
/// -grade parser would be the only third-party dependency PowerPointNet has. What is implemented is
/// the part that matters for conversion: tags with attributes, void elements, implicit closes
/// (<c>&lt;p&gt;</c> closing <c>&lt;p&gt;</c>, <c>&lt;li&gt;</c> closing <c>&lt;li&gt;</c>),
/// comments, raw-text elements, and character references.
/// </para>
/// <para>
/// The limits are real and worth knowing: no tag-soup reconstruction (a stray <c>&lt;/div&gt;</c>
/// is ignored rather than triggering the adoption agency algorithm), no table-section inference,
/// and no scripting. HTML written for a browser to render will convert; HTML that relies on a
/// browser to repair it may not.
/// </para>
/// </remarks>
public static class HtmlParser
{
    /// <summary>Elements that never have a closing tag.</summary>
    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
        "param", "source", "track", "wbr",
    };

    /// <summary>Elements whose content is text, not markup.</summary>
    private static readonly HashSet<string> RawTextElements = new(StringComparer.Ordinal)
    {
        "script", "style", "textarea", "title",
    };

    /// <summary>
    /// Which open elements a start tag implicitly closes.
    /// </summary>
    /// <remarks>
    /// A well-formed document closes its own tags, and a real one does not: <c>&lt;li&gt;a&lt;li&gt;b</c>
    /// is legal HTML and means two list items, not a nested one. Without these rules a converted
    /// list comes out indented one level deeper on every item.
    /// </remarks>
    private static readonly Dictionary<string, HashSet<string>> ImplicitCloses =
        new(StringComparer.Ordinal)
        {
            ["li"] = new(StringComparer.Ordinal) { "li" },
            ["dt"] = new(StringComparer.Ordinal) { "dt", "dd" },
            ["dd"] = new(StringComparer.Ordinal) { "dt", "dd" },
            ["tr"] = new(StringComparer.Ordinal) { "tr", "td", "th" },
            ["td"] = new(StringComparer.Ordinal) { "td", "th" },
            ["th"] = new(StringComparer.Ordinal) { "td", "th" },
            ["p"] = new(StringComparer.Ordinal) { "p" },
            ["option"] = new(StringComparer.Ordinal) { "option" },
            ["thead"] = new(StringComparer.Ordinal) { "thead", "tbody", "tfoot" },
            ["tbody"] = new(StringComparer.Ordinal) { "thead", "tbody", "tfoot" },
            ["tfoot"] = new(StringComparer.Ordinal) { "thead", "tbody", "tfoot" },
        };

    /// <summary>Block elements that close an open <c>&lt;p&gt;</c>.</summary>
    private static readonly HashSet<string> ClosesParagraph = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "div", "dl", "fieldset", "figcaption",
        "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "main",
        "nav", "ol", "p", "pre", "section", "table", "ul",
    };

    /// <summary>Parses an HTML fragment or document into a tree.</summary>
    /// <returns>A synthetic root element holding the fragment's top-level nodes.</returns>
    public static HtmlNode Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var root = HtmlNode.Element("#root");
        var stack = new Stack<HtmlNode>();
        stack.Push(root);

        var position = 0;
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length == 0)
            {
                return;
            }

            stack.Peek().Add(HtmlNode.TextNode(text.ToString()));
            text.Clear();
        }

        while (position < html.Length)
        {
            var c = html[position];

            if (c != '<')
            {
                if (c == '&')
                {
                    text.Append(DecodeReference(html, ref position));
                    continue;
                }

                text.Append(c);
                position++;
                continue;
            }

            // A '<' that does not start a tag is literal text, which unescaped comparison
            // operators in prose routinely are.
            if (position + 1 >= html.Length)
            {
                text.Append(c);
                position++;
                continue;
            }

            var next = html[position + 1];

            if (next == '!')
            {
                FlushText();
                position = SkipDeclarationOrComment(html, position);
                continue;
            }

            if (next == '/')
            {
                FlushText();
                position = HandleEndTag(html, position, stack, root);
                continue;
            }

            if (!char.IsAsciiLetter(next))
            {
                text.Append(c);
                position++;
                continue;
            }

            FlushText();
            position = HandleStartTag(html, position, stack, out var opened);

            // A raw-text element's content is not markup: a '<' inside <style> is a CSS child
            // combinator, not a tag.
            if (opened is not null && RawTextElements.Contains(opened.Name))
            {
                position = ReadRawText(html, position, opened);
                PopUntil(stack, opened.Name);
            }
        }

        FlushText();
        return root;
    }

    private static int SkipDeclarationOrComment(string html, int position)
    {
        if (html.AsSpan(position).StartsWith("<!--"))
        {
            var end = html.IndexOf("-->", position + 4, StringComparison.Ordinal);
            return end < 0 ? html.Length : end + 3;
        }

        if (html.AsSpan(position).StartsWith("<![CDATA["))
        {
            var end = html.IndexOf("]]>", position + 9, StringComparison.Ordinal);
            return end < 0 ? html.Length : end + 3;
        }

        var close = html.IndexOf('>', position);
        return close < 0 ? html.Length : close + 1;
    }

    private static int HandleEndTag(string html, int position, Stack<HtmlNode> stack, HtmlNode root)
    {
        var close = html.IndexOf('>', position);

        if (close < 0)
        {
            return html.Length;
        }

        var name = html[(position + 2)..close].Trim().ToLowerInvariant();

        // A stray close tag with no matching open is ignored. Reconstructing the tree the way a
        // browser would needs the adoption agency algorithm and is out of scope.
        if (name.Length > 0 && stack.Any(n => n.Name == name))
        {
            PopUntil(stack, name);
        }

        if (stack.Count == 0)
        {
            stack.Push(root);
        }

        return close + 1;
    }

    private static void PopUntil(Stack<HtmlNode> stack, string name)
    {
        while (stack.Count > 1)
        {
            var popped = stack.Pop();

            if (popped.Name == name)
            {
                return;
            }
        }
    }

    private static int HandleStartTag(string html, int position, Stack<HtmlNode> stack,
        out HtmlNode? opened)
    {
        opened = null;
        var cursor = position + 1;

        var nameStart = cursor;

        while (cursor < html.Length && (char.IsAsciiLetterOrDigit(html[cursor]) ||
                                        html[cursor] is '-' or ':' or '_'))
        {
            cursor++;
        }

        var name = html[nameStart..cursor].ToLowerInvariant();

        if (name.Length == 0)
        {
            return position + 1;
        }

        var element = HtmlNode.Element(name);
        var selfClosing = false;

        while (cursor < html.Length)
        {
            while (cursor < html.Length && char.IsWhiteSpace(html[cursor]))
            {
                cursor++;
            }

            if (cursor >= html.Length)
            {
                break;
            }

            if (html[cursor] == '>')
            {
                cursor++;
                break;
            }

            if (html[cursor] == '/')
            {
                selfClosing = true;
                cursor++;
                continue;
            }

            cursor = ReadAttribute(html, cursor, element);
        }

        ApplyImplicitCloses(stack, name);

        stack.Peek().Add(element);

        if (!selfClosing && !VoidElements.Contains(name))
        {
            stack.Push(element);
            opened = element;
        }

        return cursor;
    }

    private static void ApplyImplicitCloses(Stack<HtmlNode> stack, string name)
    {
        if (ImplicitCloses.TryGetValue(name, out var closes) && stack.Count > 1 &&
            closes.Contains(stack.Peek().Name))
        {
            stack.Pop();
            return;
        }

        // Any block-level start tag closes an open paragraph, which is what makes
        // "<p>one<p>two" and "<p>text<ul>…" both parse the way a browser renders them.
        if (ClosesParagraph.Contains(name) && stack.Count > 1 && stack.Peek().Name == "p")
        {
            stack.Pop();
        }
    }

    private static int ReadAttribute(string html, int cursor, HtmlNode element)
    {
        var nameStart = cursor;

        while (cursor < html.Length && html[cursor] is not ('=' or '>' or '/') &&
               !char.IsWhiteSpace(html[cursor]))
        {
            cursor++;
        }

        var name = html[nameStart..cursor];

        if (name.Length == 0)
        {
            return cursor + 1;
        }

        while (cursor < html.Length && char.IsWhiteSpace(html[cursor]))
        {
            cursor++;
        }

        // A bare attribute (disabled, checked) has no value; HTML says its value is its own name.
        if (cursor >= html.Length || html[cursor] != '=')
        {
            element.Attributes[name] = name;
            return cursor;
        }

        cursor++;

        while (cursor < html.Length && char.IsWhiteSpace(html[cursor]))
        {
            cursor++;
        }

        if (cursor >= html.Length)
        {
            element.Attributes[name] = string.Empty;
            return cursor;
        }

        var quote = html[cursor];

        if (quote is '"' or '\'')
        {
            cursor++;
            var valueStart = cursor;

            while (cursor < html.Length && html[cursor] != quote)
            {
                cursor++;
            }

            element.Attributes[name] = DecodeReferences(html[valueStart..cursor]);
            return Math.Min(cursor + 1, html.Length);
        }

        var unquotedStart = cursor;

        while (cursor < html.Length && html[cursor] != '>' && !char.IsWhiteSpace(html[cursor]))
        {
            cursor++;
        }

        element.Attributes[name] = DecodeReferences(html[unquotedStart..cursor]);
        return cursor;
    }

    private static int ReadRawText(string html, int position, HtmlNode element)
    {
        var closing = $"</{element.Name}";
        var end = html.IndexOf(closing, position, StringComparison.OrdinalIgnoreCase);

        if (end < 0)
        {
            element.Add(HtmlNode.TextNode(html[position..]));
            return html.Length;
        }

        // Raw text is not entity-decoded inside <style> or <script>, but <title> and <textarea>
        // are; decoding all four is harmless because CSS and JavaScript rarely contain '&'.
        element.Add(HtmlNode.TextNode(html[position..end]));

        var close = html.IndexOf('>', end);
        return close < 0 ? html.Length : close + 1;
    }

    // ---- Character references ----------------------------------------------------------------

    private static readonly Dictionary<string, string> NamedReferences = new(StringComparer.Ordinal)
    {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'",
        ["nbsp"] = " ", ["ndash"] = "–", ["mdash"] = "—", ["hellip"] = "…",
        ["lsquo"] = "‘", ["rsquo"] = "’", ["ldquo"] = "“", ["rdquo"] = "”",
        ["bull"] = "•", ["middot"] = "·", ["copy"] = "©", ["reg"] = "®", ["trade"] = "™",
        ["deg"] = "°", ["plusmn"] = "±", ["times"] = "×", ["divide"] = "÷",
        ["euro"] = "€", ["pound"] = "£", ["yen"] = "¥", ["cent"] = "¢",
        ["laquo"] = "«", ["raquo"] = "»", ["sect"] = "§", ["para"] = "¶",
        ["dagger"] = "†", ["Dagger"] = "‡", ["permil"] = "‰", ["prime"] = "′",
        ["frac12"] = "½", ["frac14"] = "¼", ["frac34"] = "¾",
        ["larr"] = "←", ["rarr"] = "→", ["uarr"] = "↑", ["darr"] = "↓", ["harr"] = "↔",
        ["ne"] = "≠", ["le"] = "≤", ["ge"] = "≥", ["asymp"] = "≈", ["infin"] = "∞",
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["pi"] = "π",
        ["sigma"] = "σ", ["omega"] = "ω", ["mu"] = "µ", ["lambda"] = "λ",
        ["check"] = "✓", ["cross"] = "✗", ["star"] = "★", ["hearts"] = "♥",
    };

    /// <summary>Decodes every character reference in a string.</summary>
    public static string DecodeReferences(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!value.Contains('&'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var position = 0;

        while (position < value.Length)
        {
            if (value[position] == '&')
            {
                builder.Append(DecodeReference(value, ref position));
                continue;
            }

            builder.Append(value[position++]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes one character reference, advancing past it.
    /// </summary>
    /// <remarks>
    /// A bare ampersand is legal in HTML text and must survive as itself; treating every '&amp;'
    /// as the start of a reference turns "R&amp;D" into a parse error or a dropped character.
    /// </remarks>
    private static string DecodeReference(string html, ref int position)
    {
        var start = position;
        position++; // '&'

        if (position >= html.Length)
        {
            return "&";
        }

        if (html[position] == '#')
        {
            position++;
            var hex = position < html.Length && (html[position] is 'x' or 'X');

            if (hex)
            {
                position++;
            }

            var digitsStart = position;

            while (position < html.Length &&
                   (hex ? Uri.IsHexDigit(html[position]) : char.IsAsciiDigit(html[position])))
            {
                position++;
            }

            var digits = html[digitsStart..position];

            if (position < html.Length && html[position] == ';')
            {
                position++;
            }

            if (digits.Length > 0 &&
                int.TryParse(digits, hex ? System.Globalization.NumberStyles.HexNumber
                    : System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var code) &&
                code is > 0 and <= 0x10FFFF && !(code is >= 0xD800 and <= 0xDFFF))
            {
                return char.ConvertFromUtf32(code);
            }

            position = start + 1;
            return "&";
        }

        var nameStart = position;

        while (position < html.Length && char.IsAsciiLetterOrDigit(html[position]))
        {
            position++;
        }

        var name = html[nameStart..position];

        if (position < html.Length && html[position] == ';')
        {
            position++;
        }

        if (NamedReferences.TryGetValue(name, out var replacement))
        {
            return replacement;
        }

        // An unrecognised reference stays as it was written, which is what a browser does.
        position = start + 1;
        return "&";
    }
}
