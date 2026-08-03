using System.Collections.ObjectModel;
using AutoWork.Core.Configuration;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>A parameter the user fills in before a catalogue entry can be added.</summary>
public sealed partial class McpParameterViewModel : ObservableObject
{
    public required McpParameter Parameter { get; init; }

    public string Label => Parameter.Label;
    public string Placeholder => Parameter.Placeholder;
    public bool IsSecret => Parameter.IsSecret;

    [ObservableProperty] private string _value = "";
}

/// <summary>A catalogue entry, with whatever it needs before it can be added.</summary>
public sealed partial class McpCatalogViewModel : ObservableObject
{
    public required McpCatalogEntry Entry { get; init; }

    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Category => Entry.Category;
    public string Requires => Entry.Requires;
    public string HomeUrl => Entry.HomeUrl;

    /// <summary>
    /// "Figma's own server" and "someone's Figma server" are very different things to hand your
    /// account to, so the gallery says which this is rather than leaving it to be assumed.
    /// </summary>
    public bool Official => Entry.Official;

    public bool Community => !Entry.Official;

    public string Setup => Entry.Setup;
    public bool HasSetup => !string.IsNullOrWhiteSpace(Entry.Setup);

    public ObservableCollection<McpParameterViewModel> Parameters { get; } = [];

    public bool HasParameters => Parameters.Count > 0;

    [ObservableProperty] private bool _added;
}

/// <summary>A server the user has configured, and what testing it said.</summary>
public sealed partial class McpServerViewModel : ObservableObject
{
    public required McpServerSettings Settings { get; init; }

    public string Name => Settings.Name;
    public string Description => Settings.Description;

    /// <summary>The command line, so what will actually be launched is never a mystery.</summary>
    public string CommandLine => Settings.Transport == McpTransport.Http
        ? Settings.Url
        : $"{Settings.Command} {string.Join(" ", Settings.Arguments)}".Trim();

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _testPassed;
    [ObservableProperty] private bool _busy;

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    partial void OnStatusChanged(string? value) => OnPropertyChanged(nameof(HasStatus));
}

/// <summary>
/// The MCP gallery: pick a server from the catalogue, fill in what it needs, test it, enable it.
///
/// Adding and enabling are separate steps on purpose. Adding writes a command line into the
/// config; enabling is what lets AutoWork start that program. Collapsing the two would mean a
/// click in a gallery silently launches a process — which is exactly the affordance this app
/// avoids everywhere else.
/// </summary>
public sealed partial class McpViewModel : ObservableObject
{
    private readonly AppServices _services;

    public McpViewModel(AppServices services)
    {
        _services = services;

        foreach (var entry in McpCatalog.All)
        {
            var item = new McpCatalogViewModel { Entry = entry };
            foreach (var parameter in entry.Parameters)
                item.Parameters.Add(new McpParameterViewModel { Parameter = parameter });

            Catalog.Add(item);
        }

        ReloadServers();
        ApplyFilter();
    }

    public Strings L => _services.Strings;

    public ObservableCollection<McpCatalogViewModel> Catalog { get; } = [];
    public ObservableCollection<McpCatalogViewModel> Results { get; } = [];
    public ObservableCollection<McpServerViewModel> Servers { get; } = [];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _status;

    /// <summary>Mirrors the permission switch, so the gallery can say why nothing is running.</summary>
    public bool Allowed => _services.Config.Current.Permissions.AllowMcpServers;

    public bool HasServers => Servers.Count > 0;

    partial void OnSearchChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var needle = Search.Trim();

        Results.Clear();
        foreach (var item in Catalog)
        {
            if (needle.Length > 0
                && !item.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !item.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !item.Category.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;

            Results.Add(item);
        }
    }

    private void ReloadServers()
    {
        Servers.Clear();

        foreach (var settings in _services.Config.Current.McpServers)
        {
            var item = new McpServerViewModel { Settings = settings, Enabled = settings.Enabled };

            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(McpServerViewModel.Enabled)) return;

                item.Settings.Enabled = item.Enabled;
                Persist();
            };

