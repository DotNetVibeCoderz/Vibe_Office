// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ExcelNet;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OfficeNet.TestKit;
using WordNet;
using Xunit;

namespace OfficeNet.Mcp.Tests;

/// <summary>
/// The workspace's documents as resources, and the ready-made prompts — through the process, as a
/// client sees them.
/// </summary>
public class ResourceAndPromptTests : IAsyncLifetime
{
    private readonly TempDirectory _root = new();
    private McpClient _client = null!;

    private static CancellationToken Deadline => new CancellationTokenSource(ServerFixture.Deadline).Token;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root.Path, "laporan 2026"));

        using (var document = WordDocument.Create())
        {
            document.AddHeading("Ringkasan", 1);
            document.AddParagraph("Pendapatan naik 32 persen.");
            document.Save(Path.Combine(_root.Path, "laporan 2026", "ringkasan.docx"));
        }

        using (var workbook = Workbook.Create("Penjualan"))
        {
            workbook.Worksheets[0]["A1"].Set("Bulan");
            workbook.Worksheets[0]["B1"].Set("Jumlah");
            workbook.Worksheets[0]["A2"].Set("Januari");
            workbook.Worksheets[0]["B2"].Set(1200);
            workbook.AddSheet("Biaya")["A1"].Set("Sewa");
            workbook.Save(Path.Combine(_root.Path, "data.xlsx"));
        }

        File.Copy(Path.Combine(_root.Path, "laporan 2026", "ringkasan.docx"), Path.Combine(_root.Path, "kode%41.docx"));
        File.WriteAllText(Path.Combine(_root.Path, "catatan.txt"), "bukan dokumen");
        _client = await ServerFixture.Start(_root.Path);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _client.DisposeAsync();
        _root.Dispose();
    }

    [Fact]
    public async Task TheWorkspacesDocumentsAreListedAsResources()
    {
        var resources = await _client.ListResourcesAsync(cancellationToken: Deadline);

        // Documents only, with their path as the name; the space is percent-encoded in the URI.
        Assert.Equal(["data.xlsx", "kode%41.docx", "laporan 2026/ringkasan.docx"], resources.Select(r => r.Name).Order(StringComparer.Ordinal));
        Assert.Contains(resources, r => r.Uri == "officenet://workspace/laporan%202026/ringkasan.docx");

        // A listed URI reads back as the file it names — decoded once, so a literal % survives.
        var percent = resources.Single(r => r.Name == "kode%41.docx");
        Assert.Equal("officenet://workspace/kode%2541.docx", percent.Uri);
        var read = await _client.ReadResourceAsync(percent.Uri, cancellationToken: Deadline);
        Assert.Contains("Ringkasan", Assert.IsType<TextResourceContents>(Assert.Single(read.Contents)).Text, StringComparison.Ordinal);

        var templates = await _client.ListResourceTemplatesAsync(cancellationToken: Deadline);
        Assert.Contains(templates, t => t.UriTemplate == "officenet://workspace/{+path}");
    }

    [Fact]
    public async Task AWordDocumentReadsAsMarkdownAndAWorkbookAsCsvPerSheet()
    {
        var word = await _client.ReadResourceAsync("officenet://workspace/laporan%202026/ringkasan.docx", cancellationToken: Deadline);
        var markdown = Assert.IsType<TextResourceContents>(Assert.Single(word.Contents));
        Assert.Equal("text/markdown", markdown.MimeType);
        Assert.Contains("# Ringkasan", markdown.Text, StringComparison.Ordinal);
        Assert.Contains("Pendapatan naik 32 persen.", markdown.Text, StringComparison.Ordinal);

        var excel = await _client.ReadResourceAsync("officenet://workspace/data.xlsx", cancellationToken: Deadline);
        var csv = Assert.IsType<TextResourceContents>(Assert.Single(excel.Contents)).Text;
        Assert.Contains("# Penjualan", csv, StringComparison.Ordinal);
        Assert.Contains("Januari,1200", csv, StringComparison.Ordinal);
        Assert.Contains("# Biaya", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResourceOutsideTheWorkspaceIsRefused()
    {
        using var outside = new TempDirectory();
        File.Copy(Path.Combine(_root.Path, "data.xlsx"), Path.Combine(outside.Path, "rahasia.xlsx"));
        var escape = Path.GetRelativePath(_root.Path, Path.Combine(outside.Path, "rahasia.xlsx")).Replace('\\', '/');

        await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await _client.ReadResourceAsync("officenet://workspace/" + escape, cancellationToken: Deadline));

        await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await _client.ReadResourceAsync("officenet://workspace/..%2F" + Path.GetFileName(outside.Path) + "%2Frahasia.xlsx", cancellationToken: Deadline));

        // And a file that is not a document says so.
        await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await _client.ReadResourceAsync("officenet://workspace/catatan.txt", cancellationToken: Deadline));
    }

    [Fact]
    public async Task ThePromptsArePlansInTermsOfTheTools()
    {
        var prompts = await _client.ListPromptsAsync(cancellationToken: Deadline);
        Assert.Equal(["deck_from_document", "report_from_spreadsheet", "summarize_document"],
            prompts.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(prompts, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));

        var report = await _client.GetPromptAsync("report_from_spreadsheet",
            new Dictionary<string, object?> { ["spreadsheet"] = "data.xlsx" }, cancellationToken: Deadline);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(report.Messages).Content).Text;
        Assert.Contains("`data.xlsx`", text, StringComparison.Ordinal);
        Assert.Contains("`laporan.docx`", text, StringComparison.Ordinal);

        // Every tool a prompt names exists, so the plan can be followed.
        var tools = (await _client.ListToolsAsync(cancellationToken: Deadline)).Select(t => t.Name).ToHashSet();

        foreach (var prompt in prompts)
        {
            var arguments = prompt.ProtocolPrompt.Arguments?.Where(a => a.Required == true)
                .ToDictionary(a => a.Name, _ => (object?)"x.docx") ?? [];
            var body = string.Concat((await _client.GetPromptAsync(prompt.Name, arguments, cancellationToken: Deadline))
                .Messages.Select(m => (m.Content as TextContentBlock)?.Text));

            foreach (System.Text.RegularExpressions.Match named in System.Text.RegularExpressions.Regex.Matches(body, "`([a-z]+_[a-z_]+)`"))
            {
                Assert.Contains(named.Groups[1].Value, tools);
            }
        }
    }
}
