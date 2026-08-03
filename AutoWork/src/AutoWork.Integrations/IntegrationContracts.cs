using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Security;

namespace AutoWork.Integrations;

/// <summary>One setting a connector needs. Drives the form rendered in Settings › Integrations.</summary>
public sealed record IntegrationField(
    string Key,
    string Label,
    string Help,
    bool Secret = false,
    bool Required = true,
    string? Placeholder = null);

public sealed record IntegrationStatus(bool Connected, string Message, string? Account = null);

/// <summary>
/// Resolved credentials for one connector: non-secret values straight from config, secrets
/// pulled from the secret store or the environment at the moment of use.
/// </summary>
public sealed class IntegrationCredentials
{
    private readonly IntegrationSettings _settings;
    private readonly ISecretStore _secrets;

    public IntegrationCredentials(IntegrationSettings settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    public string Id => _settings.Id;
    public bool Enabled => _settings.Enabled;

    public string? Value(string key) =>
        _settings.Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public string? Secret(string key) =>
        _settings.SecretRefs.TryGetValue(key, out var reference) ? _secrets.Resolve(reference) : null;

    /// <summary>Secret first, then plain value — so a field can be filled either way.</summary>
    public string? Any(string key) => Secret(key) ?? Value(key);

    public string Require(string key) =>
        Any(key) ?? throw new InvalidOperationException(
            $"The \"{key}\" setting is missing. Fill it in under Settings › Integrations.");
}

/// <summary>
/// A connected service. Each one contributes tools to the agent when it is enabled and its
/// credentials resolve — a half-configured integration contributes nothing rather than
/// contributing tools that fail on every call.
/// </summary>
public interface IIntegration
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    string DocsUrl { get; }

    /// <summary>What the user must fill in.</summary>
    IReadOnlyList<IntegrationField> Fields { get; }

    /// <summary>Makes one cheap authenticated call so Settings can show a real status.</summary>
    Task<IntegrationStatus> TestAsync(IntegrationCredentials credentials, CancellationToken cancellationToken = default);

    IEnumerable<ToolDescriptor> GetTools(ToolContext context, IntegrationCredentials credentials);
}
