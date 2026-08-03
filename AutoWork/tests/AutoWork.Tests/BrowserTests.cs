using AutoWork.Core.Agents;
using AutoWork.Core.Browsing;
using AutoWork.Core.Configuration;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Tools;

namespace AutoWork.Tests;

/// <summary>
/// The browser tools are the most powerful in the product — a signed-in browser is every account
/// the person has — so most of these are about what they refuse to do.
/// </summary>
public sealed class BrowserToolTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "autowork-browser", Guid.NewGuid().ToString("n")[..8]);

    public BrowserToolTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private ToolContext Context(BrowserSettings browser, PermissionPolicy? policy = null) => new()
    {
        Guard = new PathGuard(policy ?? new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            AllowNetwork = true,
        }),
        Approvals = new AutoApproveBroker(),
        Log = NullActionLog.Instance,
        Options = new AgentOptions(),
        RunId = "browser",
        WorkingDirectory = _workspace,
        Browser = browser,
    };

    private static IReadOnlyList<ToolDescriptor> Tools(ToolContext context) =>
        [.. new BrowserTools(context).GetTools(context)];

    /// <summary>Advertising a tool and refusing every call wastes tokens and invites retries.</summary>
    [Fact]
    public void Nothing_is_offered_while_the_browser_capability_is_off()
    {
        Assert.Empty(Tools(Context(new BrowserSettings { Enabled = false })));
    }

    [Fact]
    public void Nothing_is_offered_when_the_network_is_off_however_the_browser_is_configured()
    {
        var context = Context(new BrowserSettings { Enabled = true }, new PermissionPolicy { AllowNetwork = false });

        Assert.Empty(Tools(context));
    }

    [Fact]
    public void Turning_it_on_offers_the_whole_set()
    {
        var names = Tools(Context(new BrowserSettings { Enabled = true })).Select(t => t.Name).ToArray();

        Assert.Equal(
            ["browser_go", "browser_read", "browser_outline", "browser_click", "browser_type"],
            names);
    }

    /// <summary>
    /// Reading a page you are already on is not the same act as opening one, or as clicking
    /// something as the signed-in user, and the consent prompts have to say so.
    /// </summary>
    [Fact]
    public void Acting_as_the_user_is_treated_as_heavier_than_reading()
    {
        var tools = Tools(Context(new BrowserSettings { Enabled = true })).ToDictionary(t => t.Name);

        Assert.Equal(ToolRisk.Safe, tools["browser_read"].Risk);
        Assert.Equal(ToolRisk.Safe, tools["browser_outline"].Risk);

        Assert.Equal(ToolRisk.System, tools["browser_go"].Risk);
        Assert.Equal(ApprovalKind.NetworkAccess, tools["browser_go"].ApprovalKind);

        Assert.Equal(ToolRisk.System, tools["browser_click"].Risk);
        Assert.Equal(ApprovalKind.ControlInput, tools["browser_click"].ApprovalKind);

        Assert.Equal(ToolRisk.System, tools["browser_type"].Risk);
        Assert.Equal(ApprovalKind.ControlInput, tools["browser_type"].ApprovalKind);
    }

    /// <summary>
    /// A browser that ignored the outbound allow-list would be a way around it — the same list
    /// the web tools follow, enforced in the same place.
    /// </summary>
    [Theory]
    [InlineData("example.com", "example.com", true)]
    [InlineData("docs.example.com", "example.com", true)]
    [InlineData("example.com.evil.net", "example.com", false)]
    [InlineData("notexample.com", "example.com", false)]
    public void The_host_allow_list_matches_on_a_label_boundary(string host, string allowed, bool expected) =>
        Assert.Equal(expected, BrowserTools.MatchesHost(host, allowed));

    [Fact]
    public void A_browser_is_looked_for_rather_than_downloaded()
    {
        // Nothing is bundled, so this is whatever is installed — on this machine, Edge.
        var found = BrowserSession.FindBrowser();

        if (found is not null) Assert.True(File.Exists(found));

        // A configured path that does not exist is not silently replaced with a guess.
        Assert.Null(BrowserSession.FindBrowser(@"C:\definitely\not\here\browser.exe"));
    }
}

