using System.Collections.ObjectModel;
using AutoWork.Core.Configuration;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using AutoWork.Integrations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>One editable setting on a connector. Secrets go to the secret store, not to config.json.</summary>
public sealed partial class IntegrationFieldViewModel : ObservableObject
{
    public required IntegrationField Field { get; init; }

    [ObservableProperty] private string _value = "";

    public string Label => Field.Label;
    public string Help => Field.Help;
    public bool IsSecret => Field.Secret;
    public string? Placeholder => Field.Placeholder;

    /// <summary>Masks secret fields. NUL means "show the text as typed".</summary>
    public char PasswordChar => Field.Secret ? '•' : '\0';
}

public sealed partial class IntegrationViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IIntegration _integration;

    public IntegrationViewModel(AppServices services, IIntegration integration)
    {
        _services = services;
        _integration = integration;

        var settings = services.Config.Current.GetIntegration(integration.Id);
        _enabled = settings.Enabled;

        foreach (var field in integration.Fields)
        {
            var stored = field.Secret
                // Show that a secret exists without ever putting it back on screen.
                ? settings.SecretRefs.TryGetValue(field.Key, out var reference) && !string.IsNullOrEmpty(reference)
                    ? reference.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ? reference : "••••••••"
                    : ""
                : settings.Values.GetValueOrDefault(field.Key) ?? "";

            Fields.Add(new IntegrationFieldViewModel { Field = field, Value = stored });
        }
    }

    public string Id => _integration.Id;
    public string DisplayName => _integration.DisplayName;
    public string Description => _integration.Description;
    public string DocsUrl => _integration.DocsUrl;

    public ObservableCollection<IntegrationFieldViewModel> Fields { get; } = [];

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _connected;
    [ObservableProperty] private bool _busy;

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    partial void OnStatusChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    [RelayCommand]
    private void Save()
    {
        _services.Config.Update(config =>
        {
            var settings = config.GetIntegration(_integration.Id);
            settings.Enabled = Enabled;

            foreach (var field in Fields)
            {
                if (field.IsSecret)
                {
                    // The placeholder means "unchanged" — never overwrite a stored key with dots.
                    if (field.Value is "••••••••" or "") continue;

                    if (field.Value.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.SecretRefs[field.Field.Key] = field.Value;
                    }
                    else
                    {
                        var name = $"integration.{_integration.Id}.{field.Field.Key}";
                        _services.Secrets.Set(name, field.Value);
                        settings.SecretRefs[field.Field.Key] = name;
                    }

                    field.Value = "••••••••";
                }
                else
                {
                    settings.Values[field.Field.Key] = field.Value;
                }
            }
        });

        Status = _services.Strings["common.saved"];
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        Busy = true;
        Status = _services.Strings["common.testing"];

        try
        {
            var result = await _services.Integrations.TestAsync(_integration.Id).ConfigureAwait(true);
            Connected = result.Connected;
            Status = result.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void OpenDocs() => ActivityViewModel.OpenUrl(DocsUrl);
}

public sealed partial class IntegrationsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public IntegrationsViewModel(AppServices services)
    {
        _services = services;

        foreach (var integration in services.Integrations.All)
            Items.Add(new IntegrationViewModel(services, integration));
    }

    public Strings L => _services.Strings;

    public ObservableCollection<IntegrationViewModel> Items { get; } = [];
}
