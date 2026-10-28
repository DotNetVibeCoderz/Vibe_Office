// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.ComponentModel;
using ModelContextProtocol.Server;
using OfficeNet.Core;
using PowerPointNet;
using PowerPointNet.Html;

namespace OfficeNet.Mcp.Tools;

/// <summary>PowerPoint (.pptx) tools.</summary>
[McpServerToolType]
public sealed class PowerPointTools(Workspace workspace)
{
    // Layout indices of the default master: 0 title, 1 title and content, 2 title only, 3 blank,
    // 5 section header.
    private const int TitleOnly = 2;
    private const int Blank = 3;

    [McpServerTool(Name = "pptx_create", Title = "Create a presentation", Destructive = true)]
    [Description(
        "Creates a .pptx from a list of slides: title, bullets, section, table, chart (native, " +
        "editable in PowerPoint), image or blank. Each slide can carry speaker notes.")]
    public string Create(
        [Description("Workspace path of the .pptx to write.")] string path,
        [Description("The slides, in order.")] SlideSpec[] slides,
        [Description("16:9 when true (default), 4:3 when false.")] bool widescreen = true) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.ForWriting(path);

            using var presentation = Presentation.Create();

            if (widescreen)
            {
                presentation.UseWidescreen();
            }
            else
            {
                presentation.UseStandard();
            }

            foreach (var spec in slides)
            {
                var slide = Add(presentation, spec);

                if (!string.IsNullOrWhiteSpace(spec.Notes))
                {
                    slide.Notes = spec.Notes;
                }
            }

            presentation.Save(full);
            return $"Created {workspace.Relative(full)} with {slides.Length} slide(s).";
        });

    [McpServerTool(Name = "pptx_from_html", Title = "HTML to slides", Destructive = true)]
    [Description(
        "Turns HTML into a deck: each h1/h2 starts a slide, its content becomes the body, long " +
        "content is paginated, tables become native tables.")]
    public string FromHtml(
        [Description("Workspace path of the .pptx to write.")] string path,
        [Description("The HTML.")] string html,
        [Description("An optional title slide.")] string? title = null,
        [Description("Its subtitle.")] string? subtitle = null) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.ForWriting(path);

            using var presentation = HtmlToSlides.CreatePresentation(html, new HtmlSlideOptions
            {
                TitleSlide = title,
                SubtitleSlide = subtitle,
            });

            presentation.Save(full);
            return $"Created {workspace.Relative(full)} with {presentation.Slides.Count} slide(s).";
        });

    private Slide Add(Presentation presentation, SlideSpec spec)
    {
        var width = presentation.SlideWidth;
        var height = presentation.SlideHeight;
        var margin = Units.Inches(0.6);

        switch (spec.Type.ToLowerInvariant())
        {
            case "title":
                return presentation.AddTitleSlide(spec.Title ?? "", spec.Subtitle);

            case "section":
                return presentation.AddSectionSlide(spec.Title ?? "", spec.Subtitle);

            case "bullets":
                return presentation.AddBulletSlide(spec.Title ?? "", spec.Bullets ?? []);

            case "table":
            {
                if (spec.Rows is not { Length: > 0 } rows)
                {
                    throw new ArgumentException("A table slide needs rows.");
                }

                var slide = presentation.AddSlide(TitleOnly);
                slide.SetTitle(spec.Title ?? "");
                var columns = rows.Max(r => r.Length);

                slide.AddTable(rows.Length, columns, margin, Units.Inches(1.6),
                        width - margin - margin, Length.FromPoints(Math.Min(rows.Length * 32.0, height.Points - 150)))
                    .SetData(rows.Select(r => r.Concat(Enumerable.Repeat("", columns - r.Length)).ToArray()).ToArray());
                return slide;
            }

            case "chart":
            {
                var slide = presentation.AddSlide(TitleOnly);
                slide.SetTitle(spec.Title ?? spec.Chart?.Title ?? "");
                slide.AddChart(Values.Chart(spec.Chart ?? throw new ArgumentException("A chart slide needs a chart.")));
                return slide;
            }

            case "image":
            {
                var slide = presentation.AddSlide(spec.Title is null ? Blank : TitleOnly);

                if (spec.Title is not null)
                {
                    slide.SetTitle(spec.Title);
                }

                var image = workspace.Existing(spec.ImagePath ?? throw new ArgumentException("An image slide needs imagePath."));
                var top = spec.Title is null ? margin : Units.Inches(1.6);
                slide.AddPicture(image, margin, top, height: height - top - margin);
                return slide;
            }

            case "blank":
            {
                var slide = presentation.AddSlide(Blank);
                return slide;
            }

            default:
                throw new ArgumentException(
                    $"Unknown slide type '{spec.Type}'. Use title, bullets, section, table, chart, image or blank.");
        }
    }
}
