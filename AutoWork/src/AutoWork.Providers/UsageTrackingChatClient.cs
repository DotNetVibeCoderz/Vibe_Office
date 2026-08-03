using AutoWork.Core.Configuration;
using Microsoft.Extensions.AI;

namespace AutoWork.Providers;

/// <summary>A snapshot of what a run has spent so far.</summary>
public sealed record RunUsage
{
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public int Calls { get; init; }

    /// <summary>
    /// Calls the provider answered without saying what they used. The totals above are then a
    /// floor rather than a figure, and the UI says so instead of rounding the difference away.
    /// </summary>
    public int CallsWithoutUsage { get; init; }

    public decimal? Cost { get; init; }
    public string Currency { get; init; } = "USD";

    public long TotalTokens => InputTokens + OutputTokens;
    public bool IsComplete => CallsWithoutUsage == 0;
    public bool HasAnything => Calls > 0;
}

/// <summary>
/// Accumulates token usage for one run.
///
/// Per run rather than per client, because chat clients are cached and shared across runs — the
/// question "what did this job cost" is about the run, not about the connection.
/// </summary>
public sealed class RunMeter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (long Input, long Output)> _byModel = new(StringComparer.Ordinal);

    /// <summary>
    /// The profile behind each id, kept so cost can be worked out per model. Captured from the
    /// calls themselves rather than from a separate registration step — a meter whose prices
    /// depend on somebody having remembered to register first is a meter that silently reports
    /// no cost.
    /// </summary>
    private readonly Dictionary<string, ModelProfile> _models = new(StringComparer.Ordinal);

    private long _input;
    private long _output;
    private int _calls;
    private int _withoutUsage;

    /// <summary>Raised after every recorded call, so a meter in the UI can follow along.</summary>
    public event Action<RunUsage>? Changed;

    public void Record(ModelProfile model, UsageDetails? usage)
    {
        RunUsage snapshot;

        lock (_gate)
        {
            _models[model.Id] = model;
            _calls++;

            var input = usage?.InputTokenCount ?? 0;
            var output = usage?.OutputTokenCount ?? 0;

            // A provider that reports nothing is common enough — local servers especially — and
            // must not be silently counted as a free call.
            if (usage is null || (input == 0 && output == 0)) _withoutUsage++;

            _input += input;
            _output += output;

            var existing = _byModel.GetValueOrDefault(model.Id);
            _byModel[model.Id] = (existing.Input + input, existing.Output + output);

            snapshot = BuildLocked();
        }

        Changed?.Invoke(snapshot);
    }

    public RunUsage Snapshot()
    {
        lock (_gate) return BuildLocked();
    }

    /// <summary>
    /// Cost is summed per model, not from the run totals: a run that plans with one model and
    /// executes with another has two prices, and one blended figure would be wrong for both.
    /// </summary>
    private RunUsage BuildLocked()
    {
        decimal? cost = null;
        var currency = "USD";

        foreach (var (id, tokens) in _byModel)
        {
            if (!_models.TryGetValue(id, out var model)) continue;

            var part = model.CostOf(tokens.Input, tokens.Output);
            if (part is null) continue;

            cost = (cost ?? 0m) + part.Value;
            currency = model.Currency;
        }

        return new RunUsage
        {
            InputTokens = _input,
            OutputTokens = _output,
            Calls = _calls,
            CallsWithoutUsage = _withoutUsage,
            Cost = cost,
            Currency = currency,
        };
    }

}

/// <summary>
/// Reports what each request actually used into a <see cref="RunMeter"/>.
///
/// Sits outside the function-invocation loop deliberately: one agent step can issue several
/// provider round trips as tools are called and their results fed back, and every one of those
/// is billed. Counting only the outermost call would report a fraction of the real total.
/// </summary>
public sealed class UsageTrackingChatClient : DelegatingChatClient
{
    private readonly RunMeter _meter;
    private readonly ModelProfile _model;

    public UsageTrackingChatClient(IChatClient inner, RunMeter meter, ModelProfile model)
        : base(inner)
    {
        _meter = meter;
        _model = model;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        _meter.Record(_model, response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageDetails? usage = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
                if (content is UsageContent usageContent)
                    usage = usageContent.Details;

            yield return update;
        }

        _meter.Record(_model, usage);
    }

    /// <summary>
    /// The wrapped client is owned by the factory's cache and shared with other runs, so this
    /// per-run wrapper must not take it down with it.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
    }
}
