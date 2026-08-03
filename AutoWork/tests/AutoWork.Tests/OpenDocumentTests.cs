using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Tools;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// OpenDocument files, checked as packages rather than as XML.
///
/// The lesson from the PowerPoint decks that validated cleanly and would not open is that schema
/// correctness is not the same as a file the application accepts. For ODF the equivalent trap is
/// the <c>mimetype</c> entry, so that is asserted first and directly.
/// </summary>
public sealed class OpenDocumentTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "autowork-odf", Guid.NewGuid().ToString("n")[..8]);

    private readonly DocumentTools _tools;

    public OpenDocumentTests()
    {
        Directory.CreateDirectory(_workspace);

        _tools = new DocumentTools(new ToolContext
        {
            Guard = new PathGuard(new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            }),
            Approvals = new AutoApproveBroker(),
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "odf",
            WorkingDirectory = _workspace,
        });
    }

    public void Dispose()
    {
        // Kept when asked for, so the files can be opened in a real application — the only check
        // that actually settles whether a document format works.
        if (Environment.GetEnvironmentVariable("AUTOWORK_LIVE_KEEP_OUTPUT") == "1")
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"ODF output kept in {_workspace}");
            return;
        }

        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// Writes one of each into a fixed folder so a person, or the script that drives Word and
    /// Excel, can open them. Skipped unless output is being kept.
    /// </summary>
    [Fact]
    public async Task Samples_for_opening_in_a_real_application()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AUTOWORK_LIVE_KEEP_OUTPUT") == "1",
            "Set AUTOWORK_LIVE_KEEP_OUTPUT=1 to write ODF samples for manual opening.");

        var folder = Path.Combine(Path.GetTempPath(), "autowork-odf-samples");
        Directory.CreateDirectory(folder);

        // The guard only permits the test workspace, so they are written there and copied out.
        var odt = Path.Combine(_workspace, "sample.odt");
        var ods = Path.Combine(_workspace, "sample.ods");

        await InvokeAsync("doc_create_odt", new()
        {
            ["path"] = odt,
            ["title"] = "Quarterly report",
            ["content"] = "# Findings\n\nRevenue rose by 12% against a flat cost base.\n\n" +
                          "- North grew fastest\n- South held steady\n\n## Next steps\n\nReview in April.",
            ["overwrite"] = true,
        });

        await InvokeAsync("doc_create_ods", new()
        {
            ["path"] = ods,
            ["sheetsJson"] = """[{"name":"Sales","rows":[["Region","Q1","Q2","Total"],["North",100,120,"=SUM(B2:C2)"],["South",80,85,"=SUM(B3:C3)"]]}]""",
            ["overwrite"] = true,
        });

        File.Copy(odt, Path.Combine(folder, "sample.odt"), overwrite: true);
        File.Copy(ods, Path.Combine(folder, "sample.ods"), overwrite: true);

        TestContext.Current.TestOutputHelper?.WriteLine($"samples written to {folder}");
    }

    private AIFunction Tool(string name) =>
        _tools.GetTools(new ToolContext
        {
            Guard = new PathGuard(new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            }),
            Approvals = new AutoApproveBroker(),
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "odf",
            WorkingDirectory = _workspace,
        }).First(t => t.Name == name).Function;

    private async Task<string> InvokeAsync(string tool, Dictionary<string, object?> arguments) =>
        (await Tool(tool).InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken))
        ?.ToString() ?? "";

    /// <summary>
    /// The one that decides whether the file opens. A reader identifies an ODF package from a
    /// `mimetype` entry that is first in the archive and stored uncompressed — every ZIP tool
    /// will happily produce one that is neither, and report the archive as perfectly fine.
    /// </summary>
    [Fact]
    public async Task The_mimetype_entry_is_first_and_stored_uncompressed()
    {
        var path = Path.Combine(_workspace, "notes.odt");

        await InvokeAsync("doc_create_odt", new() { ["path"] = path, ["content"] = "# Title\n\nA paragraph." });

        using var archive = ZipFile.OpenRead(path);

        var first = archive.Entries[0];
        Assert.Equal("mimetype", first.FullName);

        // Stored, not deflated: compressed and uncompressed lengths match exactly.
        Assert.Equal(first.Length, first.CompressedLength);

        using var reader = new StreamReader(first.Open(), Encoding.UTF8);
        Assert.Equal("application/vnd.oasis.opendocument.text", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_odt_carries_the_parts_a_reader_looks_for()
    {
        var path = Path.Combine(_workspace, "report.odt");

        await InvokeAsync("doc_create_odt", new()
        {
            ["path"] = path,
            ["title"] = "Quarterly report",
            ["content"] = "# Findings\n\nRevenue rose.\n\n- North grew\n- South held",
        });

        using var archive = ZipFile.OpenRead(path);
        var names = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.Contains("META-INF/manifest.xml", names);
        Assert.Contains("content.xml", names);
        Assert.Contains("styles.xml", names);

        var content = XDocument.Parse(ReadEntry(archive, "content.xml"));
        var text = content.ToString();

        Assert.Contains("Quarterly report", text);
        Assert.Contains("Findings", text);
        Assert.Contains("North grew", text);

        // The manifest has to declare the package's own media type or the file is not identified.
        Assert.Contains("application/vnd.oasis.opendocument.text", ReadEntry(archive, "META-INF/manifest.xml"));
    }

    [Fact]
    public async Task An_ods_keeps_numbers_numeric_and_formulas_live()
    {
        var path = Path.Combine(_workspace, "sales.ods");

        await InvokeAsync("doc_create_ods", new()
        {
            ["path"] = path,
            ["sheetsJson"] = """[{"name":"Sales","rows":[["Region","Q1","Q2","Total"],["North",100,120,"=SUM(B2:C2)"]]}]""",
        });

        using var archive = ZipFile.OpenRead(path);
        var content = ReadEntry(archive, "content.xml");

        Assert.Contains("Sales", content);

        // A number written as a string would sort and total wrongly in the spreadsheet.
        Assert.Contains("value-type=\"float\"", content);
        Assert.Contains("office:value=\"100\"", content);

        // Namespaced, and in OpenFormula reference syntax. Excel opens a file with plain A1
        // references and silently drops the formula, which is how this was found.
        Assert.Contains("xmlns:of=\"urn:oasis:names:tc:opendocument:xmlns:of:1.2\"", content);
        Assert.Contains("table:formula=\"of:=SUM([.B2:.C2])\"", content);
    }

    [Theory]
    [InlineData("=SUM(B2:C2)", "of:=SUM([.B2:.C2])")]
    [InlineData("=B2+C2", "of:=[.B2]+[.C2]")]
    [InlineData("=A1*2", "of:=[.A1]*2")]
    [InlineData("=SUM($A$1:$B$9)", "of:=SUM([.$A$1:.$B$9])")]
    [InlineData("=ROUND(AVERAGE(B2:B9),2)", "of:=ROUND(AVERAGE([.B2:.B9]),2)")]
    [InlineData("=IF(A1>0,\"B2 stays text\",A2)", "of:=IF([.A1]>0,\"B2 stays text\",[.A2])")]
    [InlineData("=TODAY()", "of:=TODAY()")]
    public void A1_formulas_are_rewritten_into_OpenFormula(string input, string expected) =>
        Assert.Equal(expected, OpenDocumentBuilder.ToOpenFormula(input));

    [Fact]
    public async Task Creating_over_an_existing_file_is_refused_unless_asked_for()
    {
        var path = Path.Combine(_workspace, "once.odt");

        await InvokeAsync("doc_create_odt", new() { ["path"] = path, ["content"] = "first" });
        var second = await InvokeAsync("doc_create_odt", new() { ["path"] = path, ["content"] = "second" });

        Assert.StartsWith("REFUSED:", second);

        var third = await InvokeAsync("doc_create_odt", new() { ["path"] = path, ["content"] = "second", ["overwrite"] = true });
        Assert.DoesNotContain("REFUSED", third);
    }

    // ── Markdown export ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_word_document_can_be_exported_back_to_markdown()
    {
        var docx = Path.Combine(_workspace, "source.docx");

        await InvokeAsync("doc_create_word", new()
        {
            ["path"] = docx,
            ["content"] = "# Heading one\n\nA paragraph of prose.\n\n- First bullet\n- Second bullet",
        });

        var result = await InvokeAsync("doc_export_markdown", new() { ["path"] = docx });

        Assert.DoesNotContain("ERROR", result);

        var markdown = Path.Combine(_workspace, "source.md");
        Assert.True(File.Exists(markdown), result);

        var text = await File.ReadAllTextAsync(markdown, TestContext.Current.CancellationToken);

        Assert.Contains("Heading one", text);
        Assert.Contains("A paragraph of prose.", text);
        Assert.Contains("First bullet", text);
    }

    [Fact]
    public async Task Exporting_a_format_that_cannot_be_converted_says_so_rather_than_writing_nothing()
    {
        var path = Path.Combine(_workspace, "picture.png");
        await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47], TestContext.Current.CancellationToken);

        var result = await InvokeAsync("doc_export_markdown", new() { ["path"] = path });

        Assert.StartsWith("REFUSED:", result);
        Assert.False(File.Exists(Path.Combine(_workspace, "picture.md")));
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Template filling. The interesting case is a Word template, where a placeholder typed as one
/// word is routinely stored split across runs — the reason a naive implementation reports a
/// template with no placeholders in it.
/// </summary>
public sealed class DocumentTemplateTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "autowork-template", Guid.NewGuid().ToString("n")[..8]);

    public DocumentTemplateTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Text_placeholders_are_filled_and_spacing_inside_the_braces_is_tolerated()
    {
        var result = DocumentTemplate.FillText(
            "Dear {{name}}, invoice {{ invoice }} is due on {{due}}.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = "Ada", ["invoice"] = "INV-42", ["due"] = "1 May",
            });

        Assert.Equal("Dear Ada, invoice INV-42 is due on 1 May.", result.Text);
        Assert.Equal(3, result.Replaced);
        Assert.Empty(result.Unfilled);
        Assert.Empty(result.Unused);
    }

    /// <summary>
    /// A document shipped with {{amount}} still in it is worse than a refusal, so the leftovers
    /// are named and the marker is left intact for a second pass.
    /// </summary>
    [Fact]
    public void A_placeholder_with_no_value_is_reported_and_left_alone()
    {
        var result = DocumentTemplate.FillText(
            "Pay {{amount}} to {{payee}}.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["payee"] = "Acme" });

        Assert.Equal("Pay {{amount}} to Acme.", result.Text);
        Assert.Equal(1, result.Replaced);
        Assert.Equal(["amount"], result.Unfilled);
    }

    [Fact]
    public void A_value_matching_no_placeholder_is_reported_too()
    {
        var result = DocumentTemplate.FillText(
            "Hello {{name}}.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Ada", ["nmae"] = "typo" });

        Assert.Equal(["nmae"], result.Unused);
    }

    /// <summary>
    /// The case a naive implementation gets wrong. Word splits runs whenever anything about the
    /// text changes, so a placeholder typed as one word is stored in pieces.
    /// </summary>
    [Fact]
    public void A_word_placeholder_split_across_runs_is_still_filled()
    {
        var path = Path.Combine(_workspace, "split.docx");
        WriteDocx(path, [["Dear {{na", "me}}, welcome."]]);

        var result = DocumentTemplate.FillWord(path,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Ada" });

        Assert.Equal(1, result.Replaced);
        Assert.Equal("Dear Ada, welcome.", ReadDocxText(path));
    }

    [Fact]
    public void A_word_placeholder_split_across_three_runs_is_still_filled()
    {
        var path = Path.Combine(_workspace, "split3.docx");
        WriteDocx(path, [["Invoice {{inv", "oice_num", "ber}} attached."]]);

        var result = DocumentTemplate.FillWord(path,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["invoice_number"] = "INV-42" });

        Assert.Equal(1, result.Replaced);
        Assert.Equal("Invoice INV-42 attached.", ReadDocxText(path));
    }

    [Fact]
    public void Paragraphs_without_placeholders_are_left_exactly_as_they_were()
    {
        var path = Path.Combine(_workspace, "mixed.docx");
        WriteDocx(path, [["Untouched ", "prose."], ["Hello {{name}}."]]);

        DocumentTemplate.FillWord(path,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Ada" });

        using var document = WordprocessingDocument.Open(path, isEditable: false);
        var paragraphs = document.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ToList();

        // The untouched paragraph keeps both of its runs; only a substituted one is collapsed.
        Assert.Equal(2, paragraphs[0].Descendants<Run>().Count());
        Assert.Equal("Untouched prose.", string.Concat(paragraphs[0].Descendants<Text>().Select(t => t.Text)));
    }

    [Fact]
    public void The_filled_document_still_opens_as_a_valid_word_file()
    {
        var path = Path.Combine(_workspace, "valid.docx");
        WriteDocx(path, [["Dear {{na", "me}},"], ["Your reference is {{ref}}."]]);

        DocumentTemplate.FillWord(path,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Ada", ["ref"] = "R-7" });

        using var document = WordprocessingDocument.Open(path, isEditable: false);

        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(document, TestContext.Current.CancellationToken)
            .Select(e => $"{e.Description} ({e.Path?.XPath})")
            .ToList();

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    /// <summary>Leading and trailing spaces around a substitution must survive the round trip.</summary>
    [Fact]
    public void Spacing_around_a_substitution_is_preserved()
    {
        var path = Path.Combine(_workspace, "spacing.docx");
        WriteDocx(path, [["  {{name}}  is here"]]);

        DocumentTemplate.FillWord(path,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Ada" });

        Assert.Equal("  Ada  is here", ReadDocxText(path));
    }

    private static void WriteDocx(string path, IReadOnlyList<IReadOnlyList<string>> paragraphs)
    {
        using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new Document();
        var body = main.Document.AppendChild(new Body());

        foreach (var runs in paragraphs)
        {
            var paragraph = body.AppendChild(new Paragraph());

            foreach (var run in runs)
            {
                paragraph.AppendChild(new Run(new Text(run)
                {
                    Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve,
                }));
            }
        }

        main.Document.Save();
    }

    private static string ReadDocxText(string path)
    {
        using var document = WordprocessingDocument.Open(path, isEditable: false);

        return string.Join("\n", document.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text))));
    }
}
