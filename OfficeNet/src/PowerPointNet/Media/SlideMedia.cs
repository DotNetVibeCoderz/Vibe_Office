// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using PowerPointNet.Shapes;

namespace PowerPointNet.Media;

/// <summary>What kind of media a slide holds.</summary>
public enum MediaKind
{
    /// <summary>Video.</summary>
    Video,

    /// <summary>Audio.</summary>
    Audio,

    /// <summary>A video hosted online, linked rather than embedded.</summary>
    OnlineVideo,
}

/// <summary>
/// A video or audio clip on a slide.
/// </summary>
/// <remarks>
/// <para>
/// Media is the one place PresentationML needs the same part referenced twice from one shape.
/// <c>a:videoFile</c> (in the picture's non-visual properties) is what PowerPoint 2007 reads, and
/// <c>p14:media</c> (in an extension list) is what 2010 and later read. A file with only one of
/// them plays in some versions and shows a black rectangle in others, so both are written.
/// </para>
/// <para>
/// The poster frame is a real requirement, not decoration: a media shape is a <c>p:pic</c>, and a
/// picture with no image is what produces the black rectangle even when the media itself is fine.
/// </para>
/// </remarks>
public sealed class SlideMedia : Shape
{
    internal SlideMedia(Presentation presentation, XElement element, MediaKind kind)
        : base(presentation, element)
    {
        MediaType = kind;
    }

    /// <summary>
    /// Whether this is video, audio or a linked online video.
    /// </summary>
    /// <remarks>
    /// Named <c>MediaType</c> rather than <c>Kind</c> because <see cref="Shape.Kind"/> already
    /// reports what element the shape is, and a media clip is a picture as far as that is
    /// concerned.
    /// </remarks>
    public MediaKind MediaType { get; }

    /// <summary>The relationship id of the media part, or the external link.</summary>
    public string? MediaRelationshipId =>
        Element.Element(Ns.P + "nvPicPr")?.Element(Ns.P + "nvPr")
            ?.Element(Ns.A + "videoFile")?.Attr(Ns.R + "link")
        ?? Element.Element(Ns.P + "nvPicPr")?.Element(Ns.P + "nvPr")
            ?.Element(Ns.A + "audioFile")?.Attr(Ns.R + "link");

    /// <summary>
    /// True when the clip starts by itself rather than on a click.
    /// </summary>
    /// <remarks>
    /// Playback is not a property of the shape: it is a command node in the slide's
    /// <c>p:timing</c> whose start condition is either <c>delay="0"</c> or
    /// <c>delay="indefinite"</c>. Reading the shape's own markup for it always says no.
    /// </remarks>
    public bool AutoPlay
    {
        get
        {
            var slide = OwningSlide();

            if (slide is null)
            {
                return false;
            }

            // The media node names the shape this timing belongs to; without matching on it a
            // slide holding two clips would report both as whatever the first one is.
            var timing = slide.Root.Element(Ns.P + "timing");

            var owns = timing?.Descendants(Ns.P14 + "media")
                .Any(m => m.Attr("spid") == Id.ToString()) ?? false;

            if (!owns)
            {
                return false;
            }

            // The outermost par's start condition is the switch; an inner one is always delay="0".
            return timing!.Descendants(Ns.P + "cTn")
                .Where(c => c.Attr("nodeType") is null && c.Attr("fill") == "hold")
                .Select(c => c.Element(Ns.P + "stCondLst")?.Element(Ns.P + "cond")?.Attr("delay"))
                .FirstOrDefault() == "0";
        }
        set
        {
            OwningSlide()?.SetMediaAutoPlay(this, value);
        }
    }

    private Slide? OwningSlide() =>
        Presentation.Slides.FirstOrDefault(s => s.Root.Descendants().Contains(Element));

