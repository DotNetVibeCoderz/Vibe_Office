using System.Text.Json;
using AutoWork.Tools;

namespace AutoWork.Tests;

/// <summary>
/// The endpoints themselves. Skipped unless credentials are present, and the keyless test is
/// tolerant by design: DuckDuckGo is blocked outright on some networks, which is the very
/// situation the fallback chain exists for, so it asserts that *something* answered rather
/// than that a particular backend did.
/// </summary>
public sealed class LiveWebSearchTests
{
    [Fact]
    public async Task Tavily_returns_sources_and_a_synthesised_answer()
    {
        var key = Environment.GetEnvironmentVariable("AUTOWORK_LIVE_TAVILY_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "Set AUTOWORK_LIVE_TAVILY_KEY to run this. Skipped.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var chain = WebSearchChain.Create(http, key);

        var outcome = await chain.SearchAsync(
            "what is retrieval augmented generation", 3,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(outcome);
        Assert.Equal("Tavily", outcome.Backend);
        Assert.NotEmpty(outcome.Hits);
        Assert.All(outcome.Hits, hit => Assert.StartsWith("http", hit.Url));
    }

    [Fact]
    public async Task The_keyless_chain_still_finds_something_without_any_credentials()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("AUTOWORK_LIVE_TAVILY_KEY") is null,
            "Runs alongside the other live tests. Skipped.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var chain = WebSearchChain.Create(http, tavilyApiKey: null);

        var outcome = await chain.SearchAsync(
            "retrieval augmented generation", 3,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(outcome);
        Assert.NotEqual("Tavily", outcome.Backend);
        Assert.NotEmpty(outcome.Hits);
    }
}

/// <summary>
/// Search has to keep working for someone who has signed up for nothing, so the chain and each
/// backend's parsing are tested without the network. The live behaviour of the endpoints is
/// covered by <c>LiveWebSearchTests</c>.
/// </summary>
public sealed class WebSearchChainTests
{
    [Fact]
    public async Task The_first_backend_that_answers_wins_and_the_rest_are_never_called()
    {
        var second = new StubBackend("Second", hits: 3);

        var chain = new WebSearchChain([new StubBackend("First", hits: 2), second]);

        var outcome = await chain.SearchAsync("anything", 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(outcome);
        Assert.Equal("First", outcome.Backend);
        Assert.Equal(0, second.Calls);
    }

    /// <summary>
    /// A backend returning nothing is the normal case for a blocked or rate-limited endpoint,
    /// not an error — the whole point of the chain is that the next one gets a turn.
    /// </summary>
    [Fact]
    public async Task A_backend_that_finds_nothing_hands_over_to_the_next()
    {
        var chain = new WebSearchChain([new StubBackend("Empty", hits: 0), new StubBackend("Useful", hits: 2)]);

        var outcome = await chain.SearchAsync("anything", 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Useful", outcome?.Backend);
    }

    [Fact]
    public async Task When_every_backend_comes_up_empty_the_caller_is_told_rather_than_guessing()
    {
        var chain = new WebSearchChain([new StubBackend("A", hits: 0), new StubBackend("B", hits: 0)]);

        Assert.Null(await chain.SearchAsync("anything", 5, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The outbound allow-list is a permission, and a fallback must not be a way around it.
    /// A user who allowed only Tavily has not thereby allowed Wikipedia.
    /// </summary>
    [Fact]
    public async Task A_backend_outside_the_host_allow_list_is_skipped_not_called()
    {
        var blocked = new StubBackend("Blocked", hits: 5, host: "search.example.com");
        var permitted = new StubBackend("Permitted", hits: 1, host: "allowed.example.com");

        var chain = new WebSearchChain([blocked, permitted]);

        var outcome = await chain.SearchAsync("anything", 5,
            hostAllowed: host => host == "allowed.example.com",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Permitted", outcome?.Backend);
        Assert.Equal(0, blocked.Calls);
    }

    [Fact]
    public void Tavily_drops_out_of_the_chain_when_no_key_is_configured()
    {
        using var http = new HttpClient();

        Assert.DoesNotContain("Tavily", WebSearchChain.Create(http, tavilyApiKey: null).BackendNames);
        Assert.DoesNotContain("Tavily", WebSearchChain.Create(http, tavilyApiKey: "   ").BackendNames);

        // With a key it leads, because it is the only backend that returns extracted content.
        Assert.Equal("Tavily", WebSearchChain.Create(http, "tvly-something").BackendNames[0]);
    }

    [Fact]
    public void The_keyless_chain_still_offers_a_real_search_engine_before_its_last_resort()
    {
        using var http = new HttpClient();

        Assert.Equal(["DuckDuckGo", "Wikipedia"], WebSearchChain.Create(http, null).BackendNames);
    }

    private sealed class StubBackend : IWebSearchBackend
    {
        private readonly int _hits;

        public StubBackend(string name, int hits, string host = "stub.example.com")
        {
            Name = name;
            _hits = hits;
            Hosts = [host];
        }

        public string Name { get; }
        public IReadOnlyList<string> Hosts { get; }
        public int Calls { get; private set; }

        public Task<WebSearchOutcome?> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            Calls++;

            var hits = Enumerable.Range(1, _hits)
                .Select(i => new WebSearchHit($"{Name} result {i}", $"https://{Name}.test/{i}", "snippet"))
                .ToArray();

            return Task.FromResult<WebSearchOutcome?>(new WebSearchOutcome(Name, null, hits));
        }
    }
}

public sealed class WebSearchParsingTests
{
    [Fact]
    public void Tavily_results_and_its_synthesised_answer_are_both_read()
    {
        using var document = JsonDocument.Parse("""
            {
              "query": "rag",
              "answer": "Retrieval-augmented generation supplements a model with retrieved documents.",
              "results": [
                {"url": "https://en.wikipedia.org/wiki/Retrieval-augmented_generation",
                 "title": "Retrieval-augmented generation", "content": "RAG is a technique that..."},
                {"url": "https://example.com/rag", "title": "What is RAG", "content": "An overview."}
              ]
            }
            """);

        var outcome = WebSearchChain.TavilyBackend.Parse(document.RootElement);

        Assert.Equal("Tavily", outcome.Backend);
        Assert.StartsWith("Retrieval-augmented generation supplements", outcome.Answer);
        Assert.Equal(2, outcome.Hits.Count);
        Assert.Equal("https://example.com/rag", outcome.Hits[1].Url);
    }

    [Fact]
    public void A_tavily_result_without_a_url_is_dropped_rather_than_cited_as_a_source()
    {
        using var document = JsonDocument.Parse("""
            {"results": [{"title": "No link here", "content": "text"}, {"url": "https://ok.test", "title": "Fine"}]}
            """);

        var hit = Assert.Single(WebSearchChain.TavilyBackend.Parse(document.RootElement).Hits);
        Assert.Equal("https://ok.test", hit.Url);
    }

    /// <summary>
    /// DuckDuckGo returns markup, not a contract, so the parser is pinned to a captured sample.
    /// If DuckDuckGo changes its markup this test keeps passing while the live endpoint stops
    /// returning rows — which is exactly why an empty parse falls through to the next backend
    /// instead of being reported as success.
    /// </summary>
    [Fact]
    public void DuckDuckGo_rows_are_read_and_the_real_url_is_recovered_from_the_redirect()
    {
        const string html = """
            <div class="result results_links">
              <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fpage&amp;rut=abc">
                Example &amp; Co
              </a>
              <a class="result__snippet" href="#">The <b>first</b> snippet.</a>
            </div>
            <div class="result results_links">
              <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fsecond.test%2Fx">Second</a>
              <a class="result__snippet" href="#">Another snippet.</a>
            </div>
            """;

        var hits = WebSearchChain.DuckDuckGoBackend.ParseResults(html, maxResults: 10);

        Assert.Equal(2, hits.Count);
        Assert.Equal("https://example.com/page", hits[0].Url);
        Assert.Equal("Example & Co", hits[0].Title);
        Assert.Equal("The first snippet.", hits[0].Snippet);
        Assert.Equal("https://second.test/x", hits[1].Url);
    }

    [Fact]
    public void The_requested_result_count_is_respected()
    {
        var html = string.Concat(Enumerable.Range(1, 8).Select(i => $"""
            <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fsite{i}.test">Title {i}</a>
            <a class="result__snippet">Snippet {i}</a>
            """));

        Assert.Equal(3, WebSearchChain.DuckDuckGoBackend.ParseResults(html, maxResults: 3).Count);
    }

    /// <summary>
    /// Handing back the redirect would make every citation in a report point at duckduckgo.com
    /// instead of the source, which quietly destroys the value of citing anything.
    /// </summary>
    [Fact]
    public void A_link_that_is_only_a_duckduckgo_redirect_with_no_target_is_discarded()
    {
        Assert.Equal("", WebSearchChain.DuckDuckGoBackend.Unwrap("//duckduckgo.com/y.js?ad_provider=x"));
        Assert.Equal("https://direct.test/page", WebSearchChain.DuckDuckGoBackend.Unwrap("https://direct.test/page"));
    }

    [Fact]
    public void Wikipedia_entries_become_hits_with_working_article_urls()
    {
        using var document = JsonDocument.Parse("""
            {"query": {"search": [
              {"title": "Retrieval-augmented generation",
               "snippet": "<span class=\"searchmatch\">RAG</span> is a technique."},
              {"title": "Large language model", "snippet": "An <b>LLM</b> is..."}
            ]}}
            """);

        var hits = WebSearchChain.WikipediaBackend.Parse(document.RootElement);

        Assert.Equal(2, hits.Count);
        Assert.Equal("https://en.wikipedia.org/wiki/Retrieval-augmented_generation", hits[0].Url);

        // The snippet arrives as markup and must be readable text by the time a model sees it.
        Assert.Equal("RAG is a technique.", hits[0].Snippet);
        Assert.DoesNotContain("<", hits[1].Snippet);
    }

    [Fact]
    public void A_wikipedia_response_with_no_matches_yields_nothing_to_fall_through_on()
    {
        using var document = JsonDocument.Parse("""{"query": {"search": []}}""");

        Assert.Empty(WebSearchChain.WikipediaBackend.Parse(document.RootElement));
    }
}
