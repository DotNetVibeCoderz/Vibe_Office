// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace WordNet.Merging;

/// <summary>
/// Cuts a document into several, each a complete document of its own.
/// </summary>
/// <remarks>
/// <para>
/// Each part starts as a full copy of the source and has everything outside its range removed.
/// That is the opposite of how <see cref="DocumentMerger"/> works, on purpose: a part that keeps
/// the source's styles, lists, headers and relationships intact cannot get a reference wrong, and
/// what it does not use is pruned afterwards — images, headers, footnotes and comments nothing
/// points at any more.
/// </para>
/// <para>
/// Splitting by <em>page</em> is not offered. Where a page ends is decided by a layout engine
/// equivalent to Word's, which this library deliberately does not have; an explicit page break is
/// the only page boundary written in the file.
/// </para>
/// </remarks>
internal static class DocumentSplitter
{
    /// <summary>Splits before every block for which <paramref name="startsPart"/> is true.</summary>
    internal static IReadOnlyList<WordDocument> Split(WordDocument source,
        Func<XElement, int, bool> startsPart, bool cutAtPageBreaks = false)
    {
        var bytes = source.ToArray();

        // Count blocks on a scratch copy, split explicit page breaks there too, and decide the
        // boundaries once; every part is then cut from a fresh copy to the same plan.
        using var plan = WordDocument.Open(bytes);

        if (cutAtPageBreaks)
        {
            SplitParagraphsAtPageBreaks(plan.Body);
            bytes = plan.ToArray();
        }

        var blocks = Blocks(plan.Body);
        var starts = new List<int> { 0 };

        for (var i = 1; i < blocks.Count; i++)
        {
            if (startsPart(blocks[i], i))
            {
                starts.Add(i);
            }
        }

        var parts = new List<WordDocument>(starts.Count);

        for (var p = 0; p < starts.Count; p++)
        {
            var first = starts[p];
            var last = p + 1 < starts.Count ? starts[p + 1] - 1 : blocks.Count - 1;

            if (last < first)
            {
                continue;
            }

            var part = WordDocument.Open(bytes);
            Cut(part, first, last, cutAtPageBreaks);
            Prune(part);
            parts.Add(part);
        }

        return parts;
    }

    internal static List<XElement> Blocks(XElement body) =>
        [.. body.Elements().Where(e => e.Name != Ns.W + "sectPr")];

    /// <summary>The section properties a block ends with, if it ends a section.</summary>
    internal static XElement? EndsSection(XElement block) =>
        block.Name == Ns.W + "p" ? block.Element(Ns.W + "pPr")?.Element(Ns.W + "sectPr") : null;

    /// <summary>Whether a block holds an explicit page break (<c>w:br w:type="page"</c>).</summary>
    internal static bool EndsWithPageBreak(XElement block) =>
        block.Name == Ns.W + "p" &&
        block.Elements(Ns.W + "r").LastOrDefault()?.Elements().LastOrDefault() is { } last &&
        last.Name == Ns.W + "br" && last.Attr(Ns.W + "type") == "page";

    /// <summary>
    /// Keeps blocks <paramref name="first"/> to <paramref name="last"/> and gives the part the
    /// section properties of the section its last block belongs to.
    /// </summary>
    private static void Cut(WordDocument part, int first, int last, bool trimPageBreak)
    {
        var body = part.Body;
        var blocks = Blocks(body);
        var final = body.Element(Ns.W + "sectPr");

        // The section the part's last block is in ends at the next block carrying a sectPr at or
        // after it — or at the body's own final one.
        var ending = blocks.Skip(last).Select(EndsSection).FirstOrDefault(s => s is not null) ?? final;

        if (ending is not null)
        {
            var copy = new XElement(ending);

            // A part's first section starts the document; "next page" or "continuous" there
            // describes a break that no longer exists.
            copy.Elements(Ns.W + "type").Remove();

            // A section that inherits its header from an earlier one ("Link to Previous") has no
            // reference of its own. Cut loose from those earlier sections it would have no header
            // at all, so the inherited references are written into it.
            Inherit(body, ending, copy);

            if (final is not null)
            {
                final.ReplaceWith(copy);
            }
            else
            {
                body.Add(copy);
            }

            // The block that carried it no longer ends a section; the body does.
            if (ending != final && EndsSection(blocks[last]) is { } carried)
            {
                carried.Remove();
            }
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            if (i < first || i > last)
            {
                blocks[i].Remove();
            }
        }

        if (trimPageBreak && EndsWithPageBreak(blocks[last]))
        {
            blocks[last].Elements(Ns.W + "r").Last().Elements().Last().Remove();
        }

        part.SeedIdCounters();
        part.Touch();
    }

