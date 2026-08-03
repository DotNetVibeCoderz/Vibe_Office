using System.Collections.ObjectModel;
using AutoWork.Core;
using AutoWork.Core.Configuration;
using AutoWork.Core.Meetings;
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

        _inputPrice = profile.InputPricePerMillion;
        _outputPrice = profile.OutputPricePerMillion;
        _currency = profile.Currency;

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

    /// <summary>
    /// Left empty unless the user fills them in. AutoWork ships no price table on purpose —
    /// prices move faster than model ids, and a stale built-in number that under-reports what a
    /// run cost would be worse than showing tokens alone.
    /// </summary>
    [ObservableProperty] private decimal? _inputPrice;
    [ObservableProperty] private decimal? _outputPrice;
    [ObservableProperty] private string _currency = "USD";

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

        // A blank price stays null rather than becoming zero — "free" and "I have not told you"
        // are different claims, and the meter shows a cost only for the first.
        profile.InputPricePerMillion = InputPrice is > 0 ? InputPrice : null;
        profile.OutputPricePerMillion = OutputPrice is > 0 ? OutputPrice : null;
        profile.Currency = string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();
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
/// One standing rule, edited in place.
///
/// The problem it is validated against continuously rather than on save: a rule that says
/// "allow" but has not been given a folder yet would, if saved, be the broadest possible grant.
/// The user should see that the moment it is true, not discover it afterwards.
/// </summary>
public sealed partial class ApprovalRuleViewModel : ObservableObject
{
    public ApprovalRuleViewModel(ApprovalRule rule, Strings strings)
    {
        Rule = rule;
        L = strings;

        _allow = rule.Effect == RuleEffect.Allow;
        _kind = rule.Kind ?? ApprovalKind.WriteFiles;
        _anyKind = rule.Kind is null;
        _path = rule.Path;
        _enabled = rule.Enabled;
        _note = rule.Note;
    }

    public ApprovalRule Rule { get; }

    /// <summary>
    /// Held on the row rather than reached through the parent list.
    ///
    /// A <c>ComboBox</c> is itself an <c>ItemsControl</c>, so <c>$parent[ItemsControl]</c> written
    /// inside one of its items finds the ComboBox, not the list of rules — the cast then fails
    /// silently and the picker renders blank. Found by looking at it.
    /// </summary>
    public Strings L { get; }

    public IReadOnlyList<string> Effects => [L["rules.never"], L["rules.always"]];

    [ObservableProperty] private bool _allow;
    [ObservableProperty] private ApprovalKind _kind;
    [ObservableProperty] private bool _anyKind;
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _note = "";

