// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.RegularExpressions;

namespace OfficeNet.Core.Markdown;

/// <summary>
/// The block phase of CommonMark: lines into a tree of containers and leaves.
/// </summary>
/// <remarks>
/// <para>
/// A port of the reference implementation's algorithm rather than an approximation of the syntax,
/// because the syntax cannot be approximated. Whether a line continues a list item, starts a new
/// one or lazily continues a paragraph three levels down depends on every container still open
/// above it, and the spec's edge cases are exactly the documents people write by hand.
/// </para>
/// <para>
/// Each line is matched against the open containers from the outside in; what is left either
/// starts new blocks or is added to the innermost one. Blocks are finalised when a line fails to
/// continue them — a paragraph's link reference definitions are pulled out at that point, and a
/// GFM table is recognised in its lines.
/// </para>
/// </remarks>
internal sealed partial class BlockParser
{
    private readonly MarkdownOptions _options;
    private readonly Dictionary<string, (string Destination, string Title)> _references = [];

    private MarkdownNode _document = null!;
    private MarkdownNode _tip = null!;
    private MarkdownNode _oldTip = null!;
    private MarkdownNode _lastMatchedContainer = null!;

    private string _line = string.Empty;
    private int _lineNumber;
    private int _offset;
    private int _column;
    private int _nextNonspace;
    private int _nextNonspaceColumn;
    private int _indent;
    private bool _indented;
    private bool _blank;
    private bool _partiallyConsumedTab;
    private bool _allClosed;

    public BlockParser(MarkdownOptions options) => _options = options;

    public Dictionary<string, (string Destination, string Title)> References => _references;

    public MarkdownNode Parse(string markdown)
    {
        _document = new MarkdownNode(MarkdownNodeType.Document) { StartLine = 1 };
        _tip = _document;
        _lineNumber = 0;

        var lines = NewLine().Split(markdown);
        var count = lines.Length;

        // A final newline ends the last line rather than starting an empty one.
        if (count > 0 && lines[^1].Length == 0)
        {
            count--;
        }

        for (var i = 0; i < count; i++)
        {
            IncorporateLine(lines[i].Replace('\0', '�'));
        }

        while (_tip is not null)
        {
            Finalize(_tip);
        }

        new InlineParser(_references, _options).ProcessTree(_document);
        return _document;
    }

    private char At(int index) => index < _line.Length ? _line[index] : '\0';

    private static bool IsSpaceOrTab(char c) => c is ' ' or '\t';

    private void FindNextNonspace()
    {
        var i = _offset;
        var columns = _column;

        while (i < _line.Length)
        {
            var c = _line[i];

            if (c == ' ')
            {
                i++;
                columns++;
            }
            else if (c == '\t')
            {
                i++;
                columns += 4 - (columns % 4);
            }
            else
            {
                break;
            }
        }

        _blank = i >= _line.Length;
        _nextNonspace = i;
        _nextNonspaceColumn = columns;
        _indent = _nextNonspaceColumn - _column;
        _indented = _indent >= 4;
    }

    private void AdvanceNextNonspace()
    {
        _offset = _nextNonspace;
        _column = _nextNonspaceColumn;
        _partiallyConsumedTab = false;
    }

    /// <summary>
    /// Moves along the line by characters or by columns. A tab counts as the columns to the next
    /// tab stop, and can be consumed partly — the rest becomes spaces in the content.
    /// </summary>
    private void AdvanceOffset(int count, bool columns)
    {
        while (count > 0 && _offset < _line.Length)
        {
            if (_line[_offset] == '\t')
            {
                var charsToTab = 4 - (_column % 4);

                if (columns)
                {
                    _partiallyConsumedTab = charsToTab > count;
                    var advance = Math.Min(count, charsToTab);
                    _column += advance;
                    _offset += _partiallyConsumedTab ? 0 : 1;
                    count -= advance;
                }
                else
                {
                    _partiallyConsumedTab = false;
                    _column += charsToTab;
                    _offset++;
                    count--;
                }
            }
            else
            {
                _partiallyConsumedTab = false;
                _offset++;
                _column++;
                count--;
            }
        }
    }

    private void AddLine()
    {
        if (_partiallyConsumedTab)
        {
            _offset++;
            var charsToTab = 4 - (_column % 4);
            _tip.Content.Append(' ', charsToTab);
        }

        _tip.Content.Append(_line, _offset, _line.Length - _offset).Append('\n');
    }

