// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OfficeNet.Core.Markdown;

/// <summary>
/// The inline phase of CommonMark: a block's text into emphasis, links, code and the rest.
/// </summary>
/// <remarks>
/// <para>
/// Emphasis is not a regular language. Whether a <c>*</c> opens, closes, both or neither depends on
/// the characters either side of its run, and which opener a closer pairs with depends on the
/// lengths of both runs — the "rule of three". The spec's algorithm keeps a stack of delimiter
/// runs and resolves them once a block (or a link's text) is complete; this follows it exactly.
/// </para>
/// <para>
/// Links work the same way with a stack of brackets. A closing bracket looks back for an opener and
/// then for an inline destination, a full reference, a collapsed one or a shortcut, in that order;
/// a link inside a link deactivates the outer opener, so links never nest.
/// </para>
/// </remarks>
internal sealed partial class InlineParser
{
    // The HTML grammar the spec uses for raw inline HTML and for HTML blocks of type 7.
    internal const string TagName = "[A-Za-z][A-Za-z0-9-]*";
    internal const string AttributeName = "[a-zA-Z_:][a-zA-Z0-9:._-]*";
    internal const string AttributeValue = "(?:[^\"'=<>`\\x00-\\x20]+|'[^']*'|\"[^\"]*\")";
    internal const string Attribute = "(?:\\s+" + AttributeName + "(?:\\s*=\\s*" + AttributeValue + ")?)";
    internal const string OpenTag = "<" + TagName + Attribute + "*\\s*/?>";
    internal const string CloseTag = "</" + TagName + "\\s*[>]";
    private const string HtmlComment = "<!-->|<!--->|<!--[\\s\\S]*?-->";
    private const string ProcessingInstruction = "[<][?][\\s\\S]*?[?][>]";
    private const string Declaration = "<![A-Za-z]+[^>]*>";
    private const string Cdata = "<!\\[CDATA\\[[\\s\\S]*?\\]\\]>";

    private const string Escapable = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    private readonly Dictionary<string, (string Destination, string Title)> _references;
    private readonly MarkdownOptions _options;

    private string _subject = string.Empty;
    private int _pos;
    private Delimiter? _delimiters;
    private Bracket? _brackets;

    public InlineParser(Dictionary<string, (string Destination, string Title)> references,
        MarkdownOptions options)
    {
        _references = references;
        _options = options;
    }

    private sealed class Delimiter
    {
        public char Char;
        public int Count;
        public int OriginalCount;
        public required MarkdownNode Node;
        public Delimiter? Previous;
        public Delimiter? Next;
        public bool CanOpen;
        public bool CanClose;
    }

    private sealed class Bracket
    {
        public required MarkdownNode Node;
        public Bracket? Previous;
        public Delimiter? PreviousDelimiter;
        public int Index;
        public bool Image;
        public bool Active = true;
        public bool BracketAfter;
    }

    /// <summary>Parses the inline content of every paragraph, heading and table cell.</summary>
    public void ProcessTree(MarkdownNode node)
    {
        if (node.Type is MarkdownNodeType.Paragraph or MarkdownNodeType.Heading or MarkdownNodeType.TableCell)
        {
            Parse(node, node.Content.ToString().Trim());
            return;
        }

        for (var child = node.FirstChild; child is not null;)
        {
            var next = child.Next;
            ProcessTree(child);
            child = next;
        }
    }

    private void Parse(MarkdownNode block, string subject)
    {
        _subject = subject;
        _pos = 0;
        _delimiters = null;
        _brackets = null;

        while (_pos < _subject.Length)
        {
            ParseInline(block);
        }

        ProcessEmphasis(null);

        if (_options.Autolinks)
        {
            Autolinker.Apply(block);
        }
    }

    private char Peek() => _pos < _subject.Length ? _subject[_pos] : '\0';

    private string? Match(Regex regex)
    {
        var match = regex.Match(_subject, _pos);

        if (!match.Success)
        {
            return null;
        }

        _pos = match.Index + match.Length;
        return match.Value;
    }

    private static MarkdownNode Text(string text) => new(MarkdownNodeType.Text) { Literal = text };