    /// <summary>
    /// Drives the never/always picker. Index 0 is "never" so the list reads in the order of
    /// increasing consequence, and so a picker left untouched means deny.
    /// </summary>
    public int EffectIndex
    {
        get => Allow ? 1 : 0;
        set
        {
            if (Allow == (value == 1)) return;

            Allow = value == 1;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<ApprovalKind> Kinds =>
        [ApprovalKind.WriteFiles, ApprovalKind.DeleteFiles, ApprovalKind.RunCommand,
         ApprovalKind.ControlInput, ApprovalKind.CaptureScreen, ApprovalKind.NetworkAccess];

    public string PathDisplay => string.IsNullOrWhiteSpace(Path) ? "" : PathGuard.Describe(Path);

    /// <summary>Non-null when the rule as it stands would be ignored, and why.</summary>
    public string? Problem => Snapshot().Validate();

    public bool HasProblem => Problem is not null;

    /// <summary>"Any kind" is only meaningful for a deny — an allow must say what it allows.</summary>
    public bool CanBeAnyKind => !Allow;

    partial void OnAllowChanged(bool value)
    {
        if (value) AnyKind = false;

        OnPropertyChanged(nameof(EffectIndex));
        Revalidate();
    }

    partial void OnKindChanged(ApprovalKind value) => Revalidate();
    partial void OnAnyKindChanged(bool value) => Revalidate();

    partial void OnPathChanged(string value)
    {
        OnPropertyChanged(nameof(PathDisplay));
        Revalidate();
    }

    private void Revalidate()
    {
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(CanBeAnyKind));
    }

    private ApprovalRule Snapshot() => new()
    {
        Id = Rule.Id,
        Effect = Allow ? RuleEffect.Allow : RuleEffect.Deny,
        Kind = AnyKind && !Allow ? null : Kind,
        Path = Path.Trim(),
        Enabled = Enabled,
        Note = Note,
        CreatedAt = Rule.CreatedAt,
    };

    public ApprovalRule? ToRule()
    {
        var rule = Snapshot();

        // An invalid rule is dropped rather than stored disabled: a saved rule the engine would
        // ignore is a promise the user thinks they made and did not.
        return rule.Validate() is null ? rule : null;
    }
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

        _keepRunHistory = config.KeepRunHistory;
        _runHistoryRetentionDays = config.RunHistoryRetentionDays;
        _enableStreaming = config.Agent.EnableStreaming;
        _useLocalEmbedding = config.Agent.UseLocalEmbedding;

        _allowBrowser = config.Browser.Enabled;
        _browserPath = config.Browser.ExecutablePath;
        _browserHeadless = config.Browser.Headless;

        _speechMode = (int)config.Transcription.Mode;
        _speechCommand = config.Transcription.Command;
        _speechArguments = config.Transcription.Arguments;
        _speechModelPath = config.Transcription.ModelPath;
        _speechEndpoint = config.Transcription.Endpoint;
        _speechRemoteModel = config.Transcription.RemoteModel;

        _speechApiKey = config.Transcription.ApiKeyRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? config.Transcription.ApiKeyRef
            : string.IsNullOrEmpty(config.Transcription.ApiKeyRef) ? "" : "••••••••";

        _allowDelete = config.Permissions.AllowDelete;
        _softDelete = config.Permissions.SoftDelete;
        _allowShell = config.Permissions.AllowShell;
        _allowMcp = config.Permissions.AllowMcpServers;
        _allowSkillScripts = config.Permissions.AllowSkillScripts;
        _skillScriptApproval = config.Permissions.SkillScriptsRequireApproval;
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

        foreach (var rule in config.Permissions.ApprovalRules) Rules.Add(new ApprovalRuleViewModel(rule, L));

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
    public ObservableCollection<ApprovalRuleViewModel> Rules { get; } = [];

    public bool NoRules => Rules.Count == 0;

    /// <summary>
    /// Adds a rule in its safest possible shape: a deny, covering nothing until the user says
    /// what it covers. A half-finished rule that starts life granting something is exactly the
    /// accident this feature must not enable.
    /// </summary>
    [RelayCommand]
    private void AddRule()
    {
        Rules.Add(new ApprovalRuleViewModel(
            new ApprovalRule { Effect = RuleEffect.Deny, Kind = ApprovalKind.DeleteFiles }, L));

        OnPropertyChanged(nameof(NoRules));
    }

    [RelayCommand]
    private void RemoveRule(ApprovalRuleViewModel? rule)
    {
        if (rule is null) return;

        Rules.Remove(rule);
        OnPropertyChanged(nameof(NoRules));
    }

    [RelayCommand]
    private async Task PickRuleFolderAsync(ApprovalRuleViewModel? rule)
    {
        if (rule is null || PickFolder is null) return;

        var picked = await PickFolder().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked)) rule.Path = picked;
    }

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

    /// <summary>Transcripts are the user's own record of their runs, so keeping them is their call.</summary>
    [ObservableProperty] private bool _keepRunHistory;
    [ObservableProperty] private int _runHistoryRetentionDays;

    [ObservableProperty] private bool _enableStreaming;

    /// <summary>
    /// Embed knowledge here rather than calling a provider. Worse at meaning, better at never
    /// leaving the machine — and it needs no key, so it also works when nothing is configured.
    /// </summary>
    [ObservableProperty] private bool _useLocalEmbedding;

    // ── Speech to text ────────────────────────────────────────────────────────────────────

    [ObservableProperty] private int _speechMode;
    [ObservableProperty] private string _speechCommand = "";
    [ObservableProperty] private string _speechArguments = "";
    [ObservableProperty] private string _speechModelPath = "";
    [ObservableProperty] private string _speechEndpoint = "";
    [ObservableProperty] private string _speechRemoteModel = "whisper-1";
    [ObservableProperty] private string _speechApiKey = "";

    public IReadOnlyList<string> SpeechModes =>
        [L["speech.off"], L["speech.local"], L["speech.remote"]];

    public bool SpeechIsLocal => SpeechMode == (int)TranscriptionMode.Local;
    public bool SpeechIsRemote => SpeechMode == (int)TranscriptionMode.Remote;

    partial void OnSpeechModeChanged(int value)
    {
        OnPropertyChanged(nameof(SpeechIsLocal));
        OnPropertyChanged(nameof(SpeechIsRemote));
    }

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

    /// <summary>
    /// A browser that stays signed in is every account the user has, so it is separate from
    /// "network access" — fetching a public page and acting as the logged-in user are not the
    /// same permission.
    /// </summary>
    [ObservableProperty] private bool _allowBrowser;
    [ObservableProperty] private string _browserPath = "";
    [ObservableProperty] private bool _browserHeadless;

    /// <summary>What would actually be driven, so the switch is not a leap of faith.</summary>
    public string BrowserFound =>
        AutoWork.Core.Browsing.BrowserSession.FindBrowser(string.IsNullOrWhiteSpace(BrowserPath) ? null : BrowserPath)
            is { } found
            ? string.Format(L["browser.found"], Path.GetFileNameWithoutExtension(found))
            : L["browser.notfound"];

    partial void OnBrowserPathChanged(string value) => OnPropertyChanged(nameof(BrowserFound));

    /// <summary>Running a skill's bundled script means running code from a repository.</summary>
    [ObservableProperty] private bool _allowSkillScripts;
    [ObservableProperty] private bool _skillScriptApproval = true;

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

            config.Agent.EnableStreaming = EnableStreaming;
            config.Agent.UseLocalEmbedding = UseLocalEmbedding;

            config.Browser.Enabled = AllowBrowser;
            config.Browser.ExecutablePath = BrowserPath.Trim();
            config.Browser.Headless = BrowserHeadless;

            config.Transcription.Mode = (TranscriptionMode)SpeechMode;
            config.Transcription.Command = SpeechCommand.Trim();
            config.Transcription.Arguments = SpeechArguments.Trim();
            config.Transcription.ModelPath = SpeechModelPath.Trim();
            config.Transcription.Endpoint = SpeechEndpoint.Trim();
            config.Transcription.RemoteModel = string.IsNullOrWhiteSpace(SpeechRemoteModel) ? "whisper-1" : SpeechRemoteModel.Trim();

            // Same rule as a model key: an "env:" reference is stored as typed, a real key goes
            // to the secret store, and the mask is never written back as if it were the key.
            if (SpeechApiKey.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                config.Transcription.ApiKeyRef = SpeechApiKey.Trim();
            }
            else if (SpeechApiKey is { Length: > 0 } && !SpeechApiKey.StartsWith('•'))
            {
                var name = $"transcription-{Guid.NewGuid().ToString("n")[..6]}";
                _services.Secrets.Set(name, SpeechApiKey.Trim());
                config.Transcription.ApiKeyRef = name;
            }

            config.KeepRunHistory = KeepRunHistory;

            // The floor is one day rather than zero: a retention of zero would read as "keep
            // nothing", which is what the switch above is for.
            config.RunHistoryRetentionDays = Math.Clamp(RunHistoryRetentionDays, 1, 3650);

            config.Permissions.Roots = Folders
                .Select(f => new PermissionRoot
                {
                    Path = f.Path,
                    Access = f.Writable ? FolderAccess.ReadWrite : FolderAccess.Read,
                    IncludeSubfolders = true,
                })
                .ToList();

            // Only rules that would actually take effect are stored. Keeping an unusable one
            // would leave the user believing they had set something they had not.
            config.Permissions.ApprovalRules = Rules
                .Select(r => r.ToRule())
                .OfType<ApprovalRule>()
                .ToList();

            config.Permissions.AllowDelete = AllowDelete;
            config.Permissions.SoftDelete = SoftDelete;
            config.Permissions.AllowShell = AllowShell;
            config.Permissions.AllowMcpServers = AllowMcp;
            config.Permissions.AllowSkillScripts = AllowSkillScripts;
            config.Permissions.SkillScriptsRequireApproval = SkillScriptApproval;
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