    /// <summary>
    /// Copies into <paramref name="target"/> each header and footer reference that
    /// <paramref name="section"/> inherits rather than holds.
    /// </summary>
    private static void Inherit(XElement body, XElement section, XElement target)
    {
        var sections = body.Descendants(Ns.W + "sectPr").ToList();
        var index = sections.IndexOf(section);

        foreach (var name in (XName[])[Ns.W + "headerReference", Ns.W + "footerReference"])
        {
            foreach (var kind in (string[])["default", "first", "even"])
            {
                if (target.Elements(name).Any(e => (e.Attr(Ns.W + "type") ?? "default") == kind))
                {
                    continue;
                }

                var inherited = sections.Take(index).Reverse()
                    .SelectMany(s => s.Elements(name))
                    .FirstOrDefault(e => (e.Attr(Ns.W + "type") ?? "default") == kind);

                if (inherited is not null)
                {
                    // References come first in w:sectPr, ahead of every property.
                    target.AddFirst(new XElement(inherited));
                }
            }
        }
    }

    /// <summary>
    /// Splits any paragraph with an explicit page break in its middle into two, so the break ends
    /// a paragraph and a boundary can fall between them.
    /// </summary>
    /// <remarks>
    /// Only breaks in the paragraph's own runs are handled; one nested in a hyperlink or a tracked
    /// change stays where it is and is not treated as a boundary.
    /// </remarks>
    private static void SplitParagraphsAtPageBreaks(XElement body)
    {
        foreach (var paragraph in body.Elements(Ns.W + "p").ToList())
        {
            var current = paragraph;

            while (true)
            {
                var run = current.Elements(Ns.W + "r").FirstOrDefault(r =>
                    r.Elements(Ns.W + "br").Any(b => b.Attr(Ns.W + "type") == "page") &&
                    !IsAtEnd(current, r));

                if (run is null)
                {
                    break;
                }

                var breakElement = run.Elements(Ns.W + "br").First(b => b.Attr(Ns.W + "type") == "page");

                // Everything after the break, in this run and in the paragraph, moves to a new
                // paragraph with the same properties.
                var next = new XElement(Ns.W + "p");

                if (current.Element(Ns.W + "pPr") is { } properties)
                {
                    var copy = new XElement(properties);
                    copy.Elements(Ns.W + "sectPr").Remove();
                    next.Add(copy);
                    properties.Elements(Ns.W + "sectPr").ToList().ForEach(s => { s.Remove(); copy.Add(s); });
                }

                var tail = breakElement.ElementsAfterSelf().ToList();

                if (tail.Count > 0)
                {
                    var tailRun = new XElement(Ns.W + "r");

                    if (run.Element(Ns.W + "rPr") is { } runProperties)
                    {
                        tailRun.Add(new XElement(runProperties));
                    }

                    foreach (var element in tail)
                    {
                        element.Remove();
                    }

                    tailRun.Add(tail);
                    next.Add(tailRun);
                }

                foreach (var element in run.ElementsAfterSelf().ToList())
                {
                    element.Remove();
                    next.Add(element);
                }

                current.AddAfterSelf(next);
                current = next;
            }
        }

        static bool IsAtEnd(XElement paragraph, XElement run) =>
            run == paragraph.Elements(Ns.W + "r").Last() &&
            run.Elements().Last() is { } last && last.Name == Ns.W + "br" &&
            last.Attr(Ns.W + "type") == "page" &&
            !run.ElementsAfterSelf().Any();
    }

    // ---- Pruning -------------------------------------------------------------------------------

