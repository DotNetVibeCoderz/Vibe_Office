using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoWork.Tools;

public sealed record WebSearchHit(string Title, string Url, string Snippet);

/// <summary>What one backend managed to find, and which backend that was.</summary>
public sealed record WebSearchOutcome(string Backend, string? Answer, IReadOnlyList<WebSearchHit> Hits);

/// <summary>
/// One way of answering a search. Returning null means "I could not help with this" — a dead
/// endpoint, a blocked host, no results — and the chain moves on to the next backend.
/// Throwing is reserved for cancellation.
/// </summary>
internal interface IWebSearchBackend
{
    string Name { get; }

    /// <summary>The hosts this backend contacts, so the policy allow-list can be honoured.</summary>
    IReadOnlyList<string> Hosts { get; }

    Task<WebSearchOutcome?> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

/// <summary>
/// Search, tried in order of quality until one backend answers.
///
/// Tavily is the good one, and it needs a key. The rest exist so that a user who has not signed
/// up for anything still gets a working <c>web_search</c> rather than a tool that refuses — the
/// same "degrade, do not fail" rule that gives knowledge search a keyword fallback when there is
/// no embedding model.
///
/// The order is deliberate. DuckDuckGo is a real search engine and comes first among the keyless
/// options; Wikipedia is narrow but almost never unreachable, so it sits last and stops the chain
/// from ending in silence. A backend that returns nothing is not an error — it is a reason to
/// ask the next one.
/// </summary>
public sealed partial class WebSearchChain
{
    private readonly IReadOnlyList<IWebSearchBackend> _backends;

    internal WebSearchChain(IReadOnlyList<IWebSearchBackend> backends) => _backends = backends;

    /// <summary>Builds the chain. A null or blank key simply drops Tavily out of it.</summary>
    public static WebSearchChain Create(HttpClient http, string? tavilyApiKey)
    {
        var backends = new List<IWebSearchBackend>();

        if (!string.IsNullOrWhiteSpace(tavilyApiKey))
            backends.Add(new TavilyBackend(http, tavilyApiKey));

        backends.Add(new DuckDuckGoBackend(http));
        backends.Add(new WikipediaBackend(http));

        return new WebSearchChain(backends);
    }

    /// <summary>
    /// Runs the chain. <paramref name="hostAllowed"/> lets the caller enforce the permission
    /// policy's outbound allow-list — a backend whose hosts are not permitted is skipped rather
    /// than called and refused, so a locked-down policy cannot be worked around by a fallback.
    /// </summary>
    public async Task<WebSearchOutcome?> SearchAsync(
        string query,
        int maxResults,
        Func<string, bool>? hostAllowed = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var backend in _backends)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (hostAllowed is not null && !backend.Hosts.All(hostAllowed)) continue;

            var outcome = await backend.SearchAsync(query, maxResults, cancellationToken).ConfigureAwait(false);
            if (outcome is { Hits.Count: > 0 } || outcome?.Answer is { Length: > 0 }) return outcome;
        }

