// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using OfficeNet.Core;
using WordNet;
using WordNet.Import;
using WordNet.Merging;
using WordNet.Protection;

namespace OfficeNet.Mcp.Tools;

/// <summary>Word (.docx) tools.</summary>
[McpServerToolType]
public sealed class WordTools(Workspace workspace)
{
    [McpServerTool(Name = "word_create", Title = "Create a Word document", Destructive = true)]
    [Description(
        "Creates a .docx from a list of blocks (headings, paragraphs, lists, tables, images, charts, " +
        "page breaks, a table of contents, or Markdown). Overwrites the file if it exists. Paragraph " +
        "text may use **bold** and *italic*; a markdown block takes a whole Markdown document.")]
    public string Create(
        [Description("Workspace path of the .docx to write, e.g. reports/q3.docx.")] string path,
        [Description("The document's content, in order.")] DocBlock[] blocks,
        [Description("Document title (metadata).")] string? title = null,
        [Description("Author (metadata).")] string? author = null,
        [Description("A4, Letter, Legal, A3, A5 or F4. Defaults to A4.")] string? pageSize = null) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.ForWriting(path);

            using var document = WordDocument.Create();
            document.Section.SetPageSize(pageSize ?? "A4");
            document.Properties.Title = title;
            document.Properties.Creator = author;

            Write(document, blocks);
            document.Save(full);

