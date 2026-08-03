using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.AI;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PdfDocument = QuestPDF.Fluent.Document;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace AutoWork.Tools;

/// <summary>
/// Produces the four office formats the spec calls for: Excel with live formulas, Word,
/// PowerPoint and PDF.
///
/// The model supplies structure as JSON or lightweight Markdown rather than raw OOXML,
/// because asking an LLM to emit valid OOXML is a reliable way to produce corrupt files.
/// </summary>
public sealed class DocumentTools : ToolSetBase, IToolProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static int _licenceConfigured;

    public DocumentTools(ToolContext context) : base(context) => ConfigureQuestPdfLicence();

    protected override AgentOrgan Organ => AgentOrgan.Hands;

    public string Name => "Documents";

    /// <summary>
    /// QuestPDF refuses to render until a licence is declared. AutoWork ships under the
    /// Community terms; this is documented in docs/en/licensing.md.
    /// </summary>
    private static void ConfigureQuestPdfLicence()
    {
        if (Interlocked.Exchange(ref _licenceConfigured, 1) == 0)
            QuestPDF.Settings.License = LicenseType.Community;
    }

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        var tools = new DocumentTools(context);

        yield return Describe(AIFunctionFactory.Create(tools.CreateExcelAsync, "doc_create_excel",
            """
            Create an .xlsx workbook. Pass sheets as JSON:
            [{"name":"Sales","rows":[["Region","Q1","Q2","Total"],["North",100,120,"=B2+C2"]],"freezeHeader":true,"autoFilter":true}]
            Any cell whose text starts with = becomes a live formula. Numbers stay numeric.
            """));

        yield return Describe(AIFunctionFactory.Create(tools.CreateWordAsync, "doc_create_word",
            """
            Create a .docx document from lightweight Markdown. Supported: # / ## / ### headings,
            - bullets, 1. numbered items, **bold**, and blank lines between paragraphs.
            """));

        yield return Describe(AIFunctionFactory.Create(tools.CreatePowerPointAsync, "doc_create_powerpoint",
            """
            Create a .pptx deck. Pass slides as JSON:
            [{"title":"Q3 results","bullets":["Revenue up 12%","Costs flat"],"notes":"Speaker notes"}]
            """));

        yield return Describe(AIFunctionFactory.Create(tools.CreatePdfAsync, "doc_create_pdf",
            """
            Create a .pdf from lightweight Markdown, with page numbers and an optional title block.
            Supported: # / ## / ### headings, - bullets, **bold**, and blank lines between paragraphs.
            """));

        yield return Describe(AIFunctionFactory.Create(tools.ReadExcelAsync, "doc_read_excel",
            "Read a sheet from an existing .xlsx workbook as text, including computed values."),
            ToolRisk.Safe);
    }

    private static ToolDescriptor Describe(AIFunction function, ToolRisk risk = ToolRisk.Write) => new()
    {
        Function = function,
        Organ = AgentOrgan.Hands,
        Risk = risk,
        Category = "Documents",
        ApprovalKind = risk == ToolRisk.Safe ? ApprovalKind.Other : ApprovalKind.WriteFiles,
    };

    // ── Excel ─────────────────────────────────────────────────────────────────────────────

    private sealed class SheetSpec
    {
        public string Name { get; set; } = "Sheet1";
        public List<List<JsonElement>> Rows { get; set; } = [];
        public bool FreezeHeader { get; set; } = true;
        public bool AutoFilter { get; set; }

        /// <summary>Optional explicit formulas keyed by cell reference, e.g. {"D10":"=SUM(D2:D9)"}.</summary>
        public Dictionary<string, string> Formulas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    [Description("Create an Excel workbook.")]
    private Task<string> CreateExcelAsync(
        [Description("Destination .xlsx path.")] string path,
        [Description("Sheets as a JSON array.")] string sheetsJson,
        [Description("Overwrite if the file exists.")] bool overwrite = false)
    {
        var target = Locate(path);

        return GuardedAsync("doc.excel", $"Create workbook {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureWritable(EnsureExtension(target, ".xlsx"));
            if (File.Exists(canonical) && !overwrite)
                return Refused($"{PathGuard.Describe(canonical)} already exists. Call again with overwrite=true to replace it.");

            List<SheetSpec>? sheets;
            try { sheets = JsonSerializer.Deserialize<List<SheetSpec>>(sheetsJson, Json); }
            catch (JsonException ex) { return Failed($"The sheets JSON could not be parsed: {ex.Message}"); }

            if (sheets is null || sheets.Count == 0) return Failed("No sheets were supplied.");

            using var workbook = new XLWorkbook();
            var totalCells = 0;

            foreach (var spec in sheets)
            {
                var sheet = workbook.Worksheets.Add(SanitizeSheetName(spec.Name));

                for (var r = 0; r < spec.Rows.Count; r++)
                {
                    for (var c = 0; c < spec.Rows[r].Count; c++)
                    {
                        WriteCell(sheet.Cell(r + 1, c + 1), spec.Rows[r][c]);
                        totalCells++;
                    }
                }

                foreach (var (reference, formula) in spec.Formulas)
                {
                    try { sheet.Cell(reference).FormulaA1 = formula.TrimStart('='); }
                    catch (ArgumentException) { /* skip an unparseable reference rather than failing the file */ }
                }

                if (spec.Rows.Count > 0)
                {
                    var header = sheet.Row(1);
                    header.Style.Font.Bold = true;
                    header.Style.Fill.BackgroundColor = XLColor.FromArgb(0xEF, 0xF1, 0xF5);

                    if (spec.FreezeHeader) sheet.SheetView.FreezeRows(1);
                    if (spec.AutoFilter) sheet.RangeUsed()?.SetAutoFilter();
                }

                sheet.Columns().AdjustToContents(minWidth: 8, maxWidth: 60);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
            workbook.SaveAs(canonical);

            return Ok($"Created {PathGuard.Describe(canonical)} — {sheets.Count} sheet(s), {totalCells:N0} cells.");
        },
        [target], ApprovalKind.WriteFiles);
    }

    /// <summary>Preserves types: numbers stay numeric, "=..." becomes a formula, dates parse.</summary>
    private static void WriteCell(IXLCell cell, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDouble(out var number):
                cell.Value = number;
                break;

            case JsonValueKind.True or JsonValueKind.False:
                cell.Value = value.GetBoolean();
                break;

            case JsonValueKind.Null or JsonValueKind.Undefined:
                break;

            default:
                var text = value.ToString();
                if (text.StartsWith('=')) cell.FormulaA1 = text[1..];
                else if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                             System.Globalization.DateTimeStyles.None, out var date) && text.Length >= 8)
                    cell.Value = date;
                else cell.Value = text;
                break;
        }
    }

    /// <summary>Excel rejects these characters and caps names at 31 characters.</summary>
    private static string SanitizeSheetName(string name)
    {
        var cleaned = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Sheet";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    [Description("Read an Excel sheet.")]
    private Task<string> ReadExcelAsync(
        [Description("Workbook to read.")] string path,
        [Description("Sheet name. Defaults to the first sheet.")] string? sheetName = null,
        [Description("Maximum rows to return.")] int maxRows = 200)
    {
        var target = Locate(path);

        return GuardedAsync("doc.read_excel", $"Read workbook {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureReadable(target);
            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            using var workbook = new XLWorkbook(canonical);

            var sheet = sheetName is null
                ? workbook.Worksheets.FirstOrDefault()
                : workbook.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase));

            if (sheet is null)
                return Failed($"No sheet named \"{sheetName}\". Available: {string.Join(", ", workbook.Worksheets.Select(w => w.Name))}");

            var used = sheet.RangeUsed();
            if (used is null) return Ok($"Sheet \"{sheet.Name}\" is empty.");

            var lines = used.Rows().Take(maxRows)
                .Select(row => string.Join('\t', row.Cells().Select(c => c.GetFormattedString())))
                .ToList();

            var header = $"Sheet \"{sheet.Name}\" — {used.RowCount():N0} rows × {used.ColumnCount()} columns";
            if (used.RowCount() > maxRows) header += $" (showing first {maxRows})";

            return Ok($"{header}\n{string.Join('\n', lines)}");
        }, [target]);
    }

    // ── Word ──────────────────────────────────────────────────────────────────────────────

    [Description("Create a Word document.")]
    private Task<string> CreateWordAsync(
        [Description("Destination .docx path.")] string path,
        [Description("Body content as lightweight Markdown.")] string content,
        [Description("Optional title placed at the top.")] string? title = null,
        [Description("Overwrite if the file exists.")] bool overwrite = false)
    {
        var target = Locate(path);

        return GuardedAsync("doc.word", $"Create document {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureWritable(EnsureExtension(target, ".docx"));
            if (File.Exists(canonical) && !overwrite)
                return Refused($"{PathGuard.Describe(canonical)} already exists. Call again with overwrite=true to replace it.");

            Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);

            using var document = WordprocessingDocument.Create(canonical, WordprocessingDocumentType.Document);
            var main = document.AddMainDocumentPart();
            main.Document = new WordDocument();
            var body = main.Document.AppendChild(new Body());

            if (!string.IsNullOrWhiteSpace(title))
                body.AppendChild(BuildParagraph(title, sizeHalfPoints: 40, bold: true, spaceAfter: 320));

            var blocks = 0;
            foreach (var block in ParseMarkdown(content))
            {
                body.AppendChild(block switch
                {
                    { Kind: BlockKind.Heading1 } => BuildParagraph(block.Text, 32, bold: true, spaceBefore: 280, spaceAfter: 140),
                    { Kind: BlockKind.Heading2 } => BuildParagraph(block.Text, 26, bold: true, spaceBefore: 240, spaceAfter: 120),
                    { Kind: BlockKind.Heading3 } => BuildParagraph(block.Text, 23, bold: true, spaceBefore: 200, spaceAfter: 100),
                    { Kind: BlockKind.Bullet } => BuildParagraph("• " + block.Text, 22, indentTwips: 360, spaceAfter: 60),
                    { Kind: BlockKind.Numbered } => BuildParagraph(block.Text, 22, indentTwips: 360, spaceAfter: 60),
                    _ => BuildParagraph(block.Text, 22, spaceAfter: 140),
                });
                blocks++;
            }

            main.Document.Save();

            return Ok($"Created {PathGuard.Describe(canonical)} — {blocks} blocks.");
        },
        [target], ApprovalKind.WriteFiles);
    }

    private static Paragraph BuildParagraph(string text, int sizeHalfPoints, bool bold = false,
        int indentTwips = 0, int spaceBefore = 0, int spaceAfter = 120)
    {
        var properties = new ParagraphProperties(
            new SpacingBetweenLines
            {
                Before = spaceBefore.ToString(),
                After = spaceAfter.ToString(),
                Line = "276",
                LineRule = LineSpacingRuleValues.Auto,
            });

        if (indentTwips > 0)
            properties.AppendChild(new Indentation { Left = indentTwips.ToString() });

        var paragraph = new Paragraph(properties);

        // **bold** spans become separate runs.
        foreach (var (segment, isBold) in SplitBold(text))
        {
            // Order inside rPr is fixed by the OOXML schema — w:b precedes w:sz. Appending
            // them the other way round produces a file Word opens but the validator rejects.
            var runProperties = new RunProperties();
            if (bold || isBold) runProperties.AppendChild(new Bold());
            runProperties.AppendChild(new FontSize { Val = sizeHalfPoints.ToString() });

            paragraph.AppendChild(new Run(runProperties,
                new Text(segment) { Space = SpaceProcessingModeValues.Preserve }));
        }

        return paragraph;
    }

    private static IEnumerable<(string Text, bool Bold)> SplitBold(string text)
    {
        var matches = Regex.Matches(text, @"\*\*(.+?)\*\*", RegexOptions.Singleline);
        if (matches.Count == 0) { yield return (text, false); yield break; }

        var cursor = 0;
        foreach (Match match in matches)
        {
            if (match.Index > cursor) yield return (text[cursor..match.Index], false);
            yield return (match.Groups[1].Value, true);
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length) yield return (text[cursor..], false);
    }

    // ── PowerPoint ────────────────────────────────────────────────────────────────────────

    [Description("Create a PowerPoint deck.")]
    private Task<string> CreatePowerPointAsync(
        [Description("Destination .pptx path.")] string path,
        [Description("Slides as a JSON array.")] string slidesJson,
        [Description("Overwrite if the file exists.")] bool overwrite = false)
    {
        var target = Locate(path);

        return GuardedAsync("doc.powerpoint", $"Create deck {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureWritable(EnsureExtension(target, ".pptx"));
            if (File.Exists(canonical) && !overwrite)
                return Refused($"{PathGuard.Describe(canonical)} already exists. Call again with overwrite=true to replace it.");

            List<SlideSpec>? slides;
            try { slides = JsonSerializer.Deserialize<List<SlideSpec>>(slidesJson, Json); }
            catch (JsonException ex) { return Failed($"The slides JSON could not be parsed: {ex.Message}"); }

            if (slides is null || slides.Count == 0) return Failed("No slides were supplied.");

            Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
            PowerPointBuilder.Build(canonical, slides);

            return Ok($"Created {PathGuard.Describe(canonical)} — {slides.Count} slides.");
        },
        [target], ApprovalKind.WriteFiles);
    }

    // ── PDF ───────────────────────────────────────────────────────────────────────────────

    [Description("Create a PDF document.")]
    private Task<string> CreatePdfAsync(
        [Description("Destination .pdf path.")] string path,
        [Description("Body content as lightweight Markdown.")] string content,
        [Description("Optional title placed on the first page.")] string? title = null,
        [Description("Optional subtitle or byline under the title.")] string? subtitle = null,
        [Description("Overwrite if the file exists.")] bool overwrite = false)
    {
        var target = Locate(path);

        return GuardedAsync("doc.pdf", $"Create PDF {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureWritable(EnsureExtension(target, ".pdf"));
            if (File.Exists(canonical) && !overwrite)
                return Refused($"{PathGuard.Describe(canonical)} already exists. Call again with overwrite=true to replace it.");

            var blocks = ParseMarkdown(content).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);

            PdfDocument.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(style => style.FontSize(11).FontColor("#1A1D24"));

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        page.Header().PaddingBottom(12).Column(column =>
                        {
                            column.Item().Text(title).FontSize(20).SemiBold().FontColor("#12151C");
                            if (!string.IsNullOrWhiteSpace(subtitle))
                                column.Item().PaddingTop(2).Text(subtitle).FontSize(10).FontColor("#6B7280");
                            column.Item().PaddingTop(8).LineHorizontal(0.8f).LineColor("#D7DBE2");
                        });
                    }

                    page.Content().Column(column =>
                    {
                        column.Spacing(6);

                        foreach (var block in blocks)
                        {
                            switch (block.Kind)
                            {
                                case BlockKind.Heading1:
                                    column.Item().PaddingTop(10).Text(block.Text).FontSize(16).SemiBold();
                                    break;
                                case BlockKind.Heading2:
                                    column.Item().PaddingTop(8).Text(block.Text).FontSize(13).SemiBold();
                                    break;
                                case BlockKind.Heading3:
                                    column.Item().PaddingTop(6).Text(block.Text).FontSize(11.5f).SemiBold();
                                    break;
                                case BlockKind.Bullet:
                                    column.Item().PaddingLeft(12).Text(t =>
                                    {
                                        t.Span("•  ").FontColor("#6B7280");
                                        AppendRich(t, block.Text);
                                    });
                                    break;
                                default:
                                    column.Item().Text(t => AppendRich(t, block.Text));
                                    break;
                            }
                        }
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.DefaultTextStyle(style => style.FontSize(9).FontColor("#9AA1AC"));
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            }).GeneratePdf(canonical);

            return Ok($"Created {PathGuard.Describe(canonical)} — {Human(new FileInfo(canonical).Length)}.");
        },
        [target], ApprovalKind.WriteFiles);
    }

    private static void AppendRich(TextDescriptor text, string content)
    {
        foreach (var (segment, isBold) in SplitBold(content))
        {
            if (isBold) text.Span(segment).SemiBold();
            else text.Span(segment);
        }
    }

    // ── Shared Markdown handling ──────────────────────────────────────────────────────────

    internal enum BlockKind { Paragraph, Heading1, Heading2, Heading3, Bullet, Numbered }

    internal readonly record struct MarkdownBlock(BlockKind Kind, string Text);

    /// <summary>
    /// A deliberately small Markdown subset — headings, bullets, numbers, bold. Enough for the
    /// documents an assistant actually produces, and small enough to behave predictably.
    /// </summary>
    internal static IEnumerable<MarkdownBlock> ParseMarkdown(string content)
    {
        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0) continue;

            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
                yield return new(BlockKind.Heading3, trimmed[4..].Trim());
            else if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                yield return new(BlockKind.Heading2, trimmed[3..].Trim());
            else if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                yield return new(BlockKind.Heading1, trimmed[2..].Trim());
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                yield return new(BlockKind.Bullet, trimmed[2..].Trim());
            else if (Regex.IsMatch(trimmed, @"^\d+\.\s"))
                yield return new(BlockKind.Numbered, trimmed);
            else
                yield return new(BlockKind.Paragraph, trimmed);
        }
    }

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;
}

/// <summary>Shape the model fills in for <c>doc_create_powerpoint</c>.</summary>
public sealed class SlideSpec
{
    public string Title { get; set; } = "";
    public List<string> Bullets { get; set; } = [];
    public string? Notes { get; set; }
    public string? Subtitle { get; set; }
}
