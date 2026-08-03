using System.Runtime.CompilerServices;
using AutoWork.Agents;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Providers;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// Streaming, driven through the real orchestrator with a scripted transport.
///
/// The two things worth proving are that the reply is visible while it is being written, and
/// that turning streaming on has not quietly changed what the step ends up with — the loop still
/// needs one whole reply and a set of messages to append.
/// </summary>
public sealed class StreamingTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "autowork-stream", Guid.NewGuid().ToString("n")[..8]);

    private readonly string _workspace;

    public StreamingTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private const string OneStepPlan =
        """
        {"notes":"One step.","successCriteria":[],
         "steps":[{"index":1,"title":"Think","intent":"Explain the approach","organ":"Brain","dependsOn":[],"successCriteria":[]}]}
        """;

    private (AgentOrchestrator Orchestrator, ScriptedStreamingClient Client) Build(
        bool streaming, bool streamingThrows = false)
    {
        var client = new ScriptedStreamingClient(OneStepPlan) { StreamingThrows = streamingThrows };

        var secrets = new FileSecretStore(Path.Combine(_root, "secrets.json"));
        var factory = new ModelClientFactory(secrets) { Transport = _ => client };

        var config = new ConfigStore(Path.Combine(_root, "config.json"));
        config.Save(new AutoWorkConfig
        {
            KeepRunHistory = false,
            Models =
            [
                new ModelProfile
                {
                    Id = "test", DisplayName = "Scripted", Kind = ProviderKind.OpenAICompatible,
                    Endpoint = "https://example.invalid/v1", ModelId = "scripted", ContextWindow = 100_000,
                },
            ],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowNetwork = false,
            },
            Agent = new AgentOptions
            {
                PlannerModelId = "test",
                ExecutorModelId = "test",
                MaxSteps = 3,
                EnableSubAgents = false,
                EnableSelfVerification = false,
                EnableAutoCompact = false,
                EnableStreaming = streaming,
            },
        });

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_root, "knowledge"));

        return (new AgentOrchestrator(
            config, factory, new ToolRegistry(factory, knowledge),
            NullActionLog.Instance, new AutoApproveBroker(), knowledge, secrets), client);
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public async Task The_reply_arrives_in_pieces_while_the_step_is_still_running()
    {
        var (orchestrator, _) = Build(streaming: true);

        var events = new List<RunEvent>();

        var result = await orchestrator.RunAsync("explain something", new InlineProgress<RunEvent>(e =>
        {
            lock (events) events.Add(e);
        }), TestContext.Current.CancellationToken);

        Assert.NotEqual(RunStatus.Failed, result.Status);

        RunEvent[] seen;
        lock (events) seen = [.. events];

        var deltas = seen.OfType<AssistantDeltaEvent>().ToArray();

        Assert.True(deltas.Length > 1, $"only {deltas.Length} fragment(s) arrived, so nothing was really streamed");

        // Each delta carries the running total, so the last one is the whole reply. Compared
        // trimmed: fragments arrive with whatever spacing the provider sent, and only the closing
        // message is tidied.
        Assert.Equal(ScriptedStreamingClient.StepReply, deltas[^1].Text.Trim());

        // And within a step the totals only ever grow, so a consumer can assign rather than
        // append. Each step is a separate reply and a separate row, hence the step index.
        Assert.All(deltas.GroupBy(d => d.StepIndex), step =>
        {
            var inStep = step.ToArray();

            for (var i = 1; i < inStep.Length; i++)
                Assert.StartsWith(inStep[i - 1].Text, inStep[i].Text, StringComparison.Ordinal);
        });

        // The closing message still arrives, so anything ignoring deltas is unaffected.
        Assert.Contains(seen.OfType<AssistantMessageEvent>(), m => m.Text == ScriptedStreamingClient.StepReply);
    }

    [Fact]
    public async Task Turning_streaming_off_produces_the_same_answer_without_the_fragments()
    {
        var (orchestrator, client) = Build(streaming: false);

        var events = new List<RunEvent>();

        var result = await orchestrator.RunAsync("explain something", new InlineProgress<RunEvent>(e =>
        {
            lock (events) events.Add(e);
        }), TestContext.Current.CancellationToken);

        Assert.NotEqual(RunStatus.Failed, result.Status);

        RunEvent[] seen;
        lock (events) seen = [.. events];

        Assert.Empty(seen.OfType<AssistantDeltaEvent>());
        Assert.Contains(seen.OfType<AssistantMessageEvent>(), m => m.Text == ScriptedStreamingClient.StepReply);

        Assert.Equal(0, client.StreamingCalls);
    }

    /// <summary>
    /// Some gateways advertise streaming and then reject it. A run must not die for that.
    /// </summary>
    [Fact]
    public async Task A_provider_that_cannot_stream_falls_back_to_waiting_rather_than_failing()
    {
        var (orchestrator, client) = Build(streaming: true, streamingThrows: true);

        var events = new List<RunEvent>();

        var result = await orchestrator.RunAsync("explain something", new InlineProgress<RunEvent>(e =>
        {
            lock (events) events.Add(e);
        }), TestContext.Current.CancellationToken);

        Assert.NotEqual(RunStatus.Failed, result.Status);

        RunEvent[] seen;
        lock (events) seen = [.. events];

        Assert.Empty(seen.OfType<AssistantDeltaEvent>());
        Assert.Contains(seen.OfType<AssistantMessageEvent>(), m => m.Text == ScriptedStreamingClient.StepReply);

        // It tried to stream, was refused, and asked again without it.
        Assert.True(client.StreamingCalls > 0);
        Assert.True(client.BlockingCalls > 0);
    }

    /// <summary>Streams the reply one word at a time, and can be told to refuse streaming.</summary>
    private sealed class ScriptedStreamingClient : IChatClient
    {
        public const string StepReply = "Here is the approach, explained at some length so it arrives in pieces.";

        private readonly string _plan;
        private readonly Lock _gate = new();
        private int _calls;

        public ScriptedStreamingClient(string plan) => _plan = plan;

        public bool StreamingThrows { get; init; }

        public int StreamingCalls { get; private set; }
        public int BlockingCalls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int call;
            lock (_gate)
            {
                BlockingCalls++;
                call = ++_calls;
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, call == 1 ? _plan : StepReply)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int call;
            lock (_gate)
            {
                StreamingCalls++;
                call = ++_calls;
            }

            if (StreamingThrows) throw new NotSupportedException("this endpoint does not support streaming");

            var text = call == 1 ? _plan : StepReply;

            foreach (var word in text.Split(' '))
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
