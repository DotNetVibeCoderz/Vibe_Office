// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using WordNet.Notes;

namespace WordNet;

/// <summary>
/// A paragraph: the block that holds runs, and the unit Word's layout wraps text inside.
/// </summary>
public sealed class Paragraph
{
    private readonly WordDocument _document;

    internal Paragraph(WordDocument document, XElement element)
    {
        _document = document;
        Element = element;
        Format = new ParagraphFormat(
            () => XmlUtil.GetOrCreate(element, Ns.W + "pPr", ParagraphOrder),
            document.Touch);
    }

    private static readonly XName[] ParagraphOrder =
    [
        Ns.W + "pPr", Ns.W + "r", Ns.W + "hyperlink", Ns.W + "bookmarkStart", Ns.W + "bookmarkEnd",
    ];

    /// <summary>The underlying <c>w:p</c> element.</summary>
    public XElement Element { get; }

    /// <summary>The paragraph's formatting.</summary>
    public ParagraphFormat Format { get; }

    /// <summary>The document this paragraph belongs to.</summary>
    public WordDocument Document => _document;

    /// <summary>
    /// The runs directly in the paragraph and inside its hyperlinks, in document order.
    /// </summary>
    /// <remarks>
    /// A hyperlink wraps its runs in a <c>w:hyperlink</c> element, so a paragraph's direct
    /// <c>w:r</c> children are not all of its runs. Missing that is why link text disappears from
    /// naive text extraction.
    /// </remarks>
    public IReadOnlyList<Run> Runs =>
    [
        .. Element.Elements()
            .SelectMany(e => e.Name == Ns.W + "hyperlink" ? e.Elements(Ns.W + "r") : Enumerable.Repeat(e, 1))
            .Where(e => e.Name == Ns.W + "r")
            .Select(e => new Run(_document, e)),
    ];

    /// <summary>
    /// The paragraph's text.
    /// </summary>
    /// <remarks>
    /// Setting this replaces every run with a single one, which discards the character formatting
    /// of everything that was there. That is the behaviour python-docx has and callers expect;
    /// to keep formatting, edit <see cref="Runs"/> instead.
    /// </remarks>
    public string Text
    {
        get
        {
            var builder = new StringBuilder();

            foreach (var run in Runs)
            {
                builder.Append(run.Text);
            }

            return builder.ToString();
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var properties = Element.Element(Ns.W + "pPr");
            Element.RemoveNodes();

            if (properties is not null)
            {
                Element.Add(properties);
            }

            if (value.Length > 0)
            {
                AddRun(value);
            }

            _document.Touch();
        }
    }

    /// <summary>The paragraph style id, or <c>null</c> for the default.</summary>
    public string? StyleId
    {
        get => Format.StyleId;
        set => Format.StyleId = value;
    }

    /// <summary>Horizontal alignment.</summary>
    public ParagraphAlignment? Alignment
    {
        get => Format.Alignment;
        set => Format.Alignment = value;
    }

    /// <summary>Appends a run.</summary>
    public Run AddRun(string text = "")
    {
        var element = new XElement(Ns.W + "r");
        Element.Add(element);

        var run = new Run(_document, element);

        if (text.Length > 0)
        {
            run.AppendText(text);
        }

        _document.Touch();
        return run;
    }

    /// <summary>Appends a formatted run.</summary>
    public Run AddRun(string text, bool bold = false, bool italic = false, double? sizePoints = null,
        OfficeColor? color = null, string? fontName = null)
    {
        var run = AddRun(text);

        if (bold)
        {
            run.Format.Bold = true;
        }

        if (italic)
        {
            run.Format.Italic = true;
        }

        if (sizePoints is not null)
        {
            run.Format.FontSize = Units.Pt(sizePoints.Value);
        }

        if (color is not null)
        {
            run.Format.Color = color;
        }

        if (fontName is not null)
        {
            run.Format.FontName = fontName;
        }

        return run;
    }

