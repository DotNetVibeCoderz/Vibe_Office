// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Xml;

namespace WordNet.Notes;

/// <summary>Whether a note sits at the foot of the page or at the end of the document.</summary>
public enum NoteKind
{
    /// <summary>At the bottom of the page the reference appears on.</summary>
    Footnote,

    /// <summary>Collected at the end of the document.</summary>
    Endnote,
}

/// <summary>
/// One footnote or endnote.
/// </summary>
/// <remarks>
/// A note is a small block-level document of its own: it holds paragraphs, not a string. That is
/// why <see cref="AddParagraph"/> exists rather than a settable <c>Text</c> — a footnote citing
/// three sources across two paragraphs is ordinary, and flattening it to one string would make it
/// unrepresentable.
/// </remarks>
public sealed class Note
{
    private readonly WordDocument _document;

    internal Note(WordDocument document, XElement element, NoteKind kind)
    {
        _document = document;
        Element = element;
        Kind = kind;
    }

    /// <summary>The underlying <c>w:footnote</c> or <c>w:endnote</c> element.</summary>
    public XElement Element { get; }

    /// <summary>Whether this is a footnote or an endnote.</summary>
    public NoteKind Kind { get; }

    /// <summary>The note's id, which the reference in the body points at.</summary>
    public int Id => Element.IntAttr(Ns.W + "id");

    /// <summary>The note's paragraphs.</summary>
    public IReadOnlyList<Paragraph> Paragraphs =>
        [.. Element.Elements(Ns.W + "p").Select(e => new Paragraph(_document, e))];

    /// <summary>The note's text, paragraphs joined by newlines.</summary>
    public string Text => string.Join('\n', Paragraphs.Select(p => p.Text));

    /// <summary>Appends a paragraph to the note.</summary>
    public Paragraph AddParagraph(string text = "")
    {
        var element = new XElement(Ns.W + "p");
        Element.Add(element);

        var paragraph = new Paragraph(_document, element);
        paragraph.StyleId = Kind == NoteKind.Footnote ? "FootnoteText" : "EndnoteText";

        // The reference mark inside the note — the little number before the text. It is a separate
        // run carrying w:footnoteRef, and Word renders the number itself; writing a literal digit
        // instead would not renumber when notes are inserted above it.
        var mark = new XElement(Ns.W + "r",
            new XElement(Ns.W + "rPr",
                XmlUtil.ValElement(Ns.W + "rStyle",
                    Kind == NoteKind.Footnote ? "FootnoteReference" : "EndnoteReference")),
            new XElement(Ns.W + (Kind == NoteKind.Footnote ? "footnoteRef" : "endnoteRef")));

        element.Add(mark);

        if (text.Length > 0)
        {
            // A space after the mark, because Word's own notes have one and its absence reads as
            // a typo rather than as a style choice.
            paragraph.AddRun(" " + text);
        }

        _document.Touch();
        return paragraph;
    }

    public override string ToString() => $"{Kind} {Id}: {Text}";
}

/// <summary>
/// The footnotes or endnotes of a document.
/// </summary>
/// <remarks>
/// <para>
/// The part is created on first use. A document with no notes should not carry an empty
/// <c>footnotes.xml</c>, and Word does not write one either.
/// </para>
/// <para>
/// Ids 0 and 1 are reserved and must exist before any real note: 0 is the separator drawn above the
/// notes and 1 is the continuation separator. Word repairs a part that is missing them, and a note
/// numbered 0 or 1 collides with them.
/// </para>
/// </remarks>
public sealed class NoteCollection
{
    private readonly WordDocument _document;
    private readonly XElement _root;

    internal NoteCollection(WordDocument document, XElement root, NoteKind kind)
    {
        _document = document;
        _root = root;
        Kind = kind;
    }

    /// <summary>Whether this collection holds footnotes or endnotes.</summary>
    public NoteKind Kind { get; }

    private XName NoteName => Ns.W + (Kind == NoteKind.Footnote ? "footnote" : "endnote");

