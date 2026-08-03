using System.Collections.ObjectModel;
using AutoWork.Core.Skills;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using AutoWork.Integrations.Skills;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>One skill offered by a repository, plus whether it is already installed.</summary>
public sealed partial class SkillListingViewModel : ObservableObject
{
    public required SkillListing Listing { get; init; }

    public string Name => Listing.Name;
    public string Description => Listing.Description;
    public string Repository => Listing.Repository;

    [ObservableProperty] private bool _installed;
    [ObservableProperty] private bool _busy;
}

/// <summary>
/// The Skills gallery: browse the repositories the user trusts, install what is useful, remove
/// what is not.
///
/// Browsing is explicit rather than automatic on open. Each repository is a handful of network
/// requests, and a gallery that hammers GitHub every time someone clicks the nav item earns a
/// rate limit for no benefit.
/// </summary>
public sealed partial class SkillsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public SkillsViewModel(AppServices services)
    {
        _services = services;

        ReloadRepositories();
        ReloadInstalled();

        _services.Skills.Changed += ReloadInstalled;
    }

    public Strings L => _services.Strings;

    /// <summary>Installed skills, which is what actually reaches the agent.</summary>
    public ObservableCollection<Skill> Installed { get; } = [];

    /// <summary>Repositories searched, as "owner/name".</summary>
    public ObservableCollection<string> Repositories { get; } = [];

    /// <summary>What the last browse turned up, filtered by the search box.</summary>
    public ObservableCollection<SkillListingViewModel> Results { get; } = [];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _newRepository = "";
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _busy;

    public bool HasInstalled => Installed.Count > 0;
    public bool HasResults => Results.Count > 0;

    private readonly List<SkillListingViewModel> _allResults = [];

    partial void OnSearchChanged(string value) => ApplyFilter();

    private void ReloadInstalled()
    {
        Installed.Clear();
        foreach (var skill in _services.Skills.List()) Installed.Add(skill);

        OnPropertyChanged(nameof(HasInstalled));

        // A skill installed from another repository still counts as installed here.
        foreach (var result in _allResults)
            result.Installed = Installed.Any(s => string.Equals(s.Name, result.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void ReloadRepositories()
    {
        Repositories.Clear();

        var configured = _services.Config.Current.SkillRepositories;

        // An empty list means "never configured", not "the user removed them all" — the config
        // records the choice only once something is added or removed.
        var names = configured.Count > 0
            ? configured
            : SkillGallery.DefaultRepositories.Select(r => r.FullName).ToList();

        foreach (var name in names) Repositories.Add(name);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (Busy) return;

        Busy = true;
        Status = L["skills.browsing"];

        try
        {
            _allResults.Clear();

            foreach (var name in Repositories.ToArray())
            {
                var repository = SkillRepository.Parse(name);
                if (repository is null) continue;

                var listings = await _services.SkillGallery.BrowseAsync(repository).ConfigureAwait(true);

                foreach (var listing in listings)
                {
                    _allResults.Add(new SkillListingViewModel
                    {
                        Listing = listing,
                        Installed = Installed.Any(s => string.Equals(s.Name, listing.Name, StringComparison.OrdinalIgnoreCase)),
                    });
                }
            }

            ApplyFilter();

            Status = _allResults.Count == 0
                ? L["skills.none"]
                : string.Format(L["skills.found"], _allResults.Count, Repositories.Count);
        }
        finally
        {
            Busy = false;
        }
    }

    private void ApplyFilter()
    {
        var needle = Search.Trim();

        Results.Clear();

        foreach (var result in _allResults)
        {
            if (needle.Length > 0
                && !result.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !result.Description.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;

            Results.Add(result);
        }

        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    private async Task InstallAsync(SkillListingViewModel? item)
    {
        if (item is null || item.Busy) return;

        item.Busy = true;

        try
        {
            var markdown = await _services.SkillGallery.DownloadAsync(item.Listing).ConfigureAwait(true);

            if (markdown is null)
            {
                Status = string.Format(L["skills.failed"], item.Name);
                return;
            }

            _services.Skills.Install(markdown, item.Listing.Source, item.Name);
            Status = string.Format(L["skills.installed.msg"], item.Name);
        }
        finally
        {
            item.Busy = false;
        }
    }

    [RelayCommand]
    private void Remove(Skill? skill)
    {
        if (skill is null) return;

        _services.Skills.Remove(skill.Id);
        Status = string.Format(L["skills.removed"], skill.Name);
    }

    [RelayCommand]
    private void AddRepository()
    {
        var repository = SkillRepository.Parse(NewRepository);

        if (repository is null)
        {
            Status = L["skills.repo.invalid"];
            return;
        }

        if (Repositories.Contains(repository.FullName, StringComparer.OrdinalIgnoreCase))
        {
            NewRepository = "";
            return;
        }

        Repositories.Add(repository.FullName);
        PersistRepositories();
        NewRepository = "";
    }

    [RelayCommand]
    private void RemoveRepository(string? name)
    {
        if (name is null || !Repositories.Remove(name)) return;
        PersistRepositories();
    }

    private void PersistRepositories() =>
        _services.Config.Update(config => config.SkillRepositories = [.. Repositories]);
}
