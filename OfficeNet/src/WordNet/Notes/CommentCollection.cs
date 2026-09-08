// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Xml;

namespace WordNet.Notes;

/// <summary>
/// One review comment.
/// </summary>
/// <remarks>
/// A comment is anchored to a <em>span</em> of the document, not to a point: the body carries a
/// <c>commentRangeStart</c> and a <c>commentRangeEnd</c> around the text being commented on, plus a
/// reference run that draws the marker. All three share the comment's id, and Word reports the file
/// as unreadable if they disagree.
/// </remarks>
public sealed class Comment
{
    private readonly WordDocument _document;

    internal Comment(WordDocument document, XElement element)
    {
        _document = document;
        Element = element;
    }

    /// <summary>The underlying <c>w:comment</c> element.</summary>
    public XElement Element { get; }

    /// <summary>The comment's id, shared by its range markers in the body.</summary>
    public int Id => Element.IntAttr(Ns.W + "id");

    /// <summary>Who wrote it.</summary>
    public string Author
    {
        get => Element.Attr(Ns.W + "author") ?? string.Empty;
        set
        {
            Element.SetAttributeValue(Ns.W + "author", value);
            _document.Touch();
        }
    }

    /// <summary>The author's initials, shown in the margin.</summary>
    public string Initials
    {
        get => Element.Attr(Ns.W + "initials") ?? string.Empty;
        set
        {
            Element.SetAttributeValue(Ns.W + "initials", value);
            _document.Touch();
        }
    }

    /// <summary>When it was written.</summary>
    public DateTime? Date =>
        DateTime.TryParse(Element.Attr(Ns.W + "date"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;

    /// <summary>The comment's paragraphs.</summary>
    public IReadOnlyList<Paragraph> Paragraphs =>
        [.. Element.Elements(Ns.W + "p").Select(e => new Paragraph(_document, e))];

    /// <summary>The comment's text, paragraphs joined by newlines.</summary>
    public string Text => string.Join('\n', Paragraphs.Select(p => p.Text));

    /// <summary>Appends a paragraph to the comment.</summary>
    public Paragraph AddParagraph(string text = "")
    {
        var element = new XElement(Ns.W + "p");
        Element.Add(element);

        var paragraph = new Paragraph(_document, element) { StyleId = "CommentText" };

        // The marker that makes the comment findable in the margin. Like a footnote's mark, Word
        // draws the number itself.
        element.Add(new XElement(Ns.W + "r",
            new XElement(Ns.W + "rPr",
                XmlUtil.ValElement(Ns.W + "rStyle", "CommentReference")),
            new XElement(Ns.W + "annotationRef")));

        if (text.Length > 0)
        {
            paragraph.AddRun(" " + text);
        }

        _document.Touch();
        return paragraph;
    }

    public override string ToString() => $"Comment {Id} by {Author}: {Text}";
}

/// <summary>
/// The review comments on a document.
/// </summary>
/// <remarks>
/// The part is created on first use, so a document with no comments does not carry an empty
/// <c>comments.xml</c>.
/// </remarks>
public sealed class CommentCollection
{
    private readonly WordDocument _document;
    private readonly XElement _root;

    internal CommentCollection(WordDocument document, XElement root)
    {
        _document = document;
        _root = root;
    }

    /// <summary>Every comment, in document order.</summary>
    public IReadOnlyList<Comment> All =>
        [.. _root.Elements(Ns.W + "comment").Select(e => new Comment(_document, e))];

    /// <summary>The number of comments.</summary>
    public int Count => _root.Elements(Ns.W + "comment").Count();

    /// <summary>A comment by id.</summary>
    public Comment? this[int id] =>
        _root.Elements(Ns.W + "comment").FirstOrDefault(e => e.IntAttr(Ns.W + "id") == id) is { } element
            ? new Comment(_document, element)
            : null;

    /// <summary>Comments by a given author.</summary>
    public IEnumerable<Comment> ByAuthor(string author) =>
        All.Where(c => string.Equals(c.Author, author, StringComparison.OrdinalIgnoreCase));

    internal Comment Add(string text, string author, string? initials)
    {
        var next = _root.Elements(Ns.W + "comment")
            .Select(e => e.IntAttr(Ns.W + "id"))
            .DefaultIfEmpty(-1)
            .Max() + 1;

        var element = new XElement(Ns.W + "comment",
            new XAttribute(Ns.W + "id", next.ToString(CultureInfo.InvariantCulture)),
            new XAttribute(Ns.W + "author", author),
            // Round-trip format: Word rejects a date it cannot parse and repairs the part.
            new XAttribute(Ns.W + "date", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
            new XAttribute(Ns.W + "initials", initials ?? Abbreviate(author)));

        _root.Add(element);

        var comment = new Comment(_document, element);
        comment.AddParagraph(text);

        _document.Touch();
        return comment;
    }

    /// <summary>Removes a comment and its range markers from the body.</summary>
    /// <remarks>
    /// A range marker left pointing at a deleted comment is exactly the kind of dangling reference
    /// Word calls unreadable content, so all four pieces go together.
    /// </remarks>
    public bool Remove(int id)
    {
        var element = _root.Elements(Ns.W + "comment").FirstOrDefault(e => e.IntAttr(Ns.W + "id") == id);

        if (element is null)
        {
            return false;
        }

        element.Remove();

        var markers = new[]
        {
            Ns.W + "commentRangeStart",
            Ns.W + "commentRangeEnd",
            Ns.W + "commentReference",
        };

        foreach (var marker in _document.Root.Descendants()
                     .Where(e => markers.Contains(e.Name) && e.IntAttr(Ns.W + "id") == id)
                     .ToList())
        {
            // commentReference lives inside a run; the range markers are siblings of runs.
            (marker.Name == Ns.W + "commentReference" && marker.Parent?.Name == Ns.W + "r"
                ? marker.Parent
                : marker).Remove();
        }

        _document.Touch();
        return true;
    }

    /// <summary>Initials from a name: "Kang Fadhil" becomes "KF".</summary>
    private static string Abbreviate(string author)
    {
        var parts = author.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 0
            ? "?"
            : string.Concat(parts.Take(3).Select(p => char.ToUpperInvariant(p[0])));
    }

    /// <summary>Builds an empty comments part.</summary>
    internal static XDocument CreatePart() =>
        XmlUtil.NewDocument(new XElement(Ns.W + "comments",
            new XAttribute(XNamespace.Xmlns + "w", Ns.W.NamespaceName)));
}
