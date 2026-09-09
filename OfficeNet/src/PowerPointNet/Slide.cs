// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using OfficeNet.Core;
using PowerPointNet.Charts;
using PowerPointNet.Diagrams;
using PowerPointNet.Shapes;
using System.Text;
using System.Xml.Linq;

namespace PowerPointNet;

/// <summary>The slide transitions PowerPointNet writes.</summary>
public enum SlideTransition
{
    /// <summary>No transition.</summary>
    None,

    /// <summary>A cross fade.</summary>
    Fade,

    /// <summary>A push from one edge.</summary>
    Push,

    /// <summary>A wipe.</summary>
    Wipe,

    /// <summary>A cut.</summary>
    Cut,

    /// <summary>A dissolve.</summary>
    Dissolve,

    /// <summary>A cover.</summary>
    Cover,

    /// <summary>A split.</summary>
    Split,

    /// <summary>A zoom.</summary>
    Zoom,
}

/// <summary>The basic entrance effects an animated shape can use.</summary>
public enum AnimationEffect
{
    /// <summary>No animation.</summary>
    None,

    /// <summary>The shape appears instantly.</summary>
    Appear,

    /// <summary>The shape fades in.</summary>
    Fade,

    /// <summary>The shape flies in from an edge.</summary>
    FlyIn,

    /// <summary>The shape wipes in.</summary>
    Wipe,

    /// <summary>The shape zooms in.</summary>
    Zoom,
}

/// <summary>One slide of a presentation.</summary>
public sealed class Slide
{
    private readonly Presentation _presentation;
    private uint _nextShapeId = 2;

    internal Slide(Presentation presentation, OpcPart part, XElement root)
    {
        _presentation = presentation;
        Part = part;
        Root = root;
        SeedShapeIds();
    }

    /// <summary>The slide's part in the package.</summary>
    public OpcPart Part { get; }

    /// <summary>The <c>p:sld</c> root element.</summary>
    public XElement Root { get; }

    /// <summary>The presentation this slide belongs to.</summary>
    public Presentation Presentation => _presentation;

