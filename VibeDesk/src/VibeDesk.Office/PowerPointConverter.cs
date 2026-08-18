using System.Net;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using VibeDesk.Application.Documents;
using Drawing = DocumentFormat.OpenXml.Drawing;
using VibeSlide = VibeDesk.Application.Documents.Slide;
using OpenXmlSlide = DocumentFormat.OpenXml.Presentation.Slide;

namespace VibeDesk.Office;

/// <summary>
/// .pptx to slides and back.
/// </summary>
/// <remarks>
/// The lossiest of the three, because a VibeDesk slide is a list of positioned elements while a
/// PowerPoint slide is a shape tree bound to a layout and a master. Carried: slide order, the text of
/// every text-bearing shape, and speaker notes. Dropped: images, charts, tables, SmartArt, themes,
/// animations, transitions and exact positioning.
/// </remarks>
internal static class PowerPointConverter
{
    // Slide dimensions in EMU: 1 inch is 914400, and a 16:9 deck is 13.333in by 7.5in.
    private const long SlideWidth = 12192000;
    private const long SlideHeight = 6858000;

    public static PresentationModel Read(Stream source)
    {
        using var package = PresentationDocument.Open(source, isEditable: false);

        var presentationPart = package.PresentationPart;
        var model = new PresentationModel { Slides = [] };

        if (presentationPart?.Presentation?.SlideIdList is null) return Fallback(model);

        foreach (var slideId in presentationPart.Presentation.SlideIdList.Elements<SlideId>())
        {
            if (slideId.RelationshipId?.Value is not { } relationshipId) continue;
            if (presentationPart.GetPartById(relationshipId) is not SlidePart part) continue;

            model.Slides.Add(ReadSlide(part));
        }

        return Fallback(model);
    }

    private static PresentationModel Fallback(PresentationModel model)
    {
        // A deck with no slides cannot be opened in the editor, so an unreadable file becomes an
        // empty deck rather than a broken one.
        if (model.Slides.Count == 0) model.Slides.Add(new VibeSlide());

        return model;
    }

    private static VibeSlide ReadSlide(SlidePart part)
    {
        var texts = new List<string>();

        // Same caution as the worksheet: a slide part in a malformed package may have no root.
        foreach (var shape in part.Slide?.Descendants<Shape>() ?? [])
        {
            var text = ShapeText(shape);
            if (text.Count > 0) texts.Add(string.Join("\n", text));
        }

        var slide = new VibeSlide
        {
            Layout = texts.Count > 1 ? "titleContent" : "title",
            Notes = Notes(part),
        };

        if (texts.Count > 0)
        {
            slide.Elements.Add(new SlideElement
            {
                Type = "text",
                X = 8,
                Y = texts.Count > 1 ? 12 : 38,
                W = 84,
                H = 18,
                Text = $"<h1>{WebUtility.HtmlEncode(texts[0])}</h1>",
            });
        }

        if (texts.Count > 1)
        {
            var body = string.Join("\n", texts.Skip(1));

            slide.Elements.Add(new SlideElement
            {
                Type = "text",
                X = 8,
                Y = 36,
                W = 84,
                H = 52,
                Text = string.Concat(body
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => $"<p>{WebUtility.HtmlEncode(line.Trim())}</p>")),
            });
        }

