using System.ClientModel.Primitives;

namespace AutoWork.Providers;

/// <summary>
/// Adds fixed headers to every request. Gateways such as OpenRouter want attribution headers,
/// and some corporate proxies want a tenant id, so model profiles can carry arbitrary ones.
/// </summary>
internal sealed class HeaderPolicy : PipelinePolicy
{
    private readonly KeyValuePair<string, string>[] _headers;

    public HeaderPolicy(IDictionary<string, string> headers) => _headers = headers.ToArray();

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    private void Apply(PipelineMessage message)
    {
        foreach (var (name, value) in _headers)
            message.Request.Headers.Set(name, value);
    }
}
