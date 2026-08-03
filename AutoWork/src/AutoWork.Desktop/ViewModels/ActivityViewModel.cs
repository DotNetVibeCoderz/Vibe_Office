using System.Collections.ObjectModel;
using System.Diagnostics;
using AutoWork.Core;
using AutoWork.Core.Logging;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>
/// The transparency log, made readable.
///
/// The spec asks for every action to be recorded; this is the half that makes that mean
/// something to a person. It streams live while a run is going, because a log you have to
/// refresh to trust is not much of a safeguard.
/// </summary>
public sealed partial class ActivityViewModel : ObservableObject
{
    private const int MaxRows = 500;

    private readonly AppServices _services;

    public ActivityViewModel(AppServices services)
    {
        _services = services;
        _services.ActionLog.Appended += OnAppended;
        Load();
    }

    public Strings L => _services.Strings;

    public ObservableCollection<ActionLogEntry> Entries { get; } = [];

    [ObservableProperty] private string _filter = "";

    public bool IsEmpty => Entries.Count == 0;

    partial void OnFilterChanged(string value) => Load();

    [RelayCommand]
    private void Load()
    {
        Entries.Clear();

        var query = _services.ActionLog.Read(MaxRows);

        if (!string.IsNullOrWhiteSpace(Filter))
        {
            query = query.Where(e =>
                e.Summary.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                e.Action.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                e.Paths.Any(p => p.Contains(Filter, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var entry in query) Entries.Add(entry);

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(AppPaths.LogsDirectory);

    private void OnAppended(ActionLogEntry entry) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrWhiteSpace(Filter)) return;

            Entries.Insert(0, entry);
            while (Entries.Count > MaxRows) Entries.RemoveAt(Entries.Count - 1);

            OnPropertyChanged(nameof(IsEmpty));
        });

    /// <summary>Opens a folder in the platform's file manager.</summary>
    internal static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            var (file, arguments) =
                OperatingSystem.IsWindows() ? ("explorer.exe", path)
                : OperatingSystem.IsMacOS() ? ("open", path)
                : ("xdg-open", path);

            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = OperatingSystem.IsWindows() });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // Opening a file manager is a convenience; failing to is not worth an error dialog.
        }
    }

    internal static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