    private MarkdownNode AddChild(MarkdownNodeType type)
    {
        while (!CanContain(_tip.Type, type))
        {
            Finalize(_tip);
        }

        var node = new MarkdownNode(type) { StartLine = _lineNumber };
        _tip.AppendChild(node);
        _tip = node;
        return node;
    }

    private static bool CanContain(MarkdownNodeType parent, MarkdownNodeType child) => parent switch
    {
        MarkdownNodeType.Document or MarkdownNodeType.BlockQuote or MarkdownNodeType.Item =>
            child != MarkdownNodeType.Item,
        MarkdownNodeType.List => child == MarkdownNodeType.Item,
        _ => false,
    };

    private static bool AcceptsLines(MarkdownNodeType type) =>
        type is MarkdownNodeType.Paragraph or MarkdownNodeType.CodeBlock or MarkdownNodeType.HtmlBlock;

    private void CloseUnmatchedBlocks()
    {
        if (_allClosed)
        {
            return;
        }

        while (_oldTip != _lastMatchedContainer)
        {
            var parent = _oldTip.Parent!;
            Finalize(_oldTip);
            _oldTip = parent;
        }

        _allClosed = true;
    }

    private void IncorporateLine(string line)
    {
        var container = _document;
        _oldTip = _tip;
        _offset = 0;
        _column = 0;
        _blank = false;
        _partiallyConsumedTab = false;
        _lineNumber++;
        _line = line;

        // 1. Each open container must be continued by this line, outermost first.
        while (container.LastChild is { Open: true } last)
        {
            container = last;
            FindNextNonspace();

            var result = Continue(container);

            if (result == 1)
            {
                container = container.Parent!;
                break;
            }

            if (result == 2)
            {
                return;
            }
        }

        _allClosed = container == _oldTip;
        _lastMatchedContainer = container;

        var matchedLeaf = container.Type != MarkdownNodeType.Paragraph && AcceptsLines(container.Type);

        // 2. What is left of the line may open new blocks.
        while (!matchedLeaf)
        {
            FindNextNonspace();

            var result = TryStarts(container);

            if (result == 0)
            {
                AdvanceNextNonspace();
                break;
            }

            container = _tip;

            if (result == 2)
            {
                matchedLeaf = true;
            }
        }

        // 3. The rest is text, for the innermost block or a lazily continued paragraph.
        if (!_allClosed && !_blank && _tip.Type == MarkdownNodeType.Paragraph)
        {
            AddLine();
            return;
        }

        CloseUnmatchedBlocks();

        if (_blank && container.LastChild is not null)
        {
            container.LastChild.LastLineBlank = true;
        }

        var type = container.Type;

        // A block quote line is never blank (it starts with >), blank lines inside a fenced block
        // do not loosen a list, and an empty list item has not had a blank line yet.
        var lastLineBlank = _blank &&
                            !(type == MarkdownNodeType.BlockQuote ||
                              (type == MarkdownNodeType.CodeBlock && container.IsFenced) ||
                              (type == MarkdownNodeType.Item && container.FirstChild is null &&
                               container.StartLine == _lineNumber));

        for (var node = container; node is not null; node = node.Parent)
        {
            node.LastLineBlank = lastLineBlank;
        }

        if (AcceptsLines(type))
        {
            AddLine();

            if (type == MarkdownNodeType.HtmlBlock && container.HtmlBlockType is >= 1 and <= 5 &&
                HtmlBlockClose(container.HtmlBlockType).IsMatch(_line[_offset..]))
            {
                Finalize(container);
            }
        }
        else if (_offset < _line.Length && !_blank)
        {
            AddChild(MarkdownNodeType.Paragraph);
            AdvanceNextNonspace();
            AddLine();
        }
    }

