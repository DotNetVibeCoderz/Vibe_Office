// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.ComponentModel;
using ExcelNet;
using ExcelNet.Io;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OfficeNet.Rendering;
using PdfNet.Document;
using PowerPointNet;
using WordNet;
using WordNet.Export;
using WordNet.Import;

namespace OfficeNet.Mcp.Tools;

/// <summary>Tools that work across formats: conversion, text, information, previews.</summary>
[McpServerToolType]
public sealed class DocumentTools(Workspace workspace)
{
    /// <summary>Enough text to answer questions about a document without flooding the context.</summary>
    private const int DefaultMaxCharacters = 20_000;

    [McpServerTool(Name = "convert", Title = "Convert a document", Destructive = true)]
    [Description(
        "Converts between formats, chosen by the two extensions: .docx/.xlsx/.pptx → .pdf, " +
        ".docx → .html or .md, .html → .docx or .pptx, .md → .docx, .txt → .docx (a paragraph per " +
        "line), .pdf → .docx (paragraphs, headings, tables reconstructed) or .xlsx (tables).")]
    public string Convert(
        [Description("Workspace path of the source.")] string input,
        [Description("Workspace path of the result.")] string output) =>
        ToolGuard.Run(() =>
        {
            var source = workspace.Existing(input);
            var target = workspace.ForWriting(output);
            var from = Path.GetExtension(source).ToLowerInvariant();
            var to = Path.GetExtension(target).ToLowerInvariant();

            switch (from, to)
            {
                case (".docx" or ".xlsx" or ".pptx", ".pdf"):
                    Office.ConvertToPdf(source, target);
                    break;

                case (".docx", ".html" or ".htm"):
                    WordToHtml.Convert(source, target);
                    break;

                case (".docx", ".md" or ".markdown"):
                    WordToMarkdown.Convert(source, target);
                    break;

                case (".html" or ".htm", ".docx"):
                    using (var document = HtmlToWord.CreateDocumentFromFile(source, WordImages(source)))
                    {
                        document.Save(target);
                    }

                    break;

                case (".md" or ".markdown", ".docx"):
                    using (var document = MarkdownToWord.CreateDocumentFromFile(source, options: WordImages(source)))
                    {
                        document.Save(target);
                    }

                    break;

                case (".txt", ".docx"):
                    using (var document = TextToWord.CreateDocumentFromFile(source))
                    {
                        document.Save(target);
                    }

                    break;

                case (".html" or ".htm", ".pptx"):
                    using (var deck = PowerPointNet.Html.HtmlToSlides.CreatePresentationFromFile(source,
                               new PowerPointNet.Html.HtmlSlideOptions
                               {
                                   AllowFileAccess = false,
                                   ImageResolver = workspace.ImageReader(Path.GetDirectoryName(source)!),
                               }))
                    {
                        deck.Save(target);
                    }

                    break;

                case (".pdf", ".docx"):
                    PdfToWord.Convert(source, target);
                    break;

                case (".pdf", ".xlsx"):
                    PdfToExcel.Convert(source, target);
                    break;

                default:
                    throw new ArgumentException(
                        $"Converting {from} to {to} is not supported. Supported: docx/xlsx/pptx → pdf, " +
                        "docx → html/md, html → docx/pptx, md/txt → docx, pdf → docx/xlsx.");
            }

            return $"Converted {workspace.Relative(source)} to {workspace.Relative(target)}.";
        });

    /// <summary>
    /// Pictures in an HTML or Markdown source come from beside it, inside the workspace, and the
    /// converter is not allowed to open files on its own — see <see cref="Workspace.ImageReader"/>.
    /// </summary>
    private HtmlWordOptions WordImages(string source) => new()
    {
        AllowFileAccess = false,
        ImageResolver = workspace.ImageReader(Path.GetDirectoryName(source)!),
    };

    [McpServerTool(Name = "extract_text", Title = "Read a document's text", ReadOnly = true)]
    [Description("Returns the plain text of a .docx, .xlsx, .pptx or .pdf.")]
    public string ExtractText(
        [Description("Workspace path of the document.")] string path,
        [Description("At most this many characters. Defaults to 20000.")] int maxCharacters = DefaultMaxCharacters) =>
        ToolGuard.Run(() =>
        {
            var text = Office.ExtractText(workspace.Existing(path));

            return text.Length <= maxCharacters
                ? text
                : text[..maxCharacters] + $"\n\n[… {text.Length - maxCharacters} more characters not shown]";
        });

    [McpServerTool(Name = "document_info", Title = "Describe a document", ReadOnly = true)]
    [Description("Format, size, metadata and structure (pages, sheets, slides, sections) of a document.")]
    public string Info([Description("Workspace path of the document.")] string path) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.Existing(path);
            var format = Office.DetectFormat(full);
            var bytes = new FileInfo(full).Length;

