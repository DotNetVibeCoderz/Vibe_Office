// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Xml.Linq;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace WordNet.Revisions;

/// <summary>
/// The tracked changes in a document: the body, headers, footers, footnotes, endnotes and comments.
/// </summary>
/// <remarks>
/// <para>
/// The collection is a snapshot taken when it is read from <see cref="WordDocument.Revisions"/>.
/// Accepting or rejecting edits the document; read the property again for what is left.
/// </para>
/// <para>
/// One element name means three different things depending on where it sits. <c>w:ins</c> around
/// runs is inserted text; inside a paragraph mark's <c>w:rPr</c> it says the paragraph break itself
/// was inserted; inside <c>w:trPr</c> it says a table row was. Telling them apart by the parent is
/// the whole of the classification, and getting it wrong accepts the wrong thing.
/// </para>
/// </remarks>
public sealed class RevisionCollection : IReadOnlyList<Revision>
{
    private static readonly XName Ins = Ns.W + "ins";
    private static readonly XName Del = Ns.W + "del";
    private static readonly XName MoveFrom = Ns.W + "moveFrom";
    private static readonly XName MoveTo = Ns.W + "moveTo";

    /// <summary>Property-change elements: the old properties, kept beside the new ones.</summary>
    private static readonly HashSet<XName> PropertyChanges =
    [
        Ns.W + "rPrChange", Ns.W + "pPrChange", Ns.W + "tblPrChange", Ns.W + "trPrChange",
        Ns.W + "tcPrChange", Ns.W + "sectPrChange", Ns.W + "tblGridChange", Ns.W + "tblPrExChange",
    ];

    private static readonly XName[] MoveRangeMarkers =
    [
        Ns.W + "moveFromRangeStart", Ns.W + "moveFromRangeEnd",
        Ns.W + "moveToRangeStart", Ns.W + "moveToRangeEnd",
    ];

    private readonly WordDocument _document;
    private readonly List<Revision> _revisions;
    private readonly List<XElement> _roots;

    internal RevisionCollection(WordDocument document)
    {
        _document = document;
        _roots = Roots(document).ToList();
        _revisions = [.. _roots.SelectMany(root => Find(document, root))];
    }

    /// <inheritdoc />
    public int Count => _revisions.Count;

    /// <inheritdoc />
    public Revision this[int index] => _revisions[index];

    /// <summary>The revisions one author made.</summary>
    public IEnumerable<Revision> ByAuthor(string author) =>
        _revisions.Where(r => string.Equals(r.Author, author, StringComparison.Ordinal));

    /// <summary>Accepts every revision.</summary>
    /// <returns>How many were accepted.</returns>
    public int AcceptAll() => ResolveAll(accept: true);

    /// <summary>Rejects every revision, restoring the document as it was before tracking.</summary>
    /// <returns>How many were rejected.</returns>
    public int RejectAll() => ResolveAll(accept: false);

    /// <summary>
    /// Resolves every revision in the snapshot.
    /// </summary>
    /// <remarks>
    /// Joining paragraphs moves content into the next paragraph, and the revisions still waiting
    /// in this list sit inside that content. They stay valid only because the join detaches each
    /// element before re-adding it, which LINQ to XML treats as a move; adding an element that still
    /// has a parent silently clones it, and every pending revision would then point at a node no
    /// longer in the document. Reverse order is kept so a join's target is always unvisited, but
    /// it is the move, not the order, that makes this correct.
    /// </remarks>
    private int ResolveAll(bool accept)
    {
        var count = 0;

        for (var i = _revisions.Count - 1; i >= 0; i--)
        {
            var revision = _revisions[i];

            if (revision.IsResolved)
            {
                continue;
            }

            if (accept)
            {
                revision.Accept();
            }
            else
            {
                revision.Reject();
            }

            count++;
        }

        foreach (var root in _roots)
        {
            RemoveMoveRanges(root);
        }

        _document.Touch();
        return count;
    }

    /// <summary>
    /// Removes the range markers that bracket a move once no move content is left to bracket.
    /// </summary>
    internal static void RemoveMoveRanges(XElement root)
    {
        if (root.Descendants(MoveFrom).Any(e => !IsMarker(e)) || root.Descendants(MoveTo).Any(e => !IsMarker(e)))
        {
            return;
        }

        foreach (var name in MoveRangeMarkers)
        {
            root.Descendants(name).Remove();
        }
    }

    /// <inheritdoc />
    public IEnumerator<Revision> GetEnumerator() => _revisions.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ---- Discovery -----------------------------------------------------------------------------

    internal static IEnumerable<XElement> Roots(WordDocument document)
    {
        yield return document.Root;

        foreach (var type in (string[])
                 [
                     RelationshipTypes.Header, RelationshipTypes.Footer, RelationshipTypes.Footnotes,
                     RelationshipTypes.Endnotes, RelationshipTypes.Comments,
                 ])
        {
            foreach (var part in document.DocumentPart.RelatedPartsByType(type))
            {
                if (part.Xml.Root is { } root)
                {
                    yield return root;
                }
            }
        }
    }

    private static IEnumerable<Revision> Find(WordDocument document, XElement root)
    {
        foreach (var element in root.Descendants())
        {
            if (Classify(element) is { } kind)
            {
                yield return new Revision(document, kind, element);
            }
        }
    }

    /// <summary>What a revision element means, from its name and where it sits.</summary>
    internal static RevisionKind? Classify(XElement element)
    {
        var name = element.Name;

        if (PropertyChanges.Contains(name))
        {
            return name == Ns.W + "rPrChange" ? RevisionKind.RunFormat
                : name == Ns.W + "pPrChange" ? RevisionKind.ParagraphFormat
                : RevisionKind.PropertyFormat;
        }

        if (name != Ins && name != Del && name != MoveFrom && name != MoveTo)
        {
            return null;
        }

        var parent = element.Parent?.Name;

        if (parent == Ns.W + "rPr")
        {
            // Inside a run's own properties ins/del are not allowed; inside a paragraph mark's
            // properties they mark the mark. A moved paragraph mark reads as inserted or deleted.
            return element.Parent?.Parent?.Name == Ns.W + "pPr"
                ? (name == Ins || name == MoveTo ? RevisionKind.ParagraphInserted : RevisionKind.ParagraphDeleted)
                : null;
        }

        if (parent == Ns.W + "trPr")
        {
            return name == Ins ? RevisionKind.RowInserted
                : name == Del ? RevisionKind.RowDeleted
                : null;
        }

        return name == Ins ? RevisionKind.Insertion
            : name == Del ? RevisionKind.Deletion
            : name == MoveFrom ? RevisionKind.MoveFrom
            : RevisionKind.MoveTo;
    }

    private static bool IsMarker(XElement element) => element.Parent?.Name == Ns.W + "rPr";

    // ---- Ids -----------------------------------------------------------------------------------

    /// <summary>One more than the largest annotation id anywhere in the document.</summary>
    /// <remarks>
    /// Revision ids share a space with bookmarks and comment ranges in practice — Word renumbers
    /// on save, but a duplicate on open is reported as damage — so the maximum is taken over every
    /// <c>w:id</c>, not only over revisions.
    /// </remarks>
    internal static int NextId(WordDocument document)
    {
        var max = -1;

        foreach (var root in Roots(document))
        {
            foreach (var element in root.DescendantsAndSelf())
            {
                if (element.Attribute(Ns.W + "id") is { } attribute &&
                    int.TryParse(attribute.Value, out var value) && value > max)
                {
                    max = value;
                }
            }
        }

        return max + 1;
    }
}
