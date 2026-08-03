using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Diff;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Tools;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// The diff exists so a person can decide whether to allow a write. Its job is to make the
/// change obvious and to stay cheap on a file large enough that nobody would read it anyway.
/// </summary>
public sealed class TextDiffTests
{
    private static string Lines(params string[] lines) => string.Join('\n', lines);

    [Fact]
    public void Identical_contents_report_no_change_rather_than_an_empty_diff()
    {
        var result = TextDiff.Compare("same", "same");

        Assert.True(result.IsEmpty);
        Assert.Empty(result.Lines);
        Assert.Contains("No change", result.Note);
    }

    [Fact]
    public void One_changed_line_shows_as_one_removal_and_one_addition()
    {
        var before = Lines("alpha", "beta", "gamma");
        var after = Lines("alpha", "BETA", "gamma");

        var result = TextDiff.Compare(before, after);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Removed);
        Assert.Equal("+1 −1", result.Summary);

        Assert.Contains(result.Lines, l => l.Marker == DiffMarker.Removed && l.Text == "beta");
        Assert.Contains(result.Lines, l => l.Marker == DiffMarker.Added && l.Text == "BETA");

        // The lines either side are shown so the change is not floating free.
        Assert.Contains(result.Lines, l => l.Marker == DiffMarker.Context && l.Text == "alpha");
        Assert.Contains(result.Lines, l => l.Marker == DiffMarker.Context && l.Text == "gamma");
    }

    [Fact]
    public void An_inserted_line_is_an_addition_and_nothing_else()
    {
        var result = TextDiff.Compare(Lines("one", "three"), Lines("one", "two", "three"));

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.Equal("two", Assert.Single(result.Lines, l => l.Marker == DiffMarker.Added).Text);
    }

    [Fact]
    public void A_deleted_line_is_a_removal_and_nothing_else()
    {
        var result = TextDiff.Compare(Lines("one", "two", "three"), Lines("one", "three"));

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Removed);
        Assert.Equal("two", Assert.Single(result.Lines, l => l.Marker == DiffMarker.Removed).Text);
    }

    /// <summary>
    /// The reason common head and tail are trimmed first: a one-line edit in a long file must
    /// not render as a long file.
    /// </summary>
    [Fact]
    public void A_small_edit_in_a_long_file_stays_a_small_diff()
    {
        var body = Enumerable.Range(1, 500).Select(i => $"line {i}").ToArray();
        var before = Lines(body);

        body[249] = "line 250 — edited";
        var after = Lines(body);

        var result = TextDiff.Compare(before, after);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Removed);

        // Trimming plus collapsing keeps this to a handful of rows, not five hundred.
        Assert.True(result.Lines.Count < 12, $"the diff was {result.Lines.Count} lines long");
    }

    [Fact]
    public void Long_runs_of_unchanged_lines_are_collapsed_rather_than_listed()
    {
        var before = Lines(["first", .. Enumerable.Range(1, 60).Select(i => $"middle {i}"), "last"]);
        var after = Lines(["FIRST", .. Enumerable.Range(1, 60).Select(i => $"middle {i}"), "LAST"]);

        var result = TextDiff.Compare(before, after);

        Assert.Contains(result.Lines, l => l.Marker == DiffMarker.Skipped);
        Assert.DoesNotContain(result.Lines, l => l.Text == "middle 30");
    }

    /// <summary>A consent card must stay something a person will actually read.</summary>
    [Fact]
    public void A_wholesale_rewrite_is_capped_rather_than_shown_in_full()
    {
        var before = Lines([.. Enumerable.Range(1, 600).Select(i => $"old {i}")]);
        var after = Lines([.. Enumerable.Range(1, 600).Select(i => $"new {i}")]);

        var result = TextDiff.Compare(before, after);

        Assert.True(result.Truncated);
        Assert.True(result.Lines.Count <= 201, $"{result.Lines.Count} lines reached the UI");
        Assert.Contains(result.Lines, l => l.Marker == DiffMarker.Skipped);
    }

    /// <summary>
    /// The quadratic step is bounded. Without the ceiling, two large unrelated files would
    /// allocate a table big enough to matter, inside a consent prompt the user is waiting on.
    /// </summary>
    [Fact]
    public void A_very_large_change_is_summarised_instead_of_diffed()
    {
        var before = Lines([.. Enumerable.Range(1, 4_000).Select(i => $"old {i}")]);
        var after = Lines([.. Enumerable.Range(1, 4_000).Select(i => $"new {i}")]);

        var result = TextDiff.Compare(before, after);

        Assert.True(result.Truncated);
        Assert.Empty(result.Lines);
        Assert.Contains("Too large", result.Note);
        Assert.Equal(4_000, result.Added);
        Assert.Equal(4_000, result.Removed);
    }

    [Fact]
    public void Writing_over_an_empty_string_is_all_additions()
    {
        var result = TextDiff.Compare("", Lines("one", "two"));

        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void Line_endings_do_not_show_up_as_changes_on_their_own()
    {
        var result = TextDiff.Compare("alpha\r\nbeta", "alpha\nbeta");

        // Normalised before comparing: a diff full of invisible carriage returns tells the user
        // nothing they can act on.
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void The_diff_completes_quickly_on_a_realistic_file()
    {
        var body = Enumerable.Range(1, 1_200).Select(i => $"content line {i} with some text on it").ToArray();
        var before = Lines(body);

        body[600] = "changed";
        body[900] = "also changed";
        var after = Lines(body);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = TextDiff.Compare(before, after);
        started.Stop();

        Assert.Equal(2, result.Added);
        Assert.True(started.ElapsedMilliseconds < 2_000,
            $"the diff took {started.ElapsedMilliseconds} ms, which is too slow for a prompt the user is waiting on");
    }
}

/// <summary>
/// The diff reaching the consent card. A perfect diff engine nobody is shown is worth nothing,
/// so these drive the real tool and read what the broker was actually asked to approve.
/// </summary>
public sealed class WritePreviewTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "autowork-preview", Guid.NewGuid().ToString("n")[..8]);

    private readonly RecordingBroker _broker = new();
    private readonly FileTools _tools;

    public WritePreviewTests()
    {
        Directory.CreateDirectory(_workspace);

        _tools = new FileTools(new ToolContext
        {
            Guard = new PathGuard(new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            }),
            Approvals = _broker,
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "preview",
            WorkingDirectory = _workspace,
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private AIFunction Tool(string name) =>
        _tools.GetTools(new ToolContext
        {
            Guard = new PathGuard(new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            }),
            Approvals = _broker,
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "preview",
            WorkingDirectory = _workspace,
        }).First(t => t.Name == name).Function;

    private async Task<string> InvokeAsync(string tool, Dictionary<string, object?> arguments) =>
        (await Tool(tool).InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken))
        ?.ToString() ?? "";

    [Fact]
    public async Task Overwriting_a_file_shows_the_user_what_would_change()
    {
        var path = Path.Combine(_workspace, "config.txt");
        await File.WriteAllTextAsync(path, "host=localhost\nport=8080\ndebug=false",
            TestContext.Current.CancellationToken);

        await InvokeAsync("files_write", new()
        {
            ["path"] = path,
            ["content"] = "host=localhost\nport=9090\ndebug=false",
            ["overwrite"] = true,
        });

        var request = Assert.Single(_broker.Requests);
        var preview = request.Preview;

        Assert.NotNull(preview);
        Assert.Equal(1, preview.Added);
        Assert.Equal(1, preview.Removed);
        Assert.Contains(preview.Lines, l => l.Marker == DiffMarker.Removed && l.Text == "port=8080");
        Assert.Contains(preview.Lines, l => l.Marker == DiffMarker.Added && l.Text == "port=9090");
    }

    /// <summary>
    /// A new file has nothing to diff against. Rendering it as a wall of green additions would
    /// teach the user to skim, which is the opposite of what the card is for.
    /// </summary>
    [Fact]
    public async Task Creating_a_new_file_carries_no_diff()
    {
        await InvokeAsync("files_write", new()
        {
            ["path"] = Path.Combine(_workspace, "brand-new.txt"),
            ["content"] = "first ever contents",
        });

        Assert.Null(Assert.Single(_broker.Requests).Preview);
    }

    [Fact]
    public async Task Appending_previews_the_file_as_it_would_end_up()
    {
        var path = Path.Combine(_workspace, "log.txt");
        await File.WriteAllTextAsync(path, "line one\n", TestContext.Current.CancellationToken);

        await InvokeAsync("files_append", new() { ["path"] = path, ["content"] = "line two\n" });

        var preview = Assert.Single(_broker.Requests).Preview;

        Assert.NotNull(preview);
        Assert.Contains(preview.Lines, l => l.Marker == DiffMarker.Added && l.Text == "line two");

        // Nothing is removed by an append — a preview claiming otherwise would be alarming and wrong.
        Assert.Equal(0, preview.Removed);
    }

    [Fact]
    public async Task A_binary_file_is_described_rather_than_diffed()
    {
        var path = Path.Combine(_workspace, "blob.bin");
        await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0x00, 0xFF], TestContext.Current.CancellationToken);

        await InvokeAsync("files_write", new()
        {
            ["path"] = path,
            ["content"] = "now it is text",
            ["overwrite"] = true,
        });

        var preview = Assert.Single(_broker.Requests).Preview;

        Assert.NotNull(preview);
        Assert.Empty(preview.Lines);
        Assert.Contains("not a text file", preview.Note);
    }

    /// <summary>
    /// The preview reads the file to build the diff, so it has to obey the sandbox exactly as
    /// the write does — otherwise it becomes a way to see a file the agent may not touch.
    /// </summary>
    [Fact]
    public async Task A_write_outside_the_sandbox_produces_no_preview_of_the_file_it_cannot_touch()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"autowork-outside-{Guid.NewGuid():n}.txt");
        await File.WriteAllTextAsync(outside, "secrets the agent must not see",
            TestContext.Current.CancellationToken);

        try
        {
            var result = await InvokeAsync("files_write", new()
            {
                ["path"] = outside,
                ["content"] = "overwritten",
                ["overwrite"] = true,
            });

            Assert.StartsWith("REFUSED:", result);

            foreach (var request in _broker.Requests)
            {
                Assert.Null(request.Preview);
                Assert.DoesNotContain("secrets the agent must not see", request.Detail);
            }

            Assert.Equal("secrets the agent must not see",
                await File.ReadAllTextAsync(outside, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    private sealed class RecordingBroker : IApprovalBroker
    {
        public List<ApprovalRequest> Requests { get; } = [];

        public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(ApprovalDecision.Approved);
        }
    }
}