    /// <summary>The zero-based position of the slide in the deck.</summary>
    public int Index
    {
        get
        {
            // Compared by reference: a deck can legitimately hold two slides whose content is
            // identical, and they are still different slides.
            for (var i = 0; i < _presentation.Slides.Count; i++)
            {
                if (ReferenceEquals(_presentation.Slides[i], this))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>The slide number, as shown in PowerPoint.</summary>
    public int SlideNumber => Index + 1;

    private XElement ShapeTree =>
        Root.Element(Ns.P + "cSld")?.Element(Ns.P + "spTree")
        ?? throw new OfficeNetException($"{Part.Name} has no shape tree.");

    private void SeedShapeIds()
    {
        // Shape id 1 belongs to the tree itself, so shapes start at 2. Reusing an id makes
        // PowerPoint report the slide as needing repair.
        var highest = ShapeTree.Descendants(Ns.P + "cNvPr")
            .Select(e => e.LongAttr("id"))
            .DefaultIfEmpty(1)
            .Max();

        _nextShapeId = (uint)Math.Max(2, highest + 1);
    }

    internal uint NextShapeId() => _nextShapeId++;

    /// <summary>The shapes on the slide, in z-order.</summary>
    public IReadOnlyList<Shape> Shapes
    {
        get
        {
            var result = new List<Shape>();

            foreach (var element in ShapeTree.Elements())
            {
                var name = element.Name.LocalName;

                if (name is "nvGrpSpPr" or "grpSpPr")
                {
                    continue;
                }

                // A graphic frame is a table, a chart or an embedded object depending only on what
                // is inside it — the element is the same for all three, so each is identified by
                // its graphicData rather than by its tag.
                result.Add(name switch
                {
                    // A media clip is also a p:pic; only its non-visual properties say so.
                    "pic" when Media.SlideMedia.DetectKind(element, Part) is { } mediaKind =>
                        new Media.SlideMedia(_presentation, element, mediaKind),
                    "pic" => new Picture(_presentation, element),
                    "graphicFrame" when IsTable(element) => new SlideTable(_presentation, element),
                    "graphicFrame" when SlideChart.IsChart(element) =>
                        ResolveChart(element),
                    "graphicFrame" when SmartArt.IsDiagram(element) =>
                        ResolveDiagram(element),
                    _ => new Shape(_presentation, element),
                });
            }

            return result;
        }
    }

    private static bool IsTable(XElement graphicFrame) =>
        graphicFrame.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData")
            ?.Element(Ns.A + "tbl") is not null;

    /// <summary>
    /// Rebuilds a chart handle from a frame, following its relationship to the chart part.
    /// </summary>
    /// <remarks>
    /// A frame whose relationship is broken degrades to a plain shape rather than throwing: the
    /// slide is still readable, and one damaged chart should not make the whole deck unopenable.
    /// </remarks>
    private Shape ResolveChart(XElement graphicFrame)
    {
        var id = graphicFrame.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData")
            ?.Element(Ns.C + "chart")?.Attr(Ns.R + "id");

        var part = id is null ? null : Part.RelatedPart(id);

        return part is null
            ? new Shape(_presentation, graphicFrame)
            : new SlideChart(_presentation, graphicFrame, part);
    }

    /// <summary>The slide's placeholder shapes.</summary>
    public IEnumerable<Shape> Placeholders => Shapes.Where(s => s.IsPlaceholder);

    /// <summary>The tables on the slide.</summary>
    public IEnumerable<SlideTable> Tables => Shapes.OfType<SlideTable>();

    /// <summary>The pictures on the slide.</summary>
    public IEnumerable<Picture> Pictures => Shapes.OfType<Picture>();

    /// <summary>The charts on the slide.</summary>
    public IEnumerable<SlideChart> Charts => Shapes.OfType<SlideChart>();

    /// <summary>
    /// The title placeholder, or <c>null</c> when the slide has none.
    /// </summary>
    public Shape? Title =>
        Shapes.FirstOrDefault(s => s.PlaceholderType is "title" or "ctrTitle");

    /// <summary>The body placeholder, or <c>null</c>.</summary>
    public Shape? Body =>
        Shapes.FirstOrDefault(s => s.PlaceholderType is "body" or "subTitle");

    /// <summary>
    /// Sets the title placeholder's text, creating the placeholder when the layout has one and the
    /// slide does not.
    /// </summary>
    public Shape SetTitle(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var title = Title ?? CreatePlaceholderFromLayout("title") ?? CreatePlaceholderFromLayout("ctrTitle");

        if (title is null)
        {
            // The layout has no title placeholder — a Blank layout, say. A text box in the usual
            // title position is more useful than an exception.
            title = AddTextBox(text, Units.Inches(0.7), Units.Inches(0.5),
                _presentation.SlideWidth - Units.Inches(1.4), Units.Inches(1.1));

            var frame = title.TextFrame!;
            frame.Paragraphs[0].Runs[0].FontSize = Units.Pt(32);
            frame.Paragraphs[0].Runs[0].Bold = true;
            return title;
        }

        title.Text = text;
        return title;
    }

    /// <summary>Sets the body placeholder's text as a bulleted list.</summary>
    /// <remarks>
    /// A Title Slide layout names its second placeholder <c>subTitle</c>, not <c>body</c>, and a
    /// content layout names it <c>body</c>. Both are tried so the same call works on either — a
    /// caller should not have to know which layout kind they picked to fill its content area.
    /// </remarks>
    public Shape SetBody(IEnumerable<string> bullets)
    {
        ArgumentNullException.ThrowIfNull(bullets);

        var body = Body
            ?? CreatePlaceholderFromLayout("body")
            ?? CreatePlaceholderFromLayout("subTitle")
            ?? throw new OfficeNetException(
                $"Slide {SlideNumber} has no body placeholder and its layout defines none. " +
                "Use AddTextBox instead, or pick a layout with a content placeholder.");

        var frame = body.TextFrame!;
        frame.Element.Elements(Ns.A + "p").Remove();

        // A subtitle is prose, not a list. Bulleting it is what makes a generated title slide look
        // wrong in a way that is obvious on screen and easy to miss in code.
        var bulleted = body.PlaceholderType != "subTitle";

        foreach (var item in bullets)
        {
            var paragraph = frame.AddParagraph(item);
            paragraph.HasBullet = bulleted;
        }

        if (frame.Element.Element(Ns.A + "p") is null)
        {
            frame.AddParagraph();
        }

        return body;
    }

    /// <summary>Sets the body placeholder's text.</summary>
    public Shape SetBody(string text) => SetBody(text.Replace("\r\n", "\n").Split('\n'));

    /// <summary>
    /// Copies a placeholder from the slide's layout onto the slide.
    /// </summary>
    /// <remarks>
    /// A new slide inherits its placeholders' <em>appearance</em> from the layout but holds no
    /// shapes of its own; text can only be set on a shape that exists. Copying the layout's shape
    /// — position, size and placeholder binding, but not its prompt text — is exactly what
    /// PowerPoint does when a user clicks into a placeholder and types.
    /// </remarks>
    private Shape? CreatePlaceholderFromLayout(string type)
    {
        var layout = _presentation.LayoutOf(this);
        var source = layout?.Root.Descendants(Ns.P + "sp")
            .FirstOrDefault(sp => sp.Element(Ns.P + "nvSpPr")?.Element(Ns.P + "nvPr")
                ?.Element(Ns.P + "ph")?.Attr("type") == type);

        if (source is null)
        {
            return null;
        }

        var copy = new XElement(source);

        var identity = copy.Element(Ns.P + "nvSpPr")?.Element(Ns.P + "cNvPr");
        identity?.SetAttributeValue("id", NextShapeId());

        // The layout's prompt paragraphs are not content; a slide that copies them shows
        // "Click to edit Master title style" as real text.
        var body = copy.Element(Ns.P + "txBody");

        if (body is not null)
        {
            body.Elements(Ns.A + "p").Remove();
            body.Add(new XElement(Ns.A + "p"));
        }

        ShapeTree.Add(copy);
        _presentation.Touch();
        return new Shape(_presentation, copy);
    }

    // ---- Adding shapes ---------------------------------------------------------------------------

    /// <summary>Adds a text box.</summary>
    public Shape AddTextBox(string text, Length left, Length top, Length width, Length height)
    {
        ArgumentNullException.ThrowIfNull(text);

        var id = NextShapeId();

        var element = new XElement(Ns.P + "sp",
            new XElement(Ns.P + "nvSpPr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", $"TextBox {id}")),
                // txBox="1" is what distinguishes a text box from an autoshape with text; without
                // it PowerPoint gives the shape a default fill and outline.
                new XElement(Ns.P + "cNvSpPr", new XAttribute("txBox", "1")),
                new XElement(Ns.P + "nvPr")),
            new XElement(Ns.P + "spPr",
                new XElement(Ns.A + "xfrm",
                    new XElement(Ns.A + "off",
                        new XAttribute("x", left.Emu), new XAttribute("y", top.Emu)),
                    new XElement(Ns.A + "ext",
                        new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu))),
                new XElement(Ns.A + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(Ns.A + "avLst")),
                new XElement(Ns.A + "noFill")),
            new XElement(Ns.P + "txBody",
                new XElement(Ns.A + "bodyPr",
                    new XAttribute("wrap", "square"),
                    new XElement(Ns.A + "spAutoFit")),
                new XElement(Ns.A + "lstStyle"),
                new XElement(Ns.A + "p")));

        ShapeTree.Add(element);
        _presentation.Touch();

        var shape = new Shape(_presentation, element);

        if (text.Length > 0)
        {
            shape.TextFrame!.Text = text;
        }

        return shape;
    }

