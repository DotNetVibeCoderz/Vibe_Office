// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using System.Xml.Linq;
using OfficeNet.Core.Xml;

namespace WordNet.Revisions;

/// <summary>What a tracked change did.</summary>
public enum RevisionKind
{
    /// <summary>Text inserted (<c>w:ins</c> around runs).</summary>
    Insertion,

    /// <summary>Text deleted (<c>w:del</c> around runs whose text is <c>w:delText</c>).</summary>
    Deletion,

    /// <summary>The place text was moved away from (<c>w:moveFrom</c>). Reads like a deletion.</summary>
    MoveFrom,

    /// <summary>The place text was moved to (<c>w:moveTo</c>). Reads like an insertion.</summary>
    MoveTo,

    /// <summary>Character formatting changed (<c>w:rPrChange</c>); the old formatting is kept.</summary>
    RunFormat,

    /// <summary>Paragraph formatting changed (<c>w:pPrChange</c>); the old formatting is kept.</summary>
    ParagraphFormat,

    /// <summary>A paragraph mark inserted — the paragraph was split from the one after it.</summary>
    ParagraphInserted,

    /// <summary>A paragraph mark deleted — the paragraph joins the one after it.</summary>
    ParagraphDeleted,

    /// <summary>A table row inserted.</summary>
    RowInserted,

    /// <summary>A table row deleted.</summary>
    RowDeleted,

    /// <summary>
    /// Table, row, cell or section properties changed (<c>w:tblPrChange</c>, <c>w:trPrChange</c>,
    /// <c>w:tcPrChange</c>, <c>w:sectPrChange</c>, <c>w:tblGridChange</c>, <c>w:tblPrExChange</c>).
    /// </summary>
    PropertyFormat,
}

/// <summary>
/// One tracked change. <see cref="Accept"/> makes it permanent; <see cref="Reject"/> undoes it.
/// </summary>
/// <remarks>
/// A revision is a view of its element, like every other WordNet object: accepting it edits the
/// XML in place, and the revision is then spent — <see cref="IsResolved"/> turns true and a second
/// call does nothing.
/// </remarks>
public sealed class Revision
{
    private readonly WordDocument _document;

    internal Revision(WordDocument document, RevisionKind kind, XElement element)
    {
        _document = document;
        Kind = kind;
        Element = element;
        _root = element.Document?.Root;
    }

    private readonly XElement? _root;

    /// <summary>What the change did.</summary>
    public RevisionKind Kind { get; }

    /// <summary>
    /// The element that carries the revision: the <c>w:ins</c>/<c>w:del</c> wrapper, the
    /// <c>w:rPrChange</c>/<c>w:pPrChange</c>, or the marker inside <c>w:rPr</c>/<c>w:trPr</c>.
    /// </summary>
    public XElement Element { get; }

    /// <summary>The revision id.</summary>
    public int Id => Element.IntAttr(Ns.W + "id");

    /// <summary>Who made the change.</summary>
    public string? Author => Element.Attr(Ns.W + "author");

