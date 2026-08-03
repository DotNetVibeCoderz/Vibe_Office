using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Tools;

/// <summary>
/// Reading from the web. Deliberately narrow: fetch a page as text, or download a file into a
/// granted folder. Anything that needs a real browser session belongs to the browser
/// extension described in the roadmap, not here.
/// </summary>
public sealed partial class WebTools : ToolSetBase, IToolProvider
{
    private static readonly HttpClient Http = CreateClient();

    public WebTools(ToolContext context) : base(context) { }

    protected override AgentOrgan Organ => AgentOrgan.Hands;

    public string Name => "Web";

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            // Bounded so a redirect loop cannot hang a run.
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoWork/0.1 (+https://github.com/gravicode/autowork)");
        return client;
    }

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        if (!context.Guard.Policy.AllowNetwork) yield break;

        var tools = new WebTools(context);

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.SearchAsync, "web_search",
                "Search the web and return ranked results with titles, URLs and snippets. " +
                "Use this to find sources before reading them with web_fetch."),
            Organ = AgentOrgan.Hands,
            Risk = ToolRisk.Safe,
            Category = "Web",
            ApprovalKind = ApprovalKind.NetworkAccess,
        };

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.FetchAsync, "web_fetch",
                "Fetch a web page and return its readable text with the markup stripped."),
            Organ = AgentOrgan.Hands,
            Risk = ToolRisk.Safe,
            Category = "Web",
            ApprovalKind = ApprovalKind.NetworkAccess,
        };

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.DownloadAsync, "web_download",
                "Download a file from a URL into a granted folder."),
            Organ = AgentOrgan.Hands,
            Risk = ToolRisk.Write,
            Category = "Web",
            ApprovalKind = ApprovalKind.WriteFiles,
        };
    }

    [Description("Search the web.")]
    private Task<string> SearchAsync(
        [Description("What to search for, phrased as you would type it into a search engine.")] string query,
        [Description("How many results to return.")] int maxResults = 0)
        => GuardedAsync("web.search", $"Search: {Shorten(query)}", async () =>
        {
            if (string.IsNullOrWhiteSpace(query)) return Refused("The search query was empty.");

            if (!Guard.Policy.AllowNetwork)
                return Refused("Network access is turned off in Settings › Permissions.");

            var options = Context.Search;
            var count = Math.Clamp(maxResults > 0 ? maxResults : options.MaxResults, 1, 20);

            var chain = WebSearchChain.Create(Http, Context.Secrets?.Resolve(options.ApiKeyRef));

            var outcome = await chain.SearchAsync(query, count, IsHostAllowed).ConfigureAwait(false);

            if (outcome is null)
            {
                // Naming the backends that were tried turns "search failed" into something the
                // model can act on — add a key, or widen the allow-list.
                return Failed(
                    $"No search backend could answer. Tried: {string.Join(", ", chain.BackendNames)}. " +
                    "Check network access, the outbound host allow-list, and the Tavily API key.");
            }

            return Ok(Format(query, outcome));
        });

    /// <summary>
    /// Plain text rather than JSON: the model reads this, and every character costs context.
    /// URLs are kept verbatim so they can be cited and passed to web_fetch unchanged.
    /// </summary>
    private static string Format(string query, WebSearchOutcome outcome)
    {
        var builder = new StringBuilder($"Search results for \"{query}\" (via {outcome.Backend}):\n");

        if (!string.IsNullOrWhiteSpace(outcome.Answer))
            builder.Append("\nSummary: ").AppendLine(outcome.Answer.Trim());

        var index = 0;
        foreach (var hit in outcome.Hits)
        {
            builder.AppendLine().Append(++index).Append(". ").AppendLine(hit.Title);
            builder.Append("   ").AppendLine(hit.Url);

            if (!string.IsNullOrWhiteSpace(hit.Snippet))
                builder.Append("   ").AppendLine(Cap(hit.Snippet.ReplaceLineEndings(" ").Trim(), 600));
        }

        if (index == 0 && string.IsNullOrWhiteSpace(outcome.Answer))
            builder.AppendLine("\nNo results.");

        return Cap(builder.ToString());
    }

    /// <summary>
    /// A backend is skipped when the policy's outbound allow-list does not cover it, so a
    /// fallback can never reach a host the user has not permitted.
    /// </summary>
    private bool IsHostAllowed(string host)
    {
        var allowList = Guard.Policy.NetworkAllowList;
        return allowList.Count == 0 || allowList.Any(allowed => MatchesHost(host, allowed));
    }

    [Description("Fetch a web page as text.")]
    private Task<string> FetchAsync(
        [Description("The URL to fetch.")] string url,
        [Description("Maximum characters to return.")] int maxCharacters = 10000)
        => GuardedAsync("web.fetch", $"Fetch {Shorten(url)}", async () =>
        {
            if (!TryValidate(url, out var uri, out var problem)) return Refused(problem);

            using var response = await Http.GetAsync(uri).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return Failed($"{uri.Host} returned {(int)response.StatusCode} {response.ReasonPhrase}.");

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var text = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                ? HtmlToText(body)
                : body;

            return Ok($"{uri}\n\n{Cap(text, maxCharacters)}");
        });

    [Description("Download a file.")]
    private Task<string> DownloadAsync(
        [Description("The URL to download.")] string url,
        [Description("Where to save it.")] string destination)
    {
        var target = Locate(destination);

        return GuardedAsync("web.download", $"Download {Shorten(url)}", async () =>
        {
            if (!TryValidate(url, out var uri, out var problem)) return Refused(problem);

            var canonical = Guard.EnsureWritable(target);

            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failed($"{uri.Host} returned {(int)response.StatusCode} {response.ReasonPhrase}.");

            var declared = response.Content.Headers.ContentLength;
            if (declared is { } length && length > Guard.Policy.MaxReadBytes)
                return Refused($"The file is {Human(length)}, over the {Human(Guard.Policy.MaxReadBytes)} limit.");

            Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);

            await using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var file = File.Create(canonical))
            {
                await source.CopyToAsync(file).ConfigureAwait(false);
            }

            return Ok($"Downloaded {Human(new FileInfo(canonical).Length)} to {PathGuard.Describe(canonical)}.");
        },
        [target], ApprovalKind.WriteFiles, $"{url}\n→ {PathGuard.Describe(target)}");
    }

    private bool TryValidate(string url, out Uri uri, out string problem)
    {
        uri = null!;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            problem = $"\"{url}\" is not a valid URL.";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            problem = $"Only http and https URLs are allowed; got \"{parsed.Scheme}\".";
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
        problem = "";
        return true;
    }

    /// <summary>Exact host match, or a subdomain of an allowed domain.</summary>
    private static bool MatchesHost(string host, string allowed) =>
        host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Strips markup for reading. Not a parser — script and style bodies go first, then tags,
    /// then entity decoding and whitespace collapse. Good enough to feed a model an article;
    /// not good enough to drive a page, which is what the browser extension is for.
    /// </summary>
    internal static string HtmlToText(string html)
    {
        var text = ScriptAndStyle().Replace(html, " ");
        text = BlockBreaks().Replace(text, "\n");
        text = Tags().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        text = HorizontalWhitespace().Replace(text, " ");
        text = ExcessBlankLines().Replace(text, "\n\n");

        return text.Trim();
    }

    [GeneratedRegex(@"<(script|style|noscript)\b[^>]*>.*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptAndStyle();

    [GeneratedRegex(@"</(p|div|section|article|h[1-6]|li|tr|br)\s*>|<br\s*/?>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex(@"\n\s*\n\s*\n+")]
    private static partial Regex ExcessBlankLines();

    private static string Shorten(string url) => url.Length <= 70 ? url : url[..70] + "…";
}