    private void ParseInline(MarkdownNode block)
    {
        var c = Peek();

        var handled = c switch
        {
            '\n' => ParseNewline(block),
            '\\' => ParseBackslash(block),
            '`' => ParseBackticks(block),
            '*' or '_' => HandleDelimiter(c, block),
            '~' when _options.Strikethrough => HandleDelimiter(c, block),
            '[' => ParseOpenBracket(block),
            '!' => ParseBang(block),
            ']' => ParseCloseBracket(block),
            '<' => ParseAutolink(block) || ParseHtmlTag(block),
            '&' => ParseEntity(block),
            _ => ParseString(block),
        };

        if (!handled)
        {
            _pos++;
            block.AppendChild(Text(c.ToString()));
        }
    }

    private bool ParseNewline(MarkdownNode block)
    {
        _pos++;

        if (block.LastChild is { Type: MarkdownNodeType.Text } last && last.Literal.EndsWith(' '))
        {
            var hard = last.Literal.Length >= 2 && last.Literal[^2] == ' ';
            last.Literal = last.Literal.TrimEnd(' ');
            block.AppendChild(new MarkdownNode(hard ? MarkdownNodeType.LineBreak : MarkdownNodeType.SoftBreak));
        }
        else
        {
            block.AppendChild(new MarkdownNode(MarkdownNodeType.SoftBreak));
        }

        // Leading spaces on the next line are not content.
        while (Peek() == ' ')
        {
            _pos++;
        }

        return true;
    }

    private bool ParseBackslash(MarkdownNode block)
    {
        _pos++;

        if (Peek() == '\n')
        {
            _pos++;
            block.AppendChild(new MarkdownNode(MarkdownNodeType.LineBreak));
        }
        else if (_pos < _subject.Length && Escapable.Contains(_subject[_pos], StringComparison.Ordinal))
        {
            block.AppendChild(Text(_subject[_pos].ToString()));
            _pos++;
        }
        else
        {
            block.AppendChild(Text("\\"));
        }

        return true;
    }

    private bool ParseBackticks(MarkdownNode block)
    {
        var ticks = Match(Ticks())!;
        var afterOpen = _pos;

        while (Match(TickRun()) is { } run)
        {
            if (run.Length != ticks.Length)
            {
                continue;
            }

            var contents = _subject[afterOpen..(_pos - ticks.Length)].Replace('\n', ' ');

            // One space each side is stripped, so a span can start or end with a backtick.
            if (contents.Length > 2 && contents[0] == ' ' && contents[^1] == ' ' &&
                contents.Any(ch => ch != ' '))
            {
                contents = contents[1..^1];
            }

            block.AppendChild(new MarkdownNode(MarkdownNodeType.Code) { Literal = contents });
            return true;
        }

        // No matching closer: the backticks are literal.
        _pos = afterOpen;
        block.AppendChild(Text(ticks));
        return true;
    }

    private bool HandleDelimiter(char c, MarkdownNode block)
    {
        var (count, canOpen, canClose) = ScanDelimiters(c);

        if (count == 0)
        {
            return false;
        }

        var start = _pos;
        _pos += count;

        var node = Text(_subject[start.._pos]);
        block.AppendChild(node);

        // A run of three or more tildes is never strikethrough.
        if (c == '~' && count > 2)
        {
            return true;
        }

        if (canOpen || canClose)
        {
            _delimiters = new Delimiter
            {
                Char = c,
                Count = count,
                OriginalCount = count,
                Node = node,
                Previous = _delimiters,
                CanOpen = canOpen,
                CanClose = canClose,
            };

            if (_delimiters.Previous is not null)
            {
                _delimiters.Previous.Next = _delimiters;
            }
        }

        return true;
    }

    /// <summary>Left- and right-flanking, from the characters either side of the run.</summary>
    private (int Count, bool CanOpen, bool CanClose) ScanDelimiters(char c)
    {
        var start = _pos;
        var end = _pos;

        while (end < _subject.Length && _subject[end] == c)
        {
            end++;
        }

        var count = end - start;

        if (count == 0)
        {
            return (0, false, false);
        }

        var before = start == 0 ? '\n' : _subject[start - 1];
        var after = end >= _subject.Length ? '\n' : _subject[end];

        var afterIsWhitespace = IsWhitespace(after);
        var afterIsPunctuation = IsPunctuation(after);
        var beforeIsWhitespace = IsWhitespace(before);
        var beforeIsPunctuation = IsPunctuation(before);

        var leftFlanking = !afterIsWhitespace &&
                           (!afterIsPunctuation || beforeIsWhitespace || beforeIsPunctuation);
        var rightFlanking = !beforeIsWhitespace &&
                            (!beforeIsPunctuation || afterIsWhitespace || afterIsPunctuation);

        // An underscore inside a word is not emphasis: snake_case_names stay as they are.
        return c == '_'
            ? (count, leftFlanking && (!rightFlanking || beforeIsPunctuation),
                rightFlanking && (!leftFlanking || afterIsPunctuation))
            : (count, leftFlanking, rightFlanking);
    }

