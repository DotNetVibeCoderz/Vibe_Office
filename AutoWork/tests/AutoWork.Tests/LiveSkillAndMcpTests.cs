using AutoWork.Agents;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Core.Skills;
using AutoWork.Integrations.Mcp;

namespace AutoWork.Tests;

/// <summary>
/// The last thing the wiring tests cannot show: whether a real model, given a real job, chooses
/// to reach for a skill or an MCP tool.
///
/// Everything else about these features is provable offline — a skill installs, a script runs, a
/// server lists its tools. What could not be proved without a live model is that any of it is
/// *used*, and a capability nothing ever calls is not a feature.
///
/// The assertions look at the tool-call stream rather than at the model's prose, and where
/// possible at a value the model could only produce by having read the skill.
/// </summary>
public sealed class LiveSkillUseTests : IDisposable
{
    /// <summary>
    /// A stable copy of the events seen so far. `Progress&lt;T&gt;` delivers on another thread, so a
    /// callback can still arrive while the assertions enumerate — which is exactly how one live
    /// run failed with "Collection was modified".
    /// </summary>
    private static RunEvent[] Snapshot(List<RunEvent> events)
    {
        lock (events) return [.. events];
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "autowork-live-skilluse", Guid.NewGuid().ToString("n")[..8]);
    private readonly string _workspace;
    private readonly FileSkillStore _skills;