    /// <summary>Appends a page break as its own run.</summary>
    public Paragraph AddPageBreak()
    {
        AddRun().AddBreak(BreakType.Page);
        return this;
    }

    /// <summary>Appends a line break.</summary>
    public Paragraph AddLineBreak()
    {
        AddRun().AddBreak();
        return this;
    }

    /// <summary>
    /// Appends a hyperlink to an external URL.
    /// </summary>
    public Run AddHyperlink(string text, string url, bool applyHyperlinkStyle = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var relationshipId = _document.AddHyperlinkRelationship(url);

        var hyperlink = new XElement(Ns.W + "hyperlink",
            new XAttribute(Ns.R + "id", relationshipId));

        var runElement = new XElement(Ns.W + "r");
        hyperlink.Add(runElement);
        Element.Add(hyperlink);

        var run = new Run(_document, runElement);
        run.AppendText(text);

        if (applyHyperlinkStyle)
        {
            // The Hyperlink character style has to exist in styles.xml or Word falls back to
            // unstyled text, so it is created on demand rather than assumed.
            _document.Styles.EnsureHyperlinkStyle();
            run.Format.StyleId = "Hyperlink";
        }

        _document.Touch();
        return run;
    }

    /// <summary>Appends a link to a bookmark inside the same document.</summary>
    public Run AddInternalHyperlink(string text, string bookmarkName, bool applyHyperlinkStyle = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkName);

        // An internal link uses w:anchor rather than r:id, and needs no relationship at all.
        var hyperlink = new XElement(Ns.W + "hyperlink",
            new XAttribute(Ns.W + "anchor", bookmarkName));

        var runElement = new XElement(Ns.W + "r");
        hyperlink.Add(runElement);
        Element.Add(hyperlink);

        var run = new Run(_document, runElement);
        run.AppendText(text);

        if (applyHyperlinkStyle)
        {
            _document.Styles.EnsureHyperlinkStyle();
            run.Format.StyleId = "Hyperlink";
        }

