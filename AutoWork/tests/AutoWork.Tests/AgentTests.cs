using AutoWork.Agents;
using AutoWork.Core;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

public sealed class ContextCompactionTests
{
    /// <summary>
    /// The failure this guards against is subtle and fatal: cutting a conversation between an
    /// assistant's tool call and the tool's result leaves an orphan that providers reject
    /// outright, so a long run would die exactly when compaction was supposed to save it.
    /// </summary>
    [Fact]
    public void The_preserved_segment_never_begins_with_an_orphaned_tool_result()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "goal"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "files_list", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "47 files")]),
            new(ChatRole.Assistant, [new TextContent("Found 47 files.")]),
            new(ChatRole.User, "next"),
            new(ChatRole.Assistant, [new FunctionCallContent("c2", "files_move", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("c2", "moved")]),
        };

        // Every possible cut point must land somewhere legal.
        for (var keep = 0; keep <= messages.Count; keep++)
        {
            var boundary = ContextCompactor.FindPreserveBoundary(messages, keep);

            if (boundary >= messages.Count) continue;

            var first = messages[boundary];

            Assert.DoesNotContain(first.Contents, c => c is FunctionResultContent);
            Assert.NotEqual(ChatRole.Tool, first.Role);
        }
    }

    [Fact]
    public void A_boundary_landing_on_a_tool_result_is_moved_back_past_its_call()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "goal"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "files_list", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "done")]),
        };

        // Index 2 is the tool result — illegal as a segment start.
        var adjusted = ContextCompactor.AdjustToCleanBoundary(messages, 2);

        Assert.Equal(1, adjusted);
    }

    [Fact]
    public void Compaction_does_not_trigger_while_the_window_has_room()
    {
        var compactor = new ContextCompactor(new StubChatClient(), new AgentOptions
        {
            EnableAutoCompact = true,
            AutoCompactThreshold = 0.75,
        });

        var messages = new List<ChatMessage> { new(ChatRole.User, "short") };

        Assert.False(compactor.ShouldCompact(messages, contextWindow: 128_000));
    }

    [Fact]
    public void Compaction_triggers_once_the_threshold_is_crossed()
    {
        var compactor = new ContextCompactor(new StubChatClient(), new AgentOptions
        {
            EnableAutoCompact = true,
            AutoCompactThreshold = 0.5,
        });

        // ~2,800 estimated tokens against a 4,000-token window is comfortably over half.
        var messages = new List<ChatMessage> { new(ChatRole.User, new string('x', 10_000)) };

        Assert.True(compactor.ShouldCompact(messages, contextWindow: 4_000));
    }

    [Fact]
    public void Disabling_auto_compact_is_honoured()
    {
        var compactor = new ContextCompactor(new StubChatClient(), new AgentOptions { EnableAutoCompact = false });
        var messages = new List<ChatMessage> { new(ChatRole.User, new string('x', 100_000)) };

        Assert.False(compactor.ShouldCompact(messages, contextWindow: 4_000));
    }

    [Fact]
    public async Task Compacting_keeps_the_system_prompt_and_the_original_goal()
    {
        var compactor = new ContextCompactor(new StubChatClient("SUMMARY"), new AgentOptions
        {
            EnableAutoCompact = true,
            AutoCompactThreshold = 0.3,
            CompactKeepRecentTurns = 2,
        });

        var messages = new List<ChatMessage> { new(ChatRole.System, "system rules"), new(ChatRole.User, "the original goal") };
        for (var i = 0; i < 12; i++)
            messages.Add(new ChatMessage(i % 2 == 0 ? ChatRole.Assistant : ChatRole.User, new string('y', 900)));

        var before = messages.Count;
        var outcome = await compactor.CompactAsync(messages, contextWindow: 8_000,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(outcome.Compacted);
        Assert.True(messages.Count < before);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("system rules", messages[0].Text);
        Assert.Contains(messages, m => m.Text?.Contains("the original goal") == true);
        Assert.Contains(messages, m => m.Text?.Contains("SUMMARY") == true);
    }

    /// <summary>Returns a fixed reply without touching the network.</summary>
    private sealed class StubChatClient : IChatClient
    {
        private readonly string _reply;

        public StubChatClient(string reply = "ok") => _reply = reply;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _reply)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, _reply);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

public sealed class PlannerParsingTests
{
    [Fact]
    public void A_plain_json_plan_is_parsed()
    {
        var plan = Planner.Parse("tidy downloads", """
            {"notes":"Straightforward.","successCriteria":["Downloads is sorted"],
             "steps":[{"index":1,"title":"List the folder","intent":"See what is there","organ":"Hands","dependsOn":[],"successCriteria":["File list obtained"]},
                      {"index":2,"title":"Move files","intent":"Sort by type","organ":"Hands","dependsOn":[1],"successCriteria":["Files moved"]}]}
            """);

        Assert.NotNull(plan);
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal("List the folder", plan.Steps[0].Title);
        Assert.Equal([1], plan.Steps[1].DependsOn);
        Assert.Equal("Straightforward.", plan.Notes);
    }

    [Fact]
    public void A_plan_wrapped_in_a_code_fence_with_chatter_is_still_parsed()
    {
        // Models add prose and fences no matter what the prompt says, so the extractor has to
        // cope rather than the prompt having to win.
        var plan = Planner.Parse("goal", """
            Sure! Here is the plan:

            ```json
            {"steps":[{"index":1,"title":"Do the thing","intent":"...","organ":"Brain"}]}
            ```

            Let me know if you want changes.
            """);

        Assert.NotNull(plan);
        Assert.Single(plan.Steps);
        Assert.Equal(AgentOrgan.Brain, plan.Steps[0].Organ);
    }

