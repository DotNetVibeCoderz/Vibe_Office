// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.ComponentModel;
using System.Text;
using ExcelNet;
using ExcelNet.Io;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WordNet;
using WordNet.Export;

namespace OfficeNet.Mcp;

/// <summary>
/// The workspace's documents as MCP resources: a client can list them and attach one to a
/// conversation as text, without a tool call.
/// </summary>
/// <remarks>
/// <para>
/// A resource is read as what a model can use — a Word document as Markdown, each sheet of a workbook
/// as CSV, a deck or a PDF as its text — not as the raw zip, which no client could show. Reading goes
/// through <see cref="Workspace.Existing"/> like every tool, so a URI that climbs out of the workspace
/// is refused the same way.
/// </para>
/// <para>
/// URIs are <c>officenet://workspace/</c> plus the workspace path, each segment percent-encoded.
/// </para>
/// </remarks>
[McpServerResourceType]
public sealed class WorkspaceResources(Workspace workspace)
{
    /// <summary>The prefix every resource URI starts with.</summary>
    public const string Prefix = "officenet://workspace/";

    private const int MaxCharacters = 200_000;
    private const int MaxListed = 500;

    private static readonly string[] Extensions = [".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm", ".pdf"];

    /// <summary>Reads a document as text.</summary>
    [McpServerResource(UriTemplate = Prefix + "{+path}", Name = "document", Title = "Workspace document")]
    [Description("A .docx as Markdown, each sheet of a .xlsx as CSV, or the text of a .pptx or .pdf in the workspace.")]
    public ResourceContents Read(string path) =>
        ToolGuard.Run<ResourceContents>(() =>
        {
            // The SDK has already percent-decoded the path. Decoding again would turn a file named
            // "kode%41.docx" into "kodeA.docx" — and "..%252F" into a climb the boundary must refuse.
            var relative = path;
            var full = workspace.Existing(relative);
            var (text, mimeType) = Content(full);

            if (text.Length > MaxCharacters)
            {
                text = text[..MaxCharacters] + $"\n\n[… {text.Length - MaxCharacters} more characters not shown]";
            }

            return new TextResourceContents { Uri = UriFor(relative), MimeType = mimeType, Text = text };
        });

    /// <summary>Lists the workspace's documents, so a client can offer them without a tool call.</summary>
    public ListResourcesResult List()
    {
        var resources = Directory.EnumerateFiles(workspace.Root, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Take(MaxListed)
            .Select(f =>
            {
                var relative = workspace.Relative(f);

                return new Resource
                {
                    Uri = UriFor(relative),
                    Name = relative,
                    MimeType = MimeTypeOf(f),
                    Size = new FileInfo(f).Length,
                };
            })
            .ToList();

        return new ListResourcesResult { Resources = resources };
    }

    private static string UriFor(string relative) =>
        Prefix + string.Join('/', relative.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

    private static string MimeTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".docx" or ".docm" => "text/markdown",
        ".xlsx" or ".xlsm" => "text/csv",
        _ => "text/plain",
    };

    private static (string Text, string MimeType) Content(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".docx" or ".docm":
            {
                using var document = WordDocument.Open(path);
                return (WordToMarkdown.Convert(document), "text/markdown");
            }

            case ".xlsx" or ".xlsm":
            {
                // Every sheet, each under its name: CSV alone cannot say where one sheet ends.
                using var workbook = Workbook.Open(path);
                var text = new StringBuilder();

                foreach (var sheet in workbook)
                {
                    text.Append("# ").AppendLine(sheet.Name).AppendLine(CsvIo.ExportText(sheet));
                }

                return (text.ToString(), "text/csv");
            }

            case ".pptx" or ".pptm" or ".pdf":
                return (Office.ExtractText(path), "text/plain");

            default:
                throw new McpException(
                    $"'{Path.GetFileName(path)}' is not a document this server reads: use a .docx, .xlsx, .pptx or .pdf.");
        }
    }
}
