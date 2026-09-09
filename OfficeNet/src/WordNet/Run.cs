// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace WordNet;

/// <summary>The kinds of break a run can contain.</summary>
public enum BreakType
{
    /// <summary>A line break within the paragraph.</summary>
    Line,

    /// <summary>A page break.</summary>
    Page,

    /// <summary>A column break.</summary>
    Column,

    /// <summary>A line break that clears a floating object.</summary>
    TextWrapping,
}

/// <summary>
/// A run: a span of text sharing one set of character formatting.
/// </summary>
/// <remarks>
/// A run is the smallest formattable unit in WordprocessingML, and a paragraph is a sequence of
/// them. Word splits runs freely — typing in the middle of a word can produce three runs where
/// there was one, and a spell-check mark splits one more — so code that assumes "one run per
/// logical span" is reading a document it authored, not one a person edited.
/// </remarks>
public sealed class Run
{
    private readonly WordDocument _document;

    internal Run(WordDocument document, XElement element)
    {
        _document = document;
        Element = element;
        Format = new RunFormat(() => XmlUtil.GetOrCreate(element, Ns.W + "rPr", RunOrder), document.Touch);
    }

    private static readonly XName[] RunOrder =
    [
        Ns.W + "rPr", Ns.W + "br", Ns.W + "t", Ns.W + "tab", Ns.W + "drawing",
    ];

    /// <summary>The underlying <c>w:r</c> element.</summary>
    public XElement Element { get; }

    /// <summary>The run's character formatting.</summary>
    public RunFormat Format { get; }

    /// <summary>
    /// The run's text, with tabs and breaks rendered as their characters.
    /// </summary>
    /// <remarks>
    /// Setting this replaces every text child with one <c>w:t</c>, but embedded newlines become
    /// <c>w:br</c> elements rather than literal newline characters — WordprocessingML has no
    /// newline inside <c>w:t</c>, and a document containing one opens with the line break silently
    /// collapsed to a space.
    /// </remarks>
    public string Text
    {
        get
        {
            var builder = new StringBuilder();

            foreach (var child in Element.Elements())
            {
                if (child.Name == Ns.W + "t")
                {
                    builder.Append(child.Value);
                }
                else if (child.Name == Ns.W + "tab")
                {
                    builder.Append('\t');
                }
                else if (child.Name == Ns.W + "br")
                {
                    builder.Append('\n');
                }
                else if (child.Name == Ns.W + "cr")
                {
                    builder.Append('\n');
                }
                else if (child.Name == Ns.W + "noBreakHyphen")
                {
                    builder.Append('‑');
                }
                else if (child.Name == Ns.W + "softHyphen")
                {
                    builder.Append('­');
                }
            }

            return builder.ToString();
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var properties = Element.Element(Ns.W + "rPr");
            Element.RemoveNodes();

            if (properties is not null)
            {
                Element.Add(properties);
            }

            AppendText(value);
            _document.Touch();
        }
    }