    /// <summary>The notes, excluding the reserved separators.</summary>
    /// <remarks>
    /// Ids 0 and 1 hold the separator marks rather than content, so they are filtered out: a caller
    /// asking for "the footnotes" means the ones a reader sees.
    /// </remarks>
    public IReadOnlyList<Note> All =>
        [.. _root.Elements(NoteName)
            .Where(e => e.Attr(Ns.W + "type") is null)
            .Select(e => new Note(_document, e, Kind))];

    /// <summary>The number of notes.</summary>
    public int Count => All.Count;

    /// <summary>A note by id.</summary>
    public Note? this[int id] =>
        _root.Elements(NoteName).FirstOrDefault(e => e.IntAttr(Ns.W + "id") == id) is { } element
            ? new Note(_document, element, Kind)
            : null;

    /// <summary>The raw element for a note id, for edits this library does not model.</summary>
    public XElement? Element(int id) =>
        _root.Elements(NoteName).FirstOrDefault(e => e.IntAttr(Ns.W + "id") == id);

    /// <summary>Adds a note and returns it. Use <see cref="Paragraph.AddFootnote"/> to reference it.</summary>
    internal Note Add(string text)
    {
        // Ids continue past the highest in use rather than counting the notes: deleting note 3 of
        // five must not make the next one collide with note 4.
        var next = _root.Elements(NoteName)
            .Select(e => e.IntAttr(Ns.W + "id"))
            .DefaultIfEmpty(1)
            .Max() + 1;

        var element = new XElement(NoteName,
            new XAttribute(Ns.W + "id", next.ToString(CultureInfo.InvariantCulture)));

        _root.Add(element);

        var note = new Note(_document, element, Kind);
        note.AddParagraph(text);

        _document.Touch();
        return note;
    }

    /// <summary>Removes a note and every reference to it in the body.</summary>
    /// <remarks>
    /// Removing the definition alone leaves a reference pointing at nothing, which Word reports as
    /// unreadable content — so both go together or neither does.
    /// </remarks>
    public bool Remove(int id)
    {
        if (id <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(id),
                "Ids 0 and 1 are the reserved separators and cannot be removed.");
        }

        var element = _root.Elements(NoteName).FirstOrDefault(e => e.IntAttr(Ns.W + "id") == id);

        if (element is null)
        {
            return false;
        }

        element.Remove();

        var referenceName = Ns.W + (Kind == NoteKind.Footnote ? "footnoteReference" : "endnoteReference");

        foreach (var reference in _document.Root.Descendants(referenceName)
                     .Where(e => e.IntAttr(Ns.W + "id") == id)
                     .ToList())
        {
            // The reference sits inside its own run; removing just the reference would leave an
            // empty run behind.
            (reference.Parent?.Name == Ns.W + "r" ? reference.Parent : reference).Remove();
        }

        _document.Touch();
        return true;
    }

    /// <summary>Builds an empty part holding only the two reserved separators.</summary>
    internal static XDocument CreatePart(NoteKind kind)
    {
        var plural = kind == NoteKind.Footnote ? "footnotes" : "endnotes";
        var singular = kind == NoteKind.Footnote ? "footnote" : "endnote";

        var root = new XElement(Ns.W + plural,
            new XAttribute(XNamespace.Xmlns + "w", Ns.W.NamespaceName));

        // id 0 is the separator line above the notes; id 1 is the one used when a note continues
        // onto the next page. Both are mandatory and both are empty of content.
        foreach (var (id, type) in new[] { (0, "separator"), (1, "continuationSeparator") })
        {
            root.Add(new XElement(Ns.W + singular,
                new XAttribute(Ns.W + "type", type),
                new XAttribute(Ns.W + "id", id.ToString(CultureInfo.InvariantCulture)),
                new XElement(Ns.W + "p",
                    new XElement(Ns.W + "pPr",
                        new XElement(Ns.W + "spacing",
                            new XAttribute(Ns.W + "after", "0"),
                            new XAttribute(Ns.W + "line", "240"),
                            new XAttribute(Ns.W + "lineRule", "auto"))),
                    new XElement(Ns.W + "r",
                        new XElement(Ns.W + (type == "separator" ? "separator" : "continuationSeparator"))))));
        }

        return XmlUtil.NewDocument(root);
    }
}
