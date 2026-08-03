using System.Runtime.CompilerServices;
using AutoWork.Core.Configuration;
using AutoWork.Providers;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// The meter's value depends entirely on it being trustworthy: a number that is quietly a
/// fraction of the truth, or a cost invented from a price nobody set, is worse than no meter.
/// </summary>
public sealed class UsageMeterTests
{
    private static ModelProfile Priced(decimal? input = 3m, decimal? output = 15m, string id = "m1") => new()
    {
        Id = id,
        DisplayName = "Test",
        ModelId = "test",
        InputPricePerMillion = input,
        OutputPricePerMillion = output,
        Currency = "USD",
    };

    [Fact]
    public void Usage_from_several_calls_adds_up()
    {
        var meter = new RunMeter();
        var model = Priced();

        meter.Record(model, new UsageDetails { InputTokenCount = 1_000, OutputTokenCount = 200 });
        meter.Record(model, new UsageDetails { InputTokenCount = 500, OutputTokenCount = 100 });

        var usage = meter.Snapshot();

        Assert.Equal(1_500, usage.InputTokens);
        Assert.Equal(300, usage.OutputTokens);
        Assert.Equal(1_800, usage.TotalTokens);
        Assert.Equal(2, usage.Calls);
        Assert.True(usage.IsComplete);
    }

    [Fact]
    public void Cost_follows_the_prices_the_user_entered()
    {
        var meter = new RunMeter();

        // 1M input at 3.00 and 1M output at 15.00.
        meter.Record(Priced(), new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 1_000_000 });

        Assert.Equal(18m, meter.Snapshot().Cost);
    }

    /// <summary>
    /// The whole reason no price table ships. An unpriced model must report tokens and stay
    /// silent about money rather than implying the run was free.
    /// </summary>
    [Fact]
    public void A_model_with_no_price_reports_tokens_and_no_cost()
    {
        var meter = new RunMeter();
        meter.Record(Priced(input: null, output: null), new UsageDetails { InputTokenCount = 900, OutputTokenCount = 100 });

        var usage = meter.Snapshot();

        Assert.Equal(1_000, usage.TotalTokens);
        Assert.Null(usage.Cost);
    }

    [Fact]
    public void Half_a_price_is_not_enough_to_quote_a_cost()
    {
        var meter = new RunMeter();
        meter.Record(Priced(input: 3m, output: null), new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 1_000_000 });

        Assert.Null(meter.Snapshot().Cost);
    }

    /// <summary>
    /// A run that plans with one model and executes with another has two prices. Blending them
    /// into one rate would be wrong for both, so the cost is summed per model.
    /// </summary>
    [Fact]
    public void Two_models_in_one_run_are_priced_separately()
    {
        var meter = new RunMeter();

        meter.Record(Priced(input: 1m, output: 1m, id: "cheap"), new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 0 });
        meter.Record(Priced(input: 100m, output: 100m, id: "dear"), new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 0 });

        Assert.Equal(101m, meter.Snapshot().Cost);
    }

    [Fact]
    public void A_provider_that_reports_nothing_makes_the_total_a_floor_rather_than_a_figure()
    {
        var meter = new RunMeter();
        var model = Priced();

        meter.Record(model, new UsageDetails { InputTokenCount = 800, OutputTokenCount = 200 });
        meter.Record(model, usage: null);

        var usage = meter.Snapshot();

        Assert.Equal(1_000, usage.TotalTokens);
        Assert.Equal(2, usage.Calls);
        Assert.Equal(1, usage.CallsWithoutUsage);
        Assert.False(usage.IsComplete);
    }

    [Fact]
    public void Nothing_recorded_means_nothing_to_report()
    {
        Assert.False(new RunMeter().Snapshot().HasAnything);
    }

    /// <summary>
    /// The wrapper sits outside the function-invocation loop, so it sees every provider round
    /// trip a step makes — not just the first. Counting only the outermost call would report a
    /// fraction of a tool-heavy run.
    /// </summary>
    [Fact]
    public async Task Every_request_through_the_client_is_counted()
    {
        var meter = new RunMeter();
        var inner = new CountingChatClient();

        var client = new UsageTrackingChatClient(inner, meter, Priced());

        for (var i = 0; i < 5; i++)
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
                cancellationToken: TestContext.Current.CancellationToken);

        var usage = meter.Snapshot();

        Assert.Equal(5, usage.Calls);
        Assert.Equal(5 * 70, usage.InputTokens);
        Assert.Equal(5 * 30, usage.OutputTokens);
    }

    [Fact]
    public void The_meter_notifies_as_it_goes_so_a_live_readout_is_possible()
    {
        var meter = new RunMeter();
        var seen = new List<long>();

        meter.Changed += u => seen.Add(u.TotalTokens);

        meter.Record(Priced(), new UsageDetails { InputTokenCount = 10, OutputTokenCount = 0 });
        meter.Record(Priced(), new UsageDetails { InputTokenCount = 15, OutputTokenCount = 0 });

        Assert.Equal([10L, 25L], seen);
    }

    /// <summary>
    /// The wrapper is created per run around a client the factory caches and shares. Disposing
    /// it must not take the shared client down with it.
    /// </summary>
    [Fact]
    public async Task Disposing_the_per_run_wrapper_leaves_the_shared_client_usable()
    {
        var inner = new CountingChatClient();
        var wrapper = new UsageTrackingChatClient(inner, new RunMeter(), Priced());

        wrapper.Dispose();

        Assert.False(inner.Disposed);

        var response = await inner.GetResponseAsync([new ChatMessage(ChatRole.User, "still working?")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response);
    }

    private sealed class CountingChatClient : IChatClient
    {
        public bool Disposed { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails { InputTokenCount = 70, OutputTokenCount = 30 },
            });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => Disposed = true;
    }
}
