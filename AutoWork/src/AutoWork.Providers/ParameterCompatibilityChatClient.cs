using System.ClientModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace AutoWork.Providers;

/// <summary>
/// Drops request parameters the model refuses, then retries — instead of letting one rejected
/// field end the run.
///
/// Reasoning models are the reason this exists. OpenAI's gpt-5 family and the o-series reject
/// <c>temperature</c> at anything but its default and reject <c>max_tokens</c> outright, and
/// they do it with a 400 rather than by ignoring the field. AutoWork sets both on every request
/// — the planner, the verifier, the summariser and each step — so without this a perfectly
/// well-configured Azure or OpenAI reasoning model fails on its very first call.
///
/// The alternative was a per-model capability table. That was rejected: model ids drift faster
/// than releases (design principle 4), and a table would be wrong for the next model rather than
/// merely incomplete. Asking the provider and believing its answer stays correct by construction.
///
/// What is dropped is remembered for the lifetime of the client, so the cost is one wasted call
/// per parameter per profile, not one per request.
/// </summary>
public sealed class ParameterCompatibilityChatClient : DelegatingChatClient
{
    /// <summary>
    /// Parameters worth surrendering. Each is a preference, not a requirement — losing one
    /// changes the shape of a reply but never its correctness. Nothing that affects what the
    /// model is asked to *do* (tools, messages, response format) is ever stripped.
    /// </summary>
    private static readonly (string Wire, string Friendly, Action<ChatOptions> Strip)[] Droppable =
    [
        ("temperature", "temperature", o => o.Temperature = null),
        ("top_p", "top-p", o => o.TopP = null),
        ("max_tokens", "output token limit", o => o.MaxOutputTokens = null),
        ("max_completion_tokens", "output token limit", o => o.MaxOutputTokens = null),
        ("frequency_penalty", "frequency penalty", o => o.FrequencyPenalty = null),
        ("presence_penalty", "presence penalty", o => o.PresencePenalty = null),
    ];

    private static readonly Regex QuotedName = new("['\"`]([a-z_]+)['\"`]", RegexOptions.Compiled);

    /// <summary>
    /// Budget given to a model that ran out of room before it said anything.
    ///
    /// Reasoning models bill their private deliberation against the same output cap as the
    /// reply, and they spend it first. Ask gpt-5-mini for one word with a cap of 16 and it
    /// returns HTTP 200, an empty string, and a bill for 16 tokens — a success that delivered
    /// nothing. AutoWork's short calls (the connection test, the verifier, the summariser) were
    /// all sized for models that do not do this.
    /// </summary>
    private const int ReasoningHeadroom = 4_000;

    private readonly HashSet<string> _dropped = new(StringComparer.OrdinalIgnoreCase);
    private int _floor;
    private readonly Lock _gate = new();

    public ParameterCompatibilityChatClient(IChatClient inner) : base(inner) { }

    /// <summary>
    /// Human-readable notes on what this model would not accept, for Settings to show after a
    /// connection test. A silently ignored temperature setting is worth telling someone about.
    /// </summary>
    public IReadOnlyList<string> Adjustments
    {
        get
        {
            lock (_gate)
            {
                var notes = _dropped
                    .Select(wire => Droppable.First(d => d.Wire == wire).Friendly)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (_floor > 0) notes.Add("short output limits (it reasons before it answers)");

                return notes;
            }
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = Sanitise(options);

        // Bounded by the droppable list: each retry must have learned something new, so this
        // cannot spin on a 400 that has nothing to do with parameters.
        for (var round = 0; ; round++)
        {
            ChatResponse response;

            try
            {
                response = await base.GetResponseAsync(messages, attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (round < Droppable.Length && Learn(ex))
            {
                attempt = Sanitise(options);
                continue;
            }

            if (!RanOutOfRoomBeforeAnswering(response, attempt)) return response;

            lock (_gate) _floor = ReasoningHeadroom;
            attempt = Sanitise(options);
        }
    }

    /// <summary>
    /// True when the model stopped at its token limit having produced neither text nor a tool
    /// call. That is not a short answer — it is no answer, and retrying with room is the only
    /// thing that turns it into one.
    /// </summary>
    private bool RanOutOfRoomBeforeAnswering(ChatResponse response, ChatOptions? attempt)
    {
        if (response.FinishReason != ChatFinishReason.Length) return false;
        if (attempt?.MaxOutputTokens is not { } cap || cap >= ReasoningHeadroom) return false;

        lock (_gate)
            if (_floor >= ReasoningHeadroom) return false; // Already raised; the room was not the problem.

        var producedNothing = string.IsNullOrWhiteSpace(response.Text)
                              && !response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any();

        return producedNothing;
    }

    /// <summary>
    /// Applies what has been learned so far, but does not retry: a rejection surfaces only once
    /// the stream has begun, by which point re-issuing the request would replay tokens the caller
    /// has already seen. The non-streaming path teaches this client what to strip, and every
    /// AutoWork request goes through that path today.
    /// </summary>
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Sanitise(options), cancellationToken);

    /// <summary>Returns a copy with the already-rejected parameters removed.</summary>
    private ChatOptions? Sanitise(ChatOptions? options)
    {
        if (options is null) return null;

        lock (_gate)
        {
            var needsFloor = _floor > 0 && options.MaxOutputTokens is { } cap && cap < _floor;
            if (_dropped.Count == 0 && !needsFloor) return options;

            var copy = options.Clone();

            foreach (var (wire, _, strip) in Droppable)
                if (_dropped.Contains(wire))
                    strip(copy);

            // Only ever raises a cap, and only for a model that has shown it needs the room.
            if (needsFloor && copy.MaxOutputTokens is not null) copy.MaxOutputTokens = _floor;

            return copy;
        }
    }

    /// <summary>
    /// Reads a provider rejection for the name of a parameter worth dropping.
    /// Returns true only when something new was learned — which is what makes a retry worthwhile.
    /// </summary>
    private bool Learn(Exception exception)
    {
        if (!IsBadRequest(exception)) return false;

        var message = exception.Message;

        // The complaint must actually be about the parameter being unsupported. A 400 for a
        // malformed tool schema also quotes names, and retrying that forever helps nobody.
        if (message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) is false
            && message.Contains("not supported", StringComparison.OrdinalIgnoreCase) is false)
            return false;

        var named = QuotedName.Matches(message)
            .Select(m => m.Groups[1].Value)
            .Where(name => Droppable.Any(d => string.Equals(d.Wire, name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (named.Length == 0) return false;

        lock (_gate)
        {
            var learned = false;
            foreach (var name in named) learned |= _dropped.Add(name);
            return learned;
        }
    }

    private static bool IsBadRequest(Exception exception) => exception switch
    {
        ClientResultException client => client.Status == 400,
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.BadRequest } => true,
        _ => false,
    };
}