    public LiveSkillUseTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);

        _skills = new FileSkillStore(Path.Combine(_root, "skills"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// The skill carries a rule the model cannot guess. If the answer is right, the skill was
    /// read — a stronger claim than "skill_open appears in the transcript".
    ///
    /// The goal names the skill. An earlier version left the model to notice the skill from its
    /// description alone and passed twice before a run where it simply listed the folder instead
    /// — so unprompted skill selection is a real behaviour worth knowing about, but it is the
    /// model's judgement, not this code's, and a suite that fails a third of the time measures
    /// nothing. What is asserted here is the mechanism: asked to use a skill, the agent opens it
    /// and follows what it says.
    /// </summary>
    [Fact]
    public async Task The_model_opens_a_skill_and_follows_a_rule_it_could_not_have_guessed()
    {
        var (factory, profile) = LiveModel.Require();

        _skills.Install(
            """
            ---
            name: invoice-code
            description: Use this whenever the user asks for an invoice code, a billing code, or asks how to format one. It is the only place the house rule is written down.
            ---

            # Invoice codes

            The house rule, which is not documented anywhere else:

            An invoice code is the letters `GRV`, then a hyphen, then the invoice number padded
            to six digits with leading zeros, then a hyphen, then the word `FINAL` in capitals.

            For example, invoice 42 becomes `GRV-000042-FINAL`.
            """,
            "local", "invoice-code");

        var events = await RunAsync(factory, profile, allowScripts: false,
            $"""
             Using the "invoice-code" skill, work out the invoice code for invoice number 7 and
             write it, and nothing else, to {Path.Combine(_workspace, "code.txt")}.
             """);

        var called = Snapshot(events).OfType<ToolCallEvent>().Select(e => e.Tool).Distinct().ToArray();

        Assert.Contains("skill_open", called);

        var path = Path.Combine(_workspace, "code.txt");
        Assert.True(File.Exists(path), $"code.txt was not written. Tools used: {string.Join(", ", called)}");

        // The padding and the suffix exist only in the skill.
        Assert.Contains("GRV-000007-FINAL", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The same question for the part that runs code: given a skill whose instructions say to run
    /// its script, does the model run it? The script prints a value nothing else knows.
    /// </summary>
    [Fact]
    public async Task The_model_runs_a_skills_bundled_script_when_the_skill_says_to()
    {
        var (factory, profile) = LiveModel.Require();

        _skills.Install(
            """
            ---
            name: build-token
            description: Use this whenever the user asks for the build token, the release token, or today's token. The token can only be produced by this skill's script.
            ---

            # Build token

            The build token cannot be worked out by hand. Run the bundled script to get it:

                skill_run(skill="build-token", script="scripts/token.py")

            Report exactly what the script prints.
            """,
            "local", "build-token",
            [("scripts/token.py", "print('TOKEN-9F3A-2C71')"u8.ToArray())]);

        var events = await RunAsync(factory, profile, allowScripts: true,
            $"""
             Get the build token and write it, and nothing else, to
             {Path.Combine(_workspace, "token.txt")}.
             """);

        var called = Snapshot(events).OfType<ToolCallEvent>().Select(e => e.Tool).Distinct().ToArray();

        Assert.Contains("skill_run", called);

        var path = Path.Combine(_workspace, "token.txt");
        Assert.True(File.Exists(path), $"token.txt was not written. Tools used: {string.Join(", ", called)}");

        Assert.Contains("TOKEN-9F3A-2C71", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private async Task<List<RunEvent>> RunAsync(
        AutoWork.Providers.ModelClientFactory factory, ModelProfile profile, bool allowScripts, string goal)
    {
        var config = new ConfigStore(Path.Combine(_root, "config.json"));
        config.Save(new AutoWorkConfig
        {
            Models = [profile],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowNetwork = false,
                AllowSkillScripts = allowScripts,

                // Approval is proven separately; a test cannot click the card.
                SkillScriptsRequireApproval = false,
            },
            Agent = new AgentOptions
            {
                PlannerModelId = profile.Id,
                ExecutorModelId = profile.Id,
                MaxSteps = 8,
                EnableSubAgents = false,
                EnableSelfVerification = false,
            },
        });

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));

        var orchestrator = new AgentOrchestrator(
            config, factory, new ToolRegistry(factory, knowledge, null, _skills),
            NullActionLog.Instance, new AutoApproveBroker(), knowledge, null, _skills);

        var events = new List<RunEvent>();
        await orchestrator.RunAsync(goal, new Progress<RunEvent>(e => { lock (events) events.Add(e); }), TestContext.Current.CancellationToken);

        return events;
    }
}

/// <summary>
/// Whether a model actually calls a tool that came from an MCP server. Needs Node.js, because
/// the reference server is an npm package.
/// </summary>
public sealed class LiveMcpUseTests : IDisposable
{
    /// <summary>
    /// A stable copy of the events seen so far. `Progress&lt;T&gt;` delivers on another thread, so a
    /// callback can still arrive while the assertions enumerate — which is exactly how one live
    /// run failed with "Collection was modified".
    /// </summary>
    private static RunEvent[] Snapshot(List<RunEvent> events)
    {
        lock (events) return [.. events];
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "autowork-live-mcpuse", Guid.NewGuid().ToString("n")[..8]);
    private readonly string _workspace;

    public LiveMcpUseTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task The_model_calls_a_tool_that_came_from_an_mcp_server()
    {
        var (factory, profile) = LiveModel.Require();

        var config = new ConfigStore(Path.Combine(_root, "config.json"));
        config.Save(new AutoWorkConfig
        {
            Models = [profile],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowMcpServers = true,
                AllowNetwork = false,
            },
            McpServers =
            [
                new McpServerSettings
                {
                    Name = "Everything",
                    Command = "npx",
                    Arguments = ["-y", "@modelcontextprotocol/server-everything"],
                    Enabled = true,
                },
            ],
            Agent = new AgentOptions
            {
                PlannerModelId = profile.Id,
                ExecutorModelId = profile.Id,
                MaxSteps = 6,
                EnableSubAgents = false,
                EnableSelfVerification = false,
            },
        });

        await using var mcp = new McpServerRegistry(config, new NoSecrets());

        var connected = await mcp.SyncAsync(TestContext.Current.CancellationToken);

        Assert.SkipWhen(connected.Any(r => !r.Success),
            $"The MCP server could not start ({string.Join("; ", connected.Select(r => r.Message))}). Skipped.");

        var mcpToolNames = mcp.ConnectedTools.SelectMany(kv => kv.Value).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(mcpToolNames);

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));

        var orchestrator = new AgentOrchestrator(
            config, factory,
            new ToolRegistry(factory, knowledge, [mcp.AsToolProvider()]),
            NullActionLog.Instance, new AutoApproveBroker(), knowledge);

        var events = new List<RunEvent>();

        await orchestrator.RunAsync(
            """
            Use the "echo" tool that the connected MCP server provides to echo the exact text
            HELLO-FROM-MCP, then tell me what it returned. Do not write any files.
            """,
            new Progress<RunEvent>(e => { lock (events) events.Add(e); }), TestContext.Current.CancellationToken);

        var called = Snapshot(events).OfType<ToolCallEvent>().Select(e => e.Tool).Distinct().ToArray();

        Assert.True(called.Any(mcpToolNames.Contains),
            $"No MCP tool was called. Tools used: {string.Join(", ", called)}. " +
            $"MCP offered: {string.Join(", ", mcpToolNames)}");
    }

    /// <summary>The registry only resolves references; this run configures no secrets at all.</summary>
    private sealed class NoSecrets : ISecretStore
    {
        public string? Resolve(string? reference) => reference;
        public string? Get(string name) => null;
        public void Set(string name, string value) { }
        public void Delete(string name) { }
        public IReadOnlyCollection<string> Names => [];
    }
}
