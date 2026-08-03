using AutoWork.Core.Configuration;
using AutoWork.Core.Security;
using AutoWork.Integrations;

namespace AutoWork.Tests;

/// <summary>
/// The connectors against their vendors' real APIs, using a credential that is deliberately wrong.
///
/// A successful call needs an account per vendor, which this machine has none of. But the two
/// things most likely to be silently stale can be checked without one: whether the endpoint each
/// connector points at still exists, and whether a rejected credential produces the readable
/// message the UI promises rather than a raw stack trace.
///
/// That is the failure a user meets first — a typo in a token — and until now nothing had ever
/// exercised it against a live host. A 404 here would mean the vendor moved the endpoint and the
/// connector has been broken for everyone, which is exactly the rot this catches.
/// </summary>
public sealed class LiveIntegrationEndpointTests
{
    private static void SkipUnlessLive() =>
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOWORK_LIVE_API_KEY")),
            "Reaches vendor APIs; runs with the live suite. Skipped.");

    /// <summary>
    /// Every connector, given a syntactically plausible but invalid credential.
    ///
    /// The assertion is deliberately about what the *user* sees: connection refused, and a
    /// sentence that names the problem. Not an exception, not an empty string, and not a
    /// cheerful success.
    /// </summary>
    [Theory]
    [InlineData("github")]
    [InlineData("notion")]
    [InlineData("asana")]
    [InlineData("google")]
    public async Task A_connector_reaches_its_vendor_and_reports_a_bad_credential_readably(string id)
    {
        SkipUnlessLive();

        // Field names are the connector's own — "token", "refreshToken", "clientId". Getting one
        // wrong makes the connector refuse locally without ever calling out, which looks like a
        // pass and proves nothing; the first version of this test did exactly that for two of
        // the five, so the credential set is now taken from the connector's declared fields.
        var status = await TestAsync(id, Fields(id));

        TestContext.Current.TestOutputHelper?.WriteLine($"{id}: connected={status.Connected} :: {status.Message}");

        Assert.False(status.Connected, $"{id} reported success with an invalid credential: {status.Message}");

        // "Rejected" is the mapping for 401 specifically, so this asserts more than "it failed":
        // the endpoint exists and answered. A moved endpoint would map to "Not found", a wrong
        // host to a socket error, and either would fail here.
        Assert.Contains("credentials were rejected", status.Message, StringComparison.OrdinalIgnoreCase);

        // A stack trace leaking into the UI is the specific thing the mapping exists to prevent.
        Assert.DoesNotContain("Exception", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", status.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// PayPal is separate because its "test" is an OAuth token exchange rather than a GET, so it
    /// exercises a different path — form POST and token parsing — with the same question asked.
    /// </summary>
    [Fact]
    public async Task Paypal_reaches_its_token_endpoint_and_refuses_bad_client_credentials()
    {
        SkipUnlessLive();

        var status = await TestAsync("paypal", Fields("paypal"));

        TestContext.Current.TestOutputHelper?.WriteLine($"paypal: connected={status.Connected} :: {status.Message}");

        Assert.False(status.Connected);
        Assert.Contains("credentials were rejected", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", status.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A connector with nothing configured must say so locally rather than making a request —
    /// otherwise every unconfigured integration costs a round trip on every status refresh.
    /// </summary>
    [Theory]
    [InlineData("github")]
    [InlineData("notion")]
    [InlineData("asana")]
    [InlineData("paypal")]
    [InlineData("google")]
    public async Task A_connector_with_no_credential_at_all_fails_without_calling_out(string id)
    {
        var status = await TestAsync(id, []);

        Assert.False(status.Connected);
        Assert.False(string.IsNullOrWhiteSpace(status.Message));
    }

    /// <summary>Every field the connector declares, filled with something plausible but wrong.</summary>
    private static Dictionary<string, string> Fields(string id)
    {
        var path = Path.Combine(Path.GetTempPath(), $"autowork-fields-{Guid.NewGuid():n}.json");

        try
        {
            var connector = new IntegrationRegistry(new ConfigStore(path), new InlineSecrets([]))
                .All.Single(c => c.Id == id);

            return connector.Fields.ToDictionary(
                f => f.Key,
                f => f.Key.Contains("environment", StringComparison.OrdinalIgnoreCase)
                    ? "sandbox"
                    : $"autowork-live-test-not-a-real-{f.Key}",
                StringComparer.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<IntegrationStatus> TestAsync(string id, Dictionary<string, string> secrets)
    {
        var path = Path.Combine(Path.GetTempPath(), $"autowork-integrations-{Guid.NewGuid():n}.json");

        try
        {
            var store = new ConfigStore(path);
            var inline = new InlineSecrets(secrets);

            store.Update(config =>
            {
                var settings = config.GetIntegration(id);
                settings.Enabled = true;

                foreach (var (key, _) in secrets) settings.SecretRefs[key] = key;
            });

            var registry = new IntegrationRegistry(store, inline);
            var connector = registry.All.Single(c => c.Id == id);

            return await connector
                .TestAsync(registry.CredentialsFor(id), TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Holds the throwaway values for one check; nothing reaches the real secret store.</summary>
    private sealed class InlineSecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _values;

        public InlineSecrets(Dictionary<string, string> values) => _values = values;

        public string? Resolve(string? reference) =>
            string.IsNullOrWhiteSpace(reference) ? null : _values.GetValueOrDefault(reference);

        public string? Get(string name) => _values.GetValueOrDefault(name);
        public void Set(string name, string value) => _values[name] = value;
        public void Delete(string name) => _values.Remove(name);
        public IReadOnlyCollection<string> Names => _values.Keys;
    }
}
