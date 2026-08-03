using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AutoWork.Tools;

/// <summary>
/// Builds a valid .pptx from scratch.
///
/// PowerPoint is stricter than Word or Excel about package structure: a deck without a slide
/// master, a layout and a theme will not open at all. This assembles the minimum complete set
/// of parts, then adds one slide per <see cref="SlideSpec"/> using a title-and-body layout.
/// </summary>
internal static class PowerPointBuilder
{
    // 16:9 in English Metric Units — 13.333in × 7.5in.
    private const int SlideWidth = 12_192_000;
    private const int SlideHeight = 6_858_000;

    private const int Margin = 838_200;
    private const int TitleTop = 685_800;
    private const int TitleHeight = 1_143_000;
    private const int BodyTop = 2_057_400;

    private const string Ink = "12151C";
    private const string Muted = "5B6472";
    private const string Accent = "5A47E0";

    public static void Build(string path, IReadOnlyList<SlideSpec> slides)
    {
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);

        var presentationPart = document.AddPresentationPart();
        presentationPart.Presentation = new P.Presentation();

        var masterPart = CreateSlideMaster(presentationPart);
        var layoutPart = masterPart.GetPartsOfType<SlideLayoutPart>().First();

        var slideIds = new P.SlideIdList();
        uint slideId = 256;

        foreach (var spec in slides)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.Slide = BuildSlide(spec);
            slidePart.AddPart(layoutPart);

            if (!string.IsNullOrWhiteSpace(spec.Notes))
                AttachNotes(slidePart, spec.Notes);

