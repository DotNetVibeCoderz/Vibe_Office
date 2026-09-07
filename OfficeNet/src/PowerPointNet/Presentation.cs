// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Documents;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using PowerPointNet.Layouts;

namespace PowerPointNet;

/// <summary>
/// A presentation — the entry point of PowerPointNet, and the analogue of python-pptx's
/// <c>Presentation</c>.
/// </summary>
/// <remarks>
/// <para>
/// A .pptx has a far higher structural floor than a document or a workbook: a slide is only legal
/// once a master, a layout and a theme exist for it to inherit from. <see cref="Create"/> builds
/// all of them, which is why a "blank" presentation is a dozen parts rather than two.
/// </para>
/// <para>
/// Slide order is not the order of the parts. It is the order of <c>p:sldId</c> entries in
/// <c>presentation.xml</c>, which is what makes reordering a deck an edit to one list rather than a
/// rename of every part.
/// </para>
/// </remarks>
public sealed class Presentation : OfficeDocument
{
    private static readonly OpcPartName PresentationPartName = "/ppt/presentation.xml";

    private readonly OpcPart _presentationPart;
    private readonly List<Slide> _slides = [];
    private readonly List<SlideLayout> _layouts = [];
    private uint _nextSlideId = 256;

    private Presentation(OpcPackage package, OpcPart presentationPart) : base(package)
    {
        _presentationPart = presentationPart;
    }

    /// <summary>The <c>ppt/presentation.xml</c> part.</summary>
    public OpcPart PresentationPart => _presentationPart;

    private XElement Root => _presentationPart.Xml.Root
        ?? throw new OfficeNetException("ppt/presentation.xml is empty.");

    internal void Touch() => Package.MarkDirty();

    /// <summary>The slides, in presentation order.</summary>
    public IReadOnlyList<Slide> Slides => _slides;

    /// <summary>The slide layouts available to the deck.</summary>
    public IReadOnlyList<SlideLayout> Layouts => _layouts;

    /// <summary>The slide master.</summary>
    public SlideMaster? Master { get; private set; }

    /// <summary>The number of slides.</summary>
    public int SlideCount => _slides.Count;

    /// <summary>A slide by zero-based index.</summary>
    public Slide this[int index] => _slides[index];

    // ---- Slide size ------------------------------------------------------------------------------

    /// <summary>The slide width.</summary>
    public Length SlideWidth
    {
        get => Length.FromEmu(Root.Element(Ns.P + "sldSz").LongAttr("cx"));
        set
        {
            Root.Element(Ns.P + "sldSz")?.SetAttributeValue("cx", value.Emu);
            Touch();
        }
    }

    /// <summary>The slide height.</summary>
    public Length SlideHeight
    {
        get => Length.FromEmu(Root.Element(Ns.P + "sldSz").LongAttr("cy"));
        set
        {
            Root.Element(Ns.P + "sldSz")?.SetAttributeValue("cy", value.Emu);
            Touch();
        }
    }

    /// <summary>Sets the slide size to 16:9 widescreen (13.333 x 7.5 in).</summary>
    public Presentation UseWidescreen()
    {
        SlideWidth = Units.Inches(13.333);
        SlideHeight = Units.Inches(7.5);
        return this;
    }

    /// <summary>Sets the slide size to 4:3 (10 x 7.5 in).</summary>
    public Presentation UseStandard()
    {
        SlideWidth = Units.Inches(10);
        SlideHeight = Units.Inches(7.5);
        return this;
    }

    // ---- Construction ----------------------------------------------------------------------------

