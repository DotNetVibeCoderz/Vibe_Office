// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Documents;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using WordNet.Drawing;
using WordNet.Notes;
using WordNet.Numbering;
using WordNet.Sections;
using WordNet.Styles;
using WordNet.Tables;

namespace WordNet;

/// <summary>
/// A Word document — the entry point of WordNet, and the direct analogue of python-docx's
/// <c>Document</c>.
/// </summary>
/// <remarks>
/// <para>
/// The document is a live view over the package's XML. Nothing is cached and flushed: a
/// <see cref="Paragraph"/> holds its own <c>w:p</c> element and edits it in place, so two handles
/// to the same paragraph cannot disagree, and a part this library does not understand travels
/// through a round trip byte for byte.
/// </para>
/// </remarks>
public sealed class WordDocument : OfficeDocument
{
    private static readonly OpcPartName DocumentPartName = "/word/document.xml";
    private static readonly OpcPartName StylesPartName = "/word/styles.xml";
    private static readonly OpcPartName SettingsPartName = "/word/settings.xml";
    private static readonly OpcPartName FontTablePartName = "/word/fontTable.xml";
    private static readonly OpcPartName NumberingPartName = "/word/numbering.xml";
    private static readonly OpcPartName FootnotesPartName = "/word/footnotes.xml";
    private static readonly OpcPartName EndnotesPartName = "/word/endnotes.xml";
    private static readonly OpcPartName CommentsPartName = "/word/comments.xml";

    private readonly OpcPart _documentPart;
    private StyleCollection? _styles;
    private NumberingDefinitions? _numbering;
    private NoteCollection? _footnotes;
    private NoteCollection? _endnotes;
    private CommentCollection? _comments;
    private int _nextDrawingId = 1;
    private int _nextBookmarkId;

    private WordDocument(OpcPackage package, OpcPart documentPart) : base(package)
    {
        _documentPart = documentPart;
    }

    /// <summary>The <c>word/document.xml</c> part.</summary>
    public OpcPart DocumentPart => _documentPart;

    internal XElement Root => _documentPart.Xml.Root
        ?? throw new OfficeNetException("word/document.xml is empty.");

    /// <summary>The <c>w:body</c> element.</summary>
    public XElement Body => Root.Element(Ns.W + "body")
        ?? throw new OfficeNetException("word/document.xml has no w:body.");

    internal void Touch() => Package.MarkDirty();

    internal int NextDrawingId() => _nextDrawingId++;

    internal int NextBookmarkId() => _nextBookmarkId++;

    // ---- Construction --------------------------------------------------------------------------

    /// <summary>Creates an empty document: one A4 section, the built-in styles, no content.</summary>
    public static WordDocument Create()
    {
        var package = OpcPackage.Create();

        var documentPart = package.AddXmlPart(DocumentPartName,
            ContentTypes.WordDocument, DefaultParts.Document());

        package.AddRootRelationship(documentPart, RelationshipTypes.OfficeDocument);

        var stylesPart = package.AddXmlPart(StylesPartName, ContentTypes.WordStyles, DefaultParts.Styles());
        documentPart.AddRelationship(stylesPart, RelationshipTypes.Styles);

        var settingsPart = package.AddXmlPart(SettingsPartName, ContentTypes.WordSettings, DefaultParts.Settings());
        documentPart.AddRelationship(settingsPart, RelationshipTypes.Settings);

        var fontsPart = package.AddXmlPart(FontTablePartName, ContentTypes.WordFontTable, DefaultParts.FontTable());
        documentPart.AddRelationship(fontsPart, RelationshipTypes.FontTable);

        var document = new WordDocument(package, documentPart);
        document.Properties.Created = DateTime.UtcNow;
        document.AppProperties.StampProducer();
        return document;
    }