        return null;
    }

    /// <summary>Names the backends in the order they would be tried, for diagnostics.</summary>
    public IReadOnlyList<string> BackendNames => _backends.Select(b => b.Name).ToArray();

    /// <summary>
    /// Wikipedia's API answers 403 to a request without one, and search engines treat a missing
    /// one as a bot. Set per request rather than relying on the caller's HttpClient defaults —
    /// a backend that only works when handed the right client is a trap for the next caller.
    /// </summary>
    private const string UserAgent = "AutoWork/0.1 (+https://github.com/gravicode/autowork)";

    private static HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return request;
    }

    // ── Tavily ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A search API built for language models: it returns clean extracted content and, on
    /// request, a synthesised answer, so the agent does not have to fetch and strip five pages
    /// before it can reason.
    /// </summary>
    internal sealed class TavilyBackend : IWebSearchBackend
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;

        public TavilyBackend(HttpClient http, string apiKey)
        {
            _http = http;
            _apiKey = apiKey;
        }

        public string Name => "Tavily";
        public IReadOnlyList<string> Hosts => ["api.tavily.com"];

        public async Task<WebSearchOutcome?> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            try
            {
                using var request = Request(HttpMethod.Post, "https://api.tavily.com/search");

                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        query,
                        max_results = maxResults,
                        include_answer = true,
                        search_depth = "basic",
                    }),
                    Encoding.UTF8,
                    "application/json");

                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                return Parse(document.RootElement);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        }

        internal static WebSearchOutcome Parse(JsonElement root)
        {
            var answer = root.TryGetProperty("answer", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString()
                : null;

            var hits = new List<WebSearchHit>();

            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in results.EnumerateArray())
                {
                    var url = Text(item, "url");
                    if (url.Length == 0) continue;

                    hits.Add(new WebSearchHit(Text(item, "title"), url, Text(item, "content")));
                }
            }

            return new WebSearchOutcome("Tavily", answer, hits);
        }

        private static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }

    // ── DuckDuckGo ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The keyless default. DuckDuckGo's Instant Answer API is not used: it returns an abstract
    /// for well-known nouns and nothing at all for the research questions this tool exists to
    /// serve. The HTML endpoint returns real result rows, at the cost of being markup rather
    /// than a contract — so parsing failure is treated as "no answer" and falls through rather
    /// than surfacing as an error.
    /// </summary>
    internal sealed partial class DuckDuckGoBackend : IWebSearchBackend
    {
        private readonly HttpClient _http;

        public DuckDuckGoBackend(HttpClient http) => _http = http;

        public string Name => "DuckDuckGo";
        public IReadOnlyList<string> Hosts => ["html.duckduckgo.com"];

        public async Task<WebSearchOutcome?> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            try
            {
                using var request = Request(HttpMethod.Get,
                    "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query));

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var hits = ParseResults(html, maxResults);

                return hits.Count == 0 ? null : new WebSearchOutcome("DuckDuckGo", null, hits);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        }

        internal static IReadOnlyList<WebSearchHit> ParseResults(string html, int maxResults)
        {
            var hits = new List<WebSearchHit>();
            var snippets = SnippetPattern().Matches(html);
            var index = 0;

            foreach (Match match in LinkPattern().Matches(html))
            {
                if (hits.Count >= maxResults) break;

                var url = Unwrap(WebUtility.HtmlDecode(match.Groups["href"].Value));
                if (url.Length == 0) continue;

                var title = WebTools.HtmlToText(match.Groups["title"].Value);

                var snippet = index < snippets.Count
                    ? WebTools.HtmlToText(snippets[index].Groups["text"].Value)
                    : "";

                hits.Add(new WebSearchHit(title, url, snippet));
                index++;
            }

            return hits;
        }

        /// <summary>
        /// Result links are wrapped in a DuckDuckGo redirect carrying the real URL in
        /// <c>uddg</c>. Handing the model the redirect would make every citation point at
        /// duckduckgo.com.
        /// </summary>
        internal static string Unwrap(string href)
        {
            if (href.StartsWith("//", StringComparison.Ordinal)) href = "https:" + href;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return "";

            var target = RedirectTarget().Match(uri.Query);
            if (target.Success) return Uri.UnescapeDataString(target.Groups["u"].Value);

            return uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase) ? "" : href;
        }

        [GeneratedRegex("""<a[^>]*class="[^"]*result__a[^"]*"[^>]*href="(?<href>[^"]+)"[^>]*>(?<title>.*?)</a>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex LinkPattern();

        [GeneratedRegex("""<a[^>]*class="[^"]*result__snippet[^"]*"[^>]*>(?<text>.*?)</a>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex SnippetPattern();

        [GeneratedRegex(@"[?&]uddg=(?<u>[^&]+)")]
        private static partial Regex RedirectTarget();
    }

    // ── Wikipedia ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Last resort. Narrow, but it is a documented JSON API rather than scraped markup, it needs
    /// no key, and it stays reachable on networks that block the search engines. Ending a chain
    /// with something dependable beats ending it with nothing.
    /// </summary>
    internal sealed class WikipediaBackend : IWebSearchBackend
    {
        private readonly HttpClient _http;

        public WikipediaBackend(HttpClient http) => _http = http;

        public string Name => "Wikipedia";
        public IReadOnlyList<string> Hosts => ["en.wikipedia.org"];

        public async Task<WebSearchOutcome?> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            try
            {
                using var request = Request(HttpMethod.Get,
                    "https://en.wikipedia.org/w/api.php?action=query&list=search&format=json" +
                    $"&srlimit={maxResults}&srsearch={Uri.EscapeDataString(query)}");

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                var hits = Parse(document.RootElement);
                return hits.Count == 0 ? null : new WebSearchOutcome("Wikipedia", null, hits);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        }

        internal static IReadOnlyList<WebSearchHit> Parse(JsonElement root)
        {
            if (!root.TryGetProperty("query", out var query)
                || !query.TryGetProperty("search", out var search)
                || search.ValueKind != JsonValueKind.Array)
                return [];

            var hits = new List<WebSearchHit>();

            foreach (var item in search.EnumerateArray())
            {
                if (!item.TryGetProperty("title", out var titleElement)) continue;

                var title = titleElement.GetString() ?? "";
                if (title.Length == 0) continue;

                var snippet = item.TryGetProperty("snippet", out var s)
                    ? WebTools.HtmlToText(s.GetString() ?? "")
                    : "";

                hits.Add(new WebSearchHit(
                    title,
                    "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')),
                    snippet));
            }

            return hits;
        }
    }
}
