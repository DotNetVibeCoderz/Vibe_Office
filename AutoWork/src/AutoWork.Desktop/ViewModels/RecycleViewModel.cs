using System.Collections.ObjectModel;
using AutoWork.Core.Storage;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>One recycled item, formatted for the list.</summary>
public sealed partial class RecycledItemViewModel : ObservableObject
{
    public required RecycledItem Item { get; init; }

    /// <summary>Set after a failed restore, so the reason sits on the row that caused it.</summary>
    [ObservableProperty] private string? _note;

    /// <summary>Shown instead of Restore when the original path is occupied.</summary>
    [ObservableProperty] private bool _needsOverwrite;

    public string Id => Item.Id;
    public string Name => Item.Name;
    public bool CanRestore => Item.CanRestore;

    public string Origin => Item.CanRestore
        ? AutoWork.Core.Security.PathGuard.Describe(Item.OriginalPath)
        : "";

    public string When => Item.DeletedAt.LocalDateTime.Date == DateTime.Today
        ? Item.DeletedAt.LocalDateTime.ToString("HH:mm")
        : Item.DeletedAt.LocalDateTime.ToString("d MMM, HH:mm");

    public string Size => Item.SizeBytes switch
    {
        < 1024 => $"{Item.SizeBytes} B",
        < 1024 * 1024 => $"{Item.SizeBytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{Item.SizeBytes / (1024.0 * 1024):0.#} MB",
        _ => $"{Item.SizeBytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    partial void OnNoteChanged(string? value) => OnPropertyChanged(nameof(HasNote));
}

/// <summary>
/// The soft-delete folder, made usable.
///
/// Deleting through the agent has always been recoverable in principle — the file was moved into
/// AutoWork's own folder rather than removed. In practice "recoverable" meant browsing a dated
/// folder full of timestamped names and guessing where each came from. This page is the other
/// half of that promise.
/// </summary>
public sealed partial class RecycleViewModel : ObservableObject
{
    private readonly AppServices _services;

    public RecycleViewModel(AppServices services) => _services = services;

    public Strings L => _services.Strings;

    public ObservableCollection<RecycledItemViewModel> Items { get; } = [];

    [ObservableProperty] private string? _status;

    public bool IsEmpty => Items.Count == 0;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    /// <summary>Surfaced because an empty bin means something different when soft delete is off.</summary>
    public bool SoftDeleteOff => !_services.Config.Current.Permissions.SoftDelete;

    public string TotalSize
    {
        get
        {
            var bytes = Items.Sum(i => i.Item.SizeBytes);

            return bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
                < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
                _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
            };
        }
    }

    [RelayCommand]
    public void Reload()
    {
        Items.Clear();
        foreach (var item in _services.Recycle.List())
            Items.Add(new RecycledItemViewModel { Item = item });

        Status = null;
        RaiseFlags();
    }

    [RelayCommand]
    private void Restore(RecycledItemViewModel? row) => RestoreCore(row, overwrite: false);

    /// <summary>
    /// The second click, after the first reported that something is already there. Deliberately a
    /// separate button rather than a silent overwrite: replacing a file the user has since
    /// recreated is exactly the surprise this page exists to prevent.
    /// </summary>
    [RelayCommand]
    private void RestoreOver(RecycledItemViewModel? row) => RestoreCore(row, overwrite: true);

    private void RestoreCore(RecycledItemViewModel? row, bool overwrite)
    {
        if (row is null) return;

        var result = _services.Recycle.Restore(row.Id, overwrite);

        if (result.Status == RestoreStatus.Restored)
        {
            Items.Remove(row);
            Status = result.Message;
            RaiseFlags();
            return;
        }

        row.Note = result.Message;
        row.NeedsOverwrite = result.Status == RestoreStatus.Occupied;

        // A missing item is stale UI, not an error the user can act on — drop the row.
        if (result.Status == RestoreStatus.Missing)
        {
            Items.Remove(row);
            RaiseFlags();
        }
    }

    [RelayCommand]
    private void Purge(RecycledItemViewModel? row)
    {
        if (row is null) return;

        if (_services.Recycle.Purge(row.Id))
        {
            Items.Remove(row);
            RaiseFlags();
        }
        else
        {
            row.Note = L["recycle.purge.failed"];
        }
    }

    [RelayCommand]
    private void EmptyBin()
    {
        var count = _services.Recycle.PurgeAll();
        Reload();
        Status = string.Format(L["recycle.emptied"], count);
    }

    [RelayCommand]
    private void OpenFolder() => ActivityViewModel.OpenFolder(AutoWork.Core.AppPaths.RecycleDirectory);

    private void RaiseFlags()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(TotalSize));
        OnPropertyChanged(nameof(SoftDeleteOff));
    }
}
