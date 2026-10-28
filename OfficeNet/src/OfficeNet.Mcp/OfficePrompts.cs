// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace OfficeNet.Mcp;

/// <summary>
/// Ready-made requests a client can offer as commands: each one is a plan written in terms of this
/// server's own tools, so a model follows a route that is known to work.
/// </summary>
/// <remarks>
/// A prompt only produces text; it runs nothing. Every path in it is still resolved by the tools,
/// inside the workspace, when the model calls them.
/// </remarks>
[McpServerPromptType]
public sealed class OfficePrompts
{
    [McpServerPrompt(Name = "report_from_spreadsheet", Title = "Report from a spreadsheet")]
    [Description("Writes a Word report with a summary, a table and a chart from a workbook in the workspace.")]
    public static string ReportFromSpreadsheet(
        [Description("Workspace path of the .xlsx to report on.")] string spreadsheet,
        [Description("Where to write the report. Defaults to laporan.docx.")] string? output = null) =>
        $"""
        Write a report on the workbook `{spreadsheet}` as `{output ?? "laporan.docx"}`.

        1. Call `document_info` on `{spreadsheet}` to see its sheets, then `excel_read` the ranges that hold the data.
        2. Work out what the numbers show: totals, the largest and smallest values, the trend over time if there is one.
        3. Call `word_create` with: a level-1 heading; two or three paragraphs that state the findings plainly, with the
           figures; a table of the key rows; and a chart of the main series.
        4. Call `render_page` on the report and look at it. Fix anything that reads badly, then say where the report is.

        Use only numbers that are in the workbook.
        """;

    [McpServerPrompt(Name = "summarize_document", Title = "Summarise a document")]
    [Description("Reads a .docx, .xlsx, .pptx or .pdf in the workspace and summarises it.")]
    public static string SummarizeDocument(
        [Description("Workspace path of the document.")] string path,
        [Description("How long the summary should be. Defaults to one paragraph.")] string? length = null) =>
        $"""
        Summarise `{path}` in {length ?? "one paragraph"}.

        Read it with `extract_text` (or `document_info` first, for a long document). Keep the summary to what the
        document says — names, figures and dates exactly as written — and say if part of it could not be read.
        """;

    [McpServerPrompt(Name = "deck_from_document", Title = "Slides from a document")]
    [Description("Turns a document in the workspace into a short PowerPoint deck.")]
    public static string DeckFromDocument(
        [Description("Workspace path of the source document.")] string source,
        [Description("Where to write the deck. Defaults to ringkasan.pptx.")] string? output = null,
        [Description("About how many slides. Defaults to 6.")] int slides = 6) =>
        $"""
        Make a deck of about {slides} slides from `{source}` and save it as `{output ?? "ringkasan.pptx"}`.

        1. Read the source with `extract_text`.
        2. Plan the slides: a title slide, one slide per main point with three to five short bullets, and a closing
           slide with the conclusions. Where the source has figures, give them a chart.
        3. Build it with `pptx_create`.
        4. Call `render_page` on two or three slides and check that no text overflows its box.
        """;
}