            slideIds.Append(new P.SlideId
            {
                Id = slideId++,
                RelationshipId = presentationPart.GetIdOfPart(slidePart),
            });
        }

        presentationPart.Presentation.Append(
            new P.SlideMasterIdList(new P.SlideMasterId
            {
                Id = 2147483648,
                RelationshipId = presentationPart.GetIdOfPart(masterPart),
            }),
            slideIds,
            new P.SlideSize { Cx = SlideWidth, Cy = SlideHeight },
            new P.NotesSize { Cx = SlideHeight, Cy = SlideWidth });

        presentationPart.Presentation.Save();
    }

    // ── Slides ────────────────────────────────────────────────────────────────────────────

    private static P.Slide BuildSlide(SlideSpec spec)
    {
        var tree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new D.TransformGroup()));

        tree.Append(BuildTitle(spec));

        if (spec.Bullets.Count > 0 || !string.IsNullOrWhiteSpace(spec.Subtitle))
            tree.Append(BuildBody(spec));

        return new P.Slide(
            new P.CommonSlideData(tree),
            new P.ColorMapOverride(new D.MasterColorMapping()));
    }

    private static P.Shape BuildTitle(SlideSpec spec)
    {
        var paragraph = new D.Paragraph(
            new D.ParagraphProperties { Alignment = D.TextAlignmentTypeValues.Left },
            TextRun(spec.Title, sizeHundredths: 4000, bold: true, colour: Ink));

        return BuildShape(2, "Title", P.PlaceholderValues.Title,
            x: Margin, y: TitleTop, cx: SlideWidth - 2 * Margin, cy: TitleHeight,
            paragraphs: [paragraph],
            anchor: D.TextAnchoringTypeValues.Bottom);
    }

    private static P.Shape BuildBody(SlideSpec spec)
    {
        var paragraphs = new List<D.Paragraph>();

        if (!string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            paragraphs.Add(new D.Paragraph(
                new D.ParagraphProperties { LeftMargin = 0, Indent = 0 },
                TextRun(spec.Subtitle, sizeHundredths: 2000, bold: false, colour: Accent)));
        }

        foreach (var bullet in spec.Bullets)
        {
            var properties = new D.ParagraphProperties { LeftMargin = 285_750, Indent = -285_750 };
            properties.Append(new D.CharacterBullet { Char = "•" });

            paragraphs.Add(new D.Paragraph(properties,
                TextRun(bullet, sizeHundredths: 1800, bold: false, colour: Muted)));
        }

        if (paragraphs.Count == 0) paragraphs.Add(new D.Paragraph());

        return BuildShape(3, "Content", P.PlaceholderValues.Body,
            x: Margin, y: BodyTop, cx: SlideWidth - 2 * Margin, cy: SlideHeight - BodyTop - Margin / 2,
            paragraphs: paragraphs,
            anchor: D.TextAnchoringTypeValues.Top);
    }

    private static P.Shape BuildShape(uint id, string name, P.PlaceholderValues placeholder,
        int x, int y, int cx, int cy, IReadOnlyList<D.Paragraph> paragraphs, D.TextAnchoringTypeValues anchor)
    {
        var body = new P.TextBody(
            new D.BodyProperties { Anchor = anchor, Wrap = D.TextWrappingValues.Square },
            new D.ListStyle());

        foreach (var paragraph in paragraphs) body.Append(paragraph);

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = placeholder })),
            new P.ShapeProperties(
                new D.Transform2D(
                    new D.Offset { X = x, Y = y },
                    new D.Extents { Cx = cx, Cy = cy }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }),
            body);
    }

    private static D.Run TextRun(string text, int sizeHundredths, bool bold, string colour) =>
        new(
            new D.RunProperties(new D.SolidFill(new D.RgbColorModelHex { Val = colour }))
            {
                Language = "en-US",
                FontSize = sizeHundredths,
                Bold = bold,
                Dirty = false,
            },
            new D.Text(text));

    private static void AttachNotes(SlidePart slidePart, string notes)
    {
        var notesPart = slidePart.AddNewPart<NotesSlidePart>();

        notesPart.NotesSlide = new P.NotesSlide(
            new P.CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new D.TransformGroup()),
                BuildShape(2, "Notes", P.PlaceholderValues.Body,
                    0, 0, SlideHeight, SlideWidth,
                    [new D.Paragraph(TextRun(notes, 1200, false, Ink))],
                    D.TextAnchoringTypeValues.Top))),
            new P.ColorMapOverride(new D.MasterColorMapping()));

        notesPart.NotesSlide.Save();
    }

    // ── Master, layout and theme ──────────────────────────────────────────────────────────

    private static SlideMasterPart CreateSlideMaster(PresentationPart presentationPart)
    {
        var masterPart = presentationPart.AddNewPart<SlideMasterPart>();

        var layoutPart = masterPart.AddNewPart<SlideLayoutPart>();
        // Type is optional; leaving it unset gives PowerPoint a generic layout, which is what
        // we want since every slide positions its own shapes explicitly.
        layoutPart.SlideLayout = new P.SlideLayout(
            new P.CommonSlideData(EmptyShapeTree()),
            new P.ColorMapOverride(new D.MasterColorMapping()));

        // The relationship graph has to run both ways. AddNewPart gives master → layout; without
        // the reverse the layout has no _rels part at all, and PowerPoint refuses to open the
        // file outright — with a file-corrupt error that says nothing about relationships. The
        // OpenXML validator does not check cross-part references, so this passed every test.
        layoutPart.AddPart(masterPart);

        masterPart.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(EmptyShapeTree()),
            new P.ColorMap
            {
                Background1 = D.ColorSchemeIndexValues.Light1,
                Text1 = D.ColorSchemeIndexValues.Dark1,
                Background2 = D.ColorSchemeIndexValues.Light2,
                Text2 = D.ColorSchemeIndexValues.Dark2,
                Accent1 = D.ColorSchemeIndexValues.Accent1,
                Accent2 = D.ColorSchemeIndexValues.Accent2,
                Accent3 = D.ColorSchemeIndexValues.Accent3,
                Accent4 = D.ColorSchemeIndexValues.Accent4,
                Accent5 = D.ColorSchemeIndexValues.Accent5,
                Accent6 = D.ColorSchemeIndexValues.Accent6,
                Hyperlink = D.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new P.SlideLayoutIdList(new P.SlideLayoutId
            {
                Id = 2147483649,
                RelationshipId = masterPart.GetIdOfPart(layoutPart),
            }));

        var themePart = masterPart.AddNewPart<ThemePart>();
        themePart.Theme = BuildTheme();

        return masterPart;
    }

    private static P.ShapeTree EmptyShapeTree() => new(
        new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1, Name = "" },
            new P.NonVisualGroupShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.GroupShapeProperties(new D.TransformGroup()));

    /// <summary>
    /// A theme part is mandatory, and PowerPoint validates its shape: three fill styles,
    /// three line styles, three effect styles and three background fills, no more, no fewer.
    /// </summary>
    private static D.Theme BuildTheme() => new(
        new D.ThemeElements(
            new D.ColorScheme(
                new D.Dark1Color(new D.SystemColor { Val = D.SystemColorValues.WindowText, LastColor = "000000" }),
                new D.Light1Color(new D.SystemColor { Val = D.SystemColorValues.Window, LastColor = "FFFFFF" }),
                new D.Dark2Color(new D.RgbColorModelHex { Val = Ink }),
                new D.Light2Color(new D.RgbColorModelHex { Val = "F4F5F7" }),
                new D.Accent1Color(new D.RgbColorModelHex { Val = Accent }),
                new D.Accent2Color(new D.RgbColorModelHex { Val = "0E8F8F" }),
                new D.Accent3Color(new D.RgbColorModelHex { Val = "D98324" }),
                new D.Accent4Color(new D.RgbColorModelHex { Val = "2FA84F" }),
                new D.Accent5Color(new D.RgbColorModelHex { Val = "E5484D" }),
                new D.Accent6Color(new D.RgbColorModelHex { Val = Muted }),
                new D.Hyperlink(new D.RgbColorModelHex { Val = Accent }),
                new D.FollowedHyperlinkColor(new D.RgbColorModelHex { Val = Muted }))
            { Name = "AutoWork" },

            new D.FontScheme(
                new D.MajorFont(
                    new D.LatinFont { Typeface = "Segoe UI Semibold" },
                    new D.EastAsianFont { Typeface = "" },
                    new D.ComplexScriptFont { Typeface = "" }),
                new D.MinorFont(
                    new D.LatinFont { Typeface = "Segoe UI" },
                    new D.EastAsianFont { Typeface = "" },
                    new D.ComplexScriptFont { Typeface = "" }))
            { Name = "AutoWork" },

            new D.FormatScheme(
                new D.FillStyleList(
                    SolidFill(D.SchemeColorValues.PhColor),
                    SolidFill(D.SchemeColorValues.PhColor),
                    SolidFill(D.SchemeColorValues.PhColor)),
                new D.LineStyleList(
                    OutlineStyle(9525), OutlineStyle(15875), OutlineStyle(25400)),
                new D.EffectStyleList(
                    new D.EffectStyle(new D.EffectList()),
                    new D.EffectStyle(new D.EffectList()),
                    new D.EffectStyle(new D.EffectList())),
                new D.BackgroundFillStyleList(
                    SolidFill(D.SchemeColorValues.PhColor),
                    SolidFill(D.SchemeColorValues.PhColor),
                    SolidFill(D.SchemeColorValues.PhColor)))
            { Name = "AutoWork" }),
        new D.ObjectDefaults(),
        new D.ExtraColorSchemeList())
    { Name = "AutoWork" };

    private static D.SolidFill SolidFill(D.SchemeColorValues scheme) =>
        new(new D.SchemeColor { Val = scheme });

    private static D.Outline OutlineStyle(int width) => new(
        new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }),
        new D.PresetDash { Val = D.PresetLineDashValues.Solid })
    {
        Width = width,
        CapType = D.LineCapValues.Flat,
        CompoundLineType = D.CompoundLineValues.Single,
        Alignment = D.PenAlignmentValues.Center,
    };
}