    private static bool IsWhitespace(char c) => char.IsWhiteSpace(c);

    private static bool IsPunctuation(char c) =>
        Escapable.Contains(c, StringComparison.Ordinal) || char.IsPunctuation(c) || char.IsSymbol(c);

    private static void RemoveDelimiter(ref Delimiter? top, Delimiter delimiter)
    {
        if (delimiter.Previous is not null)
        {
            delimiter.Previous.Next = delimiter.Next;
        }

        if (delimiter.Next is not null)
        {
            delimiter.Next.Previous = delimiter.Previous;
        }
        else
        {
            top = delimiter.Previous;
        }
    }

    private void ProcessEmphasis(Delimiter? stackBottom)
    {
        var openersBottom = new Dictionary<int, Delimiter?>();

        var closer = _delimiters;

        while (closer is not null && closer.Previous != stackBottom)
        {
            closer = closer.Previous;
        }

        while (closer is not null)
        {
            if (!closer.CanClose)
            {
                closer = closer.Next;
                continue;
            }

            var bottomIndex = closer.Char switch
            {
                '_' => 2,
                '~' => 14,
                _ => 8,
            } + (closer.CanOpen ? 3 : 0) + (closer.OriginalCount % 3);

            var opener = closer.Previous;
            var found = false;
            openersBottom.TryGetValue(bottomIndex, out var bottom);

            while (opener is not null && opener != stackBottom && opener != bottom)
            {
                var oddMatch = (closer.CanOpen || opener.CanClose) && closer.OriginalCount % 3 != 0 &&
                               (opener.OriginalCount + closer.OriginalCount) % 3 == 0;

                if (opener.Char == closer.Char && opener.CanOpen &&
                    (closer.Char == '~' ? opener.Count == closer.Count : !oddMatch))
                {
                    found = true;
                    break;
                }

                opener = opener.Previous;
            }

            var oldCloser = closer;

            if (!found)
            {
                closer = closer.Next;
            }
            else
            {
                var use = closer.Char == '~'
                    ? closer.Count
                    : closer.Count >= 2 && opener!.Count >= 2 ? 2 : 1;

                var openerNode = opener!.Node;
                var closerNode = closer.Node;

                opener.Count -= use;
                closer.Count -= use;
                openerNode.Literal = openerNode.Literal[..^use];
                closerNode.Literal = closerNode.Literal[..^use];

                var wrapper = new MarkdownNode(closer.Char == '~'
                    ? MarkdownNodeType.Strikethrough
                    : use == 1 ? MarkdownNodeType.Emphasis : MarkdownNodeType.Strong);

                for (var node = openerNode.Next; node is not null && node != closerNode;)
                {
                    var next = node.Next;
                    wrapper.AppendChild(node);
                    node = next;
                }

                openerNode.InsertAfter(wrapper);

                // Delimiters between the pair can no longer match anything.
                if (opener.Next != closer)
                {
                    opener.Next = closer;
                    closer.Previous = opener;
                }

                if (opener.Count == 0)
                {
                    openerNode.Unlink();
                    RemoveDelimiter(ref _delimiters, opener);
                }

                if (closer.Count == 0)
                {
                    closerNode.Unlink();
                    var next = closer.Next;
                    RemoveDelimiter(ref _delimiters, closer);
                    closer = next;
                }
            }

            if (!found)
            {
                openersBottom[bottomIndex] = oldCloser.Previous;

                if (!oldCloser.CanOpen)
                {
                    RemoveDelimiter(ref _delimiters, oldCloser);
                }
            }
        }

        while (_delimiters is not null && _delimiters != stackBottom)
        {
            RemoveDelimiter(ref _delimiters, _delimiters);
        }
    }

    private bool ParseOpenBracket(MarkdownNode block)
    {
        var start = _pos;
        _pos++;

        var node = Text("[");
        block.AppendChild(node);
        AddBracket(node, start, image: false);
        return true;
    }