/// <summary>
/// A real browser, started for real.
///
/// Gated because it launches a process and opens a window. Everything else about the browser
/// tools can be checked offline; whether the DevTools handshake actually works cannot be.
/// </summary>
public sealed class LiveBrowserTests : IAsyncDisposable
{
    private BrowserSession? _session;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null) await _session.DisposeAsync();
    }

    private static BrowserSettings Settings() => new()
    {
        Enabled = true,
        Headless = true,
        ProfileDirectory = Path.Combine(Path.GetTempPath(), "autowork-browser-profile", Guid.NewGuid().ToString("n")[..8]),
    };

    [Fact]
    public async Task A_real_browser_opens_a_page_and_its_text_can_be_read()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AUTOWORK_LIVE_BROWSER") == "1",
            "Set AUTOWORK_LIVE_BROWSER=1 to drive a real browser. It starts a browser process.");

        Assert.SkipWhen(BrowserSession.FindBrowser() is null, "No Edge or Chrome on this machine.");

        _session = new BrowserSession(Settings());

        var opened = await _session.OpenAsync(TestContext.Current.CancellationToken);
        Assert.True(opened.Success, opened.Error);

        // A local file, so the test needs no network and no third party's uptime.
        var page = Path.Combine(Path.GetTempPath(), $"autowork-page-{Guid.NewGuid():n}.html");

        await File.WriteAllTextAsync(page,
            """
            <html><head><title>Filing cabinet</title></head>
            <body>
              <h1>Quarterly review</h1>
              <p>Revenue rose by twelve per cent.</p>
              <input id="note" placeholder="Add a note" />
              <button id="save">Save the note</button>
              <script>document.getElementById('save').onclick =
                () => document.body.insertAdjacentHTML('beforeend', '<p id="done">Saved.</p>');</script>
            </body></html>
            """, TestContext.Current.CancellationToken);

        try
        {
            var navigated = await _session.NavigateAsync(new Uri(page).ToString(), TestContext.Current.CancellationToken);
            Assert.True(navigated.Success, navigated.Error);
            Assert.Contains("Filing cabinet", navigated.Text);

            var read = await _session.ReadAsync(8_000, TestContext.Current.CancellationToken);
            Assert.True(read.Success, read.Error);
            Assert.Contains("Revenue rose by twelve per cent", read.Text);

            // The outline is what lets the model aim at something that is really there.
            var outline = await _session.OutlineAsync(TestContext.Current.CancellationToken);
            Assert.True(outline.Success, outline.Error);
            Assert.Contains("save", outline.Text, StringComparison.OrdinalIgnoreCase);

            var typed = await _session.TypeAsync("#note", "reviewed", TestContext.Current.CancellationToken);
            Assert.Equal("TYPED", typed.Text);

            // Clicked by visible text, not by selector — the way a person would describe it.
            var clicked = await _session.ClickAsync("Save the note", TestContext.Current.CancellationToken);
            Assert.StartsWith("CLICKED", clicked.Text);

            var after = await _session.ReadAsync(8_000, TestContext.Current.CancellationToken);
            Assert.Contains("Saved.", after.Text);
        }
        finally
        {
            File.Delete(page);
        }
    }

    /// <summary>
    /// The point of the whole feature: a profile that remembers. Two sessions against the same
    /// profile directory should share storage, which is what keeps you signed in between runs.
    /// </summary>
    [Fact]
    public async Task The_profile_persists_between_sessions()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AUTOWORK_LIVE_BROWSER") == "1",
            "Set AUTOWORK_LIVE_BROWSER=1 to drive a real browser.");

        Assert.SkipWhen(BrowserSession.FindBrowser() is null, "No Edge or Chrome on this machine.");

        var settings = Settings();
        var page = Path.Combine(Path.GetTempPath(), $"autowork-store-{Guid.NewGuid():n}.html");
        await File.WriteAllTextAsync(page, "<html><body>store</body></html>", TestContext.Current.CancellationToken);

        try
        {
            await using (var first = new BrowserSession(settings))
            {
                Assert.True((await first.OpenAsync(TestContext.Current.CancellationToken)).Success);
                await first.NavigateAsync(new Uri(page).ToString(), TestContext.Current.CancellationToken);

                var set = await first.EvaluateAsync("localStorage.setItem('signed-in','yes'), 'ok'",
                    TestContext.Current.CancellationToken);

                Assert.True(set.Success, set.Error);
            }

            await using var second = new BrowserSession(settings);

            Assert.True((await second.OpenAsync(TestContext.Current.CancellationToken)).Success);
            await second.NavigateAsync(new Uri(page).ToString(), TestContext.Current.CancellationToken);

            var read = await second.EvaluateAsync("localStorage.getItem('signed-in')",
                TestContext.Current.CancellationToken);

            Assert.Equal("yes", read.Text);
        }
        finally
        {
            File.Delete(page);
            try { Directory.Delete(settings.ProfileDirectory, recursive: true); } catch (IOException) { }
        }
    }
}
