using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace VibeDesk.Ui.Services;

/// <summary>
/// Renders assistant markdown to HTML: tables, code, images, audio and video, per the spec.
/// </summary>
/// <remarks>
/// Model output is untrusted text that we inject as raw HTML, so this pipeline is deliberately
/// restrictive: <c>DisableHtml</c> drops inline HTML (which would otherwise be a script-injection
/// path straight through <c>MarkupString</c>), and every link and media URL is checked against an
/// allow-list of schemes. A model that emits <c>javascript:</c> or <c>data:text/html</c> gets it
/// stripped rather than rendered.
/// </remarks>
public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseGridTables()
            .UsePipeTables()
            .UseEmphasisExtras()
            .UseTaskLists()
            .UseAutoLinks()
            .UseFootnotes()
            .UseDefinitionLists()
            // No inline HTML: assistant output is untrusted and lands in a MarkupString.
            .DisableHtml()
            .Use<SafeLinkExtension>()
            .Use<MediaEmbedExtension>()
            .Build();
    }

    public string ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? string.Empty : Markdown.ToHtml(markdown, _pipeline);

    /// <summary>Plain text, for previews and titles where markup would be noise.</summary>
    public string ToPlainText(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? string.Empty : Markdown.ToPlainText(markdown, _pipeline);

    /// <summary>Schemes we are willing to emit. Everything else is dropped.</summary>
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    internal static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var trimmed = url.Trim();

        // Site-relative links are ours, so they are safe by construction.
        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal)) return true;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;

        return AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Rewrites links: unsafe schemes lose their href, and external links open in a new tab with
/// <c>rel="noopener noreferrer"</c> so the opened page cannot reach back into our window.
/// </summary>
internal sealed class SafeLinkExtension : IMarkdownExtension
{
    /// <summary>
    /// Hooks the builder's document-processed event. The rewrite has to happen on the parsed
    /// document rather than at render time, because dropping an unsafe URL must also remove it from
    /// any attributes a later extension might copy.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed += static document =>
        {
            foreach (var link in document.Descendants<LinkInline>())
            {
                if (!MarkdownRenderer.IsSafeUrl(link.Url))
                {
                    // Keep the label, drop the destination.
                    link.Url = string.Empty;
                    continue;
                }

                if (link.IsImage) continue;
                if (link.Url!.StartsWith('/')) continue;

                var attributes = link.GetAttributes();
                attributes.AddPropertyIfNotExist("target", "_blank");
                attributes.AddPropertyIfNotExist("rel", "noopener noreferrer");
            }
        };
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }
}

/// <summary>
/// Turns image links whose target is audio or video into the matching media element, so the assistant
/// can share a recording with plain markdown image syntax and have it actually play.
/// </summary>
internal sealed class MediaEmbedExtension : IMarkdownExtension
{
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".ogv", ".mov"];
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".ogg", ".m4a", ".flac"];

    public void Setup(MarkdownPipelineBuilder pipeline) { }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is not HtmlRenderer html) return;

        // Replace the built-in image renderer so media links become <video>/<audio>.
        var existing = html.ObjectRenderers.FindExact<Markdig.Renderers.Html.Inlines.LinkInlineRenderer>();
        if (existing is not null) html.ObjectRenderers.Remove(existing);

        html.ObjectRenderers.Add(new MediaLinkRenderer());
    }

    private sealed class MediaLinkRenderer : Markdig.Renderers.Html.Inlines.LinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, LinkInline link)
        {
            if (!link.IsImage || !MarkdownRenderer.IsSafeUrl(link.Url))
            {
                base.Write(renderer, link);
                return;
            }

            var url = link.Url!;
            var path = url.Split('?')[0].ToLowerInvariant();

            if (VideoExtensions.Any(ext => path.EndsWith(ext, StringComparison.Ordinal)))
            {
                renderer.Write("<video class=\"vd-md__media\" controls preload=\"metadata\" src=\"");
                renderer.WriteEscapeUrl(url);
                renderer.Write("\"></video>");
                return;
            }

            if (AudioExtensions.Any(ext => path.EndsWith(ext, StringComparison.Ordinal)))
            {
                renderer.Write("<audio class=\"vd-md__media\" controls preload=\"metadata\" src=\"");
                renderer.WriteEscapeUrl(url);
                renderer.Write("\"></audio>");
                return;
            }

            base.Write(renderer, link);
        }
    }
}