    /// <summary>0 matched, 1 not matched, 2 matched and the line is used up.</summary>
    private int Continue(MarkdownNode container)
    {
        switch (container.Type)
        {
            case MarkdownNodeType.BlockQuote:
                if (!_indented && At(_nextNonspace) == '>')
                {
                    AdvanceNextNonspace();
                    AdvanceOffset(1, false);

                    if (IsSpaceOrTab(At(_offset)))
                    {
                        AdvanceOffset(1, true);
                    }

                    return 0;
                }

                return 1;

            case MarkdownNodeType.Item:
                var list = container.List!;

                if (_blank)
                {
                    if (container.FirstChild is null)
                    {
                        return 1;
                    }

                    AdvanceNextNonspace();
                }
                else if (_indent >= list.MarkerOffset + list.Padding)
                {
                    AdvanceOffset(list.MarkerOffset + list.Padding, true);
                }
                else
                {
                    return 1;
                }

                return 0;

            case MarkdownNodeType.CodeBlock when container.IsFenced:
                var rest = _line[_nextNonspace..];

                if (_indent <= 3 && At(_nextNonspace) == container.FenceChar &&
                    ClosingFence().Match(rest) is { Success: true } close &&
                    close.Length >= container.FenceLength)
                {
                    Finalize(container);
                    return 2;
                }

                for (var i = container.FenceOffset; i > 0 && IsSpaceOrTab(At(_offset)); i--)
                {
                    AdvanceOffset(1, true);
                }

                return 0;

            case MarkdownNodeType.CodeBlock:
                if (_indent >= 4)
                {
                    AdvanceOffset(4, true);
                }
                else if (_blank)
                {
                    AdvanceNextNonspace();
                }
                else
                {
                    return 1;
                }

                return 0;

            case MarkdownNodeType.HtmlBlock:
                return _blank && container.HtmlBlockType is 6 or 7 ? 1 : 0;

            case MarkdownNodeType.Paragraph:
                return _blank ? 1 : 0;

            case MarkdownNodeType.Document or MarkdownNodeType.List:
                return 0;

            default:
                return 1;
        }
    }

    /// <summary>0 nothing started, 1 a container started, 2 a leaf started.</summary>
    private int TryStarts(MarkdownNode container)
    {
        var rest = _line[_nextNonspace..];

        // Block quote.
        if (!_indented && At(_nextNonspace) == '>')
        {
            AdvanceNextNonspace();
            AdvanceOffset(1, false);

            if (IsSpaceOrTab(At(_offset)))
            {
                AdvanceOffset(1, true);
            }

            CloseUnmatchedBlocks();
            AddChild(MarkdownNodeType.BlockQuote);
            return 1;
        }

        // ATX heading.
        if (!_indented && AtxHeading().Match(rest) is { Success: true } atx)
        {
            AdvanceNextNonspace();
            AdvanceOffset(atx.Length, false);
            CloseUnmatchedBlocks();

            var heading = AddChild(MarkdownNodeType.Heading);
            heading.Level = atx.Value.Trim().Length;

            var text = _line[_offset..];
            text = AtxClosingOnly().Replace(text, string.Empty);
            text = AtxClosing().Replace(text, string.Empty);
            heading.Content.Append(text);

            AdvanceOffset(_line.Length - _offset, false);
            return 2;
        }

        // Fenced code.
        if (!_indented && CodeFence().Match(rest) is { Success: true } fence)
        {
            CloseUnmatchedBlocks();

            var code = AddChild(MarkdownNodeType.CodeBlock);
            code.IsFenced = true;
            code.FenceLength = fence.Length;
            code.FenceChar = fence.Value[0];
            code.FenceOffset = _indent;

            AdvanceNextNonspace();
            AdvanceOffset(fence.Length, false);
            return 2;
        }

        // HTML block.
        if (!_indented && At(_nextNonspace) == '<' && _options.AllowHtml)
        {
            for (var type = 1; type <= 7; type++)
            {
                if (HtmlBlockOpen(type).IsMatch(rest) &&
                    (type < 7 || (container.Type != MarkdownNodeType.Paragraph &&
                                  !(!_allClosed && !_blank && _tip.Type == MarkdownNodeType.Paragraph))))
                {
                    CloseUnmatchedBlocks();

                    var html = AddChild(MarkdownNodeType.HtmlBlock);
                    html.HtmlBlockType = type;
                    return 2;
                }
            }
        }

        // Setext heading: the underline turns the paragraph above it into a heading.
        if (!_indented && container.Type == MarkdownNodeType.Paragraph &&
            SetextUnderline().Match(rest) is { Success: true } underline)
        {
            CloseUnmatchedBlocks();

            var content = container.Content.ToString();
            var consumed = InlineParser.ExtractReferences(content, _references);
            content = content[consumed..];

            if (content.Length > 0)
            {
                var heading = new MarkdownNode(MarkdownNodeType.Heading)
                {
                    StartLine = container.StartLine,
                    Level = underline.Value[0] == '=' ? 1 : 2,
                };

                heading.Content.Append(content);
                container.InsertAfter(heading);
                container.Unlink();
                _tip = heading;

                AdvanceOffset(_line.Length - _offset, false);
                return 2;
            }

            container.Content.Clear().Append(content);
        }

        // Thematic break.
        if (!_indented && ThematicBreak().IsMatch(rest))
        {
            CloseUnmatchedBlocks();
            AddChild(MarkdownNodeType.ThematicBreak);
            AdvanceOffset(_line.Length - _offset, false);
            return 2;
        }

        // List item.
        if ((!_indented || container.Type == MarkdownNodeType.List) &&
            ParseListMarker(container) is { } data)
        {
            CloseUnmatchedBlocks();

            if (_tip.Type != MarkdownNodeType.List || !ListsMatch(_tip.List!, data))
            {
                var list = AddChild(MarkdownNodeType.List);
                list.List = data;
            }

            var item = AddChild(MarkdownNodeType.Item);
            item.List = data;
            return 1;
        }

        // Indented code.
        if (_indented && _tip.Type != MarkdownNodeType.Paragraph && !_blank)
        {
            AdvanceOffset(4, true);
            CloseUnmatchedBlocks();
            AddChild(MarkdownNodeType.CodeBlock);
            return 2;
        }

        return 0;
    }