    private bool ParseBang(MarkdownNode block)
    {
        var start = _pos;
        _pos++;

        if (Peek() == '[')
        {
            _pos++;
            var node = Text("![");
            block.AppendChild(node);
            AddBracket(node, start + 1, image: true);
        }
        else
        {
            block.AppendChild(Text("!"));
        }

        return true;
    }

    private void AddBracket(MarkdownNode node, int index, bool image)
    {
        if (_brackets is not null)
        {
            _brackets.BracketAfter = true;
        }

        _brackets = new Bracket
        {
            Node = node,
            Previous = _brackets,
            PreviousDelimiter = _delimiters,
            Index = index,
            Image = image,
        };
    }

    private bool ParseCloseBracket(MarkdownNode block)
    {
        var start = _pos;
        _pos++;

        var opener = _brackets;

        if (opener is null)
        {
            block.AppendChild(Text("]"));
            return true;
        }

        if (!opener.Active)
        {
            block.AppendChild(Text("]"));
            _brackets = opener.Previous;
            return true;
        }

        var isImage = opener.Image;
        var save = _pos;
        var matched = false;
        string? destination = null;
        string? title = null;

        // An inline link: [text](destination "title").
        if (Peek() == '(')
        {
            _pos++;
            SkipSpacesAndNewline();

            if ((destination = ParseLinkDestination()) is not null)
            {
                SkipSpacesAndNewline();

                if (_pos > 0 && char.IsWhiteSpace(_subject[_pos - 1]))
                {
                    title = ParseLinkTitle();
                }

                SkipSpacesAndNewline();

                if (Peek() == ')')
                {
                    _pos++;
                    matched = true;
                }
            }

            if (!matched)
            {
                _pos = save;
            }
        }

        // A reference: [text][label], [text][] or [text].
        if (!matched)
        {
            var beforeLabel = _pos;
            var length = ParseLinkLabel();
            string? label = null;

            if (length > 2)
            {
                label = _subject.Substring(beforeLabel, length);
            }
            else if (!opener.BracketAfter)
            {
                label = _subject[opener.Index..(start + 1)];
            }

            if (length == 0)
            {
                _pos = save;
            }

            if (label is not null && _references.TryGetValue(NormalizeLabel(label), out var reference))
            {
                destination = reference.Destination;
                title = reference.Title;
                matched = true;
            }
        }

        if (!matched)
        {
            _brackets = opener.Previous;
            _pos = start + 1;
            block.AppendChild(Text("]"));
            return true;
        }

        var link = new MarkdownNode(isImage ? MarkdownNodeType.Image : MarkdownNodeType.Link)
        {
            Destination = destination!,
            Title = title ?? string.Empty,
        };

        for (var node = opener.Node.Next; node is not null;)
        {
            var next = node.Next;
            link.AppendChild(node);
            node = next;
        }

        block.AppendChild(link);
        ProcessEmphasis(opener.PreviousDelimiter);
        _brackets = opener.Previous;
        opener.Node.Unlink();

        // No links inside links: every earlier link opener is now inert.
        if (!isImage)
        {
            for (var earlier = _brackets; earlier is not null; earlier = earlier.Previous)
            {
                if (!earlier.Image)
                {
                    earlier.Active = false;
                }
            }
        }

        return true;
    }

    private void SkipSpacesAndNewline() => Match(SpacesNewline());

    private string? ParseLinkDestination()
    {
        if (Match(DestinationInBraces()) is { } braced)
        {
            return NormalizeUri(Unescape(braced[1..^1]));
        }

        if (Peek() == '<')
        {
            return null;
        }

        var save = _pos;
        var parens = 0;

        while (_pos < _subject.Length)
        {
            var c = _subject[_pos];

            if (c == '\\' && _pos + 1 < _subject.Length &&
                Escapable.Contains(_subject[_pos + 1], StringComparison.Ordinal))
            {
                _pos += 2;
            }
            else if (c == '(')
            {
                _pos++;
                parens++;
            }
            else if (c == ')')
            {
                if (parens < 1)
                {
                    break;
                }

                _pos++;
                parens--;
            }
            else if (c == ' ' || char.IsControl(c))
            {
                break;
            }
            else
            {
                _pos++;
            }
        }

        if (_pos == save && Peek() != ')')
        {
            return null;
        }

        if (parens != 0)
        {
            return null;
        }

        return NormalizeUri(Unescape(_subject[save.._pos]));
    }

