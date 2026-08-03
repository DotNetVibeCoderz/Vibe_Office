using System.Net;
using AutoWork.Providers;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

public sealed class AzureEndpointTests
{
    /// <summary>
    /// What the Azure portal shows is the resource root, so that is what people paste. Left
    /// as-is it 404s with nothing to suggest the cause, which is a miserable first-run
    /// experience for a key that is perfectly valid.
    /// </summary>
    [Theory]
    [InlineData("https://freellm.openai.azure.com/", "https://freellm.openai.azure.com/openai/v1")]
    [InlineData("https://freellm.openai.azure.com", "https://freellm.openai.azure.com/openai/v1")]
    [InlineData("https://my-res.cognitiveservices.azure.com/", "https://my-res.cognitiveservices.azure.com/openai/v1")]
    public void An_azure_resource_url_is_completed_to_its_openai_compatible_base(string input, string expected) =>
        Assert.Equal(expected, ModelClientFactory.NormalizeAzureEndpoint(input));

    /// <summary>
    /// Someone who typed a full path meant it — including the older deployments form, which
    /// still works and which we must not rewrite out from under them.
    /// </summary>
    [Theory]
    [InlineData("https://freellm.openai.azure.com/openai/v1")]
    [InlineData("https://freellm.openai.azure.com/openai/deployments/gpt-4o")]
    public void An_azure_url_that_already_names_a_path_is_left_alone(string input) =>
        Assert.Equal(input, ModelClientFactory.NormalizeAzureEndpoint(input));

    [Theory]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("http://localhost:11434")]
    [InlineData("not a url")]
    public void Non_azure_endpoints_are_never_rewritten(string input) =>
        Assert.Equal(input, ModelClientFactory.NormalizeAzureEndpoint(input));
}

