using ElBruno.MarkItDotNet;
using ElBruno.MarkItDotNet.Converters;
using ElBruno.MarkItDotNet.Excel;
using ElBruno.MarkItDotNet.PowerPoint;

namespace AutoWork.Tools;

public sealed record DocumentTextResult(bool Success, string Text, string Format, string Error)
{
    public static DocumentTextResult Failed(string format, string error) => new(false, "", format, error);
}

/// <summary>
/// Reads a document of almost any office format as Markdown.
///
/// Word, PowerPoint, Excel, PDF, CSV, HTML and plain text each need their own parser, and doing
/// that by hand is how a "read this file" feature ends up supporting two formats and refusing
/// the rest. MarkItDotNet already covers them, and Markdown is the right target: it keeps the
/// headings, lists and tables that carry a document's meaning without the markup noise that
/// would otherwise eat the model's context.
///
/// The converter list is written out explicitly rather than taken from the library's DI
/// registration. Two reasons: the app deliberately avoids a container, and an import feature
/// should accept the formats it says it accepts — a format nobody chose to support is refused
/// by name instead of being quietly attempted.
/// </summary>
public static class DocumentText
{
    /// <summary>Extensions offered in the file picker and accepted by the reader.</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } =
    [
        ".docx", ".pdf", ".pptx", ".xlsx", ".xls", ".csv",
        ".txt", ".md", ".markdown", ".html", ".htm", ".json", ".xml", ".rtf",
    ];

    // One registry for the process. The converters are stateless and building the registry on
    // every import would be pointless work on the UI thread.
    private static readonly Lazy<MarkdownService> Service = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    private static MarkdownService Build()
    {
        var registry = new ConverterRegistry();

        registry.Register(new PlainTextConverter());
        registry.Register(new MarkdownPassthroughConverter());
        registry.Register(new DocxConverter());
        registry.Register(new PdfConverter());
        registry.Register(new CsvConverter());
        registry.Register(new HtmlConverter());
        registry.Register(new JsonConverter());
        registry.Register(new XmlConverter());
        registry.Register(new RtfConverter());

        // Excel and PowerPoint arrive as plugins rather than converters.
        return new MarkdownService(registry, [new ExcelPlugin(), new PowerPointPlugin()]);
    }

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Converts a file to Markdown. Never throws: callers are a tool that must return a readable
    /// refusal, and a UI action that must show one.
    /// </summary>
    public static async Task<DocumentTextResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path);

        if (!File.Exists(path))
            return DocumentTextResult.Failed(extension, $"{Path.GetFileName(path)} does not exist.");

        if (!IsSupported(path))
        {
            return DocumentTextResult.Failed(extension,
                $"{(extension.Length == 0 ? "A file with no extension" : extension)} cannot be read as text. " +
                $"Supported: {string.Join(", ", SupportedExtensions)}.");
        }

        try
        {
            var result = await Service.Value.ConvertAsync(path, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
                return DocumentTextResult.Failed(extension, result.ErrorMessage ?? "The file could not be converted.");

            var text = result.Markdown ?? "";

            // A file that converts to nothing is a scanned PDF or an empty document. Saying so
            // is more use than handing back an empty note and letting the user wonder.
            if (string.IsNullOrWhiteSpace(text))
            {
                return DocumentTextResult.Failed(extension,
                    "The file converted to no text at all. If it is a scanned PDF or an image-only " +
                    "document, it needs OCR rather than text extraction.");
            }

            return new DocumentTextResult(true, text.Trim(), extension, "");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DocumentTextResult.Failed(extension, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>A note title from a filename: "Q3 Revenue Report.docx" → "Q3 Revenue Report".</summary>
    public static string TitleFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name.Replace('_', ' ').Trim();
    }
}
