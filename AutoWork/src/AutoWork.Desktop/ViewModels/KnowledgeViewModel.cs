using System.Collections.ObjectModel;
using AutoWork.Core.Knowledge;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>Knowledge bases: the memory that survives between sessions.</summary>
public sealed partial class KnowledgeViewModel : ObservableObject
{
    private readonly AppServices _services;

    public KnowledgeViewModel(AppServices services)
    {
        _services = services;
        Reload();
    }

    public Strings L => _services.Strings;

    public ObservableCollection<KnowledgeBase> Bases { get; } = [];

    [ObservableProperty] private KnowledgeBase? _selected;
    [ObservableProperty] private string _newBaseName = "";
    [ObservableProperty] private string _entryTitle = "";
    [ObservableProperty] private string _entryText = "";
    [ObservableProperty] private string? _status;

    public bool IsEmpty => Bases.Count == 0;
    public bool HasSelection => Selected is not null;

    partial void OnSelectedChanged(KnowledgeBase? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(Entries));
    }

    public IReadOnlyList<KnowledgeEntry> Entries =>
        Selected?.Entries.OrderByDescending(e => e.CreatedAt).ToArray() ?? [];

    [RelayCommand]
    private void Reload()
    {
        var previous = Selected?.Id;

        Bases.Clear();
        foreach (var kb in _services.Knowledge.List()) Bases.Add(kb);

        Selected = previous is null
            ? Bases.FirstOrDefault()
            : Bases.FirstOrDefault(b => b.Id == previous) ?? Bases.FirstOrDefault();

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void CreateBase()
    {
        var name = NewBaseName.Trim();
        if (name.Length == 0) return;

        var created = _services.Knowledge.Create(name);
        NewBaseName = "";
        Reload();
        Selected = Bases.FirstOrDefault(b => b.Id == created.Id);
    }

    [RelayCommand]
    private void DeleteBase(KnowledgeBase? target)
    {
        if (target is null) return;

        _services.Knowledge.Delete(target.Id);
        Reload();
    }

    [RelayCommand]
    private async Task AddEntryAsync()
    {
        if (Selected is null) return;

        var title = EntryTitle.Trim();
        var text = EntryText.Trim();
        if (title.Length == 0 || text.Length == 0) return;

        await _services.Knowledge.AddEntryAsync(Selected.Id, title, text, source: "user").ConfigureAwait(true);

        EntryTitle = "";
        EntryText = "";
        Status = L["common.saved"];

        Reload();
        OnPropertyChanged(nameof(Entries));
    }

    [RelayCommand]
    private void RemoveEntry(KnowledgeEntry? entry)
    {
        if (Selected is null || entry is null) return;

        _services.Knowledge.RemoveEntry(Selected.Id, entry.Id);
        OnPropertyChanged(nameof(Entries));
    }
}
