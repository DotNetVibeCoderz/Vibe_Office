using System.Collections.ObjectModel;
using AutoWork.Core.Knowledge;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using AutoWork.Tools;
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

    /// <summary>
    /// Supplied by the window, which is the only thing that can reach a TopLevel. Same
    /// arrangement as the folder picker in Settings, so the view model stays free of Avalonia.
    /// </summary>
    public Func<Task<IReadOnlyList<string>>>? PickFiles { get; set; }

    /// <summary>
    /// Imports one or more documents as notes: the file's text becomes the note, its name
    /// becomes the title.
    ///
    /// Deliberately not routed through <c>PathGuard</c>. The sandbox exists to bound what the
    /// *agent* may reach on its own; here the user has picked a specific file in their own file
    /// dialog, which is an explicit act of granting. Refusing it because the folder was never
    /// added in Settings would make the guard look arbitrary without making anything safer.
    /// </summary>
    [RelayCommand]
    private async Task AddFromFileAsync()
    {
        if (Selected is null || PickFiles is null) return;

        var paths = await PickFiles().ConfigureAwait(true);
        if (paths.Count == 0) return;

        var added = 0;
        var problems = new List<string>();

        foreach (var path in paths)
        {
            var result = await DocumentText.ReadAsync(path).ConfigureAwait(true);

            if (!result.Success)
            {
                problems.Add($"{Path.GetFileName(path)}: {result.Error}");
                continue;
            }

            await _services.Knowledge
                .AddEntryAsync(Selected.Id, DocumentText.TitleFromFileName(path), result.Text, source: path)
                .ConfigureAwait(true);

            added++;
        }

        // Report both halves. A silent partial import is the failure mode worth avoiding when
        // someone selects twelve files and two of them are scanned PDFs.
        Status = problems.Count == 0
            ? string.Format(L["knowledge.imported"], added)
            : $"{string.Format(L["knowledge.imported"], added)} {string.Join(" · ", problems)}";

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