    /// <summary>Opens a .docx from a file.</summary>
    /// <exception cref="OfficeNetException">The file is not a WordprocessingML package.</exception>
    public static WordDocument Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromPackage(OpcPackage.Open(path));
    }

    /// <summary>Opens a .docx from a stream.</summary>
    public static WordDocument Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return FromPackage(OpcPackage.Open(stream));
    }

    /// <summary>Opens a .docx from bytes.</summary>
    public static WordDocument Open(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        return FromPackage(OpcPackage.Open(stream));
    }

    /// <summary>
    /// Opens a document to use as a template: the content is cleared, everything else is kept.
    /// </summary>
    /// <remarks>
    /// This is how a corporate template becomes a generated report. The styles, theme, headers,
    /// footers, numbering and fonts all survive; only the body's blocks go, and the final
    /// <c>w:sectPr</c> is preserved because removing it would take the page setup with it.
    /// </remarks>
    public static WordDocument FromTemplate(string path)
    {
        var document = Open(path);

        var body = document.Body;
        var section = body.Elements(Ns.W + "sectPr").LastOrDefault();

        body.Elements().Where(e => e.Name != Ns.W + "sectPr").Remove();

        if (section is null)
        {
            body.Add(DefaultParts.DefaultSection());
        }

        document.Touch();
        return document;
    }

    private static WordDocument FromPackage(OpcPackage package)
    {
        var documentPart = package.MainDocumentPart
            ?? throw new OfficeNetException(
                "The package has no main document part. It is not a .docx — check whether it is " +
                "actually an .xlsx or .pptx, which use the same container.");

        var expected = new[]
        {
            ContentTypes.WordDocument, ContentTypes.WordDocumentMacroEnabled, ContentTypes.WordTemplate,
        };

        if (!expected.Contains(documentPart.ContentType))
        {
            throw new OfficeNetException(
                $"The main part's content type is '{documentPart.ContentType}', which is not " +
                "WordprocessingML. Open it with ExcelNet or PowerPointNet instead.");
        }

        // A strict-conformance document is structurally identical but uses different namespace
        // URIs, so every w:p lookup would miss. Normalising here means nothing downstream needs
        // to know which conformance class the file was written in.
        if (XmlUtil.NormalizeStrictNamespaces(documentPart.Xml))
        {
            package.MarkDirty();
        }

        var document = new WordDocument(package, documentPart);
        document.SeedIdCounters();
        return document;
    }

    private void SeedIdCounters()
    {
        // Continuing from the highest existing id is what keeps an edited document's ids unique.
        // Restarting at 1 collides with what the original producer wrote, and Word reports a
        // duplicate drawing id as unreadable content.
        var drawingIds = Root.Descendants(Ns.Wp + "docPr")
            .Select(e => e.IntAttr("id"))
            .DefaultIfEmpty(0)
            .Max();

        _nextDrawingId = drawingIds + 1;

        var bookmarkIds = Root.Descendants(Ns.W + "bookmarkStart")
            .Select(e => e.IntAttr(Ns.W + "id"))
            .DefaultIfEmpty(-1)
            .Max();

        _nextBookmarkId = bookmarkIds + 1;
    }

    // ---- Notes and comments --------------------------------------------------------------------

    /// <summary>
    /// The document's footnotes. The part is created the first time this is used.
    /// </summary>
    public NoteCollection Footnotes =>
        _footnotes ??= OpenNotes(FootnotesPartName, ContentTypes.WordFootnotes,
            RelationshipTypes.Footnotes, NoteKind.Footnote);

    /// <summary>The document's endnotes.</summary>
    public NoteCollection Endnotes =>
        _endnotes ??= OpenNotes(EndnotesPartName, ContentTypes.WordEndnotes,
            RelationshipTypes.Endnotes, NoteKind.Endnote);

    /// <summary>The document's review comments.</summary>
    public CommentCollection Comments
    {
        get
        {
            if (_comments is not null)
            {
                return _comments;
            }

            var part = Package.FindPart(CommentsPartName);

            if (part is null)
            {
                part = Package.AddXmlPart(CommentsPartName, ContentTypes.WordComments,
                    CommentCollection.CreatePart());

                _documentPart.AddRelationship(part, RelationshipTypes.Comments);
                EnsureNoteStyles();
            }

            var root = part.Xml.Root
                ?? throw new OfficeNetException($"{CommentsPartName} is empty.");

            return _comments = new CommentCollection(this, root);
        }
    }

    private NoteCollection OpenNotes(
        OpcPartName partName, string contentType, string relationshipType, NoteKind kind)
    {
        var part = Package.FindPart(partName);

        if (part is null)
        {
            part = Package.AddXmlPart(partName, contentType, NoteCollection.CreatePart(kind));
            _documentPart.AddRelationship(part, relationshipType);
            EnsureNoteStyles();
        }

        var root = part.Xml.Root ?? throw new OfficeNetException($"{partName} is empty.");
        return new NoteCollection(this, root, kind);
    }

    /// <summary>
    /// Adds the character and paragraph styles notes and comments refer to.
    /// </summary>
    /// <remarks>
    /// A note whose <c>rStyle</c> names a style the document does not define renders as body text —
    /// no superscript, no smaller size — which looks like a layout bug rather than a missing style.
    /// </remarks>
    private void EnsureNoteStyles()
    {
        foreach (var (id, name, superscript) in new[]
                 {
                     ("FootnoteReference", "footnote reference", true),
                     ("EndnoteReference", "endnote reference", true),
                     ("CommentReference", "annotation reference", false),
                 })
        {
            if (Styles.Contains(id))
            {
                continue;
            }

            var style = Styles.Add(id, name, StyleType.Character, basedOn: null);

            if (superscript)
            {
                style.RunFormat.VerticalAlignment = WordNet.VerticalAlignment.Superscript;
            }
            else
            {
                style.RunFormat.FontSize = Units.Pt(8);
            }
        }

        foreach (var (id, name) in new[]
                 {
                     ("FootnoteText", "footnote text"),
                     ("EndnoteText", "endnote text"),
                     ("CommentText", "annotation text"),
                 })
        {
            if (Styles.Contains(id))
            {
                continue;
            }

            var style = Styles.Add(id, name, StyleType.Paragraph, basedOn: "Normal");
            style.RunFormat.FontSize = Units.Pt(10);
        }
    }

    // ---- Content -------------------------------------------------------------------------------

    /// <summary>The body's paragraphs, excluding those inside tables.</summary>
    public IReadOnlyList<Paragraph> Paragraphs =>
        [.. Body.Elements(Ns.W + "p").Select(e => new Paragraph(this, e))];

    /// <summary>
    /// Every shape and text box in the body, in document order.
    /// </summary>
    /// <remarks>
    /// Pictures are excluded: a <c>w:drawing</c> can hold either, and this asks for the ones that
    /// hold a <c>wps:wsp</c>. Shapes nested inside another shape's text are included, because from a
    /// reader's point of view they are on the page like any other.
    /// </remarks>
    public IReadOnlyList<Shape> Shapes =>
    [
        .. Body.Descendants(Ns.W + "drawing")
            .Where(d => d.Descendants(Ns.Wps + "wsp").Any())
            .Select(d => new Shape(this, d)),
    ];

    /// <summary>The body's tables, excluding nested ones.</summary>
    public IReadOnlyList<Table> Tables =>
        [.. Body.Elements(Ns.W + "tbl").Select(e => new Table(this, e))];

    /// <summary>
    /// Every paragraph in the document including those inside tables, in document order.
    /// </summary>
    public IEnumerable<Paragraph> AllParagraphs =>
        Body.Descendants(Ns.W + "p").Select(e => new Paragraph(this, e));

    /// <summary>The body's blocks — paragraphs and tables — in document order.</summary>
    public IEnumerable<object> Blocks
    {
        get
        {
            foreach (var element in Body.Elements())
            {
                if (element.Name == Ns.W + "p")
                {
                    yield return new Paragraph(this, element);
                }
                else if (element.Name == Ns.W + "tbl")
                {
                    yield return new Table(this, element);
                }
            }
        }
    }

    /// <summary>Appends a paragraph.</summary>
    public Paragraph AddParagraph(string text = "", string? styleId = null)
    {
        var element = new XElement(Ns.W + "p");
        InsertBlock(element);

        var paragraph = new Paragraph(this, element);

        if (styleId is not null)
        {
            paragraph.StyleId = styleId;
        }

        if (text.Length > 0)
        {
            paragraph.AddRun(text);
        }

        Touch();
        return paragraph;
    }

    /// <summary>Appends a heading.</summary>
    /// <param name="text">The heading text.</param>
    /// <param name="level">1 through 9; 0 uses the Title style.</param>
    public Paragraph AddHeading(string text, int level = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 9);

        return AddParagraph(text, level == 0 ? "Title" : $"Heading{level}");
    }

    /// <summary>Appends a table.</summary>
    public Table AddTable(int rows, int columns, string? styleId = "TableGrid")
    {
        var table = Table.Create(this, rows, columns);
        InsertBlock(table.Element);

        if (styleId is not null)
        {
            table.StyleId = styleId;
        }

        // A table must be followed by a paragraph. Word inserts one itself when opening a document
        // that ends with a table, but a table immediately followed by w:sectPr is malformed.
        InsertBlock(new XElement(Ns.W + "p"));

        Touch();
        return table;
    }

    /// <summary>Appends a table filled from a rectangular sequence.</summary>
    public Table AddTable(IEnumerable<IEnumerable<string>> data, bool firstRowIsHeader = true,
        string? styleId = "TableGrid")
    {
        ArgumentNullException.ThrowIfNull(data);

        var rows = data.Select(r => r.ToList()).ToList();

        if (rows.Count == 0)
        {
            throw new ArgumentException("The data has no rows.", nameof(data));
        }

        var columns = rows.Max(r => r.Count);
        var table = AddTable(rows.Count, columns, styleId);
        table.SetData(rows, firstRowIsHeader);
        return table;
    }

    /// <summary>Appends a paragraph containing only a picture.</summary>
    public Paragraph AddPicture(byte[] imageBytes, Length? width = null, Length? height = null,
        string? altText = null, ParagraphAlignment alignment = ParagraphAlignment.Center)
    {
        var paragraph = AddParagraph();
        paragraph.Alignment = alignment;
        paragraph.AddPicture(imageBytes, width, height, altText);
        return paragraph;
    }

    /// <summary>Appends a paragraph containing only a picture, read from a file.</summary>
    public Paragraph AddPicture(string path, Length? width = null, Length? height = null,
        string? altText = null, ParagraphAlignment alignment = ParagraphAlignment.Center)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return AddPicture(File.ReadAllBytes(path), width, height, altText, alignment);
    }

    /// <summary>Appends a bulleted or numbered list.</summary>
    /// <param name="items">The item texts.</param>
    /// <param name="numbered">True for a numbered list, false for bullets.</param>
    /// <param name="level">The indent level, zero-based.</param>
    public IReadOnlyList<Paragraph> AddList(IEnumerable<string> items, bool numbered = false, int level = 0)
    {
        ArgumentNullException.ThrowIfNull(items);

        var numberingId = numbered
            ? Numbering.AddNumberedList()
            : Numbering.AddBulletList();

        var result = new List<Paragraph>();

        foreach (var item in items)
        {
            var paragraph = AddParagraph(item);
            paragraph.SetListItem(numberingId, level);
            result.Add(paragraph);
        }

        return result;
    }

    /// <summary>Appends a page break in its own paragraph.</summary>
    public Paragraph AddPageBreak() => AddParagraph().AddPageBreak();

    /// <summary>
    /// Appends a table-of-contents field.
    /// </summary>
    /// <param name="levels">The heading levels to include.</param>
    /// <param name="title">A heading placed above the field; <c>null</c> adds none.</param>
    /// <remarks>
    /// Word does not build the entries until the field is updated — by pressing F9, or by opening
    /// the document with "update fields on open" enabled. The placeholder text says so, because a
    /// blank space where a contents page should be reads as a bug rather than as a pending action.
    /// </remarks>
    public Paragraph AddTableOfContents(int levels = 3, string? title = "Table of Contents")
    {
        if (title is not null)
        {
            AddHeading(title, 1);
        }

        var paragraph = AddParagraph();
        paragraph.AddField(
            $"TOC \\o \"1-{levels}\" \\h \\z \\u",
            "Right-click and choose Update Field to build the table of contents.");

        // Word only offers to update fields on open when the setting is present in settings.xml.
        EnableUpdateFieldsOnOpen();

        return paragraph;
    }

    private void EnableUpdateFieldsOnOpen()
    {
        var settingsPart = _documentPart.RelatedPartByType(RelationshipTypes.Settings);

        if (settingsPart is null)
        {
            settingsPart = Package.AddXmlPart(SettingsPartName, ContentTypes.WordSettings,
                DefaultParts.Settings());
            _documentPart.AddRelationship(settingsPart, RelationshipTypes.Settings);
        }

        var root = settingsPart.Xml.Root;
        if (root is null || root.Element(Ns.W + "updateFields") is not null)
        {
            return;
        }

        // w:updateFields belongs near the top of w:settings; Word is tolerant here, unlike with
        // w:rPr, but keeping it first matches what Word itself writes.
        root.AddFirst(XmlUtil.ValElement(Ns.W + "updateFields", "true"));
        Touch();
    }

    // The block appended most recently, used to make repeated appends O(1). See InsertBlock.
    private XNode? _lastInsertedBlock;

    private void InsertBlock(XElement element)
    {
        // The body's final w:sectPr must stay last. Appending past it silently moves the page
        // setup into the middle of the document, where Word treats it as a section break.
        //
        // The obvious way to honour that — find the sectPr and AddBeforeSelf — is O(number of
        // blocks) per insert, because LINQ to XML stores children as a singly linked list and
        // inserting *before* a node means walking from the front to find its predecessor. That
        // made building a document O(N²): appending 1,000 paragraphs cost 2 ms into an empty body
        // and 52 ms into one that already held 15,000.
        //
        // AddAfterSelf needs only the node's own `next` pointer, so remembering the block appended
        // last turns the common case — append, append, append — into O(1). The guards re-verify the
        // shape each time, so anything else touching the body simply falls back to the slow path.
        var body = Body;

        if (_lastInsertedBlock is { } previous &&
            ReferenceEquals(previous.Parent, body) &&
            IsBeforeTheSectionProperties(previous.NextNode))
        {
            previous.AddAfterSelf(element);
            _lastInsertedBlock = element;
            return;
        }

        if (body.LastNode is XElement last && last.Name == Ns.W + "sectPr")
        {
            last.AddBeforeSelf(element);
        }
        else
        {
            body.Add(element);
        }

        _lastInsertedBlock = element;

        // True when inserting after the node that owns `next` still leaves w:sectPr last: either
        // the node is already the final child, or the only thing after it is the section properties.
        static bool IsBeforeTheSectionProperties(XNode? next) =>
            next is null ||
            (next is XElement element && element.Name == Ns.W + "sectPr" && element.NextNode is null);
    }

    /// <summary>Removes every block from the body, keeping the section properties.</summary>
    public void ClearContent()
    {
        Body.Elements().Where(e => e.Name != Ns.W + "sectPr").Remove();
        Touch();
    }

    // ---- Styles, numbering, sections -----------------------------------------------------------

    /// <summary>The document's style definitions.</summary>
    public StyleCollection Styles
    {
        get
        {
            if (_styles is not null)
            {
                return _styles;
            }

            var part = _documentPart.RelatedPartByType(RelationshipTypes.Styles);

            if (part is null)
            {
                part = Package.AddXmlPart(StylesPartName, ContentTypes.WordStyles, DefaultParts.Styles());
                _documentPart.AddRelationship(part, RelationshipTypes.Styles);
            }

            var root = part.Xml.Root
                ?? throw new OfficeNetException("word/styles.xml is empty.");

            _styles = new StyleCollection(this, root);
            return _styles;
        }
    }

    /// <summary>The document's list definitions.</summary>
    public NumberingDefinitions Numbering
    {
        get
        {
            if (_numbering is not null)
            {
                return _numbering;
            }

            var part = _documentPart.RelatedPartByType(RelationshipTypes.Numbering);

            if (part is null)
            {
                part = Package.AddXmlPart(NumberingPartName, ContentTypes.WordNumbering,
                    DefaultParts.Numbering());
                _documentPart.AddRelationship(part, RelationshipTypes.Numbering);
            }

            var root = part.Xml.Root
                ?? throw new OfficeNetException("word/numbering.xml is empty.");

            _numbering = new NumberingDefinitions(this, root);
            return _numbering;
        }
    }

    /// <summary>
    /// The document's sections, in order.
    /// </summary>
    public IReadOnlyList<Section> Sections
    {
        get
        {
            var result = new List<Section>();

            // Every section but the last has its properties inside a paragraph's w:pPr; the last
            // has them as the body's final child.
            foreach (var properties in Body.Descendants(Ns.W + "sectPr"))
            {
                result.Add(new Section(this, properties));
            }

            if (result.Count == 0)
            {
                var created = DefaultParts.DefaultSection();
                Body.Add(created);
                Touch();
                result.Add(new Section(this, created));
            }

            return result;
        }
    }

    /// <summary>The last (usually only) section — the one holding the document's page setup.</summary>
    public Section Section => Sections[^1];

    /// <summary>
    /// Starts a new section, so the content after it can use a different page setup.
    /// </summary>
    /// <returns>The new final section, which the following content belongs to.</returns>
    public Section AddSection(SectionStart start = SectionStart.NextPage)
    {
        var current = Body.Elements(Ns.W + "sectPr").LastOrDefault()
            ?? DefaultParts.DefaultSection();

        // The current final properties move into a new paragraph, becoming the *previous* section's
        // definition, and a copy stays at the end as the new final section. Appending a second
        // sectPr to the body instead produces a document with one section and stray markup.
        current.Remove();

        var breakParagraph = new XElement(Ns.W + "p",
            new XElement(Ns.W + "pPr", current));

        Body.Add(breakParagraph);

        var next = new XElement(current);
        Body.Add(next);

        var section = new Section(this, next) { Start = start };
        Touch();
        return section;
    }

    // ---- Headers and footers -------------------------------------------------------------------

    internal HeaderFooter GetOrCreateHeaderFooter(Section section, HeaderFooterKind kind, bool isHeader)
    {
        var referenceName = isHeader ? Ns.W + "headerReference" : Ns.W + "footerReference";
        var kindName = Section.KindName(kind);

        var existing = section.Properties.Elements(referenceName)
            .FirstOrDefault(e => (e.Attr(Ns.W + "type") ?? "default") == kindName);

        if (existing is not null)
        {
            var id = existing.Attr(Ns.R + "id");
            var part = id is null ? null : _documentPart.RelatedPart(id);

            if (part is not null)
            {
                var root = part.Xml.Root
                    ?? throw new OfficeNetException($"{part.Name} is empty.");

                return new HeaderFooter(this, part, root, isHeader);
            }

            // A reference whose relationship is gone is a dangling pointer; replacing it is the
            // repair.
            existing.Remove();
        }

        var partName = Package.NextPartName(isHeader ? "/word/header{0}.xml" : "/word/footer{0}.xml");
        var contentType = isHeader ? ContentTypes.WordHeader : ContentTypes.WordFooter;
        var relationshipType = isHeader ? RelationshipTypes.Header : RelationshipTypes.Footer;

        var newPart = Package.AddXmlPart(partName, contentType, DefaultParts.HeaderOrFooter(isHeader));
        var relationship = _documentPart.AddRelationship(newPart, relationshipType);

        var reference = new XElement(referenceName,
            new XAttribute(Ns.W + "type", kindName),
            new XAttribute(Ns.R + "id", relationship.Id));

        // Header and footer references must be the first children of w:sectPr, headers before
        // footers. Word rejects a sectPr whose pgSz precedes its headerReference.
        var firstOther = section.Properties.Elements()
            .FirstOrDefault(e => e.Name != Ns.W + "headerReference" &&
                                 (isHeader || e.Name != Ns.W + "footerReference"));

        if (firstOther is not null)
        {
            firstOther.AddBeforeSelf(reference);
        }
        else
        {
            section.Properties.AddFirst(reference);
        }

        if (kind == HeaderFooterKind.First)
        {
            section.DifferentFirstPage = true;
        }

        if (kind == HeaderFooterKind.Even)
        {
            EnableEvenAndOddHeaders();
        }

        Touch();

        var newRoot = newPart.Xml.Root
            ?? throw new OfficeNetException($"{newPart.Name} is empty.");

        return new HeaderFooter(this, newPart, newRoot, isHeader);
    }

    private void EnableEvenAndOddHeaders()
    {
        // Like w:titlePg for first pages, this is a document-wide setting rather than a section
        // one, and an even-page header without it never appears.
        var settingsPart = _documentPart.RelatedPartByType(RelationshipTypes.Settings);

        if (settingsPart is null)
        {
            settingsPart = Package.AddXmlPart(SettingsPartName, ContentTypes.WordSettings,
                DefaultParts.Settings());
            _documentPart.AddRelationship(settingsPart, RelationshipTypes.Settings);
        }

        var root = settingsPart.Xml.Root;
        if (root is null || root.Element(Ns.W + "evenAndOddHeaders") is not null)
        {
            return;
        }

        root.AddFirst(new XElement(Ns.W + "evenAndOddHeaders"));
        Touch();
    }

    internal void RemoveHeaderFooter(Section section, HeaderFooterKind kind, bool isHeader)
    {
        var referenceName = isHeader ? Ns.W + "headerReference" : Ns.W + "footerReference";
        var kindName = Section.KindName(kind);

        var reference = section.Properties.Elements(referenceName)
            .FirstOrDefault(e => (e.Attr(Ns.W + "type") ?? "default") == kindName);

        if (reference is null)
        {
            return;
        }

        var id = reference.Attr(Ns.R + "id");
        reference.Remove();

        if (id is null)
        {
            Touch();
            return;
        }

        var part = _documentPart.RelatedPart(id);
        _documentPart.RemoveRelationship(id);

        // The part is shared when several sections reference it, so it is only removed once no
        // reference remains.
        if (part is not null && !IsHeaderFooterReferenced(part))
        {
            Package.RemovePart(part.Name);
        }

        Touch();
    }

    private bool IsHeaderFooterReferenced(OpcPart part)
    {
        foreach (var reference in Body.Descendants()
                     .Where(e => e.Name == Ns.W + "headerReference" || e.Name == Ns.W + "footerReference"))
        {
            var id = reference.Attr(Ns.R + "id");
            if (id is not null && _documentPart.RelatedPart(id)?.Name == part.Name)
            {
                return true;
            }
        }

        return false;
    }

    // ---- Media and relationships ---------------------------------------------------------------

    internal (string RelationshipId, ImageInfo Info) AddImage(byte[] imageBytes)
    {
        var part = AddImagePart(imageBytes, "/word/media", out var info);

        // Reuse an existing relationship to the same part rather than adding a second one. Two
        // relationships to one image are legal and make the document larger for no benefit.
        var existing = _documentPart.RelationshipsByType(RelationshipTypes.Image)
            .FirstOrDefault(r => r.TargetMode == TargetMode.Internal && r.TargetPartName == part.Name);

        if (existing is not null)
        {
            return (existing.Id, info);
        }

        var relationship = _documentPart.AddRelationship(part, RelationshipTypes.Image);
        return (relationship.Id, info);
    }

    internal string AddHyperlinkRelationship(string url)
    {
        var existing = _documentPart.RelationshipsByType(RelationshipTypes.Hyperlink)
            .FirstOrDefault(r => r.TargetMode == TargetMode.External && r.Target == url);

        return existing?.Id
               ?? _documentPart.AddExternalRelationship(RelationshipTypes.Hyperlink, url).Id;
    }

    // ---- Text ----------------------------------------------------------------------------------

    /// <inheritdoc />
    public override string ExtractText()
    {
        var builder = new StringBuilder();

        foreach (var block in Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    // An explicit LF rather than AppendLine, which would emit Environment.NewLine
                    // and make the same document extract differently on Windows and on Linux.
                    builder.Append(paragraph.Text).Append('\n');
                    break;

                case Table table:
                    builder.Append(table.ExtractText());
                    break;
            }
        }

        return builder.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>Replaces every occurrence of a string throughout the document.</summary>
    /// <returns>How many replacements were made.</returns>
    /// <remarks>
    /// Replacement is per run, which is what makes it fast and what limits it: Word splits text
    /// across runs freely, so a phrase interrupted by a spelling mark or a formatting change is not
    /// found. <see cref="ReplaceTextAcrossRuns"/> handles that case at the cost of rewriting the
    /// paragraph's runs.
    /// </remarks>
    public int ReplaceText(string search, string replacement, StringComparison comparison =
        StringComparison.Ordinal)
    {
        ArgumentException.ThrowIfNullOrEmpty(search);
        ArgumentNullException.ThrowIfNull(replacement);

        var count = 0;

        foreach (var element in Body.Descendants(Ns.W + "t").ToList())
        {
            var text = element.Value;

            if (!text.Contains(search, comparison))
            {
                continue;
            }

            var replaced = text.Replace(search, replacement, comparison);

            // Count occurrences rather than assuming one per run.
            var index = 0;
            while ((index = text.IndexOf(search, index, comparison)) >= 0)
            {
                count++;
                index += search.Length;
            }

            element.Value = replaced;

            if (replaced.Length > 0 && (char.IsWhiteSpace(replaced[0]) || char.IsWhiteSpace(replaced[^1])))
            {
                element.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }
        }

        if (count > 0)
        {
            Touch();
        }

        return count;
    }

    /// <summary>
    /// Replaces text that may be split across several runs, at the cost of collapsing each
    /// affected paragraph's runs into one.
    /// </summary>
    /// <remarks>
    /// Use this for mail-merge placeholders. A token like <c>{{name}}</c> typed into Word is
    /// routinely stored as <c>{{</c>, <c>name</c>, <c>}}</c> in three runs — because Word split
    /// them when the spell checker flagged the word — and per-run replacement never sees it.
    /// </remarks>
    public int ReplaceTextAcrossRuns(string search, string replacement,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentException.ThrowIfNullOrEmpty(search);
        ArgumentNullException.ThrowIfNull(replacement);

        var count = 0;

        foreach (var paragraph in AllParagraphs.ToList())
        {
            var text = paragraph.Text;

            if (!text.Contains(search, comparison))
            {
                continue;
            }

            var index = 0;
            while ((index = text.IndexOf(search, index, comparison)) >= 0)
            {
                count++;
                index += search.Length;
            }

            // The first run's formatting is kept for the whole paragraph: it is the best available
            // guess, and it is what a placeholder paragraph almost always wants.
            var firstRun = paragraph.Runs.FirstOrDefault();
            var formatting = firstRun?.Element.Element(Ns.W + "rPr");

            paragraph.Text = text.Replace(search, replacement, comparison);

            if (formatting is not null)
            {
                var newRun = paragraph.Runs.FirstOrDefault();
                newRun?.Element.AddFirst(new XElement(formatting));
            }
        }

        if (count > 0)
        {
            Touch();
        }

        return count;
    }

    /// <summary>
    /// Fills <c>{{placeholder}}</c> tokens from a dictionary, including tokens split across runs.
    /// </summary>
    public int MailMerge(IReadOnlyDictionary<string, string> values, string openToken = "{{",
        string closeToken = "}}")
    {
        ArgumentNullException.ThrowIfNull(values);

        var count = 0;

        foreach (var (key, value) in values)
        {
            count += ReplaceTextAcrossRuns($"{openToken}{key}{closeToken}", value);
        }

        return count;
    }

    /// <summary>Counts the document's words, the way Word's status bar does.</summary>
    public int WordCount =>
        ExtractText().Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

    // ---- Export --------------------------------------------------------------------------------

    /// <summary>
    /// Renders the document to PDF.
    /// </summary>
    /// <param name="path">Where to write the PDF.</param>
    /// <param name="options">Layout options; the defaults suit a text document.</param>
    public void SaveAsPdf(string path, Export.PdfExportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var pdf = Export.WordToPdf.Convert(this, options);
        pdf.Save(path);
    }

    /// <summary>Renders the document to PDF and writes it to a stream.</summary>
    public void SaveAsPdf(Stream stream, Export.PdfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var pdf = Export.WordToPdf.Convert(this, options);
        pdf.Save(stream);
    }

    /// <summary>Renders the document to a PDF document for further processing.</summary>
    public PdfNet.Document.PdfDocument ToPdf(Export.PdfExportOptions? options = null) =>
        Export.WordToPdf.Convert(this, options);

    /// <inheritdoc />
    protected override void FlushToPackage()
    {
        // Every edit goes straight into the XML tree, so there is no model to write back. The
        // counts are refreshed because they are the only cached values the document holds.
        AppProperties.Paragraphs = Body.Descendants(Ns.W + "p").Count();
        AppProperties.Words = WordCount;

        EnsureHeadersAndFootersNotEmpty();
    }

    /// <summary>
    /// Gives every header and footer part at least one block-level child.
    /// </summary>
    /// <remarks>
    /// The parts are created empty so a caller's first paragraph is the first line rather than the
    /// second, but WordprocessingML requires at least one block and Word reports an empty
    /// <c>w:hdr</c> as unreadable content. Restoring the invariant at save time gets both.
    /// </remarks>
    private void EnsureHeadersAndFootersNotEmpty()
    {
        foreach (var part in Package.Parts)
        {
            if (part.ContentType != ContentTypes.WordHeader &&
                part.ContentType != ContentTypes.WordFooter)
            {
                continue;
            }

            var root = part.Xml.Root;

            if (root is not null && !root.Elements().Any())
            {
                root.Add(new XElement(Ns.W + "p"));
            }
        }
    }

    public override string ToString() =>
        $"WordDocument({Paragraphs.Count} paragraphs, {Tables.Count} tables)";
}
