// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.ComponentModel;
using ModelContextProtocol.Server;
using PdfNet.Document;

namespace OfficeNet.Mcp.Tools;

/// <summary>PDF tools.</summary>
[McpServerToolType]
public sealed class PdfTools(Workspace workspace)
{
    [McpServerTool(Name = "pdf_merge", Title = "Merge PDFs", Destructive = true)]
    [Description("Concatenates PDFs into one, in order.")]
    public string Merge(
        [Description("Workspace path of the merged PDF.")] string outputPath,
        [Description("Workspace paths of the PDFs, in order.")] string[] inputs) =>
        ToolGuard.Run(() =>
        {
            if (inputs.Length < 2)
            {
                throw new ArgumentException("Give at least two PDFs to merge.");
            }

            var output = workspace.ForWriting(outputPath);
            var documents = inputs.Select(i => PdfDocument.Open(workspace.Existing(i))).ToList();

            try
            {
                using var merged = PdfDocument.Concat([.. documents]);
                merged.Save(output);
                return $"Merged {inputs.Length} PDFs ({merged.Pages.Count} pages) into {workspace.Relative(output)}.";
            }
            finally
            {
                documents.ForEach(d => d.Dispose());
            }
        });

    [McpServerTool(Name = "pdf_split", Title = "Split a PDF", Destructive = true)]
    [Description("Splits a PDF into parts of N pages each.")]
    public string Split(
        [Description("Workspace path of the PDF.")] string path,
        [Description("Workspace directory for the parts.")] string outputDirectory,
        [Description("Pages per part. Defaults to 1.")] int pagesPerPart = 1) =>
        ToolGuard.Run(() =>
        {
            var directory = workspace.ResolveDirectory(outputDirectory, create: true);

            using var document = PdfDocument.Open(workspace.Existing(path));
            var parts = document.Split(Math.Max(1, pagesPerPart));
            var stem = Path.GetFileNameWithoutExtension(path);
            var written = new List<string>();

            for (var i = 0; i < parts.Count; i++)
            {
                using var part = parts[i];
                var file = Path.Combine(directory, $"{stem}-{i + 1:000}.pdf");
                part.Save(file);
                written.Add(workspace.Relative(file));
            }

            return Values.ToJson(new { parts = written });
        });

    [McpServerTool(Name = "pdf_protect", Title = "Encrypt a PDF", Destructive = true)]
    [Description("Encrypts a PDF with AES-256 so it needs a password to open.")]
    public string Protect(
        [Description("Workspace path of the PDF.")] string path,
        [Description("Workspace path of the encrypted copy.")] string outputPath,
        [Description("The password needed to open it.")] string userPassword,
        [Description("The owner password; defaults to the user password.")] string? ownerPassword = null) =>
        ToolGuard.Run(() =>
        {
            var output = workspace.ForWriting(outputPath);

            using var document = PdfDocument.Open(workspace.Existing(path));
            document.Encrypt(userPassword, ownerPassword);
            document.Save(output);
            return $"Wrote encrypted {workspace.Relative(output)}.";
        });
}