    /// <summary>When the change was made, if recorded.</summary>
    public DateTimeOffset? Date =>
        DateTimeOffset.TryParse(Element.Attr(Ns.W + "date"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    /// <summary>True once the revision has been accepted or rejected.</summary>
    public bool IsResolved => Element.Parent is null;

    /// <summary>
    /// The text the change inserted or removed. Empty for formatting, paragraph-mark and row
    /// changes.
    /// </summary>
    public string Text
    {
        get
        {
            if (Kind is not (RevisionKind.Insertion or RevisionKind.Deletion or RevisionKind.MoveFrom
                or RevisionKind.MoveTo))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();

            foreach (var node in Element.Descendants())
            {
                if (node.Name == Ns.W + "t" || node.Name == Ns.W + "delText")
                {
                    builder.Append(node.Value);
                }
                else if (node.Name == Ns.W + "tab")
                {
                    builder.Append('\t');
                }
                else if (node.Name == Ns.W + "br" || node.Name == Ns.W + "cr")
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>Makes the change permanent and removes its revision mark.</summary>
    public void Accept()
    {
        if (IsResolved)
        {
            return;
        }

        switch (Kind)
        {
            case RevisionKind.Insertion or RevisionKind.MoveTo:
                Unwrap(Element);
                break;

            case RevisionKind.Deletion or RevisionKind.MoveFrom:
                Element.Remove();
                break;

            case RevisionKind.RunFormat or RevisionKind.ParagraphFormat or RevisionKind.PropertyFormat:
                Element.Remove();
                break;

            case RevisionKind.ParagraphInserted:
                Element.Remove();
                break;

            case RevisionKind.ParagraphDeleted:
                JoinWithNext(MarkParagraph());
                break;

            case RevisionKind.RowInserted:
                Element.Remove();
                break;

            case RevisionKind.RowDeleted:
                RemoveRow();
                break;
        }

        Finish();
    }

    /// <summary>Undoes the change and removes its revision mark.</summary>
    public void Reject()
    {
        if (IsResolved)
        {
            return;
        }

        switch (Kind)
        {
            case RevisionKind.Insertion or RevisionKind.MoveTo:
                Element.Remove();
                break;

            case RevisionKind.Deletion or RevisionKind.MoveFrom:
                Restore(Element);
                Unwrap(Element);
                break;

            case RevisionKind.RunFormat:
                RestoreRunFormat();
                break;

            case RevisionKind.ParagraphFormat:
                RestoreParagraphFormat();
                break;

            case RevisionKind.ParagraphInserted:
                JoinWithNext(MarkParagraph());
                break;

            case RevisionKind.ParagraphDeleted:
                Element.Remove();
                break;

            case RevisionKind.RowInserted:
                RemoveRow();
                break;

            case RevisionKind.RowDeleted:
                Element.Remove();
                break;

            case RevisionKind.PropertyFormat:
                RestoreProperties();
                break;
        }

        Finish();
    }

    private void Finish()
    {
        if (_root is not null && Kind is RevisionKind.MoveFrom or RevisionKind.MoveTo)
        {
            RevisionCollection.RemoveMoveRanges(_root);
        }

        _document.Touch();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Kind} by {Author ?? "(unknown)"}{(Text.Length > 0 ? $": \"{Text}\"" : "")}";

    // ---- Mechanics -----------------------------------------------------------------------------

    /// <summary>Replaces a wrapper with its children.</summary>
    private static void Unwrap(XElement wrapper)
    {
        wrapper.AddBeforeSelf(wrapper.Nodes());
        wrapper.Remove();
    }

    /// <summary>
    /// Turns deleted text back into text: <c>w:delText</c> → <c>w:t</c> and
    /// <c>w:delInstrText</c> → <c>w:instrText</c>. A rejected deletion that kept
    /// <c>w:delText</c> would be live text Word refuses to show.
    /// </summary>
    private static void Restore(XElement wrapper)
    {
        foreach (var node in wrapper.Descendants(Ns.W + "delText").ToList())
        {
            node.Name = Ns.W + "t";
        }

        foreach (var node in wrapper.Descendants(Ns.W + "delInstrText").ToList())
        {
            node.Name = Ns.W + "instrText";
        }
    }

    /// <summary>The paragraph whose mark carries this revision (<c>w:p/w:pPr/w:rPr/w:ins</c>).</summary>
    private XElement MarkParagraph() =>
        Element.Parent?.Parent?.Parent is { } p && p.Name == Ns.W + "p"
            ? p
            : throw new InvalidOperationException("The paragraph-mark revision is not inside a w:p.");

    /// <summary>
    /// Removes a paragraph mark: the paragraph's content moves to the start of the next paragraph,
    /// which keeps its own properties, exactly as deleting the mark does in Word.
    /// </summary>
    /// <remarks>
    /// With no following paragraph in the same container — the last paragraph of a cell or of the
    /// body — there is nothing to join, so only the revision mark goes. Word does the same: the
    /// final paragraph mark of a container cannot be deleted.
    /// </remarks>
    private void JoinWithNext(XElement paragraph)
    {
        Element.Remove();

        if (paragraph.ElementsAfterSelf().FirstOrDefault() is not { } next || next.Name != Ns.W + "p")
        {
            return;
        }

        var content = paragraph.Elements().Where(e => e.Name != Ns.W + "pPr").ToList();
        var nextProperties = next.Element(Ns.W + "pPr");

        // Detached first, so adding them below moves these elements rather than cloning them.
        // Revisions still pending in the moved content hold references to these very nodes.
        foreach (var element in content)
        {
            element.Remove();
        }

        if (nextProperties is not null)
        {
            nextProperties.AddAfterSelf(content);
        }
        else
        {
            next.AddFirst(content);
        }

        paragraph.Remove();
    }

    private void RemoveRow()
    {
        var row = Element.Parent?.Parent;

        if (row is null || row.Name != Ns.W + "tr")
        {
            Element.Remove();
            return;
        }

        var table = row.Parent;
        row.Remove();

        // A table with no rows is invalid; with its last row gone the table goes too, leaving a
        // paragraph if the table was the last block of a cell, which must end in one.
        if (table is not null && !table.Elements(Ns.W + "tr").Any())
        {
            var container = table.Parent;
            table.Remove();

            if (container is not null && container.Name == Ns.W + "tc" &&
                !container.Elements(Ns.W + "p").Any())
            {
                container.Add(new XElement(Ns.W + "p"));
            }
        }
    }

    /// <summary>Puts the formatting recorded in <c>w:rPrChange</c> back.</summary>
    private void RestoreRunFormat()
    {
        var properties = Element.Parent!;
        var old = Element.Element(Ns.W + "rPr");

        // The run properties a paragraph mark carries hold its own ins/del markers; those are not
        // formatting and must survive the restore.
        var markers = properties.Elements()
            .Where(e => e.Name == Ns.W + "ins" || e.Name == Ns.W + "del" ||
                        e.Name == Ns.W + "moveFrom" || e.Name == Ns.W + "moveTo")
            .ToList();

        properties.RemoveNodes();
        properties.Add(markers);

        if (old is not null)
        {
            properties.Add(old.Elements());
        }

        // The old properties are in schema order already, and the markers come first in it.
    }

    /// <summary>Puts table, row, cell or section properties back from their change record.</summary>
    /// <remarks>
    /// The change records only the properties themselves. A row's insertion and deletion markers
    /// and a section's header and footer references are not part of it and are kept.
    /// </remarks>
    private void RestoreProperties()
    {
        var properties = Element.Parent!;
        var old = Element.Elements().FirstOrDefault();

        var kept = properties.Elements()
            .Where(e => e != Element && (e.Name == Ns.W + "ins" || e.Name == Ns.W + "del" ||
                                         e.Name == Ns.W + "headerReference" ||
                                         e.Name == Ns.W + "footerReference"))
            .ToList();

        properties.RemoveNodes();

        // Header and footer references come first in sectPr; ins/del come last in trPr.
        properties.Add(kept.Where(e => e.Name.LocalName.EndsWith("Reference", StringComparison.Ordinal)));

        if (old is not null)
        {
            properties.Add(old.Elements());
        }

        properties.Add(kept.Where(e => !e.Name.LocalName.EndsWith("Reference", StringComparison.Ordinal)));
    }

    /// <summary>Puts the paragraph formatting recorded in <c>w:pPrChange</c> back.</summary>
    /// <remarks>
    /// <c>w:pPrChange</c> records only the paragraph's own properties. The mark's run properties
    /// (<c>w:rPr</c>) and a section break (<c>w:sectPr</c>) are not part of the change, sit after
    /// those properties in the schema, and are kept as they are.
    /// </remarks>
    private void RestoreParagraphFormat()
    {
        var properties = Element.Parent!;
        var old = Element.Element(Ns.W + "pPr");

        var kept = properties.Elements()
            .Where(e => e.Name == Ns.W + "rPr" || e.Name == Ns.W + "sectPr")
            .ToList();

        properties.RemoveNodes();

        if (old is not null)
        {
            properties.Add(old.Elements());
        }

        properties.Add(kept);
    }
}