            return $"Created {workspace.Relative(full)} with {blocks.Length} blocks.";
        });

    [McpServerTool(Name = "word_append", Title = "Append to a Word document", Destructive = true)]
    [Description("Appends blocks to the end of an existing .docx, in the same format as word_create.")]
    public string Append(
        [Description("Workspace path of the .docx to edit.")] string path,
        [Description("The blocks to append.")] DocBlock[] blocks) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.Existing(path);
            workspace.ForWriting(path);

            using var document = WordDocument.Open(full);
            Write(document, blocks);
            document.Save(full);

            return $"Appended {blocks.Length} blocks to {workspace.Relative(full)}.";
        });

    [McpServerTool(Name = "word_fill_template", Title = "Fill a Word template", Destructive = true)]
    [Description(
        "Fills a .docx template and writes the result to a new file. The template uses {{field}}, " +
        "{{customer.name}}, {{total:N0}}, repeating {{#items}}...{{/items}} (rows of a table repeat " +
        "when the tags are in different cells of the row) and inverted {{^items}}...{{/items}}. " +
        "ISO date strings (2026-10-28) are dates, so {{date:dd MMMM yyyy}} formats them. " +
        "Formatting around each token is kept.")]
    public string FillTemplate(
        [Description("Workspace path of the template.")] string templatePath,
        [Description("Workspace path to write the filled document to.")] string outputPath,
        [Description("The data, as a JSON object.")] JsonElement data,
        [Description("Culture for numbers and dates, e.g. id-ID or en-US. Defaults to invariant.")]
        string? culture = null) =>
        ToolGuard.Run(() =>
        {
            var template = workspace.Existing(templatePath);
            var output = workspace.ForWriting(outputPath);

            using var document = WordDocument.Open(template);
            var count = document.FillTemplate(Values.TemplateData(data), new WordNet.Templates.TemplateOptions
            {
                Culture = culture is null
                    ? System.Globalization.CultureInfo.InvariantCulture
                    : System.Globalization.CultureInfo.GetCultureInfo(culture),
            });

            document.Save(output);
            return $"Filled {count} fields; wrote {workspace.Relative(output)}.";
        });

    [McpServerTool(Name = "word_replace_text", Title = "Find and replace in Word", Destructive = true)]
    [Description("Replaces every occurrence of a phrase in a .docx, including phrases Word split across runs.")]
    public string ReplaceText(
        [Description("Workspace path of the .docx to edit.")] string path,
        [Description("The text to find.")] string find,
        [Description("The replacement.")] string replace) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.Existing(path);
            workspace.ForWriting(path);

            using var document = WordDocument.Open(full);

            // Per run first, which keeps every run's formatting; across runs only for what that
            // missed, because it rewrites the paragraph as one run.
            var count = document.ReplaceText(find, replace);

            if (count == 0)
            {
                count = document.ReplaceTextAcrossRuns(find, replace);
            }

            document.Save(full);
            return $"Replaced {count} occurrence(s) in {workspace.Relative(full)}.";
        });

    [McpServerTool(Name = "word_track_changes", Title = "Review tracked changes")]
    [Description("Lists the tracked changes in a .docx, or accepts or rejects all of them.")]
    public string TrackChanges(
        [Description("Workspace path of the .docx.")] string path,
        [Description("list, accept_all or reject_all. Defaults to list.")] string action = "list") =>
        ToolGuard.Run(() =>
        {
            var full = workspace.Existing(path);

            using var document = WordDocument.Open(full);

            if (action == "list")
            {
                return Values.ToJson(document.Revisions.Select(r => new
                {
                    kind = r.Kind.ToString(),
                    author = r.Author,
                    date = r.Date,
                    text = r.Text,
                }).ToList());
            }

            workspace.ForWriting(path);

            var count = action switch
            {
                "accept_all" => document.Revisions.AcceptAll(),
                "reject_all" => document.Revisions.RejectAll(),
                _ => throw new ArgumentException($"Unknown action '{action}'. Use list, accept_all or reject_all."),
            };

            document.Save(full);
            return $"{(action == "accept_all" ? "Accepted" : "Rejected")} {count} change(s).";
        });

    [McpServerTool(Name = "word_protect", Title = "Restrict editing", Destructive = true)]
    [Description(
        "Restricts editing in Word: readOnly, comments, trackedChanges or forms; 'none' removes " +
        "the restriction. This prevents accidental edits; it is not encryption.")]
    public string Protect(
        [Description("Workspace path of the .docx.")] string path,
        [Description("readOnly, comments, trackedChanges, forms or none.")] string restriction,
        [Description("Password needed to lift it in Word. Optional.")] string? password = null) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.Existing(path);
            workspace.ForWriting(path);

            using var document = WordDocument.Open(full);

            if (restriction.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                document.Unprotect();
            }
            else if (Enum.TryParse<DocumentEditRestriction>(restriction, ignoreCase: true, out var value))
            {
                document.Protect(value, password);
            }
            else
            {
                throw new ArgumentException(
                    $"Unknown restriction '{restriction}'. Use readOnly, comments, trackedChanges, forms or none.");
            }

            document.Save(full);
            return $"{workspace.Relative(full)}: restriction set to {restriction}.";
        });

    [McpServerTool(Name = "word_watermark", Title = "Set a watermark", Destructive = true)]
    [Description("Puts a diagonal watermark (DRAFT, CONFIDENTIAL…) behind every page; empty text removes it.")]
    public string Watermark(
        [Description("Workspace path of the .docx.")] string path,
        [Description("The watermark text, or empty to remove it.")] string? text) =>
        ToolGuard.Run(() =>
        {
            var full = workspace.Existing(path);
            workspace.ForWriting(path);

            using var document = WordDocument.Open(full);

            if (string.IsNullOrWhiteSpace(text))
            {
                document.RemoveWatermark();
            }
            else
            {
                document.AddWatermark(text);
            }

            document.Save(full);
            return string.IsNullOrWhiteSpace(text) ? "Watermark removed." : $"Watermark set to '{text}'.";
        });

    [McpServerTool(Name = "word_merge", Title = "Merge Word documents", Destructive = true)]
    [Description(
        "Joins several .docx files into one, in order. Styles, lists, images, headers, footnotes " +
        "and comments come along and are renumbered.")]
    public string Merge(
        [Description("Workspace path of the merged document to write.")] string outputPath,
        [Description("Workspace paths of the documents, in order; at least two.")] string[] inputs,
        [Description("newPage, continuous or none. Defaults to newPage.")] string sectionBreak = "newPage") =>
        ToolGuard.Run(() =>
        {
            if (inputs.Length < 2)
            {
                throw new ArgumentException("Give at least two documents to merge.");
            }

            if (!Enum.TryParse<SectionJoin>(sectionBreak, ignoreCase: true, out var join))
            {
                throw new ArgumentException($"Unknown section break '{sectionBreak}'. Use newPage, continuous or none.");
            }

            var output = workspace.ForWriting(outputPath);

            using var merged = WordDocument.Open(workspace.Existing(inputs[0]));

            foreach (var input in inputs.Skip(1))
            {
                using var next = WordDocument.Open(workspace.Existing(input));
                merged.AppendDocument(next, new AppendOptions { SectionBreak = join });
            }

            merged.Save(output);
            return $"Merged {inputs.Length} documents into {workspace.Relative(output)}.";
        });

    [McpServerTool(Name = "word_split", Title = "Split a Word document", Destructive = true)]
    [Description("Splits a .docx into several: one per section, per heading, or per explicit page break.")]
    public string Split(
        [Description("Workspace path of the .docx.")] string path,
        [Description("Workspace directory for the parts.")] string outputDirectory,
        [Description("section, heading or pagebreak. Defaults to heading.")] string by = "heading",
        [Description("For 'heading': split before headings of this level or higher. Defaults to 1.")]
        int headingLevel = 1) =>
        ToolGuard.Run(() =>
        {
            var directory = workspace.ResolveDirectory(outputDirectory, create: true);

            using var document = WordDocument.Open(workspace.Existing(path));

            var parts = by switch
            {
                "section" => document.SplitBySection(),
                "heading" => document.SplitByHeading(headingLevel),
                "pagebreak" => document.SplitAtPageBreaks(),
                _ => throw new ArgumentException($"Unknown split '{by}'. Use section, heading or pagebreak."),
            };

            var stem = Path.GetFileNameWithoutExtension(path);
            var written = new List<string>();

            for (var i = 0; i < parts.Count; i++)
            {
                using var part = parts[i];
                var file = Path.Combine(directory, $"{stem}-{i + 1:00}.docx");
                part.Save(file);
                written.Add(workspace.Relative(file));
            }

            return Values.ToJson(new { parts = written });
        });

    // ---- Blocks --------------------------------------------------------------------------------

    private void Write(WordDocument document, IEnumerable<DocBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block.Type.ToLowerInvariant())
            {
                case "heading":
                    var heading = document.AddHeading(block.Text ?? "", Math.Clamp(block.Level ?? 1, 0, 9));
                    heading.Alignment = Values.Alignment(block.Align) ?? heading.Alignment;
                    break;

                case "paragraph" or "quote":
                    var paragraph = document.AddParagraph("", block.Type.Equals("quote", StringComparison.OrdinalIgnoreCase) ? "Quote" : null);
                    Values.AddInline(paragraph, block.Text ?? "");
                    paragraph.Alignment = Values.Alignment(block.Align) ?? paragraph.Alignment;
                    break;

                case "bullets" or "numbered":
                    foreach (var item in document.AddList(block.Items ?? [],
                                 numbered: block.Type.Equals("numbered", StringComparison.OrdinalIgnoreCase)))
                    {
                        var text = item.Text;
                        item.Text = "";
                        Values.AddInline(item, text);
                    }

                    break;

                case "table":
                    if (block.Rows is not { Length: > 0 } rows)
                    {
                        throw new ArgumentException("A table block needs rows.");
                    }

                    var width = rows.Max(r => r.Length);
                    document.AddTable(rows.Select(r => r.Concat(Enumerable.Repeat("", width - r.Length))),
                        firstRowIsHeader: block.Header ?? true);
                    break;

                case "image":
                    var image = workspace.Existing(block.ImagePath ?? throw new ArgumentException("An image block needs imagePath."));
                    document.AddPicture(image, block.WidthCm is { } cm ? Length.FromCentimeters(cm) : null,
                        alignment: Values.Alignment(block.Align) ?? ParagraphAlignment.Center);
                    break;

                case "chart":
                    document.AddChart(Values.Chart(block.Chart ?? throw new ArgumentException("A chart block needs a chart.")),
                        block.WidthCm is { } chartWidth ? Length.FromCentimeters(chartWidth) : null);
                    break;

                case "markdown":
                    // Pictures resolve from the workspace root, like an image block's path, and
                    // only through the workspace: the converter does not open files itself.
                    MarkdownToWord.Convert(document, block.Text ?? throw new ArgumentException("A markdown block needs text."),
                        options: new HtmlWordOptions
                        {
                            AllowFileAccess = false,
                            ImageResolver = workspace.ImageReader(workspace.Root),
                        });
                    break;

                case "pagebreak":
                    document.AddPageBreak();
                    break;

                case "toc":
                    document.AddTableOfContents(title: block.Text ?? "Table of Contents");
                    break;

                default:
                    throw new ArgumentException(
                        $"Unknown block type '{block.Type}'. Use heading, paragraph, quote, bullets, " +
                        "numbered, table, image, chart, markdown, pagebreak or toc.");
            }
        }
    }
}