        return slide;
    }

    private static List<string> ShapeText(Shape shape)
    {
        var lines = new List<string>();

        foreach (var paragraph in shape.Descendants<Drawing.Paragraph>())
        {
            var text = string.Concat(paragraph.Descendants<Drawing.Text>().Select(t => t.Text));

            if (!string.IsNullOrWhiteSpace(text)) lines.Add(text.Trim());
        }

        return lines;
    }

    private static string? Notes(SlidePart part)
    {
        var notes = part.NotesSlidePart?.NotesSlide;
        if (notes is null) return null;

        var text = string.Join("\n", notes
            .Descendants<Drawing.Paragraph>()
            .Select(p => string.Concat(p.Descendants<Drawing.Text>().Select(t => t.Text)))
            .Where(line => !string.IsNullOrWhiteSpace(line)));

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    // ─────────────────────────────────── write ───────────────────────────────────

    public static void Write(Stream destination, PresentationModel model, string title)
    {
        using var package = PresentationDocument.Create(destination, PresentationDocumentType.Presentation);

        var presentationPart = package.AddPresentationPart();
        presentationPart.Presentation = new Presentation();

        // A deck needs a master and a layout before any slide will open, even when every slide is
        // built from scratch and references neither for its content.
        var (masterPart, layoutPart) = AddMasterAndLayout(presentationPart);

        var slideIdList = new SlideIdList();
        uint slideId = 256;

        var slides = model.Slides.Count > 0 ? model.Slides : [new VibeSlide()];

        foreach (var slide in slides)
        {
            var part = presentationPart.AddNewPart<SlidePart>();
            part.Slide = BuildSlide(slide, title, slideIdList.ChildElements.Count == 0);
            part.AddPart(layoutPart);

            slideIdList.AppendChild(new SlideId
            {
                Id = slideId++,
                RelationshipId = presentationPart.GetIdOfPart(part),
            });
        }

        presentationPart.Presentation.AppendChild(new SlideMasterIdList(new SlideMasterId
        {
            Id = 2147483648,
            RelationshipId = presentationPart.GetIdOfPart(masterPart),
        }));

        presentationPart.Presentation.AppendChild(slideIdList);
        presentationPart.Presentation.AppendChild(new SlideSize { Cx = (int)SlideWidth, Cy = (int)SlideHeight });
        presentationPart.Presentation.AppendChild(new NotesSize { Cx = SlideHeight, Cy = SlideWidth });

        presentationPart.Presentation.Save();
    }

    private static (SlideMasterPart Master, SlideLayoutPart Layout) AddMasterAndLayout(
        PresentationPart presentationPart)
    {
        var masterPart = presentationPart.AddNewPart<SlideMasterPart>();
        var layoutPart = masterPart.AddNewPart<SlideLayoutPart>();

        layoutPart.SlideLayout = new SlideLayout(
            new CommonSlideData(new ShapeTree(
                new NonVisualGroupShapeProperties(
                    new NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                    new NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties(new Drawing.TransformGroup()))),
            new ColorMapOverride(new Drawing.MasterColorMapping()));

        masterPart.SlideMaster = new SlideMaster(
            new CommonSlideData(new ShapeTree(
                new NonVisualGroupShapeProperties(
                    new NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                    new NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties(new Drawing.TransformGroup()))),
            new ColorMap
            {
                Background1 = Drawing.ColorSchemeIndexValues.Light1,
                Text1 = Drawing.ColorSchemeIndexValues.Dark1,
                Background2 = Drawing.ColorSchemeIndexValues.Light2,
                Text2 = Drawing.ColorSchemeIndexValues.Dark2,
                Accent1 = Drawing.ColorSchemeIndexValues.Accent1,
                Accent2 = Drawing.ColorSchemeIndexValues.Accent2,
                Accent3 = Drawing.ColorSchemeIndexValues.Accent3,
                Accent4 = Drawing.ColorSchemeIndexValues.Accent4,
                Accent5 = Drawing.ColorSchemeIndexValues.Accent5,
                Accent6 = Drawing.ColorSchemeIndexValues.Accent6,
                Hyperlink = Drawing.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = Drawing.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new SlideLayoutIdList(new SlideLayoutId
            {
                Id = 2147483649,
                RelationshipId = masterPart.GetIdOfPart(layoutPart),
            }));

        AddTheme(masterPart);

        return (masterPart, layoutPart);
    }

    private static OpenXmlSlide BuildSlide(VibeSlide source, string deckTitle, bool isFirst)
    {
        var tree = new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new Drawing.TransformGroup()));

        uint id = 2;

        foreach (var element in source.Elements.Where(e => e.Type == "text"))
        {
            var lines = HtmlOutline.Parse(element.Text)
                .Select(b => (Heading: b.Tag.StartsWith('h'), Text: string.Concat(b.Spans.Select(s => s.Text))))
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            if (lines.Count == 0) continue;

            tree.AppendChild(TextBox(
                id++,
                lines,
                // Percentages of the slide, which is how VibeDesk stores geometry.
                x: (long)(element.X / 100d * SlideWidth),
                y: (long)(element.Y / 100d * SlideHeight),
                width: (long)(element.W / 100d * SlideWidth),
                height: (long)(element.H / 100d * SlideHeight)));
        }

        if (id == 2 && isFirst)
        {
            // An empty first slide still deserves the deck's name on it.
            tree.AppendChild(TextBox(
                id, [(true, deckTitle)],
                x: SlideWidth / 12, y: SlideHeight / 3, width: SlideWidth * 5 / 6, height: SlideHeight / 4));
        }

        var slide = new OpenXmlSlide(new CommonSlideData(tree))
        {
            ColorMapOverride = new ColorMapOverride(new Drawing.MasterColorMapping()),
        };

        return slide;
    }

    private static Shape TextBox(
        uint id, List<(bool Heading, string Text)> lines, long x, long y, long width, long height)
    {
        var body = new TextBody(new Drawing.BodyProperties(), new Drawing.ListStyle());

        foreach (var (heading, text) in lines)
        {
            body.AppendChild(new Drawing.Paragraph(
                new Drawing.Run(
                    new Drawing.RunProperties
                    {
                        Language = "en-US",
                        FontSize = heading ? 3200 : 1800,
                        Bold = heading,
                    },
                    new Drawing.Text(text))));
        }

        return new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = $"Text {id}" },
                new NonVisualShapeDrawingProperties(new Drawing.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties()),
            new ShapeProperties(
                new Drawing.Transform2D(
                    new Drawing.Offset { X = x, Y = y },
                    new Drawing.Extents { Cx = width, Cy = height }),
                new Drawing.PresetGeometry(new Drawing.AdjustValueList())
                {
                    Preset = Drawing.ShapeTypeValues.Rectangle,
                }),
            body);
    }

    /// <summary>
    /// The minimum theme a master needs. PowerPoint refuses to open a deck whose master has no theme
    /// part, however little of the theme the slides actually use.
    /// </summary>
    private static void AddTheme(SlideMasterPart masterPart)
    {
        var part = masterPart.AddNewPart<ThemePart>();

        var scheme = new Drawing.ColorScheme(
            new Drawing.Dark1Color(new Drawing.SystemColor { Val = Drawing.SystemColorValues.WindowText }),
            new Drawing.Light1Color(new Drawing.SystemColor { Val = Drawing.SystemColorValues.Window }),
            new Drawing.Dark2Color(new Drawing.RgbColorModelHex { Val = "1F2933" }),
            new Drawing.Light2Color(new Drawing.RgbColorModelHex { Val = "F4F6F8" }),
            new Drawing.Accent1Color(new Drawing.RgbColorModelHex { Val = "2F8F73" }),
            new Drawing.Accent2Color(new Drawing.RgbColorModelHex { Val = "4FB3A8" }),
            new Drawing.Accent3Color(new Drawing.RgbColorModelHex { Val = "E08B58" }),
            new Drawing.Accent4Color(new Drawing.RgbColorModelHex { Val = "AB86E0" }),
            new Drawing.Accent5Color(new Drawing.RgbColorModelHex { Val = "4CBB92" }),
            new Drawing.Accent6Color(new Drawing.RgbColorModelHex { Val = "B23A2F" }),
            new Drawing.Hyperlink(new Drawing.RgbColorModelHex { Val = "2F6F8F" }),
            new Drawing.FollowedHyperlinkColor(new Drawing.RgbColorModelHex { Val = "8F6F9F" }))
        { Name = "VibeDesk" };

        var fonts = new Drawing.FontScheme(
            new Drawing.MajorFont(
                new Drawing.LatinFont { Typeface = "Calibri Light" },
                new Drawing.EastAsianFont { Typeface = string.Empty },
                new Drawing.ComplexScriptFont { Typeface = string.Empty }),
            new Drawing.MinorFont(
                new Drawing.LatinFont { Typeface = "Calibri" },
                new Drawing.EastAsianFont { Typeface = string.Empty },
                new Drawing.ComplexScriptFont { Typeface = string.Empty }))
        { Name = "VibeDesk" };

        var formats = new Drawing.FormatScheme(
            new Drawing.FillStyleList(
                new Drawing.SolidFill(new Drawing.SchemeColor { Val = Drawing.SchemeColorValues.PhColor }),
                new Drawing.SolidFill(new Drawing.SchemeColor { Val = Drawing.SchemeColorValues.PhColor }),
                new Drawing.SolidFill(new Drawing.SchemeColor { Val = Drawing.SchemeColorValues.PhColor })),
            new Drawing.LineStyleList(new Drawing.Outline(), new Drawing.Outline(), new Drawing.Outline()),
            new Drawing.EffectStyleList(
                new Drawing.EffectStyle(new Drawing.EffectList()),
                new Drawing.EffectStyle(new Drawing.EffectList()),
                new Drawing.EffectStyle(new Drawing.EffectList())),
            new Drawing.BackgroundFillStyleList(
                new Drawing.SolidFill(new Drawing.SchemeColor { Val = Drawing.SchemeColorValues.PhColor }),
                new Drawing.SolidFill(new Drawing.SchemeColor { Val = Drawing.SchemeColorValues.PhColor }),
                new Drawing.SolidFill(new Drawing.SchemeColor { Val = Drawing.SchemeColorValues.PhColor })))
        { Name = "VibeDesk" };

        part.Theme = new Drawing.Theme(
            new Drawing.ThemeElements(scheme, fonts, formats),
            new Drawing.ObjectDefaults(),
            new Drawing.ExtraColorSchemeList())
        { Name = "VibeDesk" };

        part.Theme.Save();
    }
}
