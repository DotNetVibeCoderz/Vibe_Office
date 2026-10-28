// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using WordNet.Revisions;

namespace WordNet.Merging;

/// <summary>How the appended document's sections meet the target's.</summary>
public enum SectionJoin
{
    /// <summary>The appended content starts on a new page, with its own page setup and headers.</summary>
    NewPage,

    /// <summary>A section break without a page break; the appended content keeps its page setup.</summary>
    Continuous,

    /// <summary>
    /// No break at all: the appended content flows into the target's last section and takes its
    /// page setup and headers. The source's own section properties and headers are dropped.
    /// </summary>
    None,
}

/// <summary>What happens when both documents define a style with the same id.</summary>
public enum StyleConflict
{
    /// <summary>The target's definition wins, as Word's "Use destination styles".</summary>
    UseTarget,

    /// <summary>
    /// A differing source style is copied under a new id (<c>Heading1_1</c>) so the appended
    /// content looks as it did. Identical styles are shared either way.
    /// </summary>
    KeepSource,
}

/// <summary>Options for <see cref="WordDocument.AppendDocument"/>.</summary>
public sealed record AppendOptions
{
    /// <summary>How the sections meet. Defaults to a new page.</summary>
    public SectionJoin SectionBreak { get; init; } = SectionJoin.NewPage;

    /// <summary>What to do with conflicting styles. Defaults to the target's.</summary>
    public StyleConflict Styles { get; init; } = StyleConflict.UseTarget;
}

/// <summary>
/// Copies one document's body to the end of another, rewriting everything the body refers to.
/// </summary>
/// <remarks>
/// <para>
/// Copying <c>w:body</c> is the easy part and on its own produces a document that opens and is
/// wrong. The body refers outward through ids, and every id means something only in its own file:
/// </para>
/// <list type="bullet">
/// <item><c>r:id</c>/<c>r:embed</c> resolve against the relationships of the part they sit in —
/// the document, a header, or the footnotes part, each separately.</item>
/// <item><c>w:pStyle</c>/<c>w:rStyle</c>/<c>w:tblStyle</c> name styles that may not exist in the
/// target, or exist with a different definition.</item>
/// <item><c>w:numId</c> names a list definition; two documents both have a list 1.</item>
/// <item><c>w:nsid</c> on an abstract list is what Word actually uses to decide that two lists are
/// the same list. A copied definition that keeps it is merged with the original, and the appended
/// list continues the target's numbering.</item>
/// <item>Footnote, endnote and comment references point into their own parts by id.</item>
/// <item>Revision, bookmark and permission ids must be unique; bookmark <em>names</em> too.</item>
/// <item><c>wp:docPr/@id</c> must be unique across the document, or Word reports damage.</item>
/// </list>
/// <para>
/// Everything is mapped lazily — a style or list is copied the first time something refers to it —
/// so only what the appended content uses comes along, and parts copied for headers or notes get
/// the same treatment as the body.
/// </para>
/// </remarks>
internal sealed class DocumentMerger
{
    private static readonly HashSet<XName> AnnotationElements =
    [
        Ns.W + "ins", Ns.W + "del", Ns.W + "moveFrom", Ns.W + "moveTo", Ns.W + "rPrChange",
        Ns.W + "pPrChange", Ns.W + "tblPrChange", Ns.W + "trPrChange", Ns.W + "tcPrChange",
        Ns.W + "sectPrChange", Ns.W + "tblGridChange", Ns.W + "tblPrExChange",
        Ns.W + "bookmarkStart", Ns.W + "bookmarkEnd", Ns.W + "permStart", Ns.W + "permEnd",
        Ns.W + "moveFromRangeStart", Ns.W + "moveFromRangeEnd", Ns.W + "moveToRangeStart",
        Ns.W + "moveToRangeEnd", Ns.W + "cellIns", Ns.W + "cellDel", Ns.W + "cellMerge",
        Ns.W + "numberingChange",
    ];