    private string? ParseLinkTitle() =>
        Match(LinkTitle()) is { } title ? Unescape(title[1..^1]) : null;

    private int ParseLinkLabel()
    {
        var match = LinkLabel().Match(_subject, _pos);

        if (!match.Success || match.Length > 1001)
        {
            return 0;
        }

        _pos += match.Length;
        return match.Length;
    }

    private bool ParseAutolink(MarkdownNode block)
    {
        if (Match(EmailAutolink()) is { } email)
        {
            var address = email[1..^1];
            var link = new MarkdownNode(MarkdownNodeType.Link) { Destination = NormalizeUri("mailto:" + address) };
            link.AppendChild(Text(address));
            block.AppendChild(link);
            return true;
        }

        if (Match(UriAutolink()) is { } uri)
        {
            var target = uri[1..^1];
            var link = new MarkdownNode(MarkdownNodeType.Link) { Destination = NormalizeUri(target) };
            link.AppendChild(Text(target));
            block.AppendChild(link);
            return true;
        }

        return false;
    }

    private bool ParseHtmlTag(MarkdownNode block)
    {
        if (!_options.AllowHtml || Match(HtmlTag()) is not { } tag)
        {
            return false;
        }

        block.AppendChild(new MarkdownNode(MarkdownNodeType.HtmlInline) { Literal = tag });
        return true;
    }

    private bool ParseEntity(MarkdownNode block)
    {
        if (Match(Entity()) is not { } entity)
        {
            return false;
        }

        block.AppendChild(Text(DecodeEntity(entity)));
        return true;
    }

    private bool ParseString(MarkdownNode block)
    {
        if (Match(_options.Strikethrough ? PlainTextWithTilde() : PlainText()) is not { } text)
        {
            return false;
        }

        block.AppendChild(Text(text));
        return true;
    }

    /// <summary>
    /// Reads link reference definitions from the start of a paragraph's text.
    /// </summary>
    /// <returns>How many characters they took; the paragraph is what follows.</returns>
    public static int ExtractReferences(string content,
        Dictionary<string, (string Destination, string Title)> references)
    {
        var parser = new InlineParser(references, new MarkdownOptions());
        var total = 0;

        while (total < content.Length && content[total] == '[')
        {
            var consumed = parser.ParseReference(content[total..]);

            if (consumed == 0)
            {
                break;
            }

            total += consumed;
        }

        return total;
    }

    private int ParseReference(string text)
    {
        _subject = text;
        _pos = 0;

        var labelLength = ParseLinkLabel();

        if (labelLength == 0)
        {
            return 0;
        }

        var rawLabel = _subject[..labelLength];

        if (Peek() != ':')
        {
            return 0;
        }

        _pos++;
        SkipSpacesAndNewline();

        var destination = ParseLinkDestination();

        if (destination is null)
        {
            return 0;
        }

        var beforeTitle = _pos;
        SkipSpacesAndNewline();

        string? title = null;

        if (_pos != beforeTitle)
        {
            title = ParseLinkTitle();
        }

        if (title is null)
        {
            title = string.Empty;
            _pos = beforeTitle;
        }

        // The definition must end its line; a title that is followed by more text is not a title.
        var atLineEnd = true;

        if (Match(SpaceAtEndOfLine()) is null)
        {
            if (title.Length == 0)
            {
                atLineEnd = false;
            }
            else
            {
                title = string.Empty;
                _pos = beforeTitle;
                atLineEnd = Match(SpaceAtEndOfLine()) is not null;
            }
        }

        if (!atLineEnd)
        {
            return 0;
        }

        var normalized = NormalizeLabel(rawLabel);

        if (normalized.Length == 0)
        {
            return 0;
        }

        // The first definition of a label wins.
        _references.TryAdd(normalized, (destination, title));
        return _pos;
    }

