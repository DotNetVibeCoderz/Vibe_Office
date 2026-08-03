using System.Collections.ObjectModel;
using AutoWork.Core;
using AutoWork.Core.Configuration;
using AutoWork.Core.Security;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>An editable model profile. The API key is write-only from the UI's point of view.</summary>
public sealed partial class ModelViewModel : ObservableObject
{
    private readonly AppServices _services;

    public ModelViewModel(AppServices services, ModelProfile profile)
    {
        _services = services;
        Profile = profile;

        _displayName = profile.DisplayName;
        _endpoint = profile.Endpoint;
        _modelId = profile.ModelId;
        _contextWindow = profile.ContextWindow;
        _enabled = profile.Enabled;
        _presetId = profile.Preset;

        _supportsTools = profile.Supports(ModelCapabilities.Tools);
        _supportsVision = profile.Supports(ModelCapabilities.Vision);
        _supportsEmbeddings = profile.Supports(ModelCapabilities.Embeddings);

        // An existing key is represented, never revealed.
        _apiKey = profile.ApiKeyRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? profile.ApiKeyRef
            : string.IsNullOrEmpty(profile.ApiKeyRef) ? "" : "••••••••";
    }

    public ModelProfile Profile { get; }
    public string Id => Profile.Id;

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _presetId = "custom";
    [ObservableProperty] private string _endpoint = "";
    [ObservableProperty] private string _modelId = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private int _contextWindow = 128_000;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private bool _supportsTools = true;
    [ObservableProperty] private bool _supportsVision;
    [ObservableProperty] private bool _supportsEmbeddings;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _testPassed;
    [ObservableProperty] private bool _busy;

    public IReadOnlyList<ProviderPreset> Presets => ProviderPresets.All;

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    partial void OnStatusChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    /// <summary>Switching preset refills the endpoint and defaults, but keeps anything typed.</summary>
    partial void OnPresetIdChanged(string value)
    {
        var preset = ProviderPresets.Find(value);
        if (preset is null) return;

        if (string.IsNullOrWhiteSpace(Endpoint) || IsAnotherPresetEndpoint(Endpoint))
            Endpoint = preset.Endpoint;

        if (string.IsNullOrWhiteSpace(ModelId)) ModelId = preset.DefaultModelId;
        if (string.IsNullOrWhiteSpace(DisplayName)) DisplayName = $"{preset.DisplayName} — {preset.DefaultModelId}";

        ContextWindow = preset.ContextWindow;
        SupportsTools = (preset.Capabilities & ModelCapabilities.Tools) != 0;
        SupportsVision = (preset.Capabilities & ModelCapabilities.Vision) != 0;
        SupportsEmbeddings = (preset.Capabilities & ModelCapabilities.Embeddings) != 0;

        if (string.IsNullOrEmpty(ApiKey) && !preset.IsLocal && !string.IsNullOrEmpty(preset.EnvironmentVariable))
            ApiKey = $"env:{preset.EnvironmentVariable}";
    }

    private static bool IsAnotherPresetEndpoint(string endpoint) =>
        ProviderPresets.All.Any(p => string.Equals(p.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));