    /// <summary>Creates an empty widescreen presentation with a master, six layouts and a theme.</summary>
    public static Presentation Create()
    {
        var width = PptDefaultParts.DefaultSlideWidth;
        var height = PptDefaultParts.DefaultSlideHeight;

        var package = OpcPackage.Create();

        var presentationPart = package.AddXmlPart(PresentationPartName,
            ContentTypes.PowerPointPresentation, PptDefaultParts.Presentation(width, height));

        package.AddRootRelationship(presentationPart, RelationshipTypes.OfficeDocument);

        var presentation = new Presentation(package, presentationPart);

        // The theme comes first: the master references it, and a master whose theme relationship
        // dangles makes PowerPoint refuse the file.
        var themePart = package.AddXmlPart("/ppt/theme/theme1.xml", ContentTypes.Theme,
            PptDefaultParts.Theme());

        var masterPart = package.AddXmlPart("/ppt/slideMasters/slideMaster1.xml",
            ContentTypes.PowerPointSlideMaster, PptDefaultParts.SlideMaster());

        masterPart.AddRelationship(themePart, RelationshipTypes.Theme);

        var masterRelationship = presentationPart.AddRelationship(masterPart,
            RelationshipTypes.SlideMaster);

        presentationPart.Xml.Root!.Element(Ns.P + "sldMasterIdLst")?.Add(
            new XElement(Ns.P + "sldMasterId",
                new XAttribute("id", "2147483648"),
                new XAttribute(Ns.R + "id", masterRelationship.Id)));

        var layoutIds = masterPart.Xml.Root!.Element(Ns.P + "sldLayoutIdLst")!;
        uint layoutId = 2147483649;

        for (var i = 0; i < PptDefaultParts.LayoutKinds.Length; i++)
        {
            var (type, name) = PptDefaultParts.LayoutKinds[i];

            var layoutPart = package.AddXmlPart($"/ppt/slideLayouts/slideLayout{i + 1}.xml",
                ContentTypes.PowerPointSlideLayout,
                PptDefaultParts.SlideLayout(type, name, width, height));

            // A layout points back at its master and the master points forward at the layout.
            // Both directions are required.
            layoutPart.AddRelationship(masterPart, RelationshipTypes.SlideMaster);
            var relationship = masterPart.AddRelationship(layoutPart, RelationshipTypes.SlideLayout);

            layoutIds.Add(new XElement(Ns.P + "sldLayoutId",
                new XAttribute("id", layoutId++),
                new XAttribute(Ns.R + "id", relationship.Id)));

            presentation._layouts.Add(new SlideLayout(presentation, layoutPart, layoutPart.Xml.Root!));
        }

        presentation.Master = new SlideMaster(presentation, masterPart, masterPart.Xml.Root!);

        var presProps = package.AddXmlPart("/ppt/presProps.xml", ContentTypes.PowerPointPresProps,
            PptDefaultParts.PresentationProperties());
        presentationPart.AddRelationship(presProps, RelationshipTypes.PresProps);

        var viewProps = package.AddXmlPart("/ppt/viewProps.xml", ContentTypes.PowerPointViewProps,
            PptDefaultParts.ViewProperties());
        presentationPart.AddRelationship(viewProps, RelationshipTypes.ViewProps);

        var tableStyles = package.AddXmlPart("/ppt/tableStyles.xml",
            ContentTypes.PowerPointTableStyles, PptDefaultParts.TableStyles());
        presentationPart.AddRelationship(tableStyles, RelationshipTypes.TableStyles);

        presentationPart.AddRelationship(themePart, RelationshipTypes.Theme);

        presentation.Properties.Created = DateTime.UtcNow;
        presentation.AppProperties.StampProducer();
        return presentation;
    }