    private static readonly XName[] StyleReferences =
        [Ns.W + "pStyle", Ns.W + "rStyle", Ns.W + "tblStyle"];

    private readonly WordDocument _target;
    private readonly WordDocument _source;
    private readonly AppendOptions _options;

    private readonly Dictionary<string, string> _styles = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _lists = [];
    private readonly Dictionary<int, int> _abstractLists = [];
    private readonly Dictionary<(string Part, string Id), string> _relationships = [];
    private readonly Dictionary<OpcPartName, OpcPart> _copiedParts = [];
    private readonly Dictionary<string, string> _bookmarks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _targetBookmarks;

    private readonly int _idOffset;
    private int _nextDocPr;

    internal DocumentMerger(WordDocument target, WordDocument source, AppendOptions options)
    {
        _target = target;
        _source = source;
        _options = options;

        _idOffset = RevisionCollection.NextId(target);
        _nextDocPr = 1 + RevisionCollection.Roots(target)
            .SelectMany(r => r.Descendants(Ns.Wp + "docPr"))
            .Select(e => e.IntAttr("id"))
            .DefaultIfEmpty(0)
            .Max();

        _targetBookmarks = RevisionCollection.Roots(target)
            .SelectMany(r => r.Descendants(Ns.W + "bookmarkStart"))
            .Select(e => e.Attr(Ns.W + "name"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    internal void Append()
    {
        var sourceBody = _source.Body;
        var sourceFinal = sourceBody.Element(Ns.W + "sectPr") is { } s ? new XElement(s) : null;

        var blocks = sourceBody.Elements()
            .Where(e => e.Name != Ns.W + "sectPr")
            .Select(e => new XElement(e))
            .ToList();

        if (_options.SectionBreak == SectionJoin.None)
        {
            // The source's sections go with their headers; nothing refers to them any more.
            foreach (var block in blocks)
            {
                block.Descendants(Ns.W + "pPr").Elements(Ns.W + "sectPr").Remove();
            }

            sourceFinal = null;
        }

        CopyNotesAndComments(blocks);
        Transform(blocks, _source.DocumentPart, _target.DocumentPart);

        if (sourceFinal is not null)
        {
            Transform([sourceFinal], _source.DocumentPart, _target.DocumentPart);
        }

        var body = _target.Body;
        var targetFinal = body.Element(Ns.W + "sectPr");

        if (_options.SectionBreak != SectionJoin.None && targetFinal is not null)
        {
            EndTargetSection(body, targetFinal);

            var next = sourceFinal ?? new XElement(targetFinal);
            next.Elements(Ns.W + "type").Remove();

            // w:type says how the section it sits in *starts*, so it belongs on the appended
            // section's properties, not on the break that ends the target's.
            XmlUtil.SetOrdered(next, Ns.W + "type",
                XmlUtil.ValElement(Ns.W + "type",
                    _options.SectionBreak == SectionJoin.Continuous ? "continuous" : "nextPage"),
                Sections.Section.ReferenceOrder);

            targetFinal.ReplaceWith(next);
            targetFinal = next;
        }

        if (targetFinal is not null)
        {
            targetFinal.AddBeforeSelf(blocks);
        }
        else
        {
            body.Add(blocks);
        }

        // The ids handed out here are now in the document; the target's own counters must move
        // past them, or the next picture or bookmark added reuses one.
        _target.SeedIdCounters();
        _target.Touch();
    }

    /// <summary>
    /// Closes the target's last section with a copy of its properties, so it keeps its page setup
    /// and headers once the appended content takes over the body's final <c>w:sectPr</c>.
    /// </summary>
    /// <remarks>
    /// The copy goes into the last paragraph's properties when it has none, as Word does; adding a
    /// paragraph just to hold it would put a blank line at the end of the target.
    /// </remarks>
    private static void EndTargetSection(XElement body, XElement targetFinal)
    {
        var last = targetFinal.ElementsBeforeSelf().LastOrDefault();
        var carrier = last is not null && last.Name == Ns.W + "p" &&
                      last.Element(Ns.W + "pPr")?.Element(Ns.W + "sectPr") is null
            ? last
            : null;

        if (carrier is null)
        {
            carrier = new XElement(Ns.W + "p");
            targetFinal.AddBeforeSelf(carrier);
        }

        // The full successor list: with only w:pPr in it, SetOrdered finds nothing that must
        // follow and appends the properties after the runs.
        var properties = XmlUtil.GetOrCreate(carrier, Ns.W + "pPr", Paragraph.ParagraphOrder);
        XmlUtil.SetOrdered(properties, Ns.W + "sectPr", new XElement(targetFinal), ParagraphFormat.Order);
    }

    // ---- Notes and comments --------------------------------------------------------------------

    private void CopyNotesAndComments(List<XElement> blocks)
    {
        var footnotes = new Dictionary<string, string>(StringComparer.Ordinal);
        var endnotes = new Dictionary<string, string>(StringComparer.Ordinal);
        var comments = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var element in blocks.SelectMany(b => b.DescendantsAndSelf()).ToList())
        {
            var name = element.Name;
            var id = element.Attr(Ns.W + "id");

            if (id is null)
            {
                continue;
            }

            if (name == Ns.W + "footnoteReference")
            {
                element.SetAttributeValue(Ns.W + "id",
                    CopyNote(footnotes, id, RelationshipTypes.Footnotes, Ns.W + "footnote", () => _ = _target.Footnotes));
            }
            else if (name == Ns.W + "endnoteReference")
            {
                element.SetAttributeValue(Ns.W + "id",
                    CopyNote(endnotes, id, RelationshipTypes.Endnotes, Ns.W + "endnote", () => _ = _target.Endnotes));
            }
            else if (name == Ns.W + "commentRangeStart" || name == Ns.W + "commentRangeEnd" ||
                     name == Ns.W + "commentReference")
            {
                element.SetAttributeValue(Ns.W + "id",
                    CopyNote(comments, id, RelationshipTypes.Comments, Ns.W + "comment", () => _ = _target.Comments));
            }
        }
    }

    /// <summary>Copies one note or comment into the target's part and returns its new id.</summary>
    private string CopyNote(Dictionary<string, string> map, string id, string relationshipType,
        XName elementName, Action ensureTargetPart)
    {
        if (map.TryGetValue(id, out var mapped))
        {
            return mapped;
        }

        var sourcePart = _source.DocumentPart.RelatedPartByType(relationshipType);
        var sourceNote = sourcePart?.Xml.Root?.Elements(elementName)
            .FirstOrDefault(e => e.Attr(Ns.W + "id") == id);

        if (sourcePart is null || sourceNote is null)
        {
            // A reference to a note that does not exist was already broken; keep it as it was.
            map[id] = id;
            return id;
        }

        ensureTargetPart();
        var targetPart = _target.DocumentPart.RelatedPartByType(relationshipType)!;
        var targetRoot = targetPart.Xml.Root!;

        var newId = (targetRoot.Elements(elementName).Select(e => e.IntAttr(Ns.W + "id")).DefaultIfEmpty(0).Max() + 1)
            .ToString(CultureInfo.InvariantCulture);

        var copy = new XElement(sourceNote);
        copy.SetAttributeValue(Ns.W + "id", newId);
        Transform([copy], sourcePart, targetPart);
        targetRoot.Add(copy);

        map[id] = newId;
        return newId;
    }

    // ---- The rewrite ---------------------------------------------------------------------------

    /// <summary>Rewrites every outward reference in <paramref name="elements"/>.</summary>
    /// <param name="elements">Copied content, already detached from the source.</param>
    /// <param name="sourcePart">The part the content came from; its relationships resolve r:ids.</param>
    /// <param name="targetPart">The part it is going into; new relationships go here.</param>
    private void Transform(IEnumerable<XElement> elements, OpcPart sourcePart, OpcPart targetPart)
    {
        foreach (var root in elements)
        {
            foreach (var element in root.DescendantsAndSelf().ToList())
            {
                var name = element.Name;

                if (StyleReferences.Contains(name) && element.Attr(Ns.W + "val") is { } style)
                {
                    element.SetAttributeValue(Ns.W + "val", MapStyle(style));
                }
                else if (name == Ns.W + "numId" && element.Parent?.Name == Ns.W + "numPr" &&
                         int.TryParse(element.Attr(Ns.W + "val"), out var list) && list != 0)
                {
                    element.SetAttributeValue(Ns.W + "val", MapList(list));
                }
                else if (AnnotationElements.Contains(name) &&
                         int.TryParse(element.Attr(Ns.W + "id"), out var annotation))
                {
                    element.SetAttributeValue(Ns.W + "id", annotation + _idOffset);
                }
                else if (name == Ns.Wp + "docPr")
                {
                    element.SetAttributeValue("id", _nextDocPr++);
                }

                if (name == Ns.W + "bookmarkStart" && element.Attr(Ns.W + "name") is { } bookmark)
                {
                    element.SetAttributeValue(Ns.W + "name", MapBookmark(bookmark));
                }

                // Paragraph and text ids (w14) must be unique too; Word regenerates any that
                // collide, but only after reporting the file.
                if (element.Attribute(Ns.W14 + "paraId") is not null)
                {
                    element.SetAttributeValue(Ns.W14 + "paraId", NewParaId());
                }

                if (element.Attribute(Ns.W14 + "textId") is not null)
                {
                    element.SetAttributeValue(Ns.W14 + "textId", NewParaId());
                }

                foreach (var attribute in element.Attributes().Where(a => a.Name.Namespace == Ns.R).ToList())
                {
                    attribute.Value = MapRelationship(sourcePart, targetPart, attribute.Value);
                }
            }

            // Anchors are rewritten after every bookmark in the content has been renamed.
            foreach (var link in root.DescendantsAndSelf(Ns.W + "hyperlink"))
            {
                if (link.Attr(Ns.W + "anchor") is { } anchor && _bookmarks.TryGetValue(anchor, out var renamed))
                {
                    link.SetAttributeValue(Ns.W + "anchor", renamed);
                }
            }
        }
    }

    private string MapBookmark(string name)
    {
        if (_bookmarks.TryGetValue(name, out var mapped))
        {
            return mapped;
        }

        var candidate = name;

        for (var i = 2; _targetBookmarks.Contains(candidate); i++)
        {
            candidate = $"{name}_{i}";
        }

        _targetBookmarks.Add(candidate);
        _bookmarks[name] = candidate;
        return candidate;
    }

    private static string NewParaId()
    {
        // Must be below 0x80000000, per [MS-DOCX].
        var value = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        return value.ToString("X8", CultureInfo.InvariantCulture);
    }

    // ---- Styles --------------------------------------------------------------------------------

    private XElement? SourceStyles => _source.DocumentPart
        .RelatedPartByType(RelationshipTypes.Styles)?.Xml.Root;

    private XElement TargetStyles
    {
        get
        {
            _ = _target.Styles;
            return _target.DocumentPart.RelatedPartByType(RelationshipTypes.Styles)!.Xml.Root!;
        }
    }

    private string MapStyle(string id)
    {
        if (_styles.TryGetValue(id, out var mapped))
        {
            return mapped;
        }

        var source = SourceStyles?.Elements(Ns.W + "style")
            .FirstOrDefault(e => e.Attr(Ns.W + "styleId") == id);

        if (source is null)
        {
            _styles[id] = id;
            return id;
        }

        var targetRoot = TargetStyles;
        var existing = targetRoot.Elements(Ns.W + "style")
            .FirstOrDefault(e => e.Attr(Ns.W + "styleId") == id);

        if (existing is not null &&
            (_options.Styles == StyleConflict.UseTarget || XNode.DeepEquals(existing, source)))
        {
            _styles[id] = id;
            return id;
        }

        var newId = id;

        if (existing is not null)
        {
            for (var i = 1; targetRoot.Elements(Ns.W + "style").Any(e => e.Attr(Ns.W + "styleId") == newId); i++)
            {
                newId = $"{id}_{i}";
            }
        }

        // Recorded before the copy is rewritten, so a basedOn chain that loops ends here.
        _styles[id] = newId;

        var copy = new XElement(source);
        copy.SetAttributeValue(Ns.W + "styleId", newId);

        // A second default paragraph style would make Word pick one at random.
        copy.SetAttributeValue(Ns.W + "default", null);

        if (newId != id && copy.Element(Ns.W + "name") is { } nameElement)
        {
            nameElement.SetAttributeValue(Ns.W + "val", $"{nameElement.Attr(Ns.W + "val")} ({newId})");
        }

        foreach (var link in (XName[])[Ns.W + "basedOn", Ns.W + "next", Ns.W + "link"])
        {
            if (copy.Element(link) is { } reference && reference.Attr(Ns.W + "val") is { } target)
            {
                reference.SetAttributeValue(Ns.W + "val", MapStyle(target));
            }
        }

        foreach (var numId in copy.Descendants(Ns.W + "numId").ToList())
        {
            if (int.TryParse(numId.Attr(Ns.W + "val"), out var list) && list != 0)
            {
                numId.SetAttributeValue(Ns.W + "val", MapList(list));
            }
        }

        targetRoot.Add(copy);
        return newId;
    }

    // ---- Lists ---------------------------------------------------------------------------------

    private int MapList(int numId)
    {
        if (_lists.TryGetValue(numId, out var mapped))
        {
            return mapped;
        }

        var sourceRoot = _source.DocumentPart.RelatedPartByType(RelationshipTypes.Numbering)?.Xml.Root;
        var num = sourceRoot?.Elements(Ns.W + "num").FirstOrDefault(e => e.IntAttr(Ns.W + "numId") == numId);

        if (sourceRoot is null || num is null)
        {
            _lists[numId] = numId;
            return numId;
        }

        _ = _target.Numbering;
        var targetRoot = _target.DocumentPart.RelatedPartByType(RelationshipTypes.Numbering)!.Xml.Root!;

        var newNumId = targetRoot.Elements(Ns.W + "num").Select(e => e.IntAttr(Ns.W + "numId")).DefaultIfEmpty(0).Max() + 1;
        _lists[numId] = newNumId;

        var abstractId = num.Element(Ns.W + "abstractNumId").IntAttr(Ns.W + "val", -1);
        var newAbstractId = MapAbstractList(sourceRoot, targetRoot, abstractId);

        var copy = new XElement(num);
        copy.SetAttributeValue(Ns.W + "numId", newNumId);
        copy.Element(Ns.W + "abstractNumId")?.SetAttributeValue(Ns.W + "val", newAbstractId);

        // CT_Numbering: abstractNum*, then num*, then numIdMacAtCleanup.
        if (targetRoot.Element(Ns.W + "numIdMacAtCleanup") is { } cleanup)
        {
            cleanup.AddBeforeSelf(copy);
        }
        else
        {
            targetRoot.Add(copy);
        }

        return newNumId;
    }

    private int MapAbstractList(XElement sourceRoot, XElement targetRoot, int abstractId)
    {
        if (_abstractLists.TryGetValue(abstractId, out var mapped))
        {
            return mapped;
        }

        var source = sourceRoot.Elements(Ns.W + "abstractNum")
            .FirstOrDefault(e => e.IntAttr(Ns.W + "abstractNumId") == abstractId);

        var newId = targetRoot.Elements(Ns.W + "abstractNum")
            .Select(e => e.IntAttr(Ns.W + "abstractNumId")).DefaultIfEmpty(-1).Max() + 1;
        _abstractLists[abstractId] = newId;

        if (source is null)
        {
            return newId;
        }

        var copy = new XElement(source);
        copy.SetAttributeValue(Ns.W + "abstractNumId", newId);

        // A fresh nsid: Word treats two abstract lists with the same nsid as one list, so a copy
        // that kept it would continue the target's numbering instead of starting its own.
        if (copy.Element(Ns.W + "nsid") is { } nsid)
        {
            nsid.SetAttributeValue(Ns.W + "val",
                RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString("X8", CultureInfo.InvariantCulture));
        }

        foreach (var style in copy.Descendants(Ns.W + "pStyle").ToList())
        {
            if (style.Attr(Ns.W + "val") is { } id)
            {
                style.SetAttributeValue(Ns.W + "val", MapStyle(id));
            }
        }

        var lastAbstract = targetRoot.Elements(Ns.W + "abstractNum").LastOrDefault();

        if (lastAbstract is not null)
        {
            lastAbstract.AddAfterSelf(copy);
        }
        else if (targetRoot.Elements().FirstOrDefault(e => e.Name != Ns.W + "numPicBullet") is { } first)
        {
            first.AddBeforeSelf(copy);
        }
        else
        {
            targetRoot.Add(copy);
        }

        return newId;
    }

    // ---- Relationships and parts ---------------------------------------------------------------

    private string MapRelationship(OpcPart sourcePart, OpcPart targetPart, string id)
    {
        var key = (sourcePart.Name.Value + "→" + targetPart.Name.Value, id);

        if (_relationships.TryGetValue(key, out var mapped))
        {
            return mapped;
        }

        var relationship = sourcePart.RelationshipById(id);

        if (relationship is null)
        {
            return id;
        }

        string newId;

        if (relationship.TargetMode == TargetMode.External)
        {
            newId = (targetPart.RelationshipsByType(relationship.Type)
                         .FirstOrDefault(r => r.TargetMode == TargetMode.External && r.Target == relationship.Target)
                     ?? targetPart.AddExternalRelationship(relationship.Type, relationship.Target)).Id;
        }
        else if (!_source.Package.TryGetPart(relationship.TargetPartName, out var sourceTarget))
        {
            newId = id;
        }
        else if (relationship.Type == RelationshipTypes.Image)
        {
            newId = _target.AddImage(targetPart, sourceTarget.GetBytes()).RelationshipId;
        }
        else
        {
            var copy = CopyPart(sourceTarget);
            newId = (targetPart.RelationshipsByType(relationship.Type)
                         .FirstOrDefault(r => r.TargetMode == TargetMode.Internal && r.TargetPartName == copy.Name)
                     ?? targetPart.AddRelationship(copy, relationship.Type)).Id;
        }

        _relationships[key] = newId;
        return newId;
    }

    /// <summary>
    /// Copies a part — a header, a chart, an embedded workbook — under a fresh name, rewriting the
    /// references inside it when it is XML.
    /// </summary>
    private OpcPart CopyPart(OpcPart source)
    {
        if (_copiedParts.TryGetValue(source.Name, out var copied))
        {
            return copied;
        }

        var template = NameTemplate(source.Name.Value);
        var name = _target.Package.NextPartName(template);
        var part = _target.Package.AddPart(name, source.ContentType, source.GetBytes());
        _copiedParts[source.Name] = part;

        if (source.ContentType.EndsWith("xml", StringComparison.OrdinalIgnoreCase))
        {
            var document = new XDocument(source.Xml);

            if (document.Root is { } root)
            {
                Transform([root], source, part);
            }

            part.Xml = document;
        }

        return part;
    }

    /// <summary><c>/word/header2.xml</c> → <c>/word/header{0}.xml</c>.</summary>
    private static string NameTemplate(string name)
    {
        var match = Regex.Match(name, @"^(?<stem>.*?)(?<digits>\d*)(?<ext>\.[^./]+)$");

        return match.Success
            ? match.Groups["stem"].Value + "{0}" + match.Groups["ext"].Value
            : name + "{0}";
    }
}