    /// <summary>Copies the edited values back onto the profile and stores any new secret.</summary>
    public void ApplyTo(ModelProfile profile)
    {
        profile.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? ModelId : DisplayName.Trim();
        profile.Preset = PresetId;
        profile.Kind = ProviderPresets.Find(PresetId)?.Kind ?? ProviderKind.OpenAICompatible;
        profile.Endpoint = Endpoint.Trim();
        profile.ModelId = ModelId.Trim();
        profile.ContextWindow = Math.Max(4_000, ContextWindow);
        profile.Enabled = Enabled;

        var capabilities = ModelCapabilities.None;
        if (SupportsTools) capabilities |= ModelCapabilities.Tools;
        if (SupportsVision) capabilities |= ModelCapabilities.Vision;
        if (SupportsEmbeddings) capabilities |= ModelCapabilities.Embeddings;
        profile.Capabilities = capabilities;

        if (ApiKey is "••••••••" or "")
        {
            if (ApiKey.Length == 0) profile.ApiKeyRef = "";
            return;
        }

        if (ApiKey.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            profile.ApiKeyRef = ApiKey.Trim();
        }
        else
        {
            var name = $"model.{profile.Id}";
            _services.Secrets.Set(name, ApiKey.Trim());
            profile.ApiKeyRef = name;
        }

        ApiKey = "••••••••";
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        Busy = true;
        Status = _services.Strings["common.testing"];

        try
        {
            // Test what is on screen, not what was last saved, so a typo shows up immediately.
            var candidate = new ModelProfile { Id = Profile.Id };
            ApplyTo(candidate);

            _services.Models.Invalidate();
            var result = await _services.Models.TestAsync(candidate).ConfigureAwait(true);

            TestPassed = result.Success;
            Status = result.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}

public sealed partial class FolderViewModel : ObservableObject
{
    public required PermissionRoot Root { get; init; }

    public string Path => Root.Path;
    public string Display => PathGuard.Describe(Root.Path);

    [ObservableProperty] private bool _writable;
}

/// <summary>
/// Settings. The permissions page is the important one: it is the whole security model made
/// visible, so it states plainly what is and is not reachable rather than hiding behind
/// toggle labels.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;

        var config = services.Config.Current;

        _maxSteps = config.Agent.MaxSteps;
        _maxParallel = config.Agent.MaxParallelSubAgents;
        _enableSubAgents = config.Agent.EnableSubAgents;
        _enableVerification = config.Agent.EnableSelfVerification;
        _enableAutoCompact = config.Agent.EnableAutoCompact;
        _compactThreshold = config.Agent.AutoCompactThreshold;
        _confirmDestructive = config.Agent.ConfirmDestructiveActions;

        _allowDelete = config.Permissions.AllowDelete;
        _softDelete = config.Permissions.SoftDelete;
        _allowShell = config.Permissions.AllowShell;
        _allowMcp = config.Permissions.AllowMcpServers;
        _shellApproval = config.Permissions.ShellRequiresApproval;
        _allowScreen = config.Permissions.AllowScreenCapture;
        _allowInput = config.Permissions.AllowInputControl;
        _inputApproval = config.Permissions.InputControlRequiresApproval;
        _allowNetwork = config.Permissions.AllowNetwork;

        // Same convention as a model key: an "env:" reference is shown as typed so it can be
        // edited, while a stored secret is masked because it must never be readable from here.
        _searchApiKey = config.Search.ApiKeyRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? config.Search.ApiKeyRef
            : string.IsNullOrEmpty(config.Search.ApiKeyRef) ? "" : "••••••••";

        _theme = config.Appearance.Theme;
        _language = config.Appearance.Language;
        _reduceMotion = config.Appearance.ReduceMotion;

        foreach (var model in config.Models) Models.Add(new ModelViewModel(services, model));
        foreach (var root in config.Permissions.Roots)
            Folders.Add(new FolderViewModel { Root = root, Writable = root.Access == FolderAccess.ReadWrite });

        RefreshRoles();
    }

    public Strings L => _services.Strings;

    /// <summary>Set by the view, which is the only thing that can reach a TopLevel.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Raised after a save so the shell can re-apply the theme and refresh the Work view.</summary>
    public event Action? Applied;

    public ObservableCollection<ModelViewModel> Models { get; } = [];
    public ObservableCollection<FolderViewModel> Folders { get; } = [];

    [ObservableProperty] private ModelViewModel? _selectedModel;

    [ObservableProperty] private string? _plannerModelId;
    [ObservableProperty] private string? _executorModelId;
    [ObservableProperty] private string? _visionModelId;
    [ObservableProperty] private string? _embeddingModelId;

    [ObservableProperty] private int _maxSteps;
    [ObservableProperty] private int _maxParallel;
    [ObservableProperty] private bool _enableSubAgents;
    [ObservableProperty] private bool _enableVerification;
    [ObservableProperty] private bool _enableAutoCompact;
    [ObservableProperty] private double _compactThreshold;
    [ObservableProperty] private bool _confirmDestructive;

    [ObservableProperty] private bool _allowDelete;
    [ObservableProperty] private bool _softDelete;
    [ObservableProperty] private bool _allowShell;
    [ObservableProperty] private bool _shellApproval;
    [ObservableProperty] private bool _allowScreen;
    [ObservableProperty] private bool _allowInput;
    [ObservableProperty] private bool _inputApproval;
    [ObservableProperty] private bool _allowNetwork;

    /// <summary>Starting an MCP server is the same class of power as the shell, so it gets its own switch.</summary>
    [ObservableProperty] private bool _allowMcp;

    /// <summary>Tavily key for web search. Blank is fine — search falls back to keyless backends.</summary>
    [ObservableProperty] private string _searchApiKey = "";

    [ObservableProperty] private ThemeMode _theme;
    [ObservableProperty] private UiLanguage _language;
    [ObservableProperty] private bool _reduceMotion;

    [ObservableProperty] private string? _status;

    public bool NoModels => Models.Count == 0;
    public bool NoFolders => Folders.Count == 0;

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public string DataFolder => AppPaths.Root;

    public IReadOnlyList<ThemeMode> Themes => [ThemeMode.System, ThemeMode.Light, ThemeMode.Dark];
    public IReadOnlyList<UiLanguage> Languages => [UiLanguage.System, UiLanguage.English, UiLanguage.Indonesian];

    /// <summary>Model choices for the role pickers, with a null entry meaning "pick automatically".</summary>
    public IReadOnlyList<ModelViewModel> RoleChoices => Models.ToArray();

    private void RefreshRoles()
    {
        var agent = _services.Config.Current.Agent;
        PlannerModelId = agent.PlannerModelId;
        ExecutorModelId = agent.ExecutorModelId;
        VisionModelId = agent.VisionModelId;
        EmbeddingModelId = agent.EmbeddingModelId;

        OnPropertyChanged(nameof(RoleChoices));
        OnPropertyChanged(nameof(NoModels));
    }