    /// <summary>
    /// Identifies a picture that is really a media clip.
    /// </summary>
    /// <remarks>
    /// A video and an image are both <c>p:pic</c>; only the videoFile or audioFile element inside
    /// the non-visual properties distinguishes them. Without this check every clip comes back from
    /// a reopened deck as a plain <see cref="Picture"/> and its media relationship looks unused.
    /// </remarks>
    internal static MediaKind? DetectKind(XElement picture, OpcPart slidePart)
    {
        var nonVisual = picture.Element(Ns.P + "nvPicPr")?.Element(Ns.P + "nvPr");

        if (nonVisual is null)
        {
            return null;
        }

        if (nonVisual.Element(Ns.A + "audioFile") is not null)
        {
            return MediaKind.Audio;
        }

        if (nonVisual.Element(Ns.A + "videoFile") is not { } video)
        {
            return null;
        }

        // Embedded and online video use the same element; only the relationship's target mode
        // separates them, so the slide part has to be consulted rather than the markup alone.
        var id = video.Attr(Ns.R + "link");

        if (id is null)
        {
            return MediaKind.Video;
        }

        var relationship = slidePart.RelationshipById(id);

        return relationship?.TargetMode == TargetMode.External
            ? MediaKind.OnlineVideo
            : MediaKind.Video;
    }

    internal static SlideMedia Create(Presentation presentation, Slide slide, XElement shapeTree,
        uint id, byte[] media, string extension, byte[]? posterImage, MediaKind kind,
        Length left, Length top, Length width, Length height)
    {
        var contentType = ContentTypes.ForExtension(extension)
            ?? throw new OfficeNetNotSupportedException(
                $"'{extension}' is not a media type OfficeNet can declare. Use mp4, m4v, mov, " +
                "avi, wmv for video, or mp3, wav, m4a for audio.");

        var partName = presentation.Package.NextPartName($"/ppt/media/media{{0}}.{extension}");
        var part = presentation.Package.AddPart(partName, contentType, media);

        // Two relationships to one part, with different types. The video relationship is what the
        // player follows; the media relationship is what the 2010 extension reads.
        var videoRelationship = slide.Part.AddRelationship(part,
            kind == MediaKind.Audio ? RelationshipTypes.Audio : RelationshipTypes.Video);

        var mediaRelationship = slide.Part.AddRelationship(part, RelationshipTypes.Media);

        // A media shape is a picture, and a picture with no image renders as a black rectangle.
        var poster = posterImage ?? PosterFrame(kind, width, height);
        var (posterId, _) = presentation.AddImage(slide.Part, poster);

        var element = BuildPicture(id, kind, videoRelationship.Id, mediaRelationship.Id, posterId,
            left, top, width, height);

        shapeTree.Add(element);

        var shape = new SlideMedia(presentation, element, kind);
        slide.RegisterMedia(shape);
        presentation.Touch();
        return shape;
    }

    internal static SlideMedia CreateOnline(Presentation presentation, Slide slide,
        XElement shapeTree, uint id, string url, byte[]? posterImage,
        Length left, Length top, Length width, Length height)
    {
        // An online video is a link, not a part: the deck stays small and the clip stays current.
        var relationship = slide.Part.AddExternalRelationship(RelationshipTypes.Video, url);

        var poster = posterImage ?? PosterFrame(MediaKind.OnlineVideo, width, height);
        var (posterId, _) = presentation.AddImage(slide.Part, poster);

        var element = BuildPicture(id, MediaKind.OnlineVideo, relationship.Id, relationship.Id,
            posterId, left, top, width, height);

        shapeTree.Add(element);

        var shape = new SlideMedia(presentation, element, MediaKind.OnlineVideo);
        presentation.Touch();
        return shape;
    }

