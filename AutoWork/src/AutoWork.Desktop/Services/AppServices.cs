using AutoWork.Agents;
using AutoWork.Core;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Agents;
using AutoWork.Core.Automation;
using AutoWork.Core.Logging;
using AutoWork.Core.Runs;
using AutoWork.Core.Security;
using AutoWork.Core.Skills;
using AutoWork.Core.Storage;
using AutoWork.Desktop.Localization;
using AutoWork.Integrations;
using AutoWork.Integrations.Mcp;
using AutoWork.Integrations.Skills;
using AutoWork.Providers;

namespace AutoWork.Desktop.Services;

/// <summary>
/// The composition root.
///
/// Wired by hand rather than through a container: the graph is a dozen objects that never
/// change shape at runtime, and hand-wiring keeps startup fast and the ordering obvious —
/// which matters because a container's reflection cost lands squarely on the time-to-window
/// the spec asks us to keep short.
/// </summary>
public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        AppPaths.EnsureCreated();

        Config = new ConfigStore();
        Secrets = new FileSecretStore();
        ActionLog = new JsonlActionLog();
        Models = new ModelClientFactory(Secrets);

        // The knowledge store asks for an embedder lazily, so changing the embedding model in
        // Settings takes effect without rebuilding the store.
        Knowledge = new JsonKnowledgeStore(() =>
            Config.Current.Agent.UseLocalEmbedding
                ? new LocalEmbeddingGenerator()
                : Models.CreateEmbeddingGenerator(Config.Current.ResolveEmbeddingModel()));

        Integrations = new IntegrationRegistry(Config, Secrets);
        Approvals = new UiApprovalBroker();

        Skills = new FileSkillStore();
        SkillGallery = new SkillGallery();
        Mcp = new McpServerRegistry(Config, Secrets);
        History = new FileRunHistoryStore();

        // Points at the same folder and index the delete tool writes to; the store keeps no
        // in-memory state, so sharing an instance buys nothing and this stays simple.
        Recycle = new RecycleBin();

        Jobs = new FileJobStore();

        // The scheduler decides *when*; the Work view decides *how*. Keeping them apart is what
        // lets the scheduler be tested against a fake clock with no agent in sight.
        Scheduler = new JobScheduler(
            Jobs,
            () => Config.Current.Permissions,
            async (job, reason, token) =>
            {
                if (RunJob is null)
                {
                    ActionLog.Failure("", AgentOrgan.Brain, "job.skip", $"Job \"{job.DisplayName}\" came due",
                        "No window is attached to run it.");
                    return;
                }

                await RunJob(job, reason, token).ConfigureAwait(false);
            });

        Tools = new ToolRegistry(Models, Knowledge,
            [Integrations.AsToolProvider(), Mcp.AsToolProvider()], Skills);

        Orchestrator = new AgentOrchestrator(
            Config, Models, Tools, ActionLog, Approvals, Knowledge, Secrets, Skills, History)
        {
            // Starting MCP servers takes seconds, so it happens once per run rather than on the
            // startup path — an app that waits for npx before showing a window is a broken app.
            PrepareToolsAsync = async ct => await Mcp.SyncAsync(ct).ConfigureAwait(false),
        };

        Strings = new Strings { Language = Config.Current.Appearance.Language };

        // Editing a model in Settings must invalidate the cached clients, or the old key keeps
        // being used until restart.
        Config.Changed += _ => Models.Invalidate();
    }

    public ConfigStore Config { get; }
    public ISecretStore Secrets { get; }
    public JsonlActionLog ActionLog { get; }
    public ModelClientFactory Models { get; }
    public IKnowledgeStore Knowledge { get; }
    public IntegrationRegistry Integrations { get; }
    public ISkillStore Skills { get; }
    public SkillGallery SkillGallery { get; }
    public McpServerRegistry Mcp { get; }
    public IRunHistoryStore History { get; }
    public IRecycleBin Recycle { get; }
    public FileJobStore Jobs { get; }
    public JobScheduler Scheduler { get; }

    /// <summary>
    /// Set by the shell once a window exists. Until then a job that comes due is logged and
    /// skipped rather than run headless — every run can raise a consent prompt, and a prompt
    /// with nobody to answer it is a hang.
    /// </summary>
    public Func<SavedJob, string, CancellationToken, Task>? RunJob { get; set; }
    public UiApprovalBroker Approvals { get; }
    public ToolRegistry Tools { get; }
    public AgentOrchestrator Orchestrator { get; }
    public Strings Strings { get; }

    public void Dispose()
    {
        Scheduler.Dispose();

        // MCP servers are child processes; leaving them running after the window closes would
        // leak a node process per enabled server.
        Mcp.DisposeAsync().AsTask().GetAwaiter().GetResult();

        Models.Dispose();
        ActionLog.Dispose();
    }
}