    // ── Models ────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddModel()
    {
        var preset = ProviderPresets.Find("openai") ?? ProviderPresets.All[0];
        var profile = ProviderPresets.CreateProfile(preset);

        var model = new ModelViewModel(_services, profile);
        Models.Add(model);
        SelectedModel = model;

        RefreshRoles();
    }

    [RelayCommand]
    private void RemoveModel(ModelViewModel? model)
    {
        if (model is null) return;

        Models.Remove(model);
        if (SelectedModel == model) SelectedModel = Models.FirstOrDefault();

        RefreshRoles();
    }

    // ── Folders ───────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (PickFolder is null) return;

        var path = await PickFolder().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;

        if (Folders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) return;

        Folders.Add(new FolderViewModel
        {
            Root = new PermissionRoot { Path = path, Access = FolderAccess.ReadWrite, IncludeSubfolders = true },
            Writable = true,
        });

        OnPropertyChanged(nameof(NoFolders));
    }

    [RelayCommand]
    private void RemoveFolder(FolderViewModel? folder)
    {
        if (folder is null) return;

        Folders.Remove(folder);
        OnPropertyChanged(nameof(NoFolders));
    }

    [RelayCommand]
    private void OpenDataFolder() => ActivityViewModel.OpenFolder(AppPaths.Root);

    // ── Persistence ───────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Save()
    {
        _services.Config.Update(config =>
        {
            // Models: rebuild the list from what is on screen, preserving ids so secret
            // references and role selections stay valid.
            var rebuilt = new List<ModelProfile>();
            foreach (var editor in Models)
            {
                var profile = config.Models.FirstOrDefault(m => m.Id == editor.Id)
                              ?? new ModelProfile { Id = editor.Id };

                editor.ApplyTo(profile);
                rebuilt.Add(profile);
            }
            config.Models = rebuilt;

            config.Agent.PlannerModelId = Keep(PlannerModelId, rebuilt);
            config.Agent.ExecutorModelId = Keep(ExecutorModelId, rebuilt);
            config.Agent.VisionModelId = Keep(VisionModelId, rebuilt);
            config.Agent.EmbeddingModelId = Keep(EmbeddingModelId, rebuilt);

            config.Agent.MaxSteps = Math.Clamp(MaxSteps, 1, 200);
            config.Agent.MaxParallelSubAgents = Math.Clamp(MaxParallel, 1, 8);
            config.Agent.EnableSubAgents = EnableSubAgents;
            config.Agent.EnableSelfVerification = EnableVerification;
            config.Agent.EnableAutoCompact = EnableAutoCompact;
            config.Agent.AutoCompactThreshold = Math.Clamp(CompactThreshold, 0.3, 0.95);
            config.Agent.ConfirmDestructiveActions = ConfirmDestructive;

            config.Permissions.Roots = Folders
                .Select(f => new PermissionRoot
                {
                    Path = f.Path,
                    Access = f.Writable ? FolderAccess.ReadWrite : FolderAccess.Read,
                    IncludeSubfolders = true,
                })
                .ToList();

            config.Permissions.AllowDelete = AllowDelete;
            config.Permissions.SoftDelete = SoftDelete;
            config.Permissions.AllowShell = AllowShell;
            config.Permissions.AllowMcpServers = AllowMcp;
            config.Permissions.ShellRequiresApproval = ShellApproval;
            config.Permissions.AllowScreenCapture = AllowScreen;
            config.Permissions.AllowInputControl = AllowInput;
            config.Permissions.InputControlRequiresApproval = InputApproval;
            config.Permissions.AllowNetwork = AllowNetwork;

            ApplySearchKey(config);

            config.Appearance.Theme = Theme;
            config.Appearance.Language = Language;
            config.Appearance.ReduceMotion = ReduceMotion;

            config.FirstRunCompleted = true;
        });

        _services.Strings.Language = Language;
        Status = L["common.saved"];

        Applied?.Invoke();
    }

    /// <summary>
    /// Stores the search key the same way a model key is stored: config.json keeps a reference,
    /// never the secret. The mask is left untouched so re-saving Settings does not overwrite a
    /// stored key with the row of dots standing in for it.
    /// </summary>
    private void ApplySearchKey(AutoWorkConfig config)
    {
        var typed = SearchApiKey.Trim();

        if (typed == "••••••••") return;

        if (typed.Length == 0)
        {
            config.Search.ApiKeyRef = "";
            return;
        }

        if (typed.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            config.Search.ApiKeyRef = typed;
            return;
        }

        const string name = "search.tavily";
        _services.Secrets.Set(name, typed);
        config.Search.ApiKeyRef = name;

        SearchApiKey = "••••••••";
    }

    /// <summary>Drops a role assignment whose model was deleted, so it falls back to automatic.</summary>
    private static string? Keep(string? id, IReadOnlyList<ModelProfile> models) =>
        id is not null && models.Any(m => m.Id == id) ? id : null;
}
