using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Tools;
using ClosedXML.Excel;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// Reading documents back as text. The fixtures are produced by AutoWork's own generators, so a
/// change that breaks writing a .docx also shows up here as a failure to read one.
/// </summary>
public sealed class DocumentTextTests : IDisposable
{
    private readonly string _dir;

    private readonly DocumentTools _documents;

    public DocumentTextTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "autowork-doctext", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(_dir);

        _documents = new DocumentTools(Context());
    }

    private ToolContext Context() => new()
    {
        Guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _dir, Access = FolderAccess.ReadWrite }],
        }),
        Approvals = new AutoApproveBroker(),
        Log = NullActionLog.Instance,
        Options = new AgentOptions(),
        RunId = "test",
        WorkingDirectory = _dir,
    };

    /// <summary>Fixtures are written by AutoWork's own generators, so writing and reading stay in step.</summary>
    private async Task WriteWithToolAsync(string tool, Dictionary<string, object?> arguments)
    {
        var function = _documents.GetTools(Context()).Single(t => t.Name == tool).Function;
        var result = await function.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("ERROR", result?.ToString() ?? "");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Plain_text_and_markdown_come_back_as_themselves()
    {
        var text = Path.Combine(_dir, "notes.txt");
        await File.WriteAllTextAsync(text, "Decision: ship in March.", TestContext.Current.CancellationToken);

        var result = await DocumentText.ReadAsync(text, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Contains("ship in March", result.Text);
    }

    [Fact]
    public async Task A_word_document_comes_back_as_readable_text()
    {
        var path = Path.Combine(_dir, "report.docx");

        await WriteWithToolAsync("doc_create_word", new()
        {
            ["path"] = path,
            ["title"] = "Quarterly Review",
            ["content"] = "Revenue grew twelve percent.\n\nHeadcount stayed flat.",
        });

        var result = await DocumentText.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Contains("Quarterly Review", result.Text);
        Assert.Contains("Revenue grew twelve percent", result.Text);
    }

    [Fact]
    public async Task A_powerpoint_deck_comes_back_slide_by_slide()
    {
        var path = Path.Combine(_dir, "deck.pptx");

        await WriteWithToolAsync("doc_create_powerpoint", new()
        {
            ["path"] = path,
            ["slidesJson"] = """
                [{"title":"Roadmap","bullets":["Discovery in Q1"]},
                 {"title":"Risks","bullets":["Hiring is the constraint"]}]
                """,
        });

        var result = await DocumentText.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Contains("Roadmap", result.Text);
        Assert.Contains("Hiring is the constraint", result.Text);
    }

    /// <summary>A spreadsheet is only useful as text if the cells keep their rows and columns.</summary>
    [Fact]
    public async Task A_spreadsheet_comes_back_as_a_table()
    {
        var path = Path.Combine(_dir, "figures.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Regions");
            sheet.Cell(1, 1).Value = "Region";
            sheet.Cell(1, 2).Value = "Revenue";
            sheet.Cell(2, 1).Value = "Jakarta";
            sheet.Cell(2, 2).Value = 12400;
            workbook.SaveAs(path);
        }

        var result = await DocumentText.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Contains("Jakarta", result.Text);
        Assert.Contains("12400", result.Text);
    }

    /// <summary>
    /// An unsupported extension has to be refused by name. Attempting it anyway would hand the
    /// model a wall of binary garbage and charge for the privilege.
    /// </summary>
    [Fact]
    public async Task An_unsupported_format_is_refused_and_says_what_is_supported()
    {
        var path = Path.Combine(_dir, "archive.zip");
        await File.WriteAllBytesAsync(path, [0x50, 0x4B, 0x03, 0x04], TestContext.Current.CancellationToken);

        var result = await DocumentText.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(".zip", result.Error);
        Assert.Contains(".docx", result.Error);
    }

    [Fact]
    public async Task A_missing_file_says_so_rather_than_throwing()
    {
        var result = await DocumentText.ReadAsync(Path.Combine(_dir, "nothing.docx"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Error);
    }

    /// <summary>
    /// An empty document converts to nothing, which is indistinguishable from success unless it
    /// is called out — and "your note is blank" is not a mystery a user should have to solve.
    /// </summary>
    [Fact]
    public async Task A_file_that_converts_to_nothing_is_reported_rather_than_returned_empty()
    {
        var path = Path.Combine(_dir, "blank.txt");
        await File.WriteAllTextAsync(path, "   \n  \n", TestContext.Current.CancellationToken);

        var result = await DocumentText.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("no text", result.Error);
    }

    [Theory]
    [InlineData("Q3 Revenue Report.docx", "Q3 Revenue Report")]
    [InlineData("meeting_notes_2026.md", "meeting notes 2026")]
    [InlineData("/home/user/data.xlsx", "data")]
    public void The_title_is_the_file_name_without_its_extension(string path, string expected) =>
        Assert.Equal(expected, DocumentText.TitleFromFileName(path));
}
