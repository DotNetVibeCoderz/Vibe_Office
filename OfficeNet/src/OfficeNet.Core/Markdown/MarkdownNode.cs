// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;

namespace OfficeNet.Core.Markdown;

internal enum MarkdownNodeType
{
    Document,
    BlockQuote,
    List,
    Item,
    Paragraph,
    Heading,
    ThematicBreak,
    CodeBlock,
    HtmlBlock,
    Table,
    TableRow,
    TableCell,
    Text,
    SoftBreak,
    LineBreak,
    Code,
    HtmlInline,
    Emphasis,
    Strong,
    Strikethrough,
    Link,
    Image,
}

internal sealed class ListData
{
    public bool Ordered { get; init; }

    public char BulletChar { get; init; }

    public int Start { get; init; } = 1;

    public char Delimiter { get; init; }

    public int MarkerOffset { get; init; }

    public int Padding { get; set; }

    public bool Tight { get; set; } = true;
}

/// <summary>
/// One node of the parsed document — a block or an inline, in a single tree as the reference
/// implementation keeps them, so emphasis and links can be rebuilt by moving siblings.
/// </summary>
internal sealed class MarkdownNode(MarkdownNodeType type)
{
    public MarkdownNodeType Type { get; set; } = type;

    public MarkdownNode? Parent { get; private set; }

    public MarkdownNode? FirstChild { get; private set; }

    public MarkdownNode? LastChild { get; private set; }

    public MarkdownNode? Previous { get; private set; }

    public MarkdownNode? Next { get; private set; }

    public bool Open { get; set; } = true;

    public int StartLine { get; set; }

    public bool LastLineBlank { get; set; }

    /// <summary>The raw lines of a block, before inline parsing.</summary>
    public StringBuilder Content { get; } = new();

    /// <summary>The literal text of a text, code or HTML node, or a code block's body.</summary>
    public string Literal { get; set; } = string.Empty;

    public ListData? List { get; set; }

    public int Level { get; set; }

    public bool IsFenced { get; set; }

    public char FenceChar { get; set; }

    public int FenceLength { get; set; }

    public int FenceOffset { get; set; }

    public string Info { get; set; } = string.Empty;

    public int HtmlBlockType { get; set; }

    public string Destination { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>A table cell's alignment: <c>left</c>, <c>center</c>, <c>right</c>, or null.</summary>
    public string? Align { get; set; }

    public bool IsHeader { get; set; }

    public IEnumerable<MarkdownNode> Children
    {
        get
        {
            for (var child = FirstChild; child is not null; child = child.Next)
            {
                yield return child;
            }
        }
    }

    public void AppendChild(MarkdownNode child)
    {
        child.Unlink();
        child.Parent = this;

        if (LastChild is not null)
        {
            LastChild.Next = child;
            child.Previous = LastChild;
            LastChild = child;
        }
        else
        {
            FirstChild = child;
            LastChild = child;
        }
    }

    public void InsertAfter(MarkdownNode sibling)
    {
        sibling.Unlink();
        sibling.Next = Next;

        if (sibling.Next is not null)
        {
            sibling.Next.Previous = sibling;
        }

        sibling.Previous = this;
        Next = sibling;
        sibling.Parent = Parent;

        if (sibling.Next is null && Parent is not null)
        {
            Parent.LastChild = sibling;
        }
    }

    public void Unlink()
    {
        if (Previous is not null)
        {
            Previous.Next = Next;
        }
        else if (Parent is not null)
        {
            Parent.FirstChild = Next;
        }

        if (Next is not null)
        {
            Next.Previous = Previous;
        }
        else if (Parent is not null)
        {
            Parent.LastChild = Previous;
        }

        Parent = null;
        Next = null;
        Previous = null;
    }
}
