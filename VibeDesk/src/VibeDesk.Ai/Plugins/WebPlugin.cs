using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.SemanticKernel;

namespace VibeDesk.Ai.Plugins;

/// <summary>
/// The assistant's window on the world: web search, page reading, and fetching a file by URL.
/// </summary>
/// <remarks>
/// Every URL here originates from the model, which means it originates from whatever text the model
/// has read — including a document a stranger shared with the user. That makes this the app's SSRF
/// surface, so <see cref="IsFetchableAsync"/> gates each request on scheme and on the resolved
/// address rather than trusting the hostname.
/// </remarks>
public sealed class WebPlugin(HttpClient http, AssistantOptions options)
{
    private readonly int _maxChars = options.MaxToolResultChars;

    [KernelFunction("web_search")]
    [Description("Searches the web and returns titles, URLs and snippets. Use for anything current, or anything outside the user's own files.")]
    public async Task<string> SearchAsync(
        [Description("The search query, in the language the answer should be in.")] string query,
        CancellationToken ct = default)
    {
        var apiKey = options.Tavily.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "Web search is not configured. Ask the administrator to set Assistant:Tavily:ApiKey.";
        }

        if (string.IsNullOrWhiteSpace(query)) return "Query kosong.";

        var payload = JsonSerializer.Serialize(new
        {
            api_key = apiKey,
            query,
            max_results = options.Tavily.MaxResults,
            search_depth = "basic",
            include_answer = true,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Tavily.Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return $"Search failed: {(int)response.StatusCode} {response.ReasonPhrase}.";
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var builder = new StringBuilder();

        if (document.RootElement.TryGetProperty("answer", out var answer) &&
            answer.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(answer.GetString()))
        {
            builder.AppendLine($"Summary: {answer.GetString()}").AppendLine();
        }

        if (document.RootElement.TryGetProperty("results", out var results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            var index = 0;

            foreach (var result in results.EnumerateArray())
            {
                index++;
                builder
                    .AppendLine($"{index}. {Read(result, "title")}")
                    .AppendLine($"   {Read(result, "url")}")
                    .AppendLine($"   {Truncate(Read(result, "content"), 400)}")
                    .AppendLine();
            }

            if (index == 0) builder.AppendLine("No results.");
        }

        return Truncate(builder.ToString(), _maxChars);

        static string Read(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    [KernelFunction("read_web_page")]
    [Description("Fetches a web page and returns its readable text with script, style and navigation removed. Use after web_search when a snippet is not enough.")]
    public async Task<string> ReadPageAsync(
        [Description("Absolute http or https URL.")] string url,
        CancellationToken ct = default)
    {
        var (ok, reason, uri) = await IsFetchableAsync(url, ct).ConfigureAwait(false);
        if (!ok) return reason;

        string html;
        try
        {
            using var response = await http.GetAsync(uri, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return $"Fetch failed: {(int)response.StatusCode}.";

            html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"Fetch failed: {ex.Message}";
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);

        foreach (var node in document.DocumentNode
                     .SelectNodes("//script|//style|//noscript|//nav|//footer|//svg")?
                     .ToList() ?? [])
        {
            node.Remove();
        }

        var title = document.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim();
        var body = document.DocumentNode.SelectSingleNode("//main")
                   ?? document.DocumentNode.SelectSingleNode("//article")
                   ?? document.DocumentNode.SelectSingleNode("//body")
                   ?? document.DocumentNode;

        var text = HtmlEntity.DeEntitize(body.InnerText) ?? string.Empty;
        text = string.Join('\n', text
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0));

        return Truncate(
            string.IsNullOrWhiteSpace(title) ? text : $"# {title}\n({uri})\n\n{text}",
            _maxChars);
    }

    [KernelFunction("read_file_from_url")]
    [Description("Downloads a file by URL and returns its text. Works for txt, markdown, csv, json, xml and similar; binary formats are reported rather than decoded.")]
    public async Task<string> ReadFileAsync(
        [Description("Absolute http or https URL of the file.")] string url,
        CancellationToken ct = default)
    {
        var (ok, reason, uri) = await IsFetchableAsync(url, ct).ConfigureAwait(false);
        if (!ok) return reason;

        try
        {
            using var response = await http.GetAsync(uri, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return $"Download failed: {(int)response.StatusCode}.";

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var size = response.Content.Headers.ContentLength ?? 0;

            if (!IsTextLike(contentType, uri))
            {
                return $"{uri.Segments[^1]} is {contentType} ({size:N0} bytes) — a binary format this tool cannot read as text.";
            }

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return Truncate($"# {uri.Segments[^1]} ({contentType})\n\n{text}", _maxChars);
        }
        catch (Exception ex)
        {
            return $"Download failed: {ex.Message}";
        }
    }

    private static bool IsTextLike(string contentType, Uri uri)
    {
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return true;

        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("csv", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] extensions = [".txt", ".md", ".csv", ".json", ".xml", ".yaml", ".yml", ".log", ".sql"];

        return extensions.Any(e => uri.AbsolutePath.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Allows only http/https to a public address. The DNS resolution matters: a hostname that looks
    /// external can still resolve to 127.0.0.1 or a cloud metadata endpoint, and that is precisely
    /// the request an attacker plants in a shared document.
    /// </summary>
    private static async Task<(bool Ok, string Reason, Uri Uri)> IsFetchableAsync(string url, CancellationToken ct)
    {
        var placeholder = new Uri("http://invalid.invalid/");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return (false, "That is not an absolute URL.", placeholder);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return (false, "Only http and https URLs can be fetched.", placeholder);
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (false, $"Could not resolve {uri.Host}: {ex.Message}", placeholder);
        }

        if (addresses.Length == 0) return (false, $"Could not resolve {uri.Host}.", placeholder);

        if (addresses.Any(IsPrivate))
        {
            return (false, "That address is on a private network and cannot be fetched.", placeholder);
        }

        return (true, string.Empty, uri);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                   || address.IsIPv6SiteLocal
                   || address.IsIPv6UniqueLocal
                   || address.Equals(IPAddress.IPv6Any);
        }

        var bytes = address.GetAddressBytes();

        return bytes[0] switch
        {
            0 or 10 or 127 => true,
            172 => bytes[1] >= 16 && bytes[1] <= 31,
            // 169.254.0.0/16 covers the cloud metadata endpoints as well as link-local.
            169 => bytes[1] == 254,
            192 => bytes[1] == 168,
            _ => false,
        };
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "\n…(dipotong)";
}
