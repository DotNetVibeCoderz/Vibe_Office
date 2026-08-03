using System.ComponentModel;
using AutoWork.Core.Agents;
using AutoWork.Core.Browsing;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Tools;

/// <summary>
/// Driving a real browser, signed in as the user.
///
/// This is the web-automation case <c>web_fetch</c> cannot serve: fetching a URL gets the page a
/// stranger sees, and almost everything worth automating is behind a login. So these tools work
/// a browser that is already installed, against a profile that stays signed in.
///
/// That makes them the most powerful tools in the product — a signed-in browser is every account
/// the person has — so they are switched off by default, they follow the same outbound host
/// allow-list as the web tools, and every navigation and click asks first. Nothing is advertised
/// to the model until the capability is turned on.
/// </summary>
public sealed class BrowserTools : ToolSetBase, IToolProvider, IAsyncDisposable
{
    private readonly Lazy<BrowserSession> _session;

    public BrowserTools(ToolContext context) : base(context) =>
        _session = new Lazy<BrowserSession>(() => new BrowserSession(context.Browser));

    protected override AgentOrgan Organ => AgentOrgan.Hands;

    public string Name => "Browser";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        // Off means absent, not "present and refusing".
        if (!context.Browser.Enabled || !context.Guard.Policy.AllowNetwork) yield break;

        var tools = new BrowserTools(context);

        yield return Describe(AIFunctionFactory.Create(tools.GoAsync, "browser_go",
            """
            Open a page in a real browser that stays signed in between runs. Use this instead of
            web_fetch when the page needs a login, or when it only renders with JavaScript.
            """), ToolRisk.System, ApprovalKind.NetworkAccess);

        yield return Describe(AIFunctionFactory.Create(tools.ReadAsync, "browser_read",
            "Read the current page as text, as a reader sees it."), ToolRisk.Safe, ApprovalKind.Other);

        yield return Describe(AIFunctionFactory.Create(tools.OutlineAsync, "browser_outline",
            """
            List the links, buttons and fields on the current page, with the selectors to use.
            Call this before clicking or typing so you aim at something that is really there.
            """), ToolRisk.Safe, ApprovalKind.Other);

        yield return Describe(AIFunctionFactory.Create(tools.ClickAsync, "browser_click",
            "Click a link or button, by CSS selector or by its visible text."),
            ToolRisk.System, ApprovalKind.ControlInput);

        yield return Describe(AIFunctionFactory.Create(tools.TypeAsync, "browser_type",
            "Type into a field on the current page, chosen by CSS selector."),
            ToolRisk.System, ApprovalKind.ControlInput);
    }

    private static ToolDescriptor Describe(AIFunction function, ToolRisk risk, ApprovalKind approval) => new()
    {
        Function = function,
        Organ = AgentOrgan.Hands,
        Risk = risk,
        Category = "Browser",
        ApprovalKind = approval,
    };

    [Description("Open a page in the browser.")]
    private Task<string> GoAsync([Description("URL to open.")] string url)
        => GuardedAsync("browser.go", $"Open {Shorten(url)} in a signed-in browser", async () =>
        {
            if (!TryValidate(url, out var uri, out var problem)) return Refused(problem);

            var result = await _session.Value.NavigateAsync(uri.ToString()).ConfigureAwait(false);
            return result.Success ? Ok(result.Text) : Failed(result.Error);
        },
        approval: ApprovalKind.NetworkAccess,
        approvalDetail: "Uses a browser profile that stays signed in, so the page sees your accounts.");

    [Description("Read the current page.")]
    private Task<string> ReadAsync([Description("Maximum characters to return.")] int maxCharacters = 8000)
        => GuardedAsync("browser.read", "Read the current page", async () =>
        {
            var result = await _session.Value.ReadAsync(Math.Clamp(maxCharacters, 200, 40_000)).ConfigureAwait(false);

            if (!result.Success) return Failed(result.Error);

            return string.IsNullOrWhiteSpace(result.Text)
                ? Failed("The page had no readable text. Try browser_outline, or the page may still be loading.")
                : Ok(result.Text);
        });

    [Description("List what can be clicked or filled in.")]
    private Task<string> OutlineAsync()
        => GuardedAsync("browser.outline", "List what is on the page", async () =>
        {
            var result = await _session.Value.OutlineAsync().ConfigureAwait(false);

            if (!result.Success) return Failed(result.Error);

            return string.IsNullOrWhiteSpace(result.Text)
                ? Failed("Nothing interactive was found. The page may still be loading.")
                : Ok(result.Text);
        });

    [Description("Click something on the page.")]
    private Task<string> ClickAsync([Description("CSS selector, or the visible text of a link or button.")] string target)
        => GuardedAsync("browser.click", $"Click \"{Shorten(target, 60)}\" in the browser", async () =>
        {
            var result = await _session.Value.ClickAsync(target).ConfigureAwait(false);

            if (!result.Success) return Failed(result.Error);

            return result.Text == "NOTFOUND"
                ? Failed($"Nothing on the page matched \"{target}\". Call browser_outline to see what is there.")
                : Ok(result.Text);
        },
        approval: ApprovalKind.ControlInput,
        approvalDetail: "Acts as you on a page you are signed in to.");

    [Description("Type into a field.")]
    private Task<string> TypeAsync(
        [Description("CSS selector for the field.")] string selector,
        [Description("Text to enter.")] string text)
        => GuardedAsync("browser.type", $"Type into \"{Shorten(selector, 60)}\" in the browser", async () =>
        {
            var result = await _session.Value.TypeAsync(selector, text).ConfigureAwait(false);

            if (!result.Success) return Failed(result.Error);

            return result.Text == "NOTFOUND"
                ? Failed($"No field matched \"{selector}\". Call browser_outline to see what is there.")
                : Ok($"Typed into {selector}.");
        },
        approval: ApprovalKind.ControlInput,
        approvalDetail: "Acts as you on a page you are signed in to.");

    /// <summary>
    /// The same outbound rules the web tools follow. A browser that ignored the allow-list would
    /// be a way around it.
    /// </summary>
    private bool TryValidate(string url, out Uri uri, out string problem)
    {
        uri = null!;
        problem = "";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            problem = $"\"{url}\" is not an http or https address.";
            return false;
        }

        if (!Guard.Policy.AllowNetwork)
        {
            problem = "Network access is turned off in Settings › Permissions.";
            return false;
        }

        var allowList = Guard.Policy.NetworkAllowList;

        if (allowList.Count > 0 && !allowList.Any(host => MatchesHost(parsed.Host, host)))
        {
            problem = $"{parsed.Host} is not in the allowed host list in Settings › Permissions.";
            return false;
        }

        uri = parsed;
        return true;
    }

    internal static bool MatchesHost(string host, string allowed) =>
        host.Equals(allowed, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase);

    private static string Shorten(string text, int max = 80) =>
        text.Length <= max ? text : text[..max] + "…";

    public async ValueTask DisposeAsync()
    {
        if (_session.IsValueCreated) await _session.Value.DisposeAsync().ConfigureAwait(false);
    }
}