    private static bool ListsMatch(ListData a, ListData b) =>
        a.Ordered == b.Ordered && a.Delimiter == b.Delimiter && a.BulletChar == b.BulletChar;

    private ListData? ParseListMarker(MarkdownNode container)
    {
        if (_indent >= 4)
        {
            return null;
        }

        var rest = _line[_nextNonspace..];
        int length;
        ListData data;

        if (rest.Length > 0 && rest[0] is '*' or '+' or '-')
        {
            length = 1;
            data = new ListData { BulletChar = rest[0], MarkerOffset = _indent };
        }
        else if (OrderedMarker().Match(rest) is { Success: true } ordered &&
                 (container.Type != MarkdownNodeType.Paragraph || ordered.Groups[1].Value == "1"))
        {
            length = ordered.Length;
            data = new ListData
            {
                Ordered = true,
                Start = int.Parse(ordered.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Delimiter = ordered.Groups[2].Value[0],
                MarkerOffset = _indent,
            };
        }
        else
        {
            return null;
        }

        var after = At(_nextNonspace + length);

        if (!(after == '\0' || after is ' ' or '\t'))
        {
            return null;
        }

        // An empty item cannot interrupt a paragraph.
        if (container.Type == MarkdownNodeType.Paragraph &&
            _line[(_nextNonspace + length)..].Trim(' ', '\t').Length == 0)
        {
            return null;
        }

        AdvanceNextNonspace();
        AdvanceOffset(length, true);

        var spacesStartColumn = _column;
        var spacesStartOffset = _offset;

        do
        {
            AdvanceOffset(1, true);
        }
        while (_column - spacesStartColumn < 5 && IsSpaceOrTab(At(_offset)));

        var blankItem = _offset >= _line.Length;
        var spacesAfterMarker = _column - spacesStartColumn;

        // Five or more spaces after the marker means indented code inside the item, which starts
        // one space after the marker.
        if (spacesAfterMarker >= 5 || spacesAfterMarker < 1 || blankItem)
        {
            data.Padding = length + 1;
            _column = spacesStartColumn;
            _offset = spacesStartOffset;

            if (IsSpaceOrTab(At(_offset)))
            {
                AdvanceOffset(1, true);
            }
        }
        else
        {
            data.Padding = length + spacesAfterMarker;
        }

        return data;
    }

    private void Finalize(MarkdownNode block)
    {
        var above = block.Parent;
        block.Open = false;

        switch (block.Type)
        {
            case MarkdownNodeType.Paragraph:
                FinalizeParagraph(block);
                break;

            case MarkdownNodeType.CodeBlock when block.IsFenced:
                var content = block.Content.ToString();
                var newline = content.IndexOf('\n', StringComparison.Ordinal);
                var firstLine = newline < 0 ? content : content[..newline];

                block.Info = InlineParser.Unescape(firstLine.Trim());
                block.Literal = newline < 0 ? string.Empty : content[(newline + 1)..];
                break;

            case MarkdownNodeType.CodeBlock:
                block.Literal = TrailingBlankLines().Replace(block.Content.ToString(), "\n");
                break;

            case MarkdownNodeType.HtmlBlock:
                block.Literal = TrailingNewline().Replace(block.Content.ToString(), string.Empty);
                break;

            case MarkdownNodeType.List:
                block.List!.Tight = IsTight(block);
                break;
        }

        _tip = above!;
    }

    private void FinalizeParagraph(MarkdownNode block)
    {
        var content = block.Content.ToString();
        var consumed = InlineParser.ExtractReferences(content, _references);

        if (consumed > 0)
        {
            content = content[consumed..];
            block.Content.Clear().Append(content);
        }

        // Nothing left once the definitions are out — or a setext underline already took them.
        if (content.Trim().Length == 0)
        {
            block.Unlink();
            return;
        }

        if (_options.Tables)
        {
            TableParser.TryConvert(block);
        }
    }

    /// <summary>
    /// A list is loose if a blank line separates two of its items, or two blocks inside an item.
    /// </summary>
    private static bool IsTight(MarkdownNode list)
    {
        for (var item = list.FirstChild; item is not null; item = item.Next)
        {
            if (EndsWithBlankLine(item) && item.Next is not null)
            {
                return false;
            }

            for (var sub = item.FirstChild; sub is not null; sub = sub.Next)
            {
                if (EndsWithBlankLine(sub) && (item.Next is not null || sub.Next is not null))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool EndsWithBlankLine(MarkdownNode? block)
    {
        while (block is not null)
        {
            if (block.LastLineBlank)
            {
                return true;
            }

            if (block.Type is MarkdownNodeType.List or MarkdownNodeType.Item)
            {
                block = block.LastChild;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    private static Regex HtmlBlockOpen(int type) => type switch
    {
        1 => HtmlOpen1(),
        2 => HtmlOpen2(),
        3 => HtmlOpen3(),
        4 => HtmlOpen4(),
        5 => HtmlOpen5(),
        6 => HtmlOpen6(),
        _ => HtmlOpen7(),
    };

    private static Regex HtmlBlockClose(int type) => type switch
    {
        1 => HtmlClose1(),
        2 => HtmlClose2(),
        3 => HtmlClose3(),
        4 => HtmlClose4(),
        _ => HtmlClose5(),
    };

    [GeneratedRegex(@"\r\n|\n|\r")]
    private static partial Regex NewLine();

    [GeneratedRegex(@"^#{1,6}(?:[ \t]+|$)")]
    private static partial Regex AtxHeading();

    [GeneratedRegex(@"^[ \t]*#+[ \t]*$")]
    private static partial Regex AtxClosingOnly();

    [GeneratedRegex(@"[ \t]+#+[ \t]*$")]
    private static partial Regex AtxClosing();

    [GeneratedRegex(@"^`{3,}(?!.*`)|^~{3,}")]
    private static partial Regex CodeFence();

    [GeneratedRegex(@"^(?:`{3,}|~{3,})(?=[ \t]*$)")]
    private static partial Regex ClosingFence();

    [GeneratedRegex(@"^(?:=+|-+)[ \t]*$")]
    private static partial Regex SetextUnderline();

    [GeneratedRegex(@"^(?:\*[ \t]*){3,}$|^(?:_[ \t]*){3,}$|^(?:-[ \t]*){3,}$")]
    private static partial Regex ThematicBreak();

    [GeneratedRegex(@"^(\d{1,9})([.)])")]
    private static partial Regex OrderedMarker();

    [GeneratedRegex(@"(\n *)+$")]
    private static partial Regex TrailingBlankLines();

    [GeneratedRegex(@"\n$")]
    private static partial Regex TrailingNewline();

    [GeneratedRegex(@"^<(?:script|pre|textarea|style)(?:\s|>|$)", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlOpen1();

    [GeneratedRegex(@"^<!--")]
    private static partial Regex HtmlOpen2();

    [GeneratedRegex(@"^<[?]")]
    private static partial Regex HtmlOpen3();

    [GeneratedRegex(@"^<![A-Za-z]")]
    private static partial Regex HtmlOpen4();

    [GeneratedRegex(@"^<!\[CDATA\[")]
    private static partial Regex HtmlOpen5();

    [GeneratedRegex(@"^<[/]?(?:address|article|aside|base|basefont|blockquote|body|caption|center|col|colgroup|dd|details|dialog|dir|div|dl|dt|fieldset|figcaption|figure|footer|form|frame|frameset|h[123456]|head|header|hr|html|iframe|legend|li|link|main|menu|menuitem|nav|noframes|ol|optgroup|option|p|param|search|section|summary|table|tbody|td|tfoot|th|thead|title|tr|track|ul)(?:\s|[/]?[>]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlOpen6();

    [GeneratedRegex(@"^(?:" + InlineParser.OpenTag + "|" + InlineParser.CloseTag + @")\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlOpen7();

    [GeneratedRegex(@"</(?:script|pre|textarea|style)>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlClose1();

    [GeneratedRegex(@"-->")]
    private static partial Regex HtmlClose2();

    [GeneratedRegex(@"\?>")]
    private static partial Regex HtmlClose3();

    [GeneratedRegex(@">")]
    private static partial Regex HtmlClose4();

    [GeneratedRegex(@"\]\]>")]
    private static partial Regex HtmlClose5();
}
