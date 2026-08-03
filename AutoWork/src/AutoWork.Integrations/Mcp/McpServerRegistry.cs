using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace AutoWork.Integrations.Mcp;

/// <summary>What happened when a server was contacted. Shown in the MCP gallery.</summary>
public sealed record McpConnectionResult(string ServerId, bool Success, string Message, int ToolCount)
{
    public static McpConnectionResult Failed(string serverId, string message) => new(serverId, false, message, 0);
}

/// <summary>
/// Connects to the MCP servers the user has enabled and hands their tools to the agent.
///
/// MCP tools arrive as <c>AIFunction</c>s already, so the whole integration is: start the server,
/// ask what it offers, and wrap each one in a <c>ToolDescriptor</c> so the Work Tape can attribute
/// it like any other tool.
///
/// Connecting is async and starting a process is slow, so it happens once before a run rather than
/// inside <c>ToolRegistry.Build</c>. Clients are kept for the life of the app: a stdio server is a
/// child process, and starting one per run would add seconds to every job.
///
/// **This is the most powerful thing AutoWork can be asked to do.** A stdio server is an arbitrary
/// program run with the user's rights — `PathGuard` cannot see inside it, and its tools can do
/// whatever that program can. So it is gated twice: the capability switch in Permissions, and each
/// server's own enabled flag. Neither is on by default.
/// </summary>
public sealed class McpServerRegistry : IAsyncDisposable
{
    private readonly ConfigStore _config;
    private readonly ISecretStore _secrets;

    private readonly Dictionary<string, McpClient> _clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<McpClientTool>> _tools = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public McpServerRegistry(ConfigStore config, ISecretStore secrets)
    {
        _config = config;
        _secrets = secrets;
    }

    /// <summary>Servers currently connected, by settings id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<McpClientTool>> ConnectedTools
    {
        get { lock (_tools) return _tools.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal); }
    }

    /// <summary>
    /// Brings connections in line with the configuration: starts servers newly enabled, drops
    /// ones disabled or removed. Safe to call before every run.
    /// </summary>
    public async Task<IReadOnlyList<McpConnectionResult>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var config = _config.Current;
        var results = new List<McpConnectionResult>();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wanted = config.Permissions.AllowMcpServers
                ? config.McpServers.Where(s => s.Enabled).ToArray()
                : [];

            foreach (var id in _clients.Keys.Where(id => wanted.All(s => s.Id != id)).ToArray())
                await DisconnectAsync(id).ConfigureAwait(false);

            foreach (var server in wanted)
            {
                if (_clients.ContainsKey(server.Id)) continue;
                results.Add(await ConnectAsync(server, cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            _gate.Release();
        }

        return results;
    }

    /// <summary>
    /// Connects one server and reports what it offers, without changing what the agent sees.
    /// This is what the gallery's "Test" button calls.
    /// </summary>
    public async Task<McpConnectionResult> ProbeAsync(
        McpServerSettings server, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = await CreateClientAsync(server, cancellationToken).ConfigureAwait(false);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            var name = client.ServerInfo?.Name ?? server.Name;
            return new McpConnectionResult(server.Id, true,
                $"{name} answered with {tools.Count} tool{(tools.Count == 1 ? "" : "s")}.", tools.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return McpConnectionResult.Failed(server.Id, Explain(ex, server));
        }
    }

    private async Task<McpConnectionResult> ConnectAsync(McpServerSettings server, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateClientAsync(server, cancellationToken).ConfigureAwait(false);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            _clients[server.Id] = client;
            lock (_tools) _tools[server.Id] = tools.ToArray();

            return new McpConnectionResult(server.Id, true, $"{tools.Count} tools", tools.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // One unreachable server must not stop a run. The agent simply does not get its tools.
            return McpConnectionResult.Failed(server.Id, Explain(ex, server));
        }
    }

    private async Task<McpClient> CreateClientAsync(McpServerSettings server, CancellationToken cancellationToken)
    {
        IClientTransport transport = server.Transport switch
        {
            McpTransport.Http => new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = server.Name,
                Endpoint = new Uri(server.Url),

                // Headers must be present to be sent; an unresolved reference is dropped rather
                // than sent as the literal "env:TOKEN".
                AdditionalHeaders = Resolve(server.Headers)
                    .Where(kv => kv.Value is not null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.OrdinalIgnoreCase),
            }),

            _ => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = string.IsNullOrWhiteSpace(server.Name) ? server.Id : server.Name,
                Command = server.Command,
                Arguments = [.. server.Arguments],
                EnvironmentVariables = Resolve(server.Environment),
            }),
        };

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves "env:VAR" and secret-store names the same way model keys are resolved, so an MCP
    /// server's API key never has to sit in config.json either.
    /// </summary>
    private Dictionary<string, string?> Resolve(Dictionary<string, string> values)
    {
        var resolved = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            resolved[key] = value.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
                            || _secrets.Names.Contains(value)
                ? _secrets.Resolve(value)
                : value;
        }

        return resolved;
    }

    private async Task DisconnectAsync(string id)
    {
        if (_clients.Remove(id, out var client))
        {
            try { await client.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
        }

        lock (_tools) _tools.Remove(id);
    }

    private static string Explain(Exception ex, McpServerSettings server) => ex switch
    {
        System.ComponentModel.Win32Exception =>
            $"\"{server.Command}\" could not be started. Is it installed and on PATH?",
        UriFormatException => $"\"{server.Url}\" is not a valid URL.",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };

    public IToolProvider AsToolProvider() => new McpToolProvider(this);

    private sealed class McpToolProvider : IToolProvider
    {
        private readonly McpServerRegistry _registry;

        public McpToolProvider(McpServerRegistry registry) => _registry = registry;

        public string Name => "MCP";

        public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
        {
            // Re-checked here as well as at connect time: the policy can change between a run
            // being prepared and a tool being offered, and the switch has to be the last word.
            if (!context.Guard.Policy.AllowMcpServers) yield break;

            foreach (var (_, tools) in _registry.ConnectedTools)
            {
                foreach (var tool in tools)
                {
                    yield return new ToolDescriptor
                    {
                        Function = tool,
                        Organ = AgentOrgan.Hands,

                        // An MCP tool's effects are opaque to us — it may well write or delete.
                        // Calling it Safe would be a claim we cannot support.
                        Risk = ToolRisk.Write,
                        Category = "MCP",
                    };
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _clients.Keys.ToArray()) await DisconnectAsync(id).ConfigureAwait(false);
        _gate.Dispose();
    }
}
