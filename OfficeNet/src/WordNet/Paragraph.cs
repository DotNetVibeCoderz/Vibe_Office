// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using WordNet.Drawing;
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

    /// <summary>
    /// <c>w:pPr</c> and everything that may follow it. Only the first entry's position matters —
    /// <c>w:pPr</c> must be the paragraph's first child — but every possible successor has to be
    /// named, or a paragraph holding only a tracked insertion gets its properties appended after it.
    /// </summary>
    internal static readonly XName[] ParagraphOrder =
    [
        Ns.W + "pPr", Ns.W + "r", Ns.W + "hyperlink", Ns.W + "bookmarkStart", Ns.W + "bookmarkEnd",
        Ns.W + "ins", Ns.W + "del", Ns.W + "moveFrom", Ns.W + "moveTo", Ns.W + "sdt",
        Ns.W + "smartTag", Ns.W + "fldSimple", Ns.W + "customXml", Ns.W + "commentRangeStart",
        Ns.W + "commentRangeEnd", Ns.W + "proofErr", Ns.W + "permStart", Ns.W + "permEnd",
        Ns.W + "moveFromRangeStart", Ns.W + "moveFromRangeEnd", Ns.W + "moveToRangeStart",
        Ns.W + "moveToRangeEnd", Ns.W + "bdo", Ns.W + "dir",
    ];

    /// <summary>The underlying <c>w:p</c> element.</summary>
    public XElement Element { get; }

    /// <summary>The paragraph's formatting.</summary>
    public ParagraphFormat Format { get; }

    /// <summary>The document this paragraph belongs to.</summary>
    public WordDocument Document => _document;

    /// <summary>
    /// The runs the paragraph shows, in document order, wherever they are nested.
    /// </summary>
    /// <remarks>
    /// Runs sit inside hyperlinks, tracked insertions, content controls and fields as well as
    /// directly in the paragraph, so its direct <c>w:r</c> children are not all of its runs. This is
    /// the final view: runs inside a tracked deletion are left out.
    /// <see cref="WordDocument.Revisions"/> reaches those.
    /// </remarks>
    public IReadOnlyList<Run> Runs =>
        [.. ParagraphContent.Runs(Element).Select(item => new Run(_document, item.Run))];

    /// <summary>
    /// Appends the paragraph's text to a builder.
    /// </summary>
    /// <remarks>
    /// Walks the elements directly rather than through <see cref="Runs"/>, which materialises a
    /// fresh list and a fresh wrapper per run on every access — 2.4 MB for a ten-thousand-paragraph
    /// document, and it was being paid once per paragraph on top of a string per paragraph.
    /// The set of elements it visits is the same one <see cref="Runs"/> reports, because both go
    /// through <see cref="ParagraphContent"/>.
    /// </remarks>
    internal void AppendTextTo(StringBuilder builder)
    {
        foreach (var (run, _) in ParagraphContent.Runs(Element))
        {
            Run.AppendText(run, builder);
        }
    }

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
            AppendTextTo(builder);
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

        var relationshipId = _document.AddHyperlinkRelationship(_document.OwnerPartOf(Element), url);

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

    /// <summary>
    /// Adds a text box anchored to this paragraph.
    /// </summary>
    /// <param name="text">The first line of text, or empty for an empty box.</param>
    /// <param name="width">The box's width.</param>
    /// <param name="height">The box's height.</param>
    /// <param name="wrap">How body text flows around it; <c>null</c> puts it in the line of text.</param>
    /// <param name="altText">Alternative text, for screen readers.</param>
    /// <returns>The shape, so more paragraphs and formatting can be added to it.</returns>
    /// <remarks>
    /// A text box is a rectangle with a text body, so this is <see cref="AddShape"/> with the
    /// geometry chosen for you. It comes with no fill and no outline — a plain box of text sitting
    /// over the page — because that is what a pull quote or a caption wants; set
    /// <see cref="Shape.FillColor"/> and <see cref="Shape.LineColor"/> for a bordered one.
    /// </remarks>
    public Shape AddTextBox(string text, Length width, Length height,
        TextWrap? wrap = TextWrap.Square, string? altText = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var shape = AddShape(ShapeGeometry.Rectangle, width, height, wrap, altText);

        // Marking the shape as a text box is what makes Word offer text editing on it rather than
        // treating it as a drawing that happens to contain words.
        shape.Wsp.Element(Ns.Wps + "cNvSpPr")!.SetAttributeValue("txBox", "1");
        shape.AddParagraph(text);

        return shape;
    }

    /// <summary>
    /// Adds a shape anchored to this paragraph.
    /// </summary>
    /// <param name="geometry">The outline.</param>
    /// <param name="width">The shape's width.</param>
    /// <param name="height">The shape's height.</param>
    /// <param name="wrap">How body text flows around it; <c>null</c> puts it in the line of text.</param>
    /// <param name="altText">Alternative text, for screen readers.</param>
    /// <remarks>
    /// The shape floats at the paragraph's own origin until <see cref="Shape.MoveTo"/> says
    /// otherwise. An inline shape — <paramref name="wrap"/> of <c>null</c> — has no position at all;
    /// it is laid out as if it were one very large character.
    /// </remarks>
    public Shape AddShape(ShapeGeometry geometry, Length width, Length height,
        TextWrap? wrap = TextWrap.Square, string? altText = null)
    {
        var run = new XElement(Ns.W + "r");
        Element.Add(run);

        _document.Touch();

        return Shape.Create(_document, run, Shape.PresetName(geometry), width, height, wrap, altText);
    }

    /// <summary>Every shape anchored to this paragraph, in document order.</summary>
    public IReadOnlyList<Shape> Shapes =>
    [
        .. Element.Descendants(Ns.W + "drawing")
            .Where(d => d.Descendants(Ns.Wps + "wsp").Any())
            .Select(d => new Shape(_document, d)),
    ];

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
    /// <summary>
    /// Adds a native chart in the line of text, 15 × 9 cm unless sized.
    /// </summary>
    /// <remarks>It is a real Word chart, editable in Word, and is drawn by the PDF export too.</remarks>
    public Drawing.WordChart AddChart(OfficeNet.Core.Charts.ChartData data, Length? width = null,
        Length? height = null, string? altText = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var run = new XElement(Ns.W + "r");
        Element.Add(run);

        var drawing = Drawing.WordChart.BuildDrawing(_document, Element, data,
            width ?? Length.FromCentimeters(15), height ?? Length.FromCentimeters(9), altText);
        run.Add(drawing);
        _document.Touch();

        var id = drawing.Descendants(Ns.C + "chart").First().Attr(Ns.R + "id")!;
        return new Drawing.WordChart(_document, drawing, _document.OwnerPartOf(Element).RelatedPart(id)!);
    }

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

    /// <summary>Appends text as a tracked insertion by <paramref name="author"/>.</summary>
    /// <returns>The run, inside its <c>w:ins</c>.</returns>
    public Run AddTrackedRun(string text, string author, DateTimeOffset? date = null)
    {
        var run = AddRun(text);
        run.TrackInsertion(author, date);
        return run;
    }

    /// <summary>
    /// Marks the whole paragraph as deleted: every run, and the paragraph mark itself.
    /// </summary>
    /// <remarks>
    /// Deleting the runs alone leaves an empty paragraph behind once the change is accepted. Word
    /// marks the paragraph mark too (<c>w:pPr/w:rPr/w:del</c>), and accepting that joins what is
    /// left with the next paragraph — so an accepted deletion of a whole paragraph leaves no gap.
    /// </remarks>
    public void TrackDeletion(string author, DateTimeOffset? date = null)
    {
        foreach (var run in Runs.Where(r => !r.IsDeleted).ToList())
        {
            run.TrackDeletion(author, date);
        }

        var markProperties = XmlUtil.GetOrCreate(
            XmlUtil.GetOrCreate(Element, Ns.W + "pPr", ParagraphOrder),
            Ns.W + "rPr", ParagraphFormat.Order);

        if (markProperties.Element(Ns.W + "del") is null)
        {
            // CT_ParaRPr opens with ins?, del?, moveFrom?, moveTo?: after any ins, before the rest.
            var marker = _document.RevisionElement(Ns.W + "del", author, date);

            if (markProperties.Element(Ns.W + "ins") is { } inserted)
            {
                inserted.AddAfterSelf(marker);
            }
            else
            {
                markProperties.AddFirst(marker);
            }
        }

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
