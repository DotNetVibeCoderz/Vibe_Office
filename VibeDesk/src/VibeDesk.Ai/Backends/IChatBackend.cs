using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using VibeDesk.Application.Assistant;

namespace VibeDesk.Ai.Backends;

/// <summary>Everything a single generation needs that is not the conversation itself.</summary>
public sealed record ChatRunSettings(
    string Model,
    double Temperature,
    int MaxTokens,
    int MaxToolIterations,
    int MaxToolResultChars);

/// <summary>
/// A provider that can turn a <see cref="ChatHistory"/> plus the kernel's plugins into a stream of
/// reply chunks.
/// </summary>
/// <remarks>
/// Semantic Kernel already abstracts chat completion, but its automatic function invocation is
/// implemented per connector and there is no Anthropic connector at all. Rather than pretend one
/// abstraction fits both, the two backends share the same <see cref="Kernel"/> — so the plugins the
/// model can call are literally the same <see cref="KernelFunction"/> objects either way — and differ
/// only in how they drive the tool loop.
/// </remarks>
public interface IChatBackend
{
    IAsyncEnumerable<ClippyChunk> StreamAsync(
        Kernel kernel,
        ChatHistory history,
        ChatRunSettings settings,
        CancellationToken ct = default);
}
