// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using WordNet.Sections;

namespace WordNet.Drawing;

/// <summary>
/// A text watermark: a large, pale, diagonal word behind every page's text.
/// </summary>
/// <remarks>
/// <para>
/// Word keeps a watermark in the <em>header</em>, not in the body. A header is drawn on every page
/// of its section, and a shape anchored in it with "behind text" wrapping is positioned against the
/// page rather than the header, so one shape covers every page. That is the whole trick, and it is
/// why a watermark set in Word survives reflowing the document.
/// </para>
/// <para>
/// The shape here is DrawingML (<c>wps:wsp</c>), the same kind <see cref="Shape"/> writes, rather
/// than the VML WordArt object Word's own dialog inserts. Word 2010 and later read both. It is
/// found again by name, so <see cref="Find"/> and <see cref="Remove"/> recognise only watermarks
/// this library wrote — a watermark made in Word is VML and is left alone.
/// </para>
/// </remarks>
internal static class Watermark
{
    internal const string ShapeName = "OfficeNet Watermark";

    internal static void Add(WordDocument document, string text, OfficeColor? color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Remove(document);

        foreach (var (section, header) in Headers(document))
        {
            AddTo(document, section, header, text, color ?? OfficeColor.FromRgb(0xD9, 0xD9, 0xD9));
        }
    }

    internal static string? Find(WordDocument document) =>
        Shapes(document).Select(s => string.Concat(s.Drawing
                .Descendants(Ns.W + "t").Select(t => t.Value)))
            .FirstOrDefault();

    internal static bool Remove(WordDocument document)
    {
        var shapes = Shapes(document).ToList();

        foreach (var shape in shapes)
        {
            // The run that held the anchor goes with it; an empty run is noise in the header.
            var run = shape.Drawing.Parent;
            shape.Drawing.Remove();

            if (run is not null && run.Name == Ns.W + "r" && !run.Elements().Any(e => e.Name != Ns.W + "rPr"))
            {
                run.Remove();
            }
        }

        if (shapes.Count > 0)
        {
            document.Touch();
        }

        return shapes.Count > 0;
    }

    private static IEnumerable<Shape> Shapes(WordDocument document) =>
        document.DocumentPart.RelatedPartsByType(RelationshipTypes.Header)
            .Select(p => p.Xml.Root)
            .OfType<XElement>()
            .SelectMany(r => r.Descendants(Ns.W + "drawing"))
            .Where(d => d.Descendants(Ns.Wp + "docPr").Any(p => p.Attr("name") == ShapeName))
            .Select(d => new Shape(document, d));

    /// <summary>
    /// The headers a watermark must go into: every header part a section actually references,
    /// once each, plus a new default header for the first section when it has none.
    /// </summary>
    /// <remarks>
    /// A section without its own reference inherits the previous section's header, so it already
    /// shows that header's watermark. Calling <c>GetHeader()</c> on it would give it a new, empty
    /// header and break the inheritance — exactly what must not happen here.
    /// </remarks>
    private static IEnumerable<(Section Section, XElement Root)> Headers(WordDocument document)
    {
        var sections = document.Sections;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!sections[0].Properties.Elements(Ns.W + "headerReference").Any())
        {
            _ = sections[0].GetHeader();
        }

        foreach (var section in sections)
        {
            foreach (var reference in section.Properties.Elements(Ns.W + "headerReference"))
            {
                if (reference.Attr(Ns.R + "id") is not { } id || !seen.Add(id) ||
                    document.DocumentPart.RelatedPart(id)?.Xml.Root is not { } root)
                {
                    continue;
                }

                yield return (section, root);
            }
        }
    }

    private static void AddTo(WordDocument document, Section section, XElement header, string text,
        OfficeColor color)
    {
        var paragraphElement = header.Elements(Ns.W + "p").FirstOrDefault();

        if (paragraphElement is null)
        {
            paragraphElement = new XElement(Ns.W + "p");
            header.Add(paragraphElement);
        }

        var paragraph = new Paragraph(document, paragraphElement);

        // As wide as the text column and a quarter as tall, set diagonally from bottom-left to
        // top-right. The font is sized to the box, not the other way round: a long word in a
        // fixed size runs off the page.
        var width = section.ContentWidth;
        var height = Length.FromPoints(width.Points / 4);
        var fontPoints = Math.Clamp(Math.Min(height.Points * 0.8, width.Points / (text.Length * 0.62)), 12, 200);

        var shape = paragraph.AddShape(ShapeGeometry.Rectangle, width, height, TextWrap.BehindText,
            altText: $"Watermark: {text}");
        shape.Name = ShapeName;
        shape.Wsp.Element(Ns.Wps + "cNvSpPr")!.SetAttributeValue("txBox", "1");
        shape.Rotation = 315;
        shape.MoveTo(Length.Zero, Length.FromPoints((section.ContentHeight.Points - height.Points) / 2),
            HorizontalAnchor.Margin, VerticalAnchor.Margin);

        var inner = shape.AddParagraph();
        inner.Alignment = ParagraphAlignment.Center;
        inner.Format.SpaceAfter = Length.Zero;
        var run = inner.AddRun(text);
        run.Format.FontSize = Length.FromPoints(fontPoints);
        run.Format.Color = color;
        run.Format.Bold = true;

        // Centre the text vertically too, so the word sits on the page's diagonal.
        shape.Wsp.Element(Ns.Wps + "bodyPr")?.SetAttributeValue("anchor", "ctr");

        document.Touch();
    }
}