public sealed class ParameterCompatibilityTests
{
    [Fact]
    public async Task A_rejected_temperature_is_dropped_and_the_request_retried()
    {
        var inner = new FussyChatClient(rejects: ["temperature"]);
        var client = new ParameterCompatibilityChatClient(inner);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Temperature = 0 },
            TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, inner.Attempts);
        Assert.Null(inner.LastOptions?.Temperature);
    }

    [Fact]
    public async Task Several_rejected_parameters_are_learned_one_refusal_at_a_time()
    {
        var inner = new FussyChatClient(rejects: ["temperature", "max_tokens"]);
        var client = new ParameterCompatibilityChatClient(inner);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Temperature = 0, MaxOutputTokens = 100 },
            TestContext.Current.CancellationToken);

        Assert.Null(inner.LastOptions?.Temperature);
        Assert.Null(inner.LastOptions?.MaxOutputTokens);
        Assert.Equal(["temperature", "output token limit"], client.Adjustments);
    }

    /// <summary>
    /// The cost of learning must be paid once per client, not once per request — otherwise every
    /// step of a long run burns a wasted call.
    /// </summary>
    [Fact]
    public async Task What_was_learned_is_applied_to_later_requests_without_another_refusal()
    {
        var inner = new FussyChatClient(rejects: ["temperature"]);
        var client = new ParameterCompatibilityChatClient(inner);

        var options = new ChatOptions { Temperature = 0.2f };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "a")], options, TestContext.Current.CancellationToken);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "b")], options, TestContext.Current.CancellationToken);

        // Two calls for the first request (one refused, one retried) and one for the second.
        Assert.Equal(3, inner.Attempts);
    }

    /// <summary>
    /// The caller's own options object is shared across every step of a run. Mutating it would
    /// silently rewrite the user's configured temperature for the rest of the session.
    /// </summary>
    [Fact]
    public async Task The_callers_options_object_is_not_mutated()
    {
        var inner = new FussyChatClient(rejects: ["temperature"]);
        var client = new ParameterCompatibilityChatClient(inner);

        var options = new ChatOptions { Temperature = 0.2f };
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "a")], options, TestContext.Current.CancellationToken);

        Assert.Equal(0.2f, options.Temperature);
    }

    /// <summary>
    /// A 400 that is not about a droppable parameter — a malformed tool schema, say — must
    /// surface immediately rather than being retried into a loop.
    /// </summary>
    [Fact]
    public async Task An_unrelated_bad_request_is_surfaced_rather_than_retried()
    {
        var inner = new ThrowingChatClient(
            new HttpRequestException("Invalid schema for function 'files_list'.", null, HttpStatusCode.BadRequest));

        var client = new ParameterCompatibilityChatClient(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Temperature = 0 },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Attempts);
    }

    [Fact]
    public async Task A_server_error_is_not_mistaken_for_a_parameter_problem()
    {
        var inner = new ThrowingChatClient(
            new HttpRequestException("temperature is unsupported", null, HttpStatusCode.InternalServerError));

        var client = new ParameterCompatibilityChatClient(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")], new ChatOptions { Temperature = 0 },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Attempts);
    }

    /// <summary>
    /// The exact shape gpt-5-mini returns for a 16-token cap: HTTP 200, finish reason "length",
    /// empty content, and the whole budget spent on reasoning. Treating that as a valid reply
    /// would let an empty verification verdict pass for a real one.
    /// </summary>
    [Fact]
    public async Task A_reasoning_model_that_spent_its_whole_budget_thinking_is_retried_with_room()
    {
        var inner = new ReasoningChatClient(needs: 500);
        var client = new ParameterCompatibilityChatClient(inner);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Reply with the single word: ok")],
            new ChatOptions { MaxOutputTokens = 16 },
            TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, inner.Attempts);
        Assert.Contains(client.Adjustments, note => note.Contains("output limits"));
    }

    [Fact]
    public async Task The_raised_limit_is_remembered_so_later_short_calls_are_not_wasted()
    {
        var inner = new ReasoningChatClient(needs: 500);
        var client = new ParameterCompatibilityChatClient(inner);

        var options = new ChatOptions { MaxOutputTokens = 16 };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "a")], options, TestContext.Current.CancellationToken);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "b")], options, TestContext.Current.CancellationToken);

        Assert.Equal(3, inner.Attempts);
        Assert.Equal(16, options.MaxOutputTokens);
    }

    /// <summary>
    /// A genuinely truncated long answer is a real answer. Retrying it would double the cost of
    /// every capped request that had something to say.
    /// </summary>
    [Fact]
    public async Task A_truncated_reply_that_still_said_something_is_returned_as_is()
    {
        var inner = new ReasoningChatClient(needs: 500, partialText: "Once upon a ti");
        var client = new ParameterCompatibilityChatClient(inner);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "tell me a story")],
            new ChatOptions { MaxOutputTokens = 16 },
            TestContext.Current.CancellationToken);

        Assert.Equal("Once upon a ti", response.Text);
        Assert.Equal(1, inner.Attempts);
    }

    /// <summary>Refuses whichever parameters it was told to, in the wording real providers use.</summary>
    private sealed class FussyChatClient : IChatClient
    {
        private readonly string[] _rejects;

        public FussyChatClient(string[] rejects) => _rejects = rejects;

        public int Attempts { get; private set; }
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Attempts++;
            LastOptions = options;

            if (_rejects.Contains("temperature") && options?.Temperature is not null)
            {
                throw new HttpRequestException(
                    "Unsupported value: 'temperature' does not support 0 with this model. Only the default (1) value is supported.",
                    null, HttpStatusCode.BadRequest);
            }

            if (_rejects.Contains("max_tokens") && options?.MaxOutputTokens is not null)
            {
                throw new HttpRequestException(
                    "Unsupported parameter: 'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead.",
                    null, HttpStatusCode.BadRequest);
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>Burns the whole output cap on reasoning unless given at least <c>needs</c> tokens.</summary>
    private sealed class ReasoningChatClient : IChatClient
    {
        private readonly int _needs;
        private readonly string? _partialText;

        public ReasoningChatClient(int needs, string? partialText = null)
        {
            _needs = needs;
            _partialText = partialText;
        }

        public int Attempts { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Attempts++;

            if (options?.MaxOutputTokens is { } cap && cap < _needs)
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _partialText ?? ""))
                {
                    FinishReason = ChatFinishReason.Length,
                });
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                FinishReason = ChatFinishReason.Stop,
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        private readonly Exception _exception;

        public ThrowingChatClient(Exception exception) => _exception = exception;

        public int Attempts { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw _exception;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
