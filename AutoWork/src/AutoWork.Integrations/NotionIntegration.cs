using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using AutoWork.Core.Agents;
using Microsoft.Extensions.AI;

namespace AutoWork.Integrations;

/// <summary>
/// Notion via an internal integration token.
///
/// Notion's permission model is share-based: a page is invisible to the token until the user
/// shares it with the integration. That surprises people often enough that the connection test
/// says so explicitly when it finds nothing.
/// </summary>
public sealed class NotionIntegration : RestConnector
{
    private const string Api = "https://api.notion.com/v1";
    private const string Version = "2022-06-28";

    public override string Id => "notion";
    public override string DisplayName => "Notion";
    public override string Description => "Search pages, read their content, and append notes.";
    public override string DocsUrl => "https://www.notion.so/my-integrations";

    public override IReadOnlyList<IntegrationField> Fields =>
    [
        new("token", "Internal integration token", "Create an integration at notion.so/my-integrations, then share the pages you want AutoWork to see with it.", Secret: true),
    ];

    private static Action<HttpRequestMessage> Auth(string token) =>
        Bearer(token, ("Notion-Version", Version));

    public override async Task<IntegrationStatus> TestAsync(
        IntegrationCredentials credentials, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = credentials.Require("token");
            var result = await PostAsync($"{Api}/search", Auth(token), new { page_size = 1 }, cancellationToken)
                .ConfigureAwait(false);

            var count = (result?["results"] as JsonArray)?.Count ?? 0;

            return count > 0
                ? new IntegrationStatus(true, "Connected.")
                : new IntegrationStatus(true,
                    "Connected, but no pages are shared with this integration yet. " +
                    "Open a page in Notion, choose Connections, and add AutoWork.");
        }
        catch (Exception ex) when (ex is IntegrationException or InvalidOperationException or HttpRequestException)
        {
            return new IntegrationStatus(false, ex.Message);
        }
    }

    public override IEnumerable<ToolDescriptor> GetTools(ToolContext context, IntegrationCredentials credentials)
    {
        if (credentials.Any("token") is null) yield break;

        yield return Tool(AIFunctionFactory.Create(
            ([Description("What to search for. Leave empty to list everything shared with AutoWork.")] string query = "",
             [Description("Maximum results.")] int limit = 10) =>
                SafelyAsync(async () =>
                {
                    var token = credentials.Require("token");
                    var result = await PostAsync($"{Api}/search", Auth(token),
                        new { query, page_size = Math.Clamp(limit, 1, 50) }).ConfigureAwait(false);

                    if (result?["results"] is not JsonArray items || items.Count == 0)
                        return "Nothing found. Remember that pages must be shared with the AutoWork integration in Notion first.";

                    var lines = items.Select(item =>
                        $"{TitleOf(item)}  [{item?["object"]}]  id={item?["id"]}");

                    return string.Join('\n', lines);
                }),
            "notion_search", "Search Notion pages and databases shared with AutoWork."));

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Page id from notion_search.")] string pageId,
             [Description("Maximum blocks to read.")] int limit = 100) =>
                SafelyAsync(async () =>
                {
                    var token = credentials.Require("token");
                    var result = await GetAsync(
                        $"{Api}/blocks/{pageId}/children?page_size={Math.Clamp(limit, 1, 100)}",
                        Auth(token)).ConfigureAwait(false);

                    if (result?["results"] is not JsonArray blocks || blocks.Count == 0)
                        return "That page has no readable content.";

                    var builder = new StringBuilder();
                    foreach (var block in blocks)
                    {
                        var text = PlainText(block);
                        if (!string.IsNullOrWhiteSpace(text)) builder.AppendLine(text);
                    }

                    return Trim(builder.ToString(), 12_000);
                }),
            "notion_read_page", "Read the text content of a Notion page."));

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Page id to append to.")] string pageId,
             [Description("Text to append as a paragraph.")] string text) =>
                SafelyAsync(async () =>
                {
                    var token = credentials.Require("token");

                    var body = new
                    {
                        children = new object[]
                        {
                            new
                            {
                                @object = "block",
                                type = "paragraph",
                                paragraph = new { rich_text = new object[] { new { type = "text", text = new { content = text } } } },
                            },
                        },
                    };

                    await SendAsync(HttpMethod.Patch, $"{Api}/blocks/{pageId}/children", Auth(token), body)
                        .ConfigureAwait(false);

                    return $"Appended {text.Length} characters to the page.";
                }),
            "notion_append", "Append a paragraph to a Notion page."),
            ToolRisk.Write);
    }

    /// <summary>Notion puts the title in a different place depending on the object type.</summary>
    private static string TitleOf(JsonNode? item)
    {
        var properties = item?["properties"];
        if (properties is JsonObject bag)
        {
            foreach (var (_, value) in bag)
            {
                if (value?["type"]?.GetValue<string>() == "title")
                    return Concat(value["title"] as JsonArray) is { Length: > 0 } title ? title : "(untitled)";
            }
        }

        return Concat(item?["title"] as JsonArray) is { Length: > 0 } databaseTitle ? databaseTitle : "(untitled)";
    }

    private static string PlainText(JsonNode? block)
    {
        var type = block?["type"]?.GetValue<string>();
        if (type is null) return "";

        var content = block?[type];
        var text = Concat(content?["rich_text"] as JsonArray);

        return type switch
        {
            "heading_1" => $"# {text}",
            "heading_2" => $"## {text}",
            "heading_3" => $"### {text}",
            "bulleted_list_item" or "numbered_list_item" => $"- {text}",
            "to_do" => $"- [{(content?["checked"]?.GetValue<bool>() == true ? "x" : " ")}] {text}",
            _ => text,
        };
    }

    private static string Concat(JsonArray? richText) =>
        richText is null ? "" : string.Concat(richText.Select(part => part?["plain_text"]?.GetValue<string>() ?? ""));
}