    private static XElement BuildPicture(uint id, MediaKind kind, string mediaRelationshipId,
        string extensionRelationshipId, string posterRelationshipId,
        Length left, Length top, Length width, Length height)
    {
        var fileElement = kind == MediaKind.Audio ? Ns.A + "audioFile" : Ns.A + "videoFile";

        var nonVisual = new XElement(Ns.P + "nvPr",
            new XElement(fileElement, new XAttribute(Ns.R + "link", mediaRelationshipId)),
            // p14:media is the PowerPoint 2010 form. Without it, 2010 and later show the poster
            // frame and no play button.
            new XElement(Ns.A + "extLst",
                new XElement(Ns.A + "ext",
                    new XAttribute("uri", "{DAA4B4D4-6D71-4841-9C94-3DE7FCFB9230}"),
                    new XElement(Ns.P14 + "media",
                        new XAttribute(XNamespace.Xmlns + "p14", Ns.P14.NamespaceName),
                        new XAttribute(Ns.R + "embed", extensionRelationshipId)))));

        return new XElement(Ns.P + "pic",
            new XElement(Ns.P + "nvPicPr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", $"{kind} {id}"),
                    // A hyperlink-on-click with no action is what gives the shape its play button
                    // in the 2007 model.
                    new XElement(Ns.A + "hlinkClick",
                        new XAttribute(Ns.R + "id", string.Empty),
                        new XAttribute("action", "ppaction://media"))),
                new XElement(Ns.P + "cNvPicPr",
                    new XElement(Ns.A + "picLocks", new XAttribute("noChangeAspect", "1"))),
                nonVisual),
            new XElement(Ns.P + "blipFill",
                new XElement(Ns.A + "blip", new XAttribute(Ns.R + "embed", posterRelationshipId)),
                new XElement(Ns.A + "stretch", new XElement(Ns.A + "fillRect"))),
            new XElement(Ns.P + "spPr",
                new XElement(Ns.A + "xfrm",
                    new XElement(Ns.A + "off",
                        new XAttribute("x", left.Emu), new XAttribute("y", top.Emu)),
                    new XElement(Ns.A + "ext",
                        new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu))),
                new XElement(Ns.A + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(Ns.A + "avLst"))));
    }

    /// <summary>
    /// Draws a placeholder poster frame: a dark panel with a play triangle or a speaker.
    /// </summary>
    /// <remarks>
    /// Generated rather than shipped as a resource so the library carries no binary assets. It is
    /// only a fallback — a caller with a real thumbnail should pass one, because the poster is what
    /// the audience sees until the clip is started.
    /// </remarks>
    private static byte[] PosterFrame(MediaKind kind, Length width, Length height)
    {
        var pixelWidth = Math.Clamp((int)Math.Round(width.Pixels), 64, 1280);
        var pixelHeight = Math.Clamp((int)Math.Round(height.Pixels), 48, 720);

        var rgb = new byte[pixelWidth * pixelHeight * 3];

        // A dark neutral panel, so a white glyph reads against it at any projector brightness.
        for (var i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = 0x25;
            rgb[i + 1] = 0x2A;
            rgb[i + 2] = 0x33;
        }

        var centreX = pixelWidth / 2.0;
        var centreY = pixelHeight / 2.0;
        var size = Math.Min(pixelWidth, pixelHeight) * 0.28;

        for (var y = 0; y < pixelHeight; y++)
        {
            for (var x = 0; x < pixelWidth; x++)
            {
                if (!InGlyph(kind, x - centreX, y - centreY, size))
                {
                    continue;
                }

                var offset = (y * pixelWidth + x) * 3;
                rgb[offset] = 0xF2;
                rgb[offset + 1] = 0xF5;
                rgb[offset + 2] = 0xFA;
            }
        }

        return PdfNet.Text.ImageExtractor.EncodePng(rgb, pixelWidth, pixelHeight);
    }

    private static bool InGlyph(MediaKind kind, double dx, double dy, double size)
    {
        if (kind == MediaKind.Audio)
        {
            // A speaker: a small square body with a triangular cone opening to the right.
            if (dx > -size * 0.7 && dx < -size * 0.25 && Math.Abs(dy) < size * 0.35)
            {
                return true;
            }

            return dx >= -size * 0.25 && dx < size * 0.5 &&
                   Math.Abs(dy) < size * 0.35 + (dx + size * 0.25) * 0.8;
        }

        // A play triangle pointing right: inside the vertical span, and the x extent shrinks
        // linearly as the point moves away from the centre line.
        var half = size * 0.9;

        return dx >= -size * 0.5 && dx <= size * 0.7 &&
               Math.Abs(dy) <= half * (1 - (dx + size * 0.5) / (size * 1.2));
    }

    public override string ToString() => $"{MediaType} \"{Name}\"";
}
