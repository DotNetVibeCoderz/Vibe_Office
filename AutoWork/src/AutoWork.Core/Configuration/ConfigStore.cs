using System.Text.Json;
using System.Text.Json.Serialization;
using AutoWork.Core.Security;

namespace AutoWork.Core.Configuration;

/// <summary>
/// Loads and saves config.json, and applies the environment overlay on top of it.
///
/// Precedence, lowest to highest: built-in defaults → config.json → environment variables.
/// The overlay is never written back, so exporting AUTOWORK_ALLOW_SHELL=false for one launch
/// does not silently rewrite the user's saved policy.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private AutoWorkConfig _current;

    public ConfigStore(string? path = null)
    {
        _path = path ?? AppPaths.ConfigFile;
        _current = LoadFromDisk();
        ApplyEnvironmentOverlay(_current);
    }

    /// <summary>Fires after <see cref="Save"/>. Subscribers rebuild whatever they cached.</summary>
    public event Action<AutoWorkConfig>? Changed;

    public string Path => _path;

    public AutoWorkConfig Current
    {
        get { lock (_gate) return _current; }
    }

    public void Save(AutoWorkConfig config)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);

            // Write to a sibling then swap, so a crash mid-write cannot leave a truncated config.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, SerializerOptions));

            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);

            _current = config;
        }

        Changed?.Invoke(config);
    }

    /// <summary>Mutates the current config and persists it in one step.</summary>
    public void Update(Action<AutoWorkConfig> mutate)
    {
        AutoWorkConfig config;
        lock (_gate) config = _current;

        mutate(config);
        Save(config);
    }

    private AutoWorkConfig LoadFromDisk()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<AutoWorkConfig>(json, SerializerOptions);
                if (loaded is not null) return Migrate(loaded);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt config must not block launch. Keep the bad file for the user to inspect
            // and continue with defaults; Settings will overwrite it on the next save.
            TryQuarantine();
        }

        return CreateDefault();
    }

    private void TryQuarantine()
    {
        try
        {
            if (File.Exists(_path))
                File.Move(_path, _path + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch (IOException) { }
    }

    private static AutoWorkConfig Migrate(AutoWorkConfig config)
    {
        // Nothing to migrate at schema 1; the hook exists so v2 has somewhere to live.
        config.SchemaVersion = 1;
        return config;
    }

    public static AutoWorkConfig CreateDefault() => new()
    {
        Permissions = PermissionPolicy.CreateStarter(AppPaths.WorkspaceDirectory),
    };

    // ── Environment overlay ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies AUTOWORK_* variables, then auto-seeds a model for any vendor key that is present
    /// in the environment but not yet configured. That last part is what makes a headless or
    /// containerised install work with zero clicks: export ANTHROPIC_API_KEY and AutoWork has
    /// a usable model on first launch.
    /// </summary>
    public static void ApplyEnvironmentOverlay(AutoWorkConfig config)
    {
        if (ReadEnum<ThemeMode>("AUTOWORK_THEME") is { } theme) config.Appearance.Theme = theme;
        if (ReadEnum<UiLanguage>("AUTOWORK_LANGUAGE") is { } language) config.Appearance.Language = language;

        if (ReadBool("AUTOWORK_ALLOW_SHELL") is { } allowShell) config.Permissions.AllowShell = allowShell;
        if (ReadBool("AUTOWORK_ALLOW_DELETE") is { } allowDelete) config.Permissions.AllowDelete = allowDelete;
        if (ReadBool("AUTOWORK_ALLOW_INPUT") is { } allowInput) config.Permissions.AllowInputControl = allowInput;
        if (ReadBool("AUTOWORK_ALLOW_SCREEN") is { } allowScreen) config.Permissions.AllowScreenCapture = allowScreen;

        if (ReadInt("AUTOWORK_MAX_STEPS") is { } maxSteps) config.Agent.MaxSteps = maxSteps;
        if (ReadBool("AUTOWORK_AUTO_COMPACT") is { } autoCompact) config.Agent.EnableAutoCompact = autoCompact;
        if (ReadDouble("AUTOWORK_COMPACT_THRESHOLD") is { } threshold) config.Agent.AutoCompactThreshold = threshold;

        ApplyFolderGrants(config);
        SeedModelsFromEnvironment(config);
        ApplyExplicitModelOverride(config);
    }

    private static void ApplyFolderGrants(AutoWorkConfig config)
    {
        void Grant(string variable, FolderAccess access)
        {
            var raw = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(raw)) return;

            foreach (var entry in raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var expanded = Environment.ExpandEnvironmentVariables(entry);
                if (config.Permissions.Roots.Any(r =>
                        string.Equals(r.Path, expanded, StringComparison.OrdinalIgnoreCase)))
                    continue;

                config.Permissions.Roots.Add(new PermissionRoot
                {
                    Path = expanded,
                    Access = access,
                    IncludeSubfolders = true,
                });
            }
        }

        Grant("AUTOWORK_FOLDERS_READONLY", FolderAccess.Read);
        Grant("AUTOWORK_FOLDERS", FolderAccess.ReadWrite);
    }

    private static void SeedModelsFromEnvironment(AutoWorkConfig config)
    {
        foreach (var preset in ProviderPresets.All)
        {
            if (string.IsNullOrEmpty(preset.EnvironmentVariable)) continue;

            var value = Environment.GetEnvironmentVariable(preset.EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Already configured for this preset? Leave the user's own entry alone.
            if (config.Models.Any(m => string.Equals(m.Preset, preset.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            var endpoint = string.IsNullOrEmpty(preset.EndpointEnvironmentVariable)
                ? null
                : Environment.GetEnvironmentVariable(preset.EndpointEnvironmentVariable);

            // A preset whose endpoint is per-account cannot be seeded from a key alone. Adding
            // it anyway would put an uncallable model at the top of the list, where it becomes
            // the default planner and fails every run with a confusing 404.
            if (preset.EndpointIsPlaceholder && string.IsNullOrWhiteSpace(endpoint)) continue;

            var profile = ProviderPresets.CreateProfile(preset);
            profile.Id = $"env-{preset.Id}";

            if (!string.IsNullOrWhiteSpace(endpoint)) profile.Endpoint = endpoint.Trim();

            if (!string.IsNullOrEmpty(preset.ModelEnvironmentVariable)
                && Environment.GetEnvironmentVariable(preset.ModelEnvironmentVariable) is { Length: > 0 } deployment)
            {
                profile.ModelId = deployment.Trim();
                profile.DisplayName = $"{preset.DisplayName} — {profile.ModelId}";
            }

            // OLLAMA_HOST holds a URL, not a key.
            if (preset.IsLocal)
            {
                profile.Endpoint = value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : preset.Endpoint;
                profile.ApiKeyRef = "";
            }

            config.Models.Add(profile);
        }

        config.Agent.PlannerModelId ??= config.Models
            .FirstOrDefault(m => m.Enabled && m.Supports(ModelCapabilities.Tools))?.Id;

        config.Agent.EmbeddingModelId ??= config.Models
            .FirstOrDefault(m => m.Enabled && m.Supports(ModelCapabilities.Embeddings))?.Id;
    }

    /// <summary>
    /// AUTOWORK_MODEL / AUTOWORK_ENDPOINT / AUTOWORK_API_KEY / AUTOWORK_PROVIDER define one
    /// model outright and select it. This is the escape hatch for CI and containers.
    /// </summary>
    private static void ApplyExplicitModelOverride(AutoWorkConfig config)
    {
        var modelId = Environment.GetEnvironmentVariable("AUTOWORK_MODEL");
        if (string.IsNullOrWhiteSpace(modelId)) return;

        var presetId = Environment.GetEnvironmentVariable("AUTOWORK_PROVIDER") ?? "custom";
        var preset = ProviderPresets.Find(presetId) ?? ProviderPresets.Find("custom")!;

        var endpoint = Environment.GetEnvironmentVariable("AUTOWORK_ENDPOINT");
        var profile = ProviderPresets.CreateProfile(preset, modelId);
        profile.Id = "env-override";
        profile.DisplayName = $"{modelId} (environment)";
        if (!string.IsNullOrWhiteSpace(endpoint)) profile.Endpoint = endpoint;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOWORK_API_KEY")))
            profile.ApiKeyRef = "env:AUTOWORK_API_KEY";

        if (ReadInt("AUTOWORK_CONTEXT_WINDOW") is { } window) profile.ContextWindow = window;

        config.Models.RemoveAll(m => m.Id == "env-override");
        config.Models.Insert(0, profile);
        config.Agent.PlannerModelId = profile.Id;
    }

    private static bool? ReadBool(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : null;

    private static int? ReadInt(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : null;

    private static double? ReadDouble(string name) =>
        double.TryParse(Environment.GetEnvironmentVariable(name),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private static T? ReadEnum<T>(string name) where T : struct, Enum =>
        Enum.TryParse<T>(Environment.GetEnvironmentVariable(name), ignoreCase: true, out var value) ? value : null;
}