    /// <summary>Opens a .pptx from a file.</summary>
    public static Presentation Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromPackage(OpcPackage.Open(path));
    }

    /// <summary>Opens a .pptx from a stream.</summary>
    public static Presentation Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return FromPackage(OpcPackage.Open(stream));
    }

    /// <summary>Opens a .pptx from bytes.</summary>
    public static Presentation Open(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        return FromPackage(OpcPackage.Open(stream));
    }

    /// <summary>
    /// Opens a presentation as a template: the slides are removed, everything else is kept.
    /// </summary>
    public static Presentation FromTemplate(string path)
    {
        var presentation = Open(path);

        while (presentation._slides.Count > 0)
        {
            presentation.RemoveSlide(0);
        }

        return presentation;
    }

    private static Presentation FromPackage(OpcPackage package)
    {
        var part = package.MainDocumentPart
            ?? throw new OfficeNetException(
                "The package has no main presentation part. It is not a .pptx — check whether it " +
                "is a .docx or .xlsx, which use the same container.");

        var expected = new[]
        {
            ContentTypes.PowerPointPresentation, ContentTypes.PowerPointSlideshow,
            ContentTypes.PowerPointTemplate,
        };

        if (!expected.Contains(part.ContentType))
        {
            throw new OfficeNetException(
                $"The main part's content type is '{part.ContentType}', which is not " +
                "PresentationML. Open it with WordNet or ExcelNet instead.");
        }

        if (XmlUtil.NormalizeStrictNamespaces(part.Xml))
        {
            package.MarkDirty();
        }

        var presentation = new Presentation(package, part);
        presentation.Load();
        return presentation;
    }

    private void Load()
    {
        var masterPart = _presentationPart.RelatedPartsByType(RelationshipTypes.SlideMaster)
            .FirstOrDefault();

        if (masterPart?.Xml.Root is { } masterRoot)
        {
            Master = new SlideMaster(this, masterPart, masterRoot);

            foreach (var layoutPart in masterPart.RelatedPartsByType(RelationshipTypes.SlideLayout))
            {
                if (layoutPart.Xml.Root is { } layoutRoot)
                {
                    _layouts.Add(new SlideLayout(this, layoutPart, layoutRoot));
                }
            }
        }

        // Slide order comes from sldIdLst, not from the part names — a deck reordered in
        // PowerPoint keeps slide7.xml in third position.
        foreach (var element in Root.Element(Ns.P + "sldIdLst")?.Elements(Ns.P + "sldId") ?? [])
        {
            var relationshipId = element.Attr(Ns.R + "id");

            if (relationshipId is null)
            {
                continue;
            }

            var slidePart = _presentationPart.RelatedPart(relationshipId);

            if (slidePart?.Xml.Root is not { } slideRoot)
            {
                continue;
            }

            _slides.Add(new Slide(this, slidePart, slideRoot));

            var id = (uint)element.LongAttr("id");

            if (id >= _nextSlideId)
            {
                _nextSlideId = id + 1;
            }
        }
    }

    // ---- Slides ----------------------------------------------------------------------------------

    /// <summary>
    /// Adds a slide.
    /// </summary>
    /// <param name="layoutIndex">
    /// Which layout to base it on: 0 title, 1 title and content, 2 title only, 3 blank,
    /// 4 two content, 5 section header.
    /// </param>
    public Slide AddSlide(int layoutIndex = 1)
    {
        if (_layouts.Count == 0)
        {
            throw new OfficeNetException(
                "The presentation has no slide layouts, so a slide has nothing to inherit from.");
        }

        var layout = _layouts[Math.Clamp(layoutIndex, 0, _layouts.Count - 1)];
        return AddSlide(layout);
    }

    /// <summary>Adds a slide based on a specific layout.</summary>
    public Slide AddSlide(SlideLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var partName = Package.NextPartName("/ppt/slides/slide{0}.xml");
        var part = Package.AddXmlPart(partName, ContentTypes.PowerPointSlide, PptDefaultParts.Slide());

        // A slide must relate to its layout, and the presentation must relate to the slide.
        part.AddRelationship(layout.Part, RelationshipTypes.SlideLayout);
        var relationship = _presentationPart.AddRelationship(part, RelationshipTypes.Slide);

        var list = Root.Element(Ns.P + "sldIdLst");

        if (list is null)
        {
            list = new XElement(Ns.P + "sldIdLst");
            // p:sldIdLst follows p:sldMasterIdLst and precedes p:sldSz.
            Root.Element(Ns.P + "sldSz")?.AddBeforeSelf(list);
        }

        list.Add(new XElement(Ns.P + "sldId",
            // Slide ids must be at least 256; PowerPoint rejects anything lower.
            new XAttribute("id", _nextSlideId++),
            new XAttribute(Ns.R + "id", relationship.Id)));

        var slide = new Slide(this, part, part.Xml.Root!);
        _slides.Add(slide);
        Touch();
        return slide;
    }

    /// <summary>Adds a title slide and fills it.</summary>
    public Slide AddTitleSlide(string title, string? subtitle = null)
    {
        var slide = AddSlide(0);
        slide.SetTitle(title);

        if (subtitle is not null)
        {
            // SetBody resolves subTitle as well as body, and creates the placeholder from the
            // layout when the slide does not have one yet.
            slide.SetBody(subtitle);
        }

        return slide;
    }

    /// <summary>Adds a title-and-content slide and fills it.</summary>
    public Slide AddBulletSlide(string title, IEnumerable<string> bullets)
    {
        var slide = AddSlide(1);
        slide.SetTitle(title);
        slide.SetBody(bullets);
        return slide;
    }

    /// <summary>Adds a section-header slide.</summary>
    public Slide AddSectionSlide(string title, string? subtitle = null)
    {
        var slide = AddSlide(5);
        slide.SetTitle(title);

        if (subtitle is not null)
        {
            slide.SetBody(subtitle);
        }

        return slide;
    }

    /// <summary>Removes a slide and its part.</summary>
    public void RemoveSlide(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _slides.Count);

        var slide = _slides[index];

        var entry = Root.Element(Ns.P + "sldIdLst")?.Elements(Ns.P + "sldId")
            .FirstOrDefault(e =>
            {
                var id = e.Attr(Ns.R + "id");
                return id is not null && _presentationPart.RelatedPart(id)?.Name == slide.Part.Name;
            });

        entry?.Remove();
        _slides.RemoveAt(index);

        // RemovePart also clears the relationships that pointed at it, so no dangling r:id is left
        // behind in presentation.xml.
        Package.RemovePart(slide.Part.Name);
        Touch();
    }

    /// <summary>Moves a slide to a different position.</summary>
    public void MoveSlide(int fromIndex, int toIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromIndex, _slides.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(toIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toIndex, _slides.Count);

        var slide = _slides[fromIndex];
        _slides.RemoveAt(fromIndex);
        _slides.Insert(toIndex, slide);

        RebuildSlideIdList();
    }

    /// <summary>Reverses the deck.</summary>
    public void ReverseSlides()
    {
        _slides.Reverse();
        RebuildSlideIdList();
    }

    private void RebuildSlideIdList()
    {
        var list = Root.Element(Ns.P + "sldIdLst");

        if (list is null)
        {
            return;
        }

        // The existing entries are reused rather than regenerated so each slide keeps its id;
        // ids appear in custom shows and in hyperlinks between slides.
        var entries = list.Elements(Ns.P + "sldId").ToList();

        var byPart = entries.ToDictionary(
            e => _presentationPart.RelatedPart(e.Attr(Ns.R + "id") ?? string.Empty)?.Name.Value
                 ?? string.Empty,
            e => e);

        list.RemoveNodes();

        foreach (var slide in _slides)
        {
            if (byPart.TryGetValue(slide.Part.Name.Value, out var entry))
            {
                list.Add(entry);
            }
        }

        Touch();
    }

    /// <summary>
    /// Duplicates a slide, including every shape on it.
    /// </summary>
    public Slide DuplicateSlide(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _slides.Count);

        var source = _slides[index];
        var layout = LayoutOf(source) ?? _layouts.FirstOrDefault()
            ?? throw new OfficeNetException("The presentation has no layouts.");

        var copy = AddSlide(layout);

        var sourceCommon = source.Root.Element(Ns.P + "cSld");
        var targetCommon = copy.Root.Element(Ns.P + "cSld");

        if (sourceCommon is not null && targetCommon is not null)
        {
            targetCommon.ReplaceWith(new XElement(sourceCommon));
        }

        // Image relationships are per-part, so the copy needs its own pointing at the same media.
        foreach (var relationship in source.Part.RelationshipsByType(RelationshipTypes.Image))
        {
            if (relationship.TargetMode != TargetMode.Internal)
            {
                continue;
            }

            var media = Package.FindPart(relationship.TargetPartName);

            if (media is null)
            {
                continue;
            }

            var added = copy.Part.AddRelationship(media, RelationshipTypes.Image);

            // The copied shapes still carry the source's relationship ids; they have to be
            // rewritten to the ids the new part actually has.
            foreach (var blip in copy.Root.Descendants(Ns.A + "blip"))
            {
                if (blip.Attr(Ns.R + "embed") == relationship.Id)
                {
                    blip.SetAttributeValue(Ns.R + "embed", added.Id);
                }
            }
        }

        // Move it directly after the original, which is what "duplicate" means in PowerPoint.
        _slides.Remove(copy);
        _slides.Insert(index + 1, copy);
        RebuildSlideIdList();

        return copy;
    }

    /// <summary>The layout a slide is based on, or <c>null</c>.</summary>
    public SlideLayout? LayoutOf(Slide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);

        var part = slide.Part.RelatedPartByType(RelationshipTypes.SlideLayout);
        return part is null ? null : _layouts.FirstOrDefault(l => l.Part.Name == part.Name);
    }

    /// <summary>Finds a layout by its name, for example <c>Title and Content</c>.</summary>
    public SlideLayout? FindLayout(string name) =>
        _layouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    // ---- Media and relationships -------------------------------------------------------------------

    internal (string RelationshipId, ImageInfo Info) AddImage(OpcPart owner, byte[] imageBytes)
    {
        var part = AddImagePart(imageBytes, "/ppt/media", out var info);

        var existing = owner.RelationshipsByType(RelationshipTypes.Image)
            .FirstOrDefault(r => r.TargetMode == TargetMode.Internal && r.TargetPartName == part.Name);

        return existing is not null
            ? (existing.Id, info)
            : (owner.AddRelationship(part, RelationshipTypes.Image).Id, info);
    }

    internal byte[]? ResolveImage(XElement shapeElement, string relationshipId)
    {
        // The shape does not know which part it lives in, so the owner is found by walking up to
        // the slide whose tree contains it.
        var owner = _slides.FirstOrDefault(s => s.Root.Descendants().Contains(shapeElement))?.Part
                    ?? _layouts.FirstOrDefault(l => l.Root.Descendants().Contains(shapeElement))?.Part
                    ?? Master?.Part;

        return owner?.RelatedPart(relationshipId)?.GetBytes();
    }

    internal string AddHyperlink(XElement runElement, string url)
    {
        var owner = _slides.FirstOrDefault(s => s.Root.Descendants().Contains(runElement))?.Part
                    ?? _presentationPart;

        var existing = owner.RelationshipsByType(RelationshipTypes.Hyperlink)
            .FirstOrDefault(r => r.TargetMode == TargetMode.External && r.Target == url);

        return existing?.Id ?? owner.AddExternalRelationship(RelationshipTypes.Hyperlink, url).Id;
    }

    internal OpcPart CreateNotesPart(Slide slide)
    {
        var partName = Package.NextPartName("/ppt/notesSlides/notesSlide{0}.xml");
        var part = Package.AddXmlPart(partName, ContentTypes.PowerPointNotesSlide,
            PptDefaultParts.NotesSlide());

        // The notes part relates back to its slide, which is how PowerPoint pairs them.
        part.AddRelationship(slide.Part, RelationshipTypes.Slide);
        slide.Part.AddRelationship(part, RelationshipTypes.NotesSlide);

        Touch();
        return part;
    }

    // ---- Text and export ---------------------------------------------------------------------------

    /// <inheritdoc />
    public override string ExtractText()
    {
        var builder = new StringBuilder();

        foreach (var slide in _slides)
        {
            builder.Append("# Slide ").Append(slide.SlideNumber).Append('\n');

            var text = slide.ExtractText();

            if (text.Length > 0)
            {
                builder.Append(text).Append('\n');
            }

            var notes = slide.Notes;

            if (notes.Length > 0)
            {
                builder.Append("[notes] ").Append(notes).Append('\n');
            }

            builder.Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Renders the presentation to PDF, one page per slide.</summary>
    public void SaveAsPdf(string path, Export.SlidePdfOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var pdf = Export.PptToPdf.Convert(this, options);
        pdf.Save(path);
    }

    /// <summary>Renders the presentation to PDF and writes it to a stream.</summary>
    /// <remarks>
    /// The stream overload exists so a deck can be converted without touching the file system — a
    /// web handler returning bytes, or a test asserting on the result.
    /// </remarks>
    public void SaveAsPdf(Stream stream, Export.SlidePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var pdf = Export.PptToPdf.Convert(this, options);
        pdf.Save(stream);
    }

    /// <summary>Renders the presentation to a PDF document.</summary>
    public PdfNet.Document.PdfDocument ToPdf(Export.SlidePdfOptions? options = null) =>
        Export.PptToPdf.Convert(this, options);

    /// <inheritdoc />
    protected override void FlushToPackage()
    {
        AppProperties.Slides = _slides.Count;
        AppProperties.Notes = _slides.Count(s => s.Notes.Length > 0);
        AppProperties.HiddenSlides = _slides.Count(s => s.IsHidden);
    }

    public override string ToString() => $"Presentation({_slides.Count} slides)";
}
