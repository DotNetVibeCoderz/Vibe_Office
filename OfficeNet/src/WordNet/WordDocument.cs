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
using WordNet.Revisions;
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

    /// <summary>Opens a password-encrypted .docx from a file.</summary>
    /// <remarks>
    /// The document stays encrypted with the same password when saved; set <c>Password</c> to
    /// <c>null</c> to remove it. An unencrypted file opens too, and stays unencrypted.
    /// </remarks>
    /// <exception cref="OfficeNetPasswordException">The password is wrong.</exception>
    public static WordDocument Open(string path, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(password);
        return FromPackage(OpcPackage.Open(path, password));
    }

    /// <summary>Opens a password-encrypted .docx from a stream.</summary>
    /// <exception cref="OfficeNetPasswordException">The password is wrong.</exception>
    public static WordDocument Open(Stream stream, string password)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(password);
        return FromPackage(OpcPackage.Open(stream, password));
    }

    /// <summary>Opens a password-encrypted .docx from bytes.</summary>
    /// <exception cref="OfficeNetPasswordException">The password is wrong.</exception>
    public static WordDocument Open(byte[] bytes, string password)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(password);
        using var stream = new MemoryStream(bytes, writable: false);
        return FromPackage(OpcPackage.Open(stream, password));
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

    /// <remarks>
    /// Called on open, and again after <see cref="AppendDocument"/> brings in content with ids of
    /// its own. It only ever moves the counters forward.
    /// </remarks>
    internal void SeedIdCounters()
    {
        // Continuing from the highest existing id is what keeps an edited document's ids unique.
        // Restarting at 1 collides with what the original producer wrote, and Word reports a
        // duplicate drawing id as unreadable content. Headers and footers count: docPr ids are
        // unique across the whole document, not per part.
        var drawingIds = RevisionCollection.Roots(this)
            .SelectMany(r => r.Descendants(Ns.Wp + "docPr"))
            .Select(e => e.IntAttr("id"))
            .DefaultIfEmpty(0)
            .Max();

        _nextDrawingId = Math.Max(_nextDrawingId, drawingIds + 1);

        var bookmarkIds = Root.Descendants(Ns.W + "bookmarkStart")
            .Select(e => e.IntAttr(Ns.W + "id"))
            .DefaultIfEmpty(-1)
            .Max();

        _nextBookmarkId = Math.Max(_nextBookmarkId, bookmarkIds + 1);
    }

    // ---- Tracked changes -----------------------------------------------------------------------

    /// <summary>
    /// The tracked changes in the body, headers, footers, notes and comments, as a snapshot.
    /// </summary>
    /// <remarks>
    /// Everything else in WordNet — <see cref="Paragraph.Runs"/>, <see cref="ExtractText"/>, the PDF
    /// and HTML exports — reads the <em>final</em> view: inserted text is shown and deleted text is
    /// not. To get the original instead, <see cref="RevisionCollection.RejectAll"/> on a copy.
    /// </remarks>
    public RevisionCollection Revisions => new(this);

    /// <summary>
    /// Whether Word records edits as tracked changes (<c>w:trackRevisions</c>).
    /// </summary>
    /// <remarks>
    /// This is the switch Word shows as Track Changes. It changes what Word does with later
    /// edits; it does not make edits made through this library tracked — use
    /// <see cref="Paragraph.AddTrackedRun"/> and <see cref="Run.TrackDeletion"/> for those.
    /// </remarks>
    public bool TrackRevisions
    {
        get => DocumentSettings.Find(this)?.Element(Ns.W + "trackRevisions") is { } element &&
               XmlUtil.OoxmlBool(element.Attr(Ns.W + "val")) != false;
        set => DocumentSettings.Set(this, Ns.W + "trackRevisions",
            value ? new XElement(Ns.W + "trackRevisions") : null);
    }

    /// <summary>A new revision wrapper or marker with a fresh id, author and date.</summary>
    internal XElement RevisionElement(XName name, string author, DateTimeOffset? date)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        _nextRevisionId = Math.Max(_nextRevisionId, RevisionCollection.NextId(this));

        // Seconds precision and an explicit Z: Word writes this form, and a fractional or
        // offset-bearing date is shown as "unknown date" by some versions.
        var when = (date ?? DateTimeOffset.UtcNow).ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        return new XElement(name,
            new XAttribute(Ns.W + "id", _nextRevisionId++),
            new XAttribute(Ns.W + "author", author),
            new XAttribute(Ns.W + "date", when));
    }

    private int _nextRevisionId;

    // ---- Merging -------------------------------------------------------------------------------

    /// <summary>
    /// Appends another document's content to the end of this one.
    /// </summary>
    /// <remarks>
    /// Everything the content refers to comes with it and is renumbered: styles, lists, images,
    /// hyperlinks, headers and footers, footnotes, endnotes, comments, bookmarks and revision ids.
    /// The source is not modified. See <see cref="Merging.DocumentMerger"/> for why each of those
    /// needs rewriting rather than copying.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="source"/> is this document.</exception>
    public void AppendDocument(WordDocument source, Merging.AppendOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (ReferenceEquals(source, this))
        {
            throw new ArgumentException(
                "A document cannot be appended to itself; open a second copy of it instead.",
                nameof(source));
        }

        new Merging.DocumentMerger(this, source, options ?? new Merging.AppendOptions()).Append();
    }

    /// <summary>Splits the document into one document per section.</summary>
    /// <remarks>
    /// Each part is a complete document with the source's styles, lists and the headers of its own
    /// section; images, notes and comments it no longer uses are removed. The source is unchanged.
    /// </remarks>
    public IReadOnlyList<WordDocument> SplitBySection() =>
        Merging.DocumentSplitter.Split(this, (block, index) =>
        {
            var previous = block.ElementsBeforeSelf().LastOrDefault();
            return previous is not null && Merging.DocumentSplitter.EndsSection(previous) is not null;
        });

    /// <summary>
    /// Splits the document before every heading of <paramref name="level"/> or higher (a smaller
    /// number), so each chapter becomes a document. Content before the first heading is a part of
    /// its own.
    /// </summary>
    public IReadOnlyList<WordDocument> SplitByHeading(int level = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 9);

        return Merging.DocumentSplitter.Split(this, (block, _) =>
            block.Name == Ns.W + "p" && new Paragraph(this, block).HeadingLevel is { } heading &&
            heading <= level);
    }

    /// <summary>
    /// Splits the document at every explicit page break (<c>w:br w:type="page"</c>), dropping the
    /// break itself. A break in the middle of a paragraph splits the paragraph.
    /// </summary>
    /// <remarks>
    /// Pages Word creates by itself, as text fills them, are not boundaries: where they fall is
    /// decided by Word's layout engine, and that is not something this library imitates.
    /// </remarks>
    public IReadOnlyList<WordDocument> SplitAtPageBreaks() =>
        Merging.DocumentSplitter.Split(this, (block, _) =>
            block.ElementsBeforeSelf().LastOrDefault() is { } previous &&
            Merging.DocumentSplitter.EndsWithPageBreak(previous),
            cutAtPageBreaks: true);

    // ---- Watermark -----------------------------------------------------------------------------

    /// <summary>
    /// Puts a large, pale, diagonal word — <c>DRAFT</c>, <c>RAHASIA</c> — behind the text of every
    /// page, replacing a watermark this library added before.
    /// </summary>
    /// <param name="text">The watermark text.</param>
    /// <param name="color">Defaults to a light grey that prints without hiding the text.</param>
    /// <remarks>
    /// The watermark lives in the headers, as it does when Word adds one, so it is on every page of
    /// every section; a section that inherits its header inherits the watermark with it. The PDF
    /// export draws it too, unless <see cref="Export.PdfExportOptions.Watermark"/> overrides it.
    /// </remarks>
    public void AddWatermark(string text, OfficeColor? color = null) =>
        WordNet.Drawing.Watermark.Add(this, text, color);

    /// <summary>The text of a watermark this library added, or <c>null</c>.</summary>
    public string? Watermark => WordNet.Drawing.Watermark.Find(this);

    /// <summary>Removes a watermark this library added. Returns false when there was none.</summary>
    public bool RemoveWatermark() => WordNet.Drawing.Watermark.Remove(this);

    // ---- Protection ----------------------------------------------------------------------------

    /// <summary>The editing restriction, or <c>null</c> when the document has none.</summary>
    public WordNet.Protection.DocumentProtection? Protection =>
        DocumentSettings.Find(this)?.Element(Ns.W + "documentProtection") is { } element
            ? new WordNet.Protection.DocumentProtection(element)
            : null;

    /// <summary>
    /// Restricts editing in Word. Prevention of mistakes, not security — see
    /// <see cref="WordNet.Protection.DocumentProtection"/>.
    /// </summary>
    /// <param name="restriction">What readers may still do.</param>
    /// <param name="password">
    /// Needed to lift the restriction in Word; <c>null</c> lets anyone lift it.
    /// </param>
    /// <remarks>
    /// <see cref="WordNet.Protection.DocumentEditRestriction.TrackedChanges"/> also turns on
    /// <see cref="TrackRevisions"/>: the restriction forbids turning tracking off, which means
    /// nothing if it was never on.
    /// </remarks>
    public void Protect(WordNet.Protection.DocumentEditRestriction restriction, string? password = null)
    {
        DocumentSettings.Set(this, Ns.W + "documentProtection",
            WordNet.Protection.DocumentProtection.Build(restriction, password));

        if (restriction == WordNet.Protection.DocumentEditRestriction.TrackedChanges)
        {
            TrackRevisions = true;
        }
    }

    /// <summary>Removes the editing restriction. The ranges opened for editing stay.</summary>
    public void Unprotect() => DocumentSettings.Set(this, Ns.W + "documentProtection", null);

    /// <summary>
    /// Lets <paramref name="editor"/> edit from the start of <paramref name="first"/> to the end of
    /// <paramref name="last"/> while the document is read-only.
    /// </summary>
    /// <param name="first">The first paragraph of the range.</param>
    /// <param name="last">The last paragraph; the same as <paramref name="first"/> for one.</param>
    /// <param name="editor">
    /// A user (<c>"ani@example.org"</c> or <c>"DOMAIN\ani"</c>); <c>null</c> lets everyone edit.
    /// </param>
    /// <remarks>
    /// An exception is a pair of markers, <c>w:permStart</c> and <c>w:permEnd</c>, sharing an id.
    /// They only mean anything under <see cref="WordNet.Protection.DocumentEditRestriction.ReadOnly"/>;
    /// Word ignores them otherwise.
    /// </remarks>
    public void AllowEditing(Paragraph first, Paragraph? last = null, string? editor = null)
    {
        ArgumentNullException.ThrowIfNull(first);
        last ??= first;

        var id = Math.Max(_nextRevisionId, RevisionCollection.NextId(this));
        _nextRevisionId = id + 1;

        var start = new XElement(Ns.W + "permStart", new XAttribute(Ns.W + "id", id));
        start.Add(editor is null
            ? new XAttribute(Ns.W + "edGrp", "everyone")
            : new XAttribute(Ns.W + "ed", editor));

        // The start goes right after w:pPr so the range covers the whole first paragraph.
        if (first.Element.Element(Ns.W + "pPr") is { } properties)
        {
            properties.AddAfterSelf(start);
        }
        else
        {
            first.Element.AddFirst(start);
        }

        last.Element.Add(new XElement(Ns.W + "permEnd", new XAttribute(Ns.W + "id", id)));
        Touch();
    }

    /// <summary>The ranges opened for editing, as (id, editor) pairs; editor <c>null</c> is everyone.</summary>
    public IReadOnlyList<(int Id, string? Editor)> EditableRanges =>
    [
        .. Root.Descendants(Ns.W + "permStart").Select(e =>
            (e.IntAttr(Ns.W + "id"), e.Attr(Ns.W + "ed") ?? (e.Attr(Ns.W + "edGrp") == "everyone" ? null : e.Attr(Ns.W + "edGrp")))),
    ];

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

    /// <summary>Appends a centred paragraph holding a native chart.</summary>
    public Drawing.WordChart AddChart(OfficeNet.Core.Charts.ChartData data, Length? width = null,
        Length? height = null, string? altText = null)
    {
        var paragraph = AddParagraph();
        paragraph.Alignment = ParagraphAlignment.Center;
        return paragraph.AddChart(data, width, height, altText);
    }

    /// <summary>The charts in the body, in document order.</summary>
    public IReadOnlyList<Drawing.WordChart> Charts => [.. Drawing.WordChart.Find(this)];

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

    private void EnableUpdateFieldsOnOpen() =>
        DocumentSettings.Ensure(this, XmlUtil.ValElement(Ns.W + "updateFields", "true"));

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

        // Without its own header and footer references the new section inherits the previous
        // section's, which is exactly Word's "Link to Previous". Copying the references instead
        // made both sections point at one part, so writing the new section's header rewrote every
        // section's — with no way to separate them. Now GetHeader() on the new section gives it
        // a header of its own, and leaving it alone keeps the inherited one.
        next.Elements(Ns.W + "headerReference").Remove();
        next.Elements(Ns.W + "footerReference").Remove();

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
        DocumentSettings.Ensure(this, new XElement(Ns.W + "evenAndOddHeaders"));
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

    /// <summary>
    /// The part whose XML holds <paramref name="element"/>: the document, or a header, footer,
    /// footnotes, endnotes or comments part.
    /// </summary>
    /// <remarks>
    /// A relationship id resolves only against the part it is used in. Images and hyperlinks
    /// added inside a header used to get their relationship on the document part, so the header's
    /// <c>r:embed</c> pointed at nothing — every picture in a header was broken, and no test looked.
    /// </remarks>
    internal OpcPart OwnerPartOf(XElement element)
    {
        var document = element.Document;

        if (document is null || document == _documentPart.Xml)
        {
            return _documentPart;
        }

        foreach (var type in (string[])
                 [
                     RelationshipTypes.Header, RelationshipTypes.Footer, RelationshipTypes.Footnotes,
                     RelationshipTypes.Endnotes, RelationshipTypes.Comments,
                 ])
        {
            foreach (var part in _documentPart.RelatedPartsByType(type))
            {
                if (part.IsXmlLoaded && part.Xml == document)
                {
                    return part;
                }
            }
        }

        return _documentPart;
    }

    internal (string RelationshipId, ImageInfo Info) AddImage(byte[] imageBytes) =>
        AddImage(_documentPart, imageBytes);

    /// <summary>
    /// Adds an image (deduplicated by content) and a relationship to it from <paramref name="owner"/>.
    /// </summary>
    /// <remarks>
    /// An <c>r:embed</c> resolves against the relationships of the part it sits in, so an image in
    /// a header or a footnote needs its relationship on that part, not on the document.
    /// </remarks>
    internal (string RelationshipId, ImageInfo Info) AddImage(OpcPart owner, byte[] imageBytes)
    {
        var part = AddImagePart(imageBytes, "/word/media", out var info);

        // Reuse an existing relationship to the same part rather than adding a second one. Two
        // relationships to one image are legal and make the document larger for no benefit.
        var existing = owner.RelationshipsByType(RelationshipTypes.Image)
            .FirstOrDefault(r => r.TargetMode == TargetMode.Internal && r.TargetPartName == part.Name);

        if (existing is not null)
        {
            return (existing.Id, info);
        }

        var relationship = owner.AddRelationship(part, RelationshipTypes.Image);
        return (relationship.Id, info);
    }

    internal string AddHyperlinkRelationship(string url) => AddHyperlinkRelationship(_documentPart, url);

    internal string AddHyperlinkRelationship(OpcPart owner, string url)
    {
        var existing = owner.RelationshipsByType(RelationshipTypes.Hyperlink)
            .FirstOrDefault(r => r.TargetMode == TargetMode.External && r.Target == url);

        return existing?.Id
               ?? owner.AddExternalRelationship(RelationshipTypes.Hyperlink, url).Id;
    }

    // ---- Text ----------------------------------------------------------------------------------

    /// <inheritdoc />
    public override string ExtractText()
    {
        var builder = new StringBuilder();

        // Walks the body itself rather than the Blocks sequence, so a paragraph costs neither a
        // wrapper nor a string of its own. Both were being paid ten thousand times over on a
        // ten-thousand-paragraph document, for a result that is one string at the end.
        foreach (var element in Body.Elements())
        {
            if (element.Name == Ns.W + "p")
            {
                new Paragraph(this, element).AppendTextTo(builder);

                // An explicit LF rather than AppendLine, which would emit Environment.NewLine and
                // make the same document extract differently on Windows and on Linux.
                builder.Append('\n');
            }
            else if (element.Name == Ns.W + "tbl")
            {
                builder.Append(new Table(this, element).ExtractText());
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

    /// <summary>
    /// Fills a template: <c>{{field}}</c>, <c>{{field:N0}}</c>, <c>{{customer.city}}</c>,
    /// repeating <c>{{#items}}…{{/items}}</c> and inverted <c>{{^items}}…{{/items}}</c> sections.
    /// </summary>
    /// <param name="data">
    /// An object, an anonymous object, a dictionary, or any nesting of them with lists.
    /// </param>
    /// <param name="options">Culture for numbers and dates, and what a missing field becomes.</param>
    /// <returns>How many fields were written.</returns>
    /// <remarks>
    /// The body, headers, footers and notes are all filled. A section repeats inline, as table
    /// rows, or as blocks, depending on where its two tags sit — see
    /// <see cref="Templates.TemplateEngine"/>. Unlike <see cref="MailMerge"/>, the formatting of
    /// every character outside a token is kept.
    /// </remarks>
    /// <exception cref="OfficeNetException">A section is not closed, or closed without opening.</exception>
    public int FillTemplate(object? data, Templates.TemplateOptions? options = null) =>
        new Templates.TemplateEngine(this, options ?? new Templates.TemplateOptions()).Fill(data);

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
