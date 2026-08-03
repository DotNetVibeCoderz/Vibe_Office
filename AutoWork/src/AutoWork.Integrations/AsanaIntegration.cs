using System.ComponentModel;
using System.Text.Json.Nodes;
using AutoWork.Core.Agents;
using Microsoft.Extensions.AI;

namespace AutoWork.Integrations;

/// <summary>Asana via a personal access token: read tasks and projects, create tasks.</summary>
public sealed class AsanaIntegration : RestConnector
{
    private const string Api = "https://app.asana.com/api/1.0";

    public override string Id => "asana";
    public override string DisplayName => "Asana";
    public override string Description => "List projects and tasks, and create new tasks.";
    public override string DocsUrl => "https://app.asana.com/0/my-apps";

    public override IReadOnlyList<IntegrationField> Fields =>
    [
        new("token", "Personal access token", "Create one under My Settings › Apps › Manage developer apps.", Secret: true),
        new("workspace", "Default workspace id", "Optional. Found in the URL when viewing your workspace.", Required: false),
    ];

    public override async Task<IntegrationStatus> TestAsync(
        IntegrationCredentials credentials, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = credentials.Require("token");
            var me = await GetAsync($"{Api}/users/me", Bearer(token), cancellationToken).ConfigureAwait(false);

            var name = me?["data"]?["name"]?.GetValue<string>();
            var workspaces = (me?["data"]?["workspaces"] as JsonArray)?.Count ?? 0;

            return new IntegrationStatus(true, $"Connected as {name} ({workspaces} workspace(s)).", name);
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
            () => SafelyAsync(async () =>
            {
                var token = credentials.Require("token");
                var workspace = credentials.Value("workspace");

                var url = workspace is null
                    ? $"{Api}/projects?limit=50&opt_fields=name,archived"
                    : $"{Api}/projects?workspace={workspace}&limit=50&opt_fields=name,archived";

                var result = await GetAsync(url, Bearer(token)).ConfigureAwait(false);

                if (result?["data"] is not JsonArray projects || projects.Count == 0)
                    return "No projects found.";

                var lines = projects
                    .Where(p => p?["archived"]?.GetValue<bool>() != true)
                    .Select(p => $"{p?["name"]}  id={p?["gid"]}");

                return string.Join('\n', lines);
            }),
            "asana_list_projects", "List Asana projects."));

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Project id from asana_list_projects.")] string projectId,
             [Description("Include tasks already completed.")] bool includeCompleted = false,
             [Description("Maximum results.")] int limit = 30) =>
                SafelyAsync(async () =>
                {
                    var token = credentials.Require("token");
                    var url = $"{Api}/tasks?project={projectId}&limit={Math.Clamp(limit, 1, 100)}" +
                              "&opt_fields=name,completed,due_on,assignee.name";

                    var result = await GetAsync(url, Bearer(token)).ConfigureAwait(false);

                    if (result?["data"] is not JsonArray tasks || tasks.Count == 0)
                        return "That project has no tasks.";

                    var lines = tasks
                        .Where(t => includeCompleted || t?["completed"]?.GetValue<bool>() != true)
                        .Select(t =>
                        {
                            var done = t?["completed"]?.GetValue<bool>() == true ? "x" : " ";
                            var due = t?["due_on"]?.GetValue<string>();
                            var assignee = t?["assignee"]?["name"]?.GetValue<string>();

                            return $"[{done}] {t?["name"]}"
                                   + (due is null ? "" : $"  due {due}")
                                   + (assignee is null ? "" : $"  @{assignee}")
                                   + $"  id={t?["gid"]}";
                        })
                        .ToList();

                    return lines.Count == 0 ? "No open tasks." : string.Join('\n', lines);
                }),
            "asana_list_tasks", "List tasks in an Asana project."));

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Project id the task belongs to.")] string projectId,
             [Description("Task name.")] string name,
             [Description("Optional description.")] string? notes = null,
             [Description("Optional due date as YYYY-MM-DD.")] string? dueOn = null) =>
                SafelyAsync(async () =>
                {
                    var token = credentials.Require("token");

                    var data = new Dictionary<string, object?>
                    {
                        ["name"] = name,
                        ["projects"] = new[] { projectId },
                    };

                    if (!string.IsNullOrWhiteSpace(notes)) data["notes"] = notes;
                    if (!string.IsNullOrWhiteSpace(dueOn)) data["due_on"] = dueOn;

                    var result = await PostAsync($"{Api}/tasks", Bearer(token), new { data }).ConfigureAwait(false);

                    return $"Created task \"{name}\" (id={result?["data"]?["gid"]}).";
                }),
            "asana_create_task", "Create an Asana task."),
            ToolRisk.Write);
    }
}