    /// <summary>Labels match case-insensitively, with runs of whitespace collapsed.</summary>
    /// <remarks>
    /// Case folding, not just upper-casing: <c>ß</c> folds to <c>ss</c>, so <c>[ẞ]</c> finds a
    /// definition written <c>[SS]</c>. It is the one full folding that changes length and that
    /// lower- and upper-casing alone miss.
    /// </remarks>
    internal static string NormalizeLabel(string label) =>
        Whitespace().Replace(label[1..^1].Trim(), " ").ToLowerInvariant()
            .Replace("ß", "ss", StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>Resolves backslash escapes and entities in a destination, title or info string.</summary>
    internal static string Unescape(string text) =>
        text.Contains('\\', StringComparison.Ordinal) || text.Contains('&', StringComparison.Ordinal)
            ? EscapeOrEntity().Replace(text, m => m.Value[0] == '\\' ? m.Value[1..] : DecodeEntity(m.Value))
            : text;

    private static string DecodeEntity(string entity)
    {
        if (entity.Length > 2 && entity[1] == '#')
        {
            var hex = entity[2] is 'x' or 'X';
            var digits = entity[(hex ? 3 : 2)..^1];

            if (!int.TryParse(digits, hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var code) ||
                code == 0 || code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF)
            {
                return "\uFFFD";
            }

            return char.ConvertFromUtf32(code);
        }

        // WebUtility knows the HTML 4 names; an unknown name stays as written, as the spec requires.
        return WebUtility.HtmlDecode(entity);
    }

    /// <summary>
    /// Percent-encodes what may not appear in a URI, leaving existing escapes alone.
    /// </summary>
    internal static string NormalizeUri(string uri)
    {
        const string Safe = ";/?:@&=+$,-_.!~*'()#";
        var builder = new StringBuilder(uri.Length);

        for (var i = 0; i < uri.Length; i++)
        {
            var c = uri[i];

            if (c == '%' && i + 2 < uri.Length && Uri.IsHexDigit(uri[i + 1]) && Uri.IsHexDigit(uri[i + 2]))
            {
                builder.Append(uri, i, 3);
                i += 2;
            }
            else if (char.IsAsciiLetterOrDigit(c) || Safe.Contains(c, StringComparison.Ordinal))
            {
                builder.Append(c);
            }
            else
            {
                var length = char.IsHighSurrogate(c) && i + 1 < uri.Length ? 2 : 1;

                foreach (var b in Encoding.UTF8.GetBytes(uri.Substring(i, length)))
                {
                    builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }

                i += length - 1;
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\G`+")]
    private static partial Regex Ticks();

    [GeneratedRegex(@"`+")]
    private static partial Regex TickRun();

    [GeneratedRegex(@"\G *(?:\n *)?")]
    private static partial Regex SpacesNewline();

    [GeneratedRegex(@"\G<(?:[^<>\n\\\x00]|\\.)*>")]
    private static partial Regex DestinationInBraces();

    [GeneratedRegex("\\G(?:\"(?:\\\\[\\s\\S]|[^\\\\\\x00\"])*\"|'(?:\\\\[\\s\\S]|[^\\\\\\x00'])*'|\\((?:\\\\[\\s\\S]|[^\\\\\\x00()])*\\))")]
    private static partial Regex LinkTitle();

    [GeneratedRegex(@"\G\[(?:[^\\\[\]]|\\.){0,1000}\]", RegexOptions.Singleline)]
    private static partial Regex LinkLabel();

    [GeneratedRegex(@"\G<([a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*)>")]
    private static partial Regex EmailAutolink();

    [GeneratedRegex(@"\G<[A-Za-z][A-Za-z0-9.+-]{1,31}:[^<>\x00-\x20]*>")]
    private static partial Regex UriAutolink();

    [GeneratedRegex(@"\G(?:" + OpenTag + "|" + CloseTag + "|" + HtmlComment + "|" + ProcessingInstruction + "|" +
                    Declaration + "|" + Cdata + ")", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\G&(?:#x[a-f0-9]{1,6}|#[0-9]{1,7}|[a-z][a-z0-9]{1,31});", RegexOptions.IgnoreCase)]
    private static partial Regex Entity();

    [GeneratedRegex(@"\G[^\n`\[\]\\!<&*_]+")]
    private static partial Regex PlainText();

    [GeneratedRegex(@"\G[^\n`\[\]\\!<&*_~]+")]
    private static partial Regex PlainTextWithTilde();

    [GeneratedRegex(@"\G[ \t]*(?:\n|$)")]
    private static partial Regex SpaceAtEndOfLine();

    [GeneratedRegex(@"[ \t\r\n]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\\[!-/:-@\[-`{-~]|&(?:#x[a-f0-9]{1,6}|#[0-9]{1,7}|[a-z][a-z0-9]{1,31});", RegexOptions.IgnoreCase)]
    private static partial Regex EscapeOrEntity();
}