    [Fact]
    public void Unparseable_output_yields_null_so_the_caller_can_fall_back()
    {
        Assert.Null(Planner.Parse("goal", "I'm sorry, I can't help with that."));
        Assert.Null(Planner.Parse("goal", ""));
        Assert.Null(Planner.Parse("goal", "{\"steps\":[]}"));
    }

    [Fact]
    public void An_unknown_organ_falls_back_to_Hands_rather_than_throwing()
    {
        var plan = Planner.Parse("goal", """{"steps":[{"index":1,"title":"x","organ":"Tentacles"}]}""");

        Assert.NotNull(plan);
        Assert.Equal(AgentOrgan.Hands, plan.Steps[0].Organ);
    }
}

public sealed class WaveSchedulingTests
{
    [Fact]
    public void Steps_without_declared_dependencies_run_one_at_a_time()
    {
        // A planner that simply omitted dependsOn must not be taken as a promise that the
        // steps are safe to run concurrently against the same folder.
        PlanStep[] steps =
        [
            new() { Index = 1, Title = "a" },
            new() { Index = 2, Title = "b" },
            new() { Index = 3, Title = "c" },
        ];

        var waves = AgentOrchestrator.GroupIntoWaves(steps);

        Assert.Equal(3, waves.Count);
        Assert.All(waves, wave => Assert.Single(wave));
    }

    [Fact]
    public void Steps_sharing_a_satisfied_dependency_are_batched_together()
    {
        PlanStep[] steps =
        [
            new() { Index = 1, Title = "survey" },
            new() { Index = 2, Title = "branch a", DependsOn = [1] },
            new() { Index = 3, Title = "branch b", DependsOn = [1] },
            new() { Index = 4, Title = "merge", DependsOn = [2, 3] },
        ];

        var waves = AgentOrchestrator.GroupIntoWaves(steps);

        Assert.Equal(3, waves.Count);
        Assert.Single(waves[0]);
        Assert.Equal(2, waves[1].Count);
        Assert.Single(waves[2]);
        Assert.Equal(4, waves[2][0].Index);
    }

    [Fact]
    public void A_dependency_cycle_degrades_to_sequential_instead_of_hanging()
    {
        PlanStep[] steps =
        [
            new() { Index = 1, Title = "a", DependsOn = [2] },
            new() { Index = 2, Title = "b", DependsOn = [1] },
        ];

        var waves = AgentOrchestrator.GroupIntoWaves(steps);

        Assert.Equal(2, waves.Count);
        Assert.All(waves, wave => Assert.Single(wave));
    }
}

public sealed class WorkingDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "autowork-cwd", Guid.NewGuid().ToString("n")[..8]);

    public WorkingDirectoryTests() => Directory.CreateDirectory(Path.Combine(_root, "granted"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void The_first_writable_grant_is_where_bare_filenames_land()
    {
        var granted = Path.Combine(_root, "granted");
        var config = Configure(granted);

        var resolved = AgentOrchestrator.ResolveWorkingDirectory(config, new PathGuard(config.Permissions));

        Assert.Equal(Path.GetFullPath(granted), resolved);
    }

    /// <summary>
    /// A grant the guard will refuse must be stepped over. Otherwise every bare filename the
    /// model writes resolves into a folder that rejects it, and the refusal blames the file
    /// rather than the configuration — which is exactly how this went unnoticed.
    /// </summary>
    [Fact]
    public void A_granted_but_unreachable_folder_is_skipped_for_one_that_works()
    {
        var granted = Path.Combine(_root, "granted");

        // AutoWork's own state directory is protected unconditionally, however it was granted.
        var config = Configure(AppPaths.Root, granted);

        var resolved = AgentOrchestrator.ResolveWorkingDirectory(config, new PathGuard(config.Permissions));

        Assert.Equal(Path.GetFullPath(granted), resolved);
    }

    [Fact]
    public void With_no_usable_grant_at_all_it_falls_back_to_the_default_workspace()
    {
        var config = Configure(AppPaths.Root);

        var resolved = AgentOrchestrator.ResolveWorkingDirectory(config, new PathGuard(config.Permissions));

        Assert.Equal(AppPaths.WorkspaceDirectory, resolved);
    }

    private static AutoWorkConfig Configure(params string[] writableRoots) => new()
    {
        Permissions = new PermissionPolicy
        {
            Roots = writableRoots
                .Select(p => new PermissionRoot { Path = p, Access = FolderAccess.ReadWrite, IncludeSubfolders = true })
                .ToList(),
        },
    };
}

public sealed class TokenEstimatorTests
{
    [Fact]
    public void Estimates_scale_with_length_and_never_under_report_badly()
    {
        var shortText = TokenEstimator.Estimate("hello world");
        var longText = TokenEstimator.Estimate(new string('a', 4000));

        Assert.True(shortText > 0);
        Assert.True(longText > shortText);

        // 4,000 characters is ~1,000 real tokens; the estimator should be at or above that.
        Assert.True(longText >= 1000, $"expected a conservative estimate, got {longText}");
    }

    [Fact]
    public void Tool_schemas_are_counted_because_they_ride_on_every_request()
    {
        var tool = AIFunctionFactory.Create(
            (string path) => "ok", "files_list", "List files in a directory.");

        Assert.True(TokenEstimator.EstimateTools([tool]) > 0);
    }

    [Fact]
    public void An_image_costs_far_more_than_its_characters_suggest()
    {
        var withImage = new ChatMessage(ChatRole.User, [new DataContent(new byte[16], "image/png")]);

        Assert.True(TokenEstimator.Estimate(withImage) > 1000);
    }
}
