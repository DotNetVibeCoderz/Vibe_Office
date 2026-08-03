using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Tools;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// Office formats fail silently: a malformed package still writes to disk and only breaks when
/// a human double-clicks it. These tests run the real OpenXML validator over what we generate,
/// which is the only way to know the files actually open.
/// </summary>
public sealed class DocumentGenerationTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "autowork-tests", Guid.NewGuid().ToString("n")[..8]);
    private readonly DocumentTools _tools;

    public DocumentGenerationTests()
    {
        Directory.CreateDirectory(_workspace);

        var policy = new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite }],
            AllowDelete = true,
        };

        _tools = new DocumentTools(new ToolContext
        {
            Guard = new PathGuard(policy),
            Approvals = new AutoApproveBroker(),
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "test",
            WorkingDirectory = _workspace,
        });
    }

    private AIFunction Tool(string name) =>
        _tools.GetTools(Context()).Single(t => t.Name == name).Function;

    private ToolContext Context() => new()
    {
        Guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite }],
            AllowDelete = true,
        }),
        Approvals = new AutoApproveBroker(),
        Log = NullActionLog.Instance,
        Options = new AgentOptions(),
        RunId = "test",
        WorkingDirectory = _workspace,
    };

    private async Task<string> InvokeAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await Tool(tool).InvokeAsync(new AIFunctionArguments(arguments));
        return result?.ToString() ?? "";
    }

    [Fact]
    public async Task Excel_workbook_opens_and_keeps_formulas_live()
    {
        var path = Path.Combine(_workspace, "sales.xlsx");

        var result = await InvokeAsync("doc_create_excel", new()
        {
            ["path"] = path,
            ["sheetsJson"] = """
                [{"name":"Q3","rows":[["Region","Jul","Aug","Total"],["North",100,120,"=B2+C2"],["South",90,80,"=B3+C3"]],"autoFilter":true}]
                """,
        });

        Assert.DoesNotContain("ERROR", result);
        Assert.True(File.Exists(path));

        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        var sheet = workbook.Worksheet("Q3");

        // The formula must survive as a formula, not be flattened to text.
        Assert.Equal("B2+C2", sheet.Cell("D2").FormulaA1);
        Assert.Equal(100, sheet.Cell("B2").GetDouble());
    }

    [Fact]
    public async Task Word_document_passes_the_openxml_validator()
    {
        var path = Path.Combine(_workspace, "report.docx");

        var result = await InvokeAsync("doc_create_word", new()
        {
            ["path"] = path,
            ["title"] = "Quarterly report",
            ["content"] = "# Summary\n\nRevenue grew **12 percent**.\n\n- North exceeded target\n- South held flat\n\n## Next steps\n\nReview pricing.",
        });

        Assert.DoesNotContain("ERROR", result);

        using var document = WordprocessingDocument.Open(path, false);
        AssertValid(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PowerPoint_deck_passes_the_openxml_validator()
    {
        var path = Path.Combine(_workspace, "deck.pptx");

        var result = await InvokeAsync("doc_create_powerpoint", new()
        {
            ["path"] = path,
            ["slidesJson"] = """
                [{"title":"Q3 results","subtitle":"Gravicode Studios","bullets":["Revenue up 12%","Costs flat"],"notes":"Open with the revenue number."},
                 {"title":"Next quarter","bullets":["Hire two engineers","Ship the browser extension"]}]
                """,
        });

        Assert.DoesNotContain("ERROR", result);

        using var presentation = PresentationDocument.Open(path, false);
        Assert.Equal(2, presentation.PresentationPart!.SlideParts.Count());

        AssertValid(new OpenXmlValidator().Validate(presentation, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// PowerPoint refuses to open a deck whose layout has no relationship back to its master —
    /// with a "file is corrupted" error that names nothing and points nowhere.
    ///
    /// The validator does not check cross-part references, so for a long time every deck
    /// AutoWork produced passed the test above and could not be opened by PowerPoint. Package
    /// wiring therefore gets asserted directly, part by part, rather than being assumed from a
    /// clean validation run.
    /// </summary>
    [Fact]
    public async Task The_deck_is_wired_up_the_way_PowerPoint_requires()
    {
        var path = Path.Combine(_workspace, "wiring.pptx");

        var result = await InvokeAsync("doc_create_powerpoint", new()
        {
            ["path"] = path,
            ["slidesJson"] = """[{"title":"One","bullets":["a"],"notes":"speaker note"},{"title":"Two","bullets":["b"]}]""",
        });

        Assert.DoesNotContain("ERROR", result);

        using var presentation = PresentationDocument.Open(path, false);
        var presentationPart = presentation.PresentationPart!;

        var master = Assert.Single(presentationPart.SlideMasterParts);
        var layout = Assert.Single(master.SlideLayoutParts);

        // The relationship that was missing. Both directions have to exist.
        Assert.NotNull(layout.SlideMasterPart);
        Assert.Equal(master.Uri, layout.SlideMasterPart!.Uri);

        // A theme is mandatory, and it hangs off the master.
        Assert.NotNull(master.ThemePart);

        foreach (var slide in presentationPart.SlideParts)
        {
            Assert.NotNull(slide.SlideLayoutPart);
            Assert.Equal(layout.Uri, slide.SlideLayoutPart!.Uri);
        }

        // Every slide id in the presentation must resolve to a slide part that is really there.
        var slideIds = presentationPart.Presentation?.SlideIdList;
        Assert.NotNull(slideIds);

        foreach (var slideId in slideIds.Elements<DocumentFormat.OpenXml.Presentation.SlideId>())
        {
            var relationshipId = slideId.RelationshipId?.Value;
            Assert.NotNull(relationshipId);
            Assert.NotNull(presentationPart.GetPartById(relationshipId));
        }
    }

    [Fact]
    public async Task Pdf_is_produced_with_a_valid_header()
    {
        var path = Path.Combine(_workspace, "note.pdf");

        var result = await InvokeAsync("doc_create_pdf", new()
        {
            ["path"] = path,
            ["title"] = "Meeting notes",
            ["content"] = "# Attendees\n\n- Fadhil\n- Team\n\nDiscussed **scope** for the next release.",
        });

        Assert.DoesNotContain("ERROR", result);

        var header = new byte[5];
        using (var stream = File.OpenRead(path)) _ = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(header));
        Assert.True(new FileInfo(path).Length > 1000);
    }

    private static void AssertValid(IEnumerable<ValidationErrorInfo> errors)
    {
        var list = errors.ToList();
        if (list.Count == 0) return;

        var detail = string.Join("\n", list.Take(10).Select(e => $"{e.Path?.XPath}: {e.Description}"));
        Assert.Fail($"OpenXML validation reported {list.Count} problem(s):\n{detail}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }
}