    /// <summary>
    /// Removes what the remaining content no longer refers to: notes, comments, and the
    /// relationships (and then parts) of images, headers and links nothing uses.
    /// </summary>
    private static void Prune(WordDocument part)
    {
        var root = part.Root;

        PruneNotes(part, root, RelationshipTypes.Footnotes, Ns.W + "footnote", Ns.W + "footnoteReference");
        PruneNotes(part, root, RelationshipTypes.Endnotes, Ns.W + "endnote", Ns.W + "endnoteReference");
        PruneNotes(part, root, RelationshipTypes.Comments, Ns.W + "comment", Ns.W + "commentReference");

        // A range cut by a part boundary leaves half of itself behind: a comment range whose
        // comment went with the reference, a bookmark whose end is in another part.
        var comments = root.Descendants(Ns.W + "commentReference")
            .Select(e => e.Attr(Ns.W + "id")).OfType<string>().ToHashSet(StringComparer.Ordinal);

        root.Descendants()
            .Where(e => (e.Name == Ns.W + "commentRangeStart" || e.Name == Ns.W + "commentRangeEnd") &&
                        !comments.Contains(e.Attr(Ns.W + "id") ?? ""))
            .Remove();

        foreach (var (start, end) in ((XName, XName)[])
                 [
                     (Ns.W + "bookmarkStart", Ns.W + "bookmarkEnd"),
                     (Ns.W + "permStart", Ns.W + "permEnd"),
                 ])
        {
            var starts = root.Descendants(start).Select(e => e.Attr(Ns.W + "id")).OfType<string>().ToHashSet();
            var ends = root.Descendants(end).Select(e => e.Attr(Ns.W + "id")).OfType<string>().ToHashSet();

            root.Descendants(start).Where(e => !ends.Contains(e.Attr(Ns.W + "id") ?? "")).Remove();
            root.Descendants(end).Where(e => !starts.Contains(e.Attr(Ns.W + "id") ?? "")).Remove();
        }

        PruneRelationships(part.DocumentPart, root);

        // A part no relationship targets any more is dead weight — a header for a section that
        // is gone, an image only the removed pages showed.
        var targeted = new HashSet<OpcPartName>(part.Package.Parts
            .SelectMany(p => p.Relationships)
            .Concat(part.Package.RootRelationships())
            .Where(r => r.TargetMode == TargetMode.Internal)
            .Select(r => r.TargetPartName));

        foreach (var orphan in part.Package.Parts
                     .Where(p => !targeted.Contains(p.Name) &&
                                 (p.ContentType.StartsWith("image/", StringComparison.Ordinal) ||
                                  p.ContentType is ContentTypes.WordHeader or ContentTypes.WordFooter))
                     .ToList())
        {
            part.Package.RemovePart(orphan.Name);
        }
    }

    private static void PruneNotes(WordDocument part, XElement root, string relationshipType,
        XName noteName, XName referenceName)
    {
        var notesPart = part.DocumentPart.RelatedPartByType(relationshipType);

        if (notesPart?.Xml.Root is not { } notesRoot)
        {
            return;
        }

        var used = root.Descendants(referenceName)
            .Select(e => e.Attr(Ns.W + "id"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var note in notesRoot.Elements(noteName).ToList())
        {
            // Separators carry a w:type and are never referenced; they must stay.
            if (note.Attr(Ns.W + "type") is not null || used.Contains(note.Attr(Ns.W + "id") ?? ""))
            {
                continue;
            }

            note.Remove();
        }

        PruneRelationships(notesPart, notesRoot);
    }

    /// <summary>Drops a part's image, hyperlink, header and footer relationships nothing uses.</summary>
    private static void PruneRelationships(OpcPart owner, XElement root)
    {
        var used = root.DescendantsAndSelf().Attributes()
            .Where(a => a.Name.Namespace == Ns.R)
            .Select(a => a.Value)
            .ToHashSet(StringComparer.Ordinal);

        string[] prunable =
        [
            RelationshipTypes.Image, RelationshipTypes.Hyperlink, RelationshipTypes.Header,
            RelationshipTypes.Footer,
        ];

        foreach (var relationship in owner.Relationships
                     .Where(r => prunable.Contains(r.Type) && !used.Contains(r.Id))
                     .ToList())
        {
            owner.RemoveRelationship(relationship.Id);
        }
    }
}