            Servers.Add(item);
        }

        foreach (var entry in Catalog)
            entry.Added = Servers.Any(s => s.Settings.CatalogId == entry.Entry.Id);

        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(Allowed));
    }

    [RelayCommand]
    private void Add(McpCatalogViewModel? item)
    {
        if (item is null) return;

        var missing = item.Parameters.FirstOrDefault(p => p.Parameter.Required && p.Value.Trim().Length == 0);
        if (missing is not null)
        {
            Status = string.Format(L["mcp.needs"], missing.Label);
            return;
        }

        var values = item.Parameters.ToDictionary(p => p.Parameter.Key, p => p.Value.Trim(), StringComparer.Ordinal);
        var settings = McpCatalog.CreateSettings(item.Entry, values);

        // A key typed here goes to the secret store, exactly like a model key. config.json keeps
        // the reference so it stays safe to copy.
        foreach (var parameter in item.Parameters.Where(p => p.IsSecret && p.Value.Trim().Length > 0))
        {
            var name = $"mcp.{settings.Id}.{parameter.Parameter.Key}";
            _services.Secrets.Set(name, parameter.Value.Trim());
            settings.Environment[parameter.Parameter.Key] = name;
            parameter.Value = "";
        }

        _services.Config.Update(config => config.McpServers.Add(settings));

        ReloadServers();
        Status = string.Format(L["mcp.added"], settings.Name);
    }

    // ── Adding one by hand ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The form for a server the catalogue does not carry — which is most of them. The vendor's
    /// own server usually is the interesting one, and Unity and Unreal only exist this way.
    /// </summary>
    [ObservableProperty] private bool _manualOpen;

    [ObservableProperty] private string _manualName = "";
    [ObservableProperty] private int _manualTransport;
    [ObservableProperty] private string _manualCommand = "npx";
    [ObservableProperty] private string _manualArguments = "";
    [ObservableProperty] private string _manualUrl = "";
    [ObservableProperty] private string _manualEnvironment = "";

    public IReadOnlyList<string> ManualTransports => [L["mcp.manual.stdio"], L["mcp.manual.http"]];

    public bool ManualIsStdio => ManualTransport == (int)McpTransport.Stdio;
    public bool ManualIsHttp => ManualTransport == (int)McpTransport.Http;

    partial void OnManualTransportChanged(int value)
    {
        OnPropertyChanged(nameof(ManualIsStdio));
        OnPropertyChanged(nameof(ManualIsHttp));
    }

    /// <summary>Where to look for a server this catalogue does not carry.</summary>
    public IReadOnlyList<McpSource> Sources => McpCatalog.Sources;

    [RelayCommand]
    private void ToggleManual() => ManualOpen = !ManualOpen;

    [RelayCommand]
    private void OpenSource(McpSource? source)
    {
        if (source is not null) ActivityViewModel.OpenUrl(source.Url);
    }

    [RelayCommand]
    private void AddManual()
    {
        var transport = (McpTransport)ManualTransport;
        var name = ManualName.Trim();

        if (name.Length == 0)
        {
            Status = L["mcp.manual.needname"];
            return;
        }

        if (transport == McpTransport.Stdio && ManualCommand.Trim().Length == 0)
        {
            Status = L["mcp.manual.needcommand"];
            return;
        }

        if (transport == McpTransport.Http && !Uri.TryCreate(ManualUrl.Trim(), UriKind.Absolute, out _))
        {
            Status = L["mcp.manual.needurl"];
            return;
        }

        var settings = new McpServerSettings
        {
            Name = name,
            Description = L["mcp.manual.added.note"],
            Transport = transport,
            Command = transport == McpTransport.Stdio ? ManualCommand.Trim() : "",
            Arguments = transport == McpTransport.Stdio ? [.. McpManualEntry.SplitArguments(ManualArguments)] : [],
            Url = transport == McpTransport.Http ? ManualUrl.Trim() : "",
        };

        foreach (var (key, value) in McpManualEntry.ParseEnvironment(ManualEnvironment))
        {
            // A value typed here could be a token, so it goes to the secret store like any other
            // and config.json keeps only the reference.
            var secretName = $"mcp.{settings.Id}.{key}";
            _services.Secrets.Set(secretName, value);
            settings.Environment[key] = secretName;
        }

        _services.Config.Update(config => config.McpServers.Add(settings));

        ManualOpen = false;
        ManualName = "";
        ManualArguments = "";
        ManualUrl = "";
        ManualEnvironment = "";

        ReloadServers();
        Status = string.Format(L["mcp.added"], settings.Name);
    }

    [RelayCommand]
    private void Remove(McpServerViewModel? server)
    {
        if (server is null) return;

        _services.Config.Update(config => config.McpServers.RemoveAll(s => s.Id == server.Settings.Id));

        ReloadServers();
        Status = string.Format(L["mcp.removed"], server.Name);
    }

    /// <summary>
    /// Starts the server once, asks what it offers, and shuts it down again — without changing
    /// what the agent can see. Finding out that npx is missing here beats finding out mid-run.
    /// </summary>
    [RelayCommand]
    private async Task TestAsync(McpServerViewModel? server)
    {
        if (server is null || server.Busy) return;

        server.Busy = true;
        server.Status = L["mcp.testing"];

        try
        {
            var result = await _services.Mcp.ProbeAsync(server.Settings).ConfigureAwait(true);

            server.TestPassed = result.Success;
            server.Status = result.Message;
        }
        finally
        {
            server.Busy = false;
        }
    }

    /// <summary>Re-read after Settings changes, so the capability switch shows through here.</summary>
    public void Refresh()
    {
        ReloadServers();
        OnPropertyChanged(nameof(Allowed));
    }

    private void Persist() => _services.Config.Update(config =>
    {
        foreach (var server in Servers)
        {
            var stored = config.McpServers.FirstOrDefault(s => s.Id == server.Settings.Id);
            if (stored is not null) stored.Enabled = server.Enabled;
        }
    });
}
