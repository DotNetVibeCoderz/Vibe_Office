// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace OfficeNet.Gallery.Ai;

/// <summary>Web search, through Tavily.</summary>
/// <remarks>
/// Tavily rather than a raw search engine because it returns extracted answers rather than a page
/// of links, which is what a model can actually use. Without a key the function says so instead of
/// failing: a chat that silently drops a tool is worse than one that admits it has none.
/// </remarks>
internal sealed partial class SearchPlugin(string? apiKey, HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new HttpClient();

    [KernelFunction("web_search")]
    [Description("Searches the web and returns the most relevant results with short extracts.")]
    public async Task<string> SearchAsync(
        [Description("What to search for.")] string query,
        [Description("How many results to return, 1 to 10.")] int count = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "Web search is unavailable: no Tavily API key is configured.";
        }

        var request = new
        {
            api_key = apiKey,
            query,
            max_results = Math.Clamp(count, 1, 10),
            include_answer = true,
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(
                "https://api.tavily.com/search", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return $"Search failed: {(int)response.StatusCode} {response.ReasonPhrase}.";
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
            var builder = new StringBuilder();

            if (payload?["answer"]?.GetValue<string>() is { Length: > 0 } answer)
            {
                builder.AppendLine(answer).AppendLine();
            }

            foreach (var result in payload?["results"] as JsonArray ?? [])
            {
                builder
                    .AppendLine(result?["title"]?.GetValue<string>() ?? "(untitled)")
                    .AppendLine(result?["url"]?.GetValue<string>() ?? string.Empty)
                    .AppendLine(Truncate(result?["content"]?.GetValue<string>() ?? string.Empty, 400))
                    .AppendLine();
            }

            return builder.Length == 0 ? "No results." : builder.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"Search failed: {ex.Message}";
        }
    }

    internal static string Truncate(string text, int length) =>
        text.Length <= length ? text : text[..length] + "…";
}

/// <summary>Fetches a page and reduces it to readable text.</summary>
internal sealed partial class ScrapePlugin(HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new HttpClient();

    [KernelFunction("fetch_page")]
    [Description("Fetches a web page and returns its readable text, without markup.")]
    public async Task<string> FetchAsync(
        [Description("The absolute URL to fetch.")] string url,
        CancellationToken cancellationToken = default)
    {
        // Only http and https: a model that has been told to read "file:///etc/passwd" should get
        // a refusal from the tool, not from the file system.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Only absolute http and https URLs can be fetched.";
        }

        try
        {
            using var response = await _http.GetAsync(uri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return $"Fetch failed: {(int)response.StatusCode} {response.ReasonPhrase}.";
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            return SearchPlugin.Truncate(ToText(html), 12_000);
        }
        catch (HttpRequestException ex)
        {
            return $"Fetch failed: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return "Fetch timed out.";
        }
    }

    /// <summary>Strips markup down to the text a reader would see.</summary>
    /// <remarks>
    /// Script and style contents are removed first: they are the bulk of a modern page's bytes and
    /// none of its meaning, and leaving them in fills the model's context with minified JavaScript.
    /// </remarks>
    private static string ToText(string html)
    {
        var text = ScriptOrStyle().Replace(html, " ");
        text = Tags().Replace(text, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Whitespace().Replace(text, " ").Trim();
    }

    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>The current date and time, which a model cannot know.</summary>
internal sealed class TimePlugin
{
    [KernelFunction("current_datetime")]
    [Description("The current date and time. Use this instead of guessing today's date.")]
    public string Now(
        [Description("IANA time zone id, for example Asia/Jakarta. Local time if omitted.")]
        string? timeZone = null)
    {
        var now = DateTimeOffset.Now;

        if (!string.IsNullOrWhiteSpace(timeZone))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
                now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
            }
            catch (TimeZoneNotFoundException)
            {
                return $"Unknown time zone '{timeZone}'.";
            }
            catch (InvalidTimeZoneException)
            {
                return $"Invalid time zone '{timeZone}'.";
            }
        }

        return now.ToString("dddd, d MMMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }

    [KernelFunction("days_between")]
    [Description("The number of days between two dates, each written as yyyy-MM-dd.")]
    public string DaysBetween(string from, string to)
    {
        if (!DateTime.TryParse(from, CultureInfo.InvariantCulture, out var start) ||
            !DateTime.TryParse(to, CultureInfo.InvariantCulture, out var end))
        {
            return "Both dates must be readable, for example 2026-01-31.";
        }

        return ((int)(end.Date - start.Date).TotalDays).ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>Arithmetic, which language models are famously unreliable at.</summary>
internal sealed class MathPlugin
{
    [KernelFunction("calculate")]
    [Description("Evaluates an arithmetic expression such as (1480 - 1120) / 1120 * 100.")]
    public string Calculate(
        [Description("The expression. Supports + - * / % and parentheses.")] string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return "Nothing to calculate.";
        }

        try
        {
            // DataTable.Compute rather than a hand-written parser: it is in the framework, it
            // handles precedence and parentheses, and it cannot call anything.
            var value = new DataTable().Compute(expression, null);

            return value is null
                ? "That expression has no value."
                : Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    .ToString("0.##########", CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is EvaluateException or SyntaxErrorException or InvalidCastException
                                       or OverflowException or FormatException)
        {
            return $"That is not an expression I can evaluate: {ex.Message}";
        }
    }

    [KernelFunction("percentage_change")]
    [Description("The percentage change from one number to another.")]
    public string PercentageChange(double from, double to)
    {
        if (Math.Abs(from) < double.Epsilon)
        {
            return "Percentage change from zero is undefined.";
        }

        return ((to - from) / Math.Abs(from) * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }
}