            object details = format switch
            {
                OfficeFormat.Word => WordInfo(full),
                OfficeFormat.Excel => ExcelInfo(full),
                OfficeFormat.PowerPoint => PowerPointInfo(full),
                OfficeFormat.Pdf => PdfInfo(full),
                _ => new { },
            };

            return Values.ToJson(new { path = workspace.Relative(full), format = format.ToString(), bytes, details });
        });

    [McpServerTool(Name = "render_page", Title = "Preview a page", ReadOnly = true)]
    [Description(
        "Renders one page, slide or sheet of a document to PNG and returns the image, so the " +
        "result can be looked at. Optionally also saves the PNG in the workspace.")]
    public IEnumerable<ContentBlock> RenderPage(
        [Description("Workspace path of the document.")] string path,
        [Description("1-based page, slide or sheet number. Defaults to 1.")] int page = 1,
        [Description("Resolution. Defaults to 96; the image is capped at 1600 pixels a side.")] int dpi = 96,
        [Description("Workspace path to also save the PNG to. Optional.")] string? savePath = null) =>
        ToolGuard.Run<IEnumerable<ContentBlock>>(() =>
        {
            var full = workspace.Existing(path);
            var options = new RenderOptions { Dpi = Math.Clamp(dpi, 36, 300), MaxPixels = 1600, DrawPageBorder = true };
            var pages = DocumentRenderer.Render(full, options);

            if (page < 1 || page > pages.Count)
            {
                throw new ArgumentException($"The document has {pages.Count} page(s); page {page} does not exist.");
            }

            var png = pages[page - 1];
            var note = $"Page {page} of {pages.Count} of {workspace.Relative(full)}.";

            if (savePath is not null)
            {
                var target = workspace.ForWriting(savePath);
                File.WriteAllBytes(target, png);
                note += $" Saved to {workspace.Relative(target)}.";
            }

            return
            [
                new TextContentBlock { Text = note },
                ImageContentBlock.FromBytes(png, "image/png"),
            ];
        });

    [McpServerTool(Name = "list_files", Title = "List workspace files", ReadOnly = true)]
    [Description("Lists the documents in a workspace directory (recursively), with sizes.")]
    public string ListFiles(
        [Description("Workspace directory; defaults to the root.")] string directory = ".",
        [Description("Only these extensions, e.g. [\".docx\", \".pdf\"]. Defaults to all.")] string[]? extensions = null) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.ResolveDirectory(directory, create: false);

            var files = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .Where(f => extensions is null ||
                            extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Take(500)
                .Select(f => new { path = workspace.Relative(f), bytes = new FileInfo(f).Length })
                .ToList();

            return Values.ToJson(new { root = workspace.Root, readOnly = workspace.ReadOnly, files });
        });

    // ---- Information ---------------------------------------------------------------------------

    private static object WordInfo(string path)
    {
        using var document = WordDocument.Open(path);

        return new
        {
            title = document.Properties.Title,
            author = document.Properties.Creator,
            paragraphs = document.Paragraphs.Count,
            tables = document.Tables.Count,
            sections = document.Sections.Count,
            words = document.WordCount,
            headings = document.Paragraphs.Where(p => p.HeadingLevel is not null)
                .Select(p => new { level = p.HeadingLevel, text = p.Text }).Take(100),
            trackedChanges = document.Revisions.Count,
            protection = document.Protection?.Restriction.ToString(),
            watermark = document.Watermark,
        };
    }

    private static object ExcelInfo(string path)
    {
        using var workbook = Workbook.Open(path);

        return new
        {
            title = workbook.Properties.Title,
            sheets = workbook.Select(s => new
            {
                name = s.Name,
                usedRange = s.UsedRange?.A1,
                cells = s.CellCount,
                charts = s.Charts.Count,
                hidden = s.IsHidden,
            }),
        };
    }

    private static object PowerPointInfo(string path)
    {
        using var presentation = Presentation.Open(path);

        return new
        {
            title = presentation.Properties.Title,
            slides = presentation.Slides.Select((s, i) => new
            {
                number = i + 1,
                title = s.Title,
                charts = s.Charts.Count(),
                tables = s.Tables.Count(),
            }),
        };
    }

    private static object PdfInfo(string path)
    {
        using var document = PdfDocument.Open(path);

        return new
        {
            title = document.Info.Title,
            author = document.Info.Author,
            pages = document.Pages.Count,
            version = document.Version,
            encrypted = document.WasEncrypted,
        };
    }
}