    /// <summary>Appends text, turning newlines into breaks and tabs into tab elements.</summary>
    public Run AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var segments = normalized.Split('\n');

        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                Element.Add(new XElement(Ns.W + "br"));
            }

            foreach (var (piece, isTab) in SplitTabs(segments[i]))
            {
                if (isTab)
                {
                    Element.Add(new XElement(Ns.W + "tab"));
                }
                else if (piece.Length > 0)
                {
                    Element.Add(XmlUtil.TextElement(Ns.W + "t", piece));
                }
            }
        }

        _document.Touch();
        return this;
    }

    private static IEnumerable<(string Piece, bool IsTab)> SplitTabs(string segment)
    {
        var start = 0;

        for (var i = 0; i < segment.Length; i++)
        {
            if (segment[i] != '\t')
            {
                continue;
            }

            yield return (segment[start..i], false);
            yield return (string.Empty, true);
            start = i + 1;
        }

        yield return (segment[start..], false);
    }

    /// <summary>Appends a break.</summary>
    public Run AddBreak(BreakType type = BreakType.Line)
    {
        var element = new XElement(Ns.W + "br");

        // A line break writes no w:type attribute; the others must.
        var typeName = type switch
        {
            BreakType.Page => "page",
            BreakType.Column => "column",
            BreakType.TextWrapping => "textWrapping",
            _ => null,
        };

        if (typeName is not null)
        {
            element.SetAttributeValue(Ns.W + "type", typeName);
        }

        Element.Add(element);
        _document.Touch();
        return this;
    }

    /// <summary>Appends a tab.</summary>
    public Run AddTab()
    {
        Element.Add(new XElement(Ns.W + "tab"));
        _document.Touch();
        return this;
    }

    /// <summary>Makes the run bold.</summary>
    public Run WithBold(bool value = true)
    {
        Format.Bold = value;
        return this;
    }

    /// <summary>Makes the run italic.</summary>
    public Run WithItalic(bool value = true)
    {
        Format.Italic = value;
        return this;
    }

    /// <summary>Underlines the run.</summary>
    public Run WithUnderline(UnderlineStyle style = UnderlineStyle.Single)
    {
        Format.Underline = style;
        return this;
    }

    /// <summary>Sets the run's colour.</summary>
    public Run WithColor(OfficeColor color)
    {
        Format.Color = color;
        return this;
    }

    /// <summary>Sets the run's font.</summary>
    public Run WithFont(string name, double? sizePoints = null)
    {
        Format.FontName = name;

        if (sizePoints is not null)
        {
            Format.FontSize = Units.Pt(sizePoints.Value);
        }

        return this;
    }

    /// <summary>Sets the run's font size in points.</summary>
    public Run WithSize(double points)
    {
        Format.FontSize = Units.Pt(points);
        return this;
    }

    /// <summary>Highlights the run.</summary>
    public Run WithHighlight(string colorName)
    {
        Format.Highlight = colorName;
        return this;
    }

    /// <summary>
    /// Adds a picture to the run.
    /// </summary>
    /// <param name="imageBytes">The image file.</param>
    /// <param name="width">The drawn width; the natural size is used when both are omitted.</param>
    /// <param name="height">The drawn height; computed from the aspect ratio when only one is given.</param>
    /// <param name="altText">Alternative text for accessibility.</param>
    /// <param name="wrap">
    /// How body text flows around it. <c>null</c>, the default, makes the picture inline — laid out
    /// as if it were one very large character. Anything else makes it float, and
    /// <see cref="Drawing.Shape.MoveTo"/> on the returned handle positions it.
    /// </param>
    public Run AddPicture(byte[] imageBytes, Length? width = null, Length? height = null,
        string? altText = null, Drawing.TextWrap? wrap = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var (relationshipId, info) = _document.AddImage(imageBytes);

        var (finalWidth, finalHeight) = ResolveSize(info, width, height);

        // Each drawing needs a document-unique id. Word tolerates duplicates in some versions and
        // reports the file as corrupt in others.
        var drawingId = _document.NextDrawingId();
        var name = $"Picture {drawingId}";

        var graphic = new XElement(Ns.A + "graphic",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XElement(Ns.A + "graphicData",
                new XAttribute("uri", Ns.Pic.NamespaceName),
                new XElement(Ns.Pic + "pic",
                    new XAttribute(XNamespace.Xmlns + "pic", Ns.Pic.NamespaceName),
                    new XElement(Ns.Pic + "nvPicPr",
                        new XElement(Ns.Pic + "cNvPr",
                            new XAttribute("id", "0"),
                            new XAttribute("name", name),
                            new XAttribute("descr", altText ?? string.Empty)),
                        new XElement(Ns.Pic + "cNvPicPr")),
                    new XElement(Ns.Pic + "blipFill",
                        new XElement(Ns.A + "blip",
                            new XAttribute(Ns.R + "embed", relationshipId)),
                        new XElement(Ns.A + "stretch",
                            new XElement(Ns.A + "fillRect"))),
                    new XElement(Ns.Pic + "spPr",
                        new XElement(Ns.A + "xfrm",
                            new XElement(Ns.A + "off",
                                new XAttribute("x", "0"), new XAttribute("y", "0")),
                            new XElement(Ns.A + "ext",
                                new XAttribute("cx", finalWidth.Emu),
                                new XAttribute("cy", finalHeight.Emu))),
                        new XElement(Ns.A + "prstGeom",
                            new XAttribute("prst", "rect"),
                            new XElement(Ns.A + "avLst"))))));

        // A picture and a shape differ in what the graphicData holds and in nothing else, so the
        // container — inline or anchored, with its five mandatory attributes — is built once, in one
        // place, rather than written out again here with one of them quietly missing.
        var drawing = new XElement(Ns.W + "drawing",
            Drawing.Shape.BuildContainer(drawingId, name, finalWidth, finalHeight, altText, wrap, graphic));

        Element.Add(drawing);
        _document.Touch();
        return this;
    }

    /// <summary>Adds a picture from a file.</summary>
    public Run AddPicture(string path, Length? width = null, Length? height = null,
        string? altText = null, Drawing.TextWrap? wrap = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return AddPicture(File.ReadAllBytes(path), width, height, altText, wrap);
    }

    /// <summary>The floating pictures and shapes this run holds.</summary>
    public IReadOnlyList<Drawing.Shape> Drawings =>
    [
        .. Element.Elements(Ns.W + "drawing").Select(d => new Drawing.Shape(_document, d)),
    ];

    private static (Length Width, Length Height) ResolveSize(ImageInfo info, Length? width, Length? height)
    {
        // Giving one dimension and letting the other follow the aspect ratio is what a caller
        // almost always wants; scaling both independently distorts the picture.
        if (width is not null && height is not null)
        {
            return (width.Value, height.Value);
        }

        if (width is not null)
        {
            return (width.Value, Length.FromEmu((long)(width.Value.Emu / info.AspectRatio)));
        }

        if (height is not null)
        {
            return (Length.FromEmu((long)(height.Value.Emu * info.AspectRatio)), height.Value);
        }

        return (info.NaturalWidth, info.NaturalHeight);
    }

    /// <summary>True when the run contains a drawing rather than text.</summary>
    public bool HasDrawing => Element.Element(Ns.W + "drawing") is not null;

    /// <summary>Removes the run from its paragraph.</summary>
    public void Remove()
    {
        Element.Remove();
        _document.Touch();
    }

    public override string ToString() => Text;
    /// <summary>Attaches a comment to this run alone.</summary>
    /// <param name="text">The comment's text.</param>
    /// <param name="author">Who is commenting.</param>
    /// <param name="initials">Shown in the margin; derived from the author when omitted.</param>
    /// <remarks>
    /// The narrower counterpart to <see cref="Paragraph.AddComment"/>: the range brackets this run
    /// rather than the whole paragraph, which is what you want when commenting on one phrase.
    /// </remarks>
    public Notes.Comment AddComment(string text, string author = "Author", string? initials = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        var comment = _document.Comments.Add(text, author, initials);
        var id = XmlUtil.Num(comment.Id);

        Element.AddBeforeSelf(new XElement(Ns.W + "commentRangeStart",
            new XAttribute(Ns.W + "id", id)));

        Element.AddAfterSelf(new XElement(Ns.W + "r",
            new XElement(Ns.W + "rPr", XmlUtil.ValElement(Ns.W + "rStyle", "CommentReference")),
            new XElement(Ns.W + "commentReference", new XAttribute(Ns.W + "id", id))));

        // Inserted after the run but before the reference above, so the order ends up
        // start, run, end, reference — which is what Word expects.
        Element.AddAfterSelf(new XElement(Ns.W + "commentRangeEnd",
            new XAttribute(Ns.W + "id", id)));

        _document.Touch();
        return comment;
    }

}
