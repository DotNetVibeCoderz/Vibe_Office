using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Security;

namespace AutoWork.Integrations;

/// <summary>
/// Holds the connectors and turns the enabled, configured ones into agent tools.
///
/// Registration is static because each connector is a small amount of hand-written code rather
/// than a plugin; making this a plugin host would mean loading third-party assemblies into a
/// process that holds the user's API keys, which is not a trade worth making here.
/// </summary>
public sealed class IntegrationRegistry
{
    private readonly IReadOnlyList<IIntegration> _integrations;
    private readonly ConfigStore _config;
    private readonly ISecretStore _secrets;

    public IntegrationRegistry(ConfigStore config, ISecretStore secrets)
    {
        _config = config;
        _secrets = secrets;

        _integrations =
        [
            new GitHubIntegration(),
            new GoogleIntegration(),
            new NotionIntegration(),
            new AsanaIntegration(),
            new PayPalIntegration(),
        ];
    }

    public IReadOnlyList<IIntegration> All => _integrations;

    public IIntegration? Find(string id) =>
        _integrations.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    public IntegrationCredentials CredentialsFor(string id) =>
        new(_config.Current.GetIntegration(id), _secrets);

    public Task<IntegrationStatus> TestAsync(string id, CancellationToken cancellationToken = default)
    {
        var integration = Find(id);
        return integration is null
            ? Task.FromResult(new IntegrationStatus(false, $"No integration named \"{id}\"."))
            : integration.TestAsync(CredentialsFor(id), cancellationToken);
    }

    /// <summary>A tool provider the agent's <c>ToolRegistry</c> can consume like any other.</summary>
    public IToolProvider AsToolProvider() => new IntegrationToolProvider(this);

    private sealed class IntegrationToolProvider : IToolProvider
    {
        private readonly IntegrationRegistry _registry;

        public IntegrationToolProvider(IntegrationRegistry registry) => _registry = registry;

        public string Name => "Integrations";

        public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
        {
            // Network off means no connector can do anything useful, so none are offered.
            if (!context.Guard.Policy.AllowNetwork) yield break;

            foreach (var integration in _registry.All)
            {
                var credentials = _registry.CredentialsFor(integration.Id);
                if (!credentials.Enabled) continue;

                ToolDescriptor[] tools;
                try
                {
                    tools = integration.GetTools(context, credentials).ToArray();
                }
                catch (Exception)
                {
                    // A misconfigured connector must not stop the others from loading.
                    continue;
                }

                foreach (var tool in tools) yield return tool;
            }
        }
    }
}