        _document.Touch();
        return run;
    }

    /// <summary>Wraps the paragraph's content in a named bookmark.</summary>
    public Paragraph AddBookmark(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var id = _document.NextBookmarkId();

        var start = new XElement(Ns.W + "bookmarkStart",
            new XAttribute(Ns.W + "id", id),
            new XAttribute(Ns.W + "name", name));

        var end = new XElement(Ns.W + "bookmarkEnd",
            new XAttribute(Ns.W + "id", id));

        // The start goes after w:pPr, which must stay the first child.
        var properties = Element.Element(Ns.W + "pPr");
        if (properties is not null)
        {
            properties.AddAfterSelf(start);
        }
        else
        {
            Element.AddFirst(start);
        }

        Element.Add(end);
        _document.Touch();
        return this;
    }

    /// <summary>
    /// Appends a field, such as a page number or a table of contents.
    /// </summary>
    /// <param name="instruction">The field instruction, for example <c>PAGE</c> or <c>TOC \o "1-3"</c>.</param>
    /// <param name="placeholder">What to show until the field is updated.</param>
    /// <remarks>
    /// A field is written as three runs — begin, instruction, separate/result, end — not as one
    /// element. The placeholder matters: Word does not evaluate fields on open unless asked, so a
    /// field with no cached result shows as blank in Word and stays blank in any PDF exported from
    /// it.
    /// </remarks>
    public Paragraph AddField(string instruction, string placeholder = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        Element.Add(new XElement(Ns.W + "r",
            new XElement(Ns.W + "fldChar", new XAttribute(Ns.W + "fldCharType", "begin"))));

        Element.Add(new XElement(Ns.W + "r",
            XmlUtil.TextElement(Ns.W + "instrText", " " + instruction + " ")));

        Element.Add(new XElement(Ns.W + "r",
            new XElement(Ns.W + "fldChar", new XAttribute(Ns.W + "fldCharType", "separate"))));

        Element.Add(new XElement(Ns.W + "r",
            XmlUtil.TextElement(Ns.W + "t", placeholder)));

        Element.Add(new XElement(Ns.W + "r",
            new XElement(Ns.W + "fldChar", new XAttribute(Ns.W + "fldCharType", "end"))));

        _document.Touch();
        return this;
    }

    // ---- Notes and comments --------------------------------------------------------------------

    /// <summary>Appends a footnote reference, and creates the footnote.</summary>
    /// <param name="text">The note's text.</param>
    /// <returns>The note, so further paragraphs can be added to it.</returns>
    /// <remarks>
    /// The reference is a run carrying <c>w:footnoteReference</c>, styled so Word draws it as a
    /// superscript number. Word owns the numbering: inserting a note above this one renumbers both.
    /// </remarks>
    public Note AddFootnote(string text) => AddNote(text, NoteKind.Footnote);

    /// <summary>Appends an endnote reference, and creates the endnote.</summary>
    public Note AddEndnote(string text) => AddNote(text, NoteKind.Endnote);

    private Note AddNote(string text, NoteKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);

        var collection = kind == NoteKind.Footnote ? _document.Footnotes : _document.Endnotes;
        var note = collection.Add(text);

        var referenceName = kind == NoteKind.Footnote ? "footnoteReference" : "endnoteReference";
        var styleId = kind == NoteKind.Footnote ? "FootnoteReference" : "EndnoteReference";

        Element.Add(new XElement(Ns.W + "r",
            new XElement(Ns.W + "rPr", XmlUtil.ValElement(Ns.W + "rStyle", styleId)),
            new XElement(Ns.W + referenceName,
                new XAttribute(Ns.W + "id", XmlUtil.Num(note.Id)))));

        _document.Touch();
        return note;
    }

    /// <summary>Attaches a comment to this whole paragraph.</summary>
    /// <param name="text">The comment's text.</param>
    /// <param name="author">Who is commenting.</param>
    /// <param name="initials">Shown in the margin; derived from the author when omitted.</param>
    /// <remarks>
    /// A comment is anchored to a range, so this wraps the paragraph's existing content between a
    /// <c>commentRangeStart</c> and a <c>commentRangeEnd</c>. Comment a specific run instead with
    /// <see cref="Run.AddComment"/>.
    /// </remarks>
    public Comment AddComment(string text, string author = "Author", string? initials = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        var comment = _document.Comments.Add(text, author, initials);
        var id = XmlUtil.Num(comment.Id);

        // The start marker goes before the first run, not at the very front: a leading w:pPr must
        // stay the paragraph's first child.
        var first = Element.Elements().FirstOrDefault(e => e.Name != Ns.W + "pPr");

        var start = new XElement(Ns.W + "commentRangeStart", new XAttribute(Ns.W + "id", id));

        if (first is not null)
        {
            first.AddBeforeSelf(start);
        }
        else
        {
            Element.Add(start);
        }

        Element.Add(new XElement(Ns.W + "commentRangeEnd", new XAttribute(Ns.W + "id", id)));

        // The reference run must follow the end marker, and carries the marker a reader clicks.
        Element.Add(new XElement(Ns.W + "r",
            new XElement(Ns.W + "rPr", XmlUtil.ValElement(Ns.W + "rStyle", "CommentReference")),
            new XElement(Ns.W + "commentReference", new XAttribute(Ns.W + "id", id))));

        _document.Touch();
        return comment;
    }

    /// <summary>Appends a <c>PAGE</c> field.</summary>
    public Paragraph AddPageNumber(string placeholder = "1") => AddField("PAGE", placeholder);

    /// <summary>Appends a <c>NUMPAGES</c> field.</summary>
    public Paragraph AddPageCount(string placeholder = "1") => AddField("NUMPAGES", placeholder);

    /// <summary>Appends an inline picture in a new run.</summary>
    public Run AddPicture(byte[] imageBytes, Length? width = null, Length? height = null,
        string? altText = null) =>
        AddRun().AddPicture(imageBytes, width, height, altText);

    /// <summary>Appends an inline picture from a file.</summary>
    public Run AddPicture(string path, Length? width = null, Length? height = null,
        string? altText = null) =>
        AddRun().AddPicture(path, width, height, altText);

    /// <summary>Makes the paragraph a bulleted or numbered list item.</summary>
    /// <param name="numberingId">The numbering definition to use.</param>
    /// <param name="level">The indent level, zero-based.</param>
    public Paragraph SetListItem(int numberingId, int level = 0)
    {
        var properties = XmlUtil.GetOrCreate(Element, Ns.W + "pPr", ParagraphOrder);

        var numbering = new XElement(Ns.W + "numPr",
            XmlUtil.ValElement(Ns.W + "ilvl", XmlUtil.Num(level)),
            XmlUtil.ValElement(Ns.W + "numId", XmlUtil.Num(numberingId)));

        XmlUtil.SetOrdered(properties, Ns.W + "numPr", numbering, ParagraphFormat.Order);

        // ListParagraph is what gives a list its indent and its contextual spacing. Word applies
        // it automatically in its UI, and a list without it has body-paragraph spacing between
        // every bullet.
        _document.Styles.EnsureListParagraphStyle();
        Format.StyleId ??= "ListParagraph";

        _document.Touch();
        return this;
    }

    /// <summary>Removes list numbering from the paragraph.</summary>
    public Paragraph ClearListItem()
    {
        Element.Element(Ns.W + "pPr")?.Elements(Ns.W + "numPr").Remove();
        _document.Touch();
        return this;
    }

    /// <summary>The list level, or <c>null</c> when the paragraph is not a list item.</summary>
    public int? ListLevel
    {
        get
        {
            var numbering = Element.Element(Ns.W + "pPr")?.Element(Ns.W + "numPr");
            var raw = numbering.Val(Ns.W + "ilvl");
            return raw is not null && int.TryParse(raw, out var level) ? level : numbering is null ? null : 0;
        }
    }

    /// <summary>Inserts an empty paragraph before this one.</summary>
    public Paragraph InsertParagraphBefore(string text = "", string? styleId = null)
    {
        var element = new XElement(Ns.W + "p");
        Element.AddBeforeSelf(element);

        var paragraph = new Paragraph(_document, element);

        if (styleId is not null)
        {
            paragraph.StyleId = styleId;
        }

        if (text.Length > 0)
        {
            paragraph.AddRun(text);
        }

        _document.Touch();
        return paragraph;
    }

    /// <summary>Inserts an empty paragraph after this one.</summary>
    public Paragraph InsertParagraphAfter(string text = "", string? styleId = null)
    {
        var element = new XElement(Ns.W + "p");
        Element.AddAfterSelf(element);

        var paragraph = new Paragraph(_document, element);

        if (styleId is not null)
        {
            paragraph.StyleId = styleId;
        }

        if (text.Length > 0)
        {
            paragraph.AddRun(text);
        }

        _document.Touch();
        return paragraph;
    }

    /// <summary>Removes the paragraph from the document.</summary>
    public void Remove()
    {
        Element.Remove();
        _document.Touch();
    }

    /// <summary>
    /// The heading level this paragraph's style implies, 1-9, or <c>null</c> for body text.
    /// </summary>
    public int? HeadingLevel
    {
        get
        {
            var style = StyleId;

            if (style is null)
            {
                return null;
            }

            // Word writes "Heading1" as the style id and "heading 1" as its name; documents from
            // other producers use either. Both are recognised.
            var normalized = style.Replace(" ", string.Empty);

            if (!normalized.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return int.TryParse(normalized.AsSpan(7), out var level) && level is >= 1 and <= 9
                ? level
                : null;
        }
    }

    public override string ToString() =>
        StyleId is null ? Text : $"[{StyleId}] {Text}";
}
