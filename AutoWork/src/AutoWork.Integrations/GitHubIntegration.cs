using System.ComponentModel;
using System.Text.Json.Nodes;
using AutoWork.Core.Agents;
using Microsoft.Extensions.AI;

namespace AutoWork.Integrations;

/// <summary>GitHub over a personal access token. Read-heavy, with issue creation as the one write.</summary>
public sealed class GitHubIntegration : RestConnector
{
    private const string Api = "https://api.github.com";

    public override string Id => "github";
    public override string DisplayName => "GitHub";
    public override string Description => "Search repositories, read issues and files, and open new issues.";
    public override string DocsUrl => "https://github.com/settings/tokens";

    public override IReadOnlyList<IntegrationField> Fields =>
    [
        new("token", "Personal access token", "A fine-grained or classic token. Needs the repo scope for private repositories.", Secret: true),
        new("defaultRepo", "Default repository", "Optional, in owner/name form. Used when a tool is called without one.", Required: false, Placeholder: "gravicode/autowork"),
    ];

    public override async Task<IntegrationStatus> TestAsync(
        IntegrationCredentials credentials, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = credentials.Require("token");
            var user = await GetAsync($"{Api}/user", Auth(token), cancellationToken).ConfigureAwait(false);
            var login = user?["login"]?.GetValue<string>();

            return new IntegrationStatus(true, $"Connected as {login}.", login);
        }
        catch (Exception ex) when (ex is IntegrationException or InvalidOperationException or HttpRequestException)
        {
            return new IntegrationStatus(false, ex.Message);
        }
    }

    private static Action<HttpRequestMessage> Auth(string token) =>
        Bearer(token, ("Accept", "application/vnd.github+json"), ("X-GitHub-Api-Version", "2022-11-28"));

    public override IEnumerable<ToolDescriptor> GetTools(ToolContext context, IntegrationCredentials credentials)
    {
        if (credentials.Any("token") is null) yield break;

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Search query, e.g. \"language:csharp stars:>100\".")] string query,
             [Description("Maximum results.")] int limit = 10) =>
                SafelyAsync(async () =>
                {
                    var token = credentials.Require("token");
                    var url = $"{Api}/search/repositories?q={Uri.EscapeDataString(query)}&per_page={Math.Clamp(limit, 1, 30)}";
                    var result = await GetAsync(url, Auth(token)).ConfigureAwait(false);

                    var items = result?["items"] as JsonArray;
                    if (items is null || items.Count == 0) return $"No repositories match \"{query}\".";

                    var lines = items.Select(item =>
                        $"{item?["full_name"]} — ★{item?["stargazers_count"]} — {Trim(item?["description"]?.GetValue<string>(), 100)}");

                    return string.Join('\n', lines);
                }),
            "github_search_repos", "Search GitHub repositories."));

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Repository in owner/name form.")] string? repository = null,
             [Description("Issue state: open, closed or all.")] string state = "open",
             [Description("Maximum results.")] int limit = 15) =>
                SafelyAsync(async () =>
                {
                    var token = credentials.Require("token");
                    var repo = repository ?? credentials.Value("defaultRepo")
                        ?? throw new InvalidOperationException("No repository was given and no default is configured.");

                    var url = $"{Api}/repos/{repo}/issues?state={state}&per_page={Math.Clamp(limit, 1, 50)}";
                    var result = await GetAsync(url, Auth(token)).ConfigureAwait(false);

                    if (result is not JsonArray issues || issues.Count == 0) return $"No {state} issues in {repo}.";

                    var lines = issues.Select(issue =>
                        $"#{issue?["number"]} [{issue?["state"]}] {issue?["title"]} — @{issue?["user"]?["login"]}");

                    return $"{issues.Count} issue(s) in {repo}:\n" + string.Join('\n', lines);
                }),
            "github_list_issues", "List issues in a repository."));

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Repository in owner/name form.")] string repository,
             [Description("Path of the file inside the repository.")] string path,
             [Description("Branch, tag or commit. Defaults to the default branch.")] string? reference = null) =>
                SafelyAsync(async () =>
                {
                    var token = credentials.Require("token");
                    var url = $"{Api}/repos/{repository}/contents/{path}"
                              + (reference is null ? "" : $"?ref={Uri.EscapeDataString(reference)}");

                    var result = await GetAsync(url, Auth(token)).ConfigureAwait(false);
                    var encoded = result?["content"]?.GetValue<string>();

                    if (encoded is null) return $"ERROR: {path} in {repository} is not a readable file.";

                    var bytes = Convert.FromBase64String(encoded.Replace("\n", ""));
                    return Trim(System.Text.Encoding.UTF8.GetString(bytes), 12_000);
                }),
            "github_read_file", "Read a file from a GitHub repository."));

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Repository in owner/name form.")] string repository,
             [Description("Issue title.")] string title,
             [Description("Issue body in Markdown.")] string body) =>
                SafelyAsync(async () =>
                {
                    var token = credentials.Require("token");
                    var result = await PostAsync($"{Api}/repos/{repository}/issues", Auth(token),
                        new { title, body }).ConfigureAwait(false);

                    return $"Opened issue #{result?["number"]} in {repository}: {result?["html_url"]}";
                }),
            "github_create_issue", "Open a new issue."),
            ToolRisk.Write);
    }
}