    /// <summary>Adds an autoshape with a preset geometry.</summary>
    public Shape AddShape(ShapeGeometry geometry, Length left, Length top, Length width, Length height,
        string? text = null, OfficeColor? fill = null)
    {
        var id = NextShapeId();

        var element = new XElement(Ns.P + "sp",
            new XElement(Ns.P + "nvSpPr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", $"{geometry} {id}")),
                new XElement(Ns.P + "cNvSpPr"),
                new XElement(Ns.P + "nvPr")),
            new XElement(Ns.P + "spPr",
                new XElement(Ns.A + "xfrm",
                    new XElement(Ns.A + "off",
                        new XAttribute("x", left.Emu), new XAttribute("y", top.Emu)),
                    new XElement(Ns.A + "ext",
                        new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu))),
                new XElement(Ns.A + "prstGeom",
                    new XAttribute("prst", Shape.GeometryName(geometry)),
                    // a:avLst holds the geometry's adjustment handles. It must be present even
                    // when empty or PowerPoint reports the shape as invalid.
                    new XElement(Ns.A + "avLst")),
                new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "srgbClr",
                        new XAttribute("val", (fill ?? OfficeColor.FromRgb(0x2E, 0x54, 0x96)).ToHex())))),
            new XElement(Ns.P + "txBody",
                new XElement(Ns.A + "bodyPr",
                    new XAttribute("anchor", "ctr")),
                new XElement(Ns.A + "lstStyle"),
                new XElement(Ns.A + "p",
                    new XElement(Ns.A + "pPr", new XAttribute("algn", "ctr")))));

        ShapeTree.Add(element);
        _presentation.Touch();

        var shape = new Shape(_presentation, element);

        if (!string.IsNullOrEmpty(text))
        {
            var paragraph = shape.TextFrame!.Paragraphs[0];
            var run = paragraph.AddRun(text);
            run.Color = (fill ?? OfficeColor.FromRgb(0x2E, 0x54, 0x96)).ContrastingForeground;
        }

        return shape;
    }

    /// <summary>Adds a picture.</summary>
    /// <param name="imageBytes">The image file.</param>
    /// <param name="left">Distance from the slide's left edge.</param>
    /// <param name="top">Distance from the slide's top edge.</param>
    /// <param name="width">The drawn width; the natural size is used when both sizes are omitted.</param>
    /// <param name="height">The drawn height; derived from the aspect ratio when only one is given.</param>
    public Picture AddPicture(byte[] imageBytes, Length left, Length top,
        Length? width = null, Length? height = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var (relationshipId, info) = _presentation.AddImage(Part, imageBytes);

        var (finalWidth, finalHeight) = (width, height) switch
        {
            ({ } w, { } h) => (w, h),
            ({ } w, null) => (w, Length.FromEmu((long)(w.Emu / info.AspectRatio))),
            (null, { } h) => (Length.FromEmu((long)(h.Emu * info.AspectRatio)), h),
            _ => (info.NaturalWidth, info.NaturalHeight),
        };

        var id = NextShapeId();

        var element = new XElement(Ns.P + "pic",
            new XElement(Ns.P + "nvPicPr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", $"Picture {id}")),
                new XElement(Ns.P + "cNvPicPr",
                    new XElement(Ns.A + "picLocks", new XAttribute("noChangeAspect", "1"))),
                new XElement(Ns.P + "nvPr")),
            new XElement(Ns.P + "blipFill",
                new XElement(Ns.A + "blip", new XAttribute(Ns.R + "embed", relationshipId)),
                new XElement(Ns.A + "stretch", new XElement(Ns.A + "fillRect"))),
            new XElement(Ns.P + "spPr",
                new XElement(Ns.A + "xfrm",
                    new XElement(Ns.A + "off",
                        new XAttribute("x", left.Emu), new XAttribute("y", top.Emu)),
                    new XElement(Ns.A + "ext",
                        new XAttribute("cx", finalWidth.Emu), new XAttribute("cy", finalHeight.Emu))),
                new XElement(Ns.A + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(Ns.A + "avLst"))));

        ShapeTree.Add(element);
        _presentation.Touch();
        return new Picture(_presentation, element);
    }

    /// <summary>Adds a picture from a file.</summary>
    public Picture AddPicture(string path, Length left, Length top,
        Length? width = null, Length? height = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return AddPicture(File.ReadAllBytes(path), left, top, width, height);
    }

    /// <summary>Adds a table.</summary>
    public SlideTable AddTable(int rows, int columns, Length left, Length top,
        Length width, Length height) =>
        SlideTable.Create(_presentation, this, ShapeTree, NextShapeId(), rows, columns,
            left, top, width, height);

    /// <summary>
    /// Adds a native PowerPoint chart.
    /// </summary>
    /// <remarks>
    /// Native rather than a rendered picture: the numbers travel with the deck, the theme restyles
    /// it, and a reader can hover a point to see its value. The data is cached inside the chart
    /// part, so no workbook has to ship alongside it.
    /// </remarks>
    public SlideChart AddChart(ChartData data, Length left, Length top,
        Length width, Length height) =>
        SlideChart.Create(_presentation, this, ShapeTree, NextShapeId(), data,
            left, top, width, height);

    /// <summary>Adds a chart filling the slide's content area below the title.</summary>
    public SlideChart AddChart(ChartData data)
    {
        var top = Title is null ? Units.Inches(0.8) : Units.Inches(1.7);

        return AddChart(data,
            Units.Inches(0.8),
            top,
            _presentation.SlideWidth - Units.Inches(1.6),
            _presentation.SlideHeight - top - Units.Inches(0.7));
    }

    // ---- Diagrams ------------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds a diagram handle from a frame, following its relationships to the parts.
    /// </summary>
    /// <remarks>
    /// A frame whose relationships are broken degrades to a plain shape rather than throwing: a
    /// deck someone edited by hand should still open.
    /// </remarks>
    private Shape ResolveDiagram(XElement frame)
    {
        var relIds = frame.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData")
            ?.Element(Ns.Dgm + "relIds");

        if (relIds?.Attr(Ns.R + "dm") is not { } dataId ||
            Part.RelatedPart(dataId) is not { } data)
        {
            return new Shape(_presentation, frame);
        }

        // The drawing hangs off the data part, not off the slide, which is why this second hop is
        // needed and why looking for it on the slide finds nothing.
        var drawing = data.RelatedPartByType(RelationshipTypes.DiagramDrawing);

        return drawing is null
            ? new Shape(_presentation, frame)
            : new SmartArt(_presentation, frame, data, drawing);
    }

    /// <summary>The SmartArt diagrams on the slide.</summary>
    public IEnumerable<SmartArt> Diagrams => Shapes.OfType<SmartArt>();

    /// <summary>
    /// Adds a SmartArt diagram.
    /// </summary>
    /// <param name="kind">The diagram's shape.</param>
    /// <param name="nodes">The nodes. Only <see cref="DiagramKind.Hierarchy"/> draws children.</param>
    /// <param name="left">Distance from the slide's left edge.</param>
    /// <param name="top">Distance from the slide's top edge.</param>
    /// <param name="width">The diagram's width.</param>
    /// <param name="height">The diagram's height.</param>
    /// <param name="fill">The node colour. The theme's first accent by default.</param>
    /// <param name="text">The text colour. White by default, for contrast against the fill.</param>
    /// <remarks>
    /// A diagram is five parts, and the geometry is computed here rather than left to PowerPoint's
    /// layout engine — see <see cref="SmartArt"/> for what that means and where the boundary sits.
    /// </remarks>
    public SmartArt AddSmartArt(DiagramKind kind, IReadOnlyList<DiagramNode> nodes,
        Length left, Length top, Length width, Length height,
        OfficeColor? fill = null, OfficeColor? text = null) =>
        SmartArt.Create(_presentation, this, ShapeTree, NextShapeId(), kind, nodes,
            left, top, width, height,
            fill ?? OfficeColor.FromRgb(0x1F, 0x3A, 0x5F),
            text ?? OfficeColor.White);

    /// <summary>Adds a diagram filling the slide's content area below the title.</summary>
    public SmartArt AddSmartArt(DiagramKind kind, IReadOnlyList<DiagramNode> nodes,
        OfficeColor? fill = null, OfficeColor? text = null)
    {
        var top = Title is null ? Units.Inches(0.8) : Units.Inches(1.7);

        return AddSmartArt(kind, nodes,
            Units.Inches(0.8),
            top,
            _presentation.SlideWidth - Units.Inches(1.6),
            _presentation.SlideHeight - top - Units.Inches(0.7),
            fill, text);
    }

    /// <summary>Adds a diagram from plain strings, one node each.</summary>
    public SmartArt AddSmartArt(DiagramKind kind, params string[] nodes) =>
        AddSmartArt(kind, [.. nodes.Select(n => new DiagramNode(n))]);

    // ---- Media ---------------------------------------------------------------------------------

    private readonly List<Media.SlideMedia> _media = [];

    internal void RegisterMedia(Media.SlideMedia media) => _media.Add(media);

    /// <summary>The video and audio clips on the slide.</summary>
    public IEnumerable<Media.SlideMedia> MediaClips => Shapes.OfType<Media.SlideMedia>();

    /// <summary>
    /// Embeds a video clip.
    /// </summary>
    /// <param name="videoBytes">The video file.</param>
    /// <param name="extension">The file extension without the dot, for example <c>mp4</c>.</param>
    /// <param name="left">Distance from the slide's left edge.</param>
    /// <param name="top">Distance from the slide's top edge.</param>
    /// <param name="width">The frame's width.</param>
    /// <param name="height">The frame's height.</param>
    /// <param name="posterImage">
    /// The still shown before playback. A generated placeholder is used when none is given, because
    /// a media shape with no image renders as a black rectangle.
    /// </param>
    /// <param name="autoPlay">Starts the clip when the slide appears rather than on a click.</param>
    public Media.SlideMedia AddVideo(byte[] videoBytes, string extension, Length left, Length top,
        Length width, Length height, byte[]? posterImage = null, bool autoPlay = false)
    {
        ArgumentNullException.ThrowIfNull(videoBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        var media = Media.SlideMedia.Create(_presentation, this, ShapeTree, NextShapeId(),
            videoBytes, extension.TrimStart('.').ToLowerInvariant(), posterImage,
            Media.MediaKind.Video, left, top, width, height);

        if (autoPlay)
        {
            SetMediaAutoPlay(media, true);
        }

        return media;
    }

    /// <summary>Embeds a video clip from a file.</summary>
    public Media.SlideMedia AddVideo(string path, Length left, Length top, Length width,
        Length height, byte[]? posterImage = null, bool autoPlay = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return AddVideo(File.ReadAllBytes(path),
            Path.GetExtension(path).TrimStart('.'), left, top, width, height, posterImage, autoPlay);
    }

    /// <summary>Embeds an audio clip.</summary>
    public Media.SlideMedia AddAudio(byte[] audioBytes, string extension, Length left, Length top,
        Length? size = null, bool autoPlay = false)
    {
        ArgumentNullException.ThrowIfNull(audioBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        // An audio clip has no picture of its own, so it is drawn as a small square speaker icon
        // rather than being stretched to a video-shaped frame.
        var edge = size ?? Units.Inches(0.8);

        var media = Media.SlideMedia.Create(_presentation, this, ShapeTree, NextShapeId(),
            audioBytes, extension.TrimStart('.').ToLowerInvariant(), null,
            Media.MediaKind.Audio, left, top, edge, edge);

        if (autoPlay)
        {
            SetMediaAutoPlay(media, true);
        }

        return media;
    }

    /// <summary>Embeds an audio clip from a file.</summary>
    public Media.SlideMedia AddAudio(string path, Length left, Length top, Length? size = null,
        bool autoPlay = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return AddAudio(File.ReadAllBytes(path),
            Path.GetExtension(path).TrimStart('.'), left, top, size, autoPlay);
    }

    /// <summary>
    /// Links a video hosted online rather than embedding it.
    /// </summary>
    /// <remarks>
    /// The clip is not copied into the deck, so the file stays small and the video stays current —
    /// and playback needs a network connection at presentation time, which an embedded clip does
    /// not. PowerPoint expects an embed URL rather than a watch page: a YouTube link has to be the
    /// <c>/embed/</c> form for the player to appear.
    /// </remarks>
    public Media.SlideMedia AddOnlineVideo(string url, Length left, Length top, Length width,
        Length height, byte[]? posterImage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return Media.SlideMedia.CreateOnline(_presentation, this, ShapeTree, NextShapeId(),
            url, posterImage, left, top, width, height);
    }

    /// <summary>
    /// Turns autoplay on or off for a clip by rewriting the slide's timing tree.
    /// </summary>
    /// <remarks>
    /// Playback is not a property of the shape — it is a command node in <c>p:timing</c> whose
    /// start condition is either <c>delay="0"</c> (autoplay) or <c>delay="indefinite"</c> (wait for
    /// a click). Setting a flag on the picture would do nothing at all.
    /// </remarks>
    internal void SetMediaAutoPlay(Media.SlideMedia media, bool autoPlay)
    {
        Root.Elements(Ns.P + "timing").Remove();

        var nodeId = 2;

        var command = new XElement(Ns.P + "cmd",
            new XAttribute("type", "call"),
            new XAttribute("cmd", "playFrom(0.0)"),
            new XElement(Ns.P + "cBhvr",
                new XElement(Ns.P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", "indefinite"),
                    new XAttribute("fill", "hold")),
                new XElement(Ns.P + "tgtEl",
                    new XElement(Ns.P + "spTgt", new XAttribute("spid", media.Id)))));

        var effect = new XElement(Ns.P + "par",
            new XElement(Ns.P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("presetID", "1"),
                new XAttribute("presetClass", "mediacall"),
                new XAttribute("presetSubtype", "0"),
                new XAttribute("fill", "hold"),
                new XAttribute("nodeType", autoPlay ? "withEffect" : "clickEffect"),
                new XElement(Ns.P + "stCondLst",
                    new XElement(Ns.P + "cond", new XAttribute("delay", "0"))),
                new XElement(Ns.P + "childTnLst", command)));

        var inner = new XElement(Ns.P + "par",
            new XElement(Ns.P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("fill", "hold"),
                new XElement(Ns.P + "stCondLst",
                    new XElement(Ns.P + "cond", new XAttribute("delay", "0"))),
                new XElement(Ns.P + "childTnLst", effect)));

        var outer = new XElement(Ns.P + "par",
            new XElement(Ns.P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("fill", "hold"),
                new XElement(Ns.P + "stCondLst",
                    // This is the switch: 0 starts with the slide, indefinite waits for a click.
                    new XElement(Ns.P + "cond",
                        new XAttribute("delay", autoPlay ? "0" : "indefinite"))),
                new XElement(Ns.P + "childTnLst", inner)));

        var timing = new XElement(Ns.P + "timing",
            new XElement(Ns.P + "tnLst",
                new XElement(Ns.P + "par",
                    new XElement(Ns.P + "cTn",
                        new XAttribute("id", "1"),
                        new XAttribute("dur", "indefinite"),
                        new XAttribute("restart", "never"),
                        new XAttribute("nodeType", "tmRoot"),
                        new XElement(Ns.P + "childTnLst",
                            new XElement(Ns.P + "seq",
                                new XAttribute("concurrent", "1"),
                                new XAttribute("nextAc", "seek"),
                                new XElement(Ns.P + "cTn",
                                    new XAttribute("id", nodeId++),
                                    new XAttribute("dur", "indefinite"),
                                    new XAttribute("nodeType", "mainSeq"),
                                    new XElement(Ns.P + "childTnLst", outer)),
                                new XElement(Ns.P + "prevCondLst",
                                    new XElement(Ns.P + "cond",
                                        new XAttribute("evt", "onPrev"),
                                        new XAttribute("delay", "0"),
                                        new XElement(Ns.P + "tgtEl",
                                            new XElement(Ns.P + "sldTgt")))),
                                new XElement(Ns.P + "nextCondLst",
                                    new XElement(Ns.P + "cond",
                                        new XAttribute("evt", "onNext"),
                                        new XAttribute("delay", "0"),
                                        new XElement(Ns.P + "tgtEl",
                                            new XElement(Ns.P + "sldTgt"))))))))),
            // The media node list is what pairs the timing with the shape; PowerPoint uses it to
            // draw the clip's playback controls.
            new XElement(Ns.P + "extLst",
                new XElement(Ns.P + "ext",
                    new XAttribute("uri", "{4BB2AC6E-4C82-4A16-9BF7-C2B0B1EA0A8B}"),
                    new XElement(Ns.P14 + "media",
                        new XAttribute(XNamespace.Xmlns + "p14", Ns.P14.NamespaceName),
                        new XAttribute("spid", media.Id)))));

        Root.Add(timing);
        _presentation.Touch();
    }

    // ---- Notes ---------------------------------------------------------------------------------

    /// <summary>
    /// The speaker notes for this slide. Reading creates the notes part when there is none.
    /// </summary>
    public string Notes
    {
        get
        {
            var part = Part.RelatedPartByType(RelationshipTypes.NotesSlide);

            if (part?.Xml.Root is not { } root)
            {
                return string.Empty;
            }

            // Line breaks are written as an explicit LF rather than through AppendLine, whose
            // Environment.NewLine would make the same deck's notes come back different on Windows
            // and on Linux — which defeats the multiplatform promise.
            var builder = new StringBuilder();

            // The notes part also contains a copy of the slide image placeholder; only the body
            // placeholder holds the actual notes.
            foreach (var shape in root.Descendants(Ns.P + "sp"))
            {
                var type = shape.Element(Ns.P + "nvSpPr")?.Element(Ns.P + "nvPr")
                    ?.Element(Ns.P + "ph")?.Attr("type");

                if (type != "body")
                {
                    continue;
                }

                foreach (var paragraph in shape.Descendants(Ns.A + "p"))
                {
                    builder.Append(string.Concat(
                        paragraph.Descendants(Ns.A + "t").Select(t => t.Value))).Append('\n');
                }
            }

            return builder.ToString().TrimEnd();
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var part = Part.RelatedPartByType(RelationshipTypes.NotesSlide)
                       ?? _presentation.CreateNotesPart(this);

            var root = part.Xml.Root!;

            var body = root.Descendants(Ns.P + "sp").FirstOrDefault(sp =>
                sp.Element(Ns.P + "nvSpPr")?.Element(Ns.P + "nvPr")?.Element(Ns.P + "ph")
                    ?.Attr("type") == "body");

            var text = body?.Element(Ns.P + "txBody");

            if (text is null)
            {
                return;
            }

            text.Elements(Ns.A + "p").Remove();

            foreach (var line in value.Replace("\r\n", "\n").Split('\n'))
            {
                text.Add(new XElement(Ns.A + "p",
                    new XElement(Ns.A + "r",
                        new XElement(Ns.A + "rPr", new XAttribute("lang", "en-US")),
                        XmlUtil.TextElement(Ns.A + "t", line))));
            }

            if (text.Element(Ns.A + "p") is null)
            {
                text.Add(new XElement(Ns.A + "p"));
            }

            _presentation.Touch();
        }
    }

    // ---- Transitions and animation ----------------------------------------------------------------

    /// <summary>Sets the slide's entrance transition.</summary>
    /// <param name="transition">The effect.</param>
    /// <param name="duration">How long it runs; PowerPoint's own default is 700 ms.</param>
    /// <param name="advanceAfter">Advances automatically after this long; <c>null</c> waits for a click.</param>
    public Slide SetTransition(SlideTransition transition, TimeSpan? duration = null,
        TimeSpan? advanceAfter = null)
    {
        Root.Elements(Ns.P + "transition").Remove();

        if (transition == SlideTransition.None && advanceAfter is null)
        {
            _presentation.Touch();
            return this;
        }

        var element = new XElement(Ns.P + "transition");

        if (duration is { } d)
        {
            // p:transition/@dur is milliseconds and is a PowerPoint 2010 extension attribute in
            // the p14 namespace for some effects; the base attribute works for the presets here.
            element.SetAttributeValue("spd", d.TotalMilliseconds < 500 ? "fast"
                : d.TotalMilliseconds > 1200 ? "slow" : "med");
        }

        if (advanceAfter is { } after)
        {
            // advTm is what makes a deck self-advance; advClick must be off or a click still wins.
            element.SetAttributeValue("advTm", (int)after.TotalMilliseconds);
            element.SetAttributeValue("advClick", "0");
        }

        var effect = transition switch
        {
            SlideTransition.Fade => new XElement(Ns.P + "fade"),
            SlideTransition.Push => new XElement(Ns.P + "push", new XAttribute("dir", "u")),
            SlideTransition.Wipe => new XElement(Ns.P + "wipe", new XAttribute("dir", "r")),
            SlideTransition.Cut => new XElement(Ns.P + "cut"),
            SlideTransition.Dissolve => new XElement(Ns.P + "dissolve"),
            SlideTransition.Cover => new XElement(Ns.P + "cover", new XAttribute("dir", "d")),
            SlideTransition.Split => new XElement(Ns.P + "split",
                new XAttribute("orient", "horz"), new XAttribute("dir", "out")),
            SlideTransition.Zoom => new XElement(Ns.P + "zoom", new XAttribute("dir", "in")),
            _ => null,
        };

        if (effect is not null)
        {
            element.Add(effect);
        }

        // p:transition follows p:cSld and p:clrMapOvr.
        Root.Add(element);
        _presentation.Touch();
        return this;
    }

    /// <summary>
    /// Animates the slide's shapes so each appears on a click, in the order given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PresentationML's animation model is a full SMIL timing tree — <c>p:timing</c> holding nested
    /// parallel and sequential time nodes, each with condition lists and behaviour elements. Even a
    /// single "fade in on click" is around forty elements deep.
    /// </para>
    /// <para>
    /// What this writes is the click-sequence shape of that tree, which covers the overwhelmingly
    /// common case: build a bullet list or a set of shapes one click at a time. Anything richer —
    /// motion paths, emphasis effects, triggers — needs the timing tree written by hand through
    /// <see cref="Root"/>.
    /// </para>
    /// </remarks>
    public Slide AnimateOnClick(AnimationEffect effect, params Shape[] shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);

        Root.Elements(Ns.P + "timing").Remove();

        if (effect == AnimationEffect.None || shapes.Length == 0)
        {
            _presentation.Touch();
            return this;
        }

        var nodeId = 2;
        var sequenceChildren = new XElement(Ns.P + "childTnLst");

        foreach (var shape in shapes)
        {
            sequenceChildren.Add(BuildClickStep(effect, shape.Id, ref nodeId));
        }

        var timing = new XElement(Ns.P + "timing",
            new XElement(Ns.P + "tnLst",
                new XElement(Ns.P + "par",
                    new XElement(Ns.P + "cTn",
                        new XAttribute("id", "1"),
                        new XAttribute("dur", "indefinite"),
                        new XAttribute("restart", "never"),
                        new XAttribute("nodeType", "tmRoot"),
                        new XElement(Ns.P + "childTnLst",
                            new XElement(Ns.P + "seq",
                                new XAttribute("concurrent", "1"),
                                new XAttribute("nextAc", "seek"),
                                new XElement(Ns.P + "cTn",
                                    new XAttribute("id", nodeId++),
                                    new XAttribute("dur", "indefinite"),
                                    new XAttribute("nodeType", "mainSeq"),
                                    sequenceChildren),
                                // The previous/next condition lists are what wire the sequence to
                                // the space bar and the arrow keys.
                                new XElement(Ns.P + "prevCondLst",
                                    new XElement(Ns.P + "cond",
                                        new XAttribute("evt", "onPrev"),
                                        new XAttribute("delay", "0"),
                                        new XElement(Ns.P + "tgtEl",
                                            new XElement(Ns.P + "sldTgt")))),
                                new XElement(Ns.P + "nextCondLst",
                                    new XElement(Ns.P + "cond",
                                        new XAttribute("evt", "onNext"),
                                        new XAttribute("delay", "0"),
                                        new XElement(Ns.P + "tgtEl",
                                            new XElement(Ns.P + "sldTgt"))))))))));

        Root.Add(timing);
        _presentation.Touch();
        return this;
    }

    private static XElement BuildClickStep(AnimationEffect effect, uint shapeId, ref int nodeId)
    {
        var presetId = effect switch
        {
            AnimationEffect.Appear => 1,
            AnimationEffect.Fade => 10,
            AnimationEffect.FlyIn => 2,
            AnimationEffect.Wipe => 22,
            AnimationEffect.Zoom => 23,
            _ => 10,
        };

        var target = new XElement(Ns.P + "tgtEl",
            new XElement(Ns.P + "spTgt", new XAttribute("spid", shapeId)));

        var behaviour = new XElement(Ns.P + "animEffect",
            new XAttribute("transition", "in"),
            new XAttribute("filter", effect switch
            {
                AnimationEffect.Fade => "fade",
                AnimationEffect.Wipe => "wipe(right)",
                AnimationEffect.Zoom => "fade",
                _ => "fade",
            }),
            new XElement(Ns.P + "cBhvr",
                new XElement(Ns.P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", "500")),
                target));

        // "set" flips the shape's visibility at the start of the effect. Without it the shape is
        // already visible before its animation runs, which defeats an entrance effect entirely.
        var reveal = new XElement(Ns.P + "set",
            new XElement(Ns.P + "cBhvr",
                new XElement(Ns.P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", "1"),
                    new XAttribute("fill", "hold")),
                new XElement(Ns.P + "tgtEl",
                    new XElement(Ns.P + "spTgt", new XAttribute("spid", shapeId))),
                new XElement(Ns.P + "attrNameLst",
                    new XElement(Ns.P + "attrName", "style.visibility"))),
            new XElement(Ns.P + "to",
                new XElement(Ns.P + "strVal", new XAttribute("val", "visible"))));

        return new XElement(Ns.P + "par",
            new XElement(Ns.P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("fill", "hold"),
                new XElement(Ns.P + "stCondLst",
                    new XElement(Ns.P + "cond",
                        new XAttribute("delay", "indefinite"))),
                new XElement(Ns.P + "childTnLst",
                    new XElement(Ns.P + "par",
                        new XElement(Ns.P + "cTn",
                            new XAttribute("id", nodeId++),
                            new XAttribute("fill", "hold"),
                            new XElement(Ns.P + "stCondLst",
                                new XElement(Ns.P + "cond", new XAttribute("delay", "0"))),
                            new XElement(Ns.P + "childTnLst",
                                new XElement(Ns.P + "par",
                                    new XElement(Ns.P + "cTn",
                                        new XAttribute("id", nodeId++),
                                        new XAttribute("presetID", presetId),
                                        new XAttribute("presetClass", "entr"),
                                        new XAttribute("fill", "hold"),
                                        new XAttribute("nodeType", "clickEffect"),
                                        new XElement(Ns.P + "stCondLst",
                                            new XElement(Ns.P + "cond",
                                                new XAttribute("delay", "0"))),
                                        new XElement(Ns.P + "childTnLst",
                                            reveal,
                                            behaviour)))))))));
    }

    /// <summary>The slide's background colour; <c>null</c> inherits from the layout.</summary>
    public OfficeColor? BackgroundColor
    {
        get
        {
            var raw = Root.Element(Ns.P + "cSld")?.Element(Ns.P + "bg")
                ?.Element(Ns.P + "bgPr")?.Element(Ns.A + "solidFill")
                ?.Element(Ns.A + "srgbClr")?.Attr("val");

            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set
        {
            var common = Root.Element(Ns.P + "cSld")
                ?? throw new OfficeNetException($"{Part.Name} has no p:cSld.");

            common.Elements(Ns.P + "bg").Remove();

            if (value is not null)
            {
                // p:bg must be the first child of p:cSld, before p:spTree.
                common.AddFirst(new XElement(Ns.P + "bg",
                    new XElement(Ns.P + "bgPr",
                        new XElement(Ns.A + "solidFill",
                            new XElement(Ns.A + "srgbClr",
                                new XAttribute("val", value.Value.ToHex()))),
                        new XElement(Ns.A + "effectLst"))));
            }

            _presentation.Touch();
        }
    }

    /// <summary>True when the slide is skipped during a slideshow.</summary>
    public bool IsHidden
    {
        get => Root.Attr("show") is "0";
        set
        {
            Root.SetAttributeValue("show", value ? "0" : null);
            _presentation.Touch();
        }
    }

    /// <summary>All of the slide's text, in shape order.</summary>
    public string ExtractText()
    {
        var builder = new StringBuilder();

        foreach (var shape in Shapes)
        {
            var text = shape.Text;

            if (text.Length > 0)
            {
                builder.Append(text).Append('\n');
            }
        }

        return builder.ToString().TrimEnd();
    }

    public override string ToString() =>
        $"Slide {SlideNumber}{(Title?.Text is { Length: > 0 } t ? $": {t}" : "")}";
}
