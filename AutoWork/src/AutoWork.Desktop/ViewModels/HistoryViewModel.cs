using System.Collections.ObjectModel;
using AutoWork.Core.Agents;
using AutoWork.Core.Runs;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>One past run, formatted for the list.</summary>
public sealed class RunHistoryItemViewModel
{
    public required RunHistoryEntry Entry { get; init; }
    public required string StatusLabel { get; init; }

    public string Id => Entry.Id;
    public string Goal => Entry.Goal;
    public string? Model => Entry.ModelDisplayName;

    public bool Succeeded => Entry.Status == RunStatus.Succeeded;
    public bool Failed => Entry.Status == RunStatus.Failed;

    /// <summary>Today's runs show the time; older ones show the date, which is what people scan by.</summary>
    public string When => Entry.StartedAt.LocalDateTime.Date == DateTime.Today
        ? Entry.StartedAt.LocalDateTime.ToString("HH:mm")
        : Entry.StartedAt.LocalDateTime.ToString("d MMM, HH:mm");

    public string Duration => Entry.ElapsedMs switch
    {
        < 1000 => $"{Entry.ElapsedMs} ms",
        < 60_000 => $"{Entry.ElapsedMs / 1000.0:0.0} s",
        _ => $"{Entry.ElapsedMs / 60_000}m {Entry.ElapsedMs % 60_000 / 1000}s",
    };

    public string Counts => $"{Entry.TotalSteps} · {Entry.ToolCalls}";

    public bool HasUsage => Entry.TotalTokens > 0;

    public string Tokens => Entry.UsageComplete
        ? $"{Entry.TotalTokens:N0} tok"
        : $"≥ {Entry.TotalTokens:N0} tok";

    public bool HasCost => Entry.Cost is not null;
    public string Cost => Entry.Cost is { } c ? $"{c:0.####} {Entry.Currency}" : "";

    public string Summary => string.IsNullOrWhiteSpace(Entry.Summary) ? Entry.Error ?? "" : Entry.Summary;
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
}

/// <summary>
/// Past runs: what was asked, what happened, and a way to set the same job going again.
///
/// Opening a run hands its stored events back to the Work Tape rather than rendering them here,
/// so a run you look at later reads exactly as it did while it ran.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly WorkViewModel _work;
    private readonly Action _showWork;

    public HistoryViewModel(AppServices services, WorkViewModel work, Action showWork)
    {
        _services = services;
        _work = work;
        _showWork = showWork;
    }

    public Strings L => _services.Strings;

    public ObservableCollection<RunHistoryItemViewModel> Runs { get; } = [];

    public bool HasRuns => Runs.Count > 0;

    /// <summary>Surfaced so an empty list does not read as "nothing ran" when it means "nothing kept".</summary>
    public bool HistoryDisabled => !_services.Config.Current.KeepRunHistory;

    [RelayCommand]
    public void Reload()
    {
        Runs.Clear();

        foreach (var entry in _services.History.List())
            Runs.Add(new RunHistoryItemViewModel { Entry = entry, StatusLabel = Describe(entry.Status) });

        OnPropertyChanged(nameof(HasRuns));
        OnPropertyChanged(nameof(HistoryDisabled));
    }

    [RelayCommand]
    private void Open(RunHistoryItemViewModel? item)
    {
        if (item is null) return;

        var transcript = _services.History.Load(item.Id);

        // Pruned or deleted from under us — drop the row rather than leaving a dead button.
        if (transcript is null)
        {
            Runs.Remove(item);
            OnPropertyChanged(nameof(HasRuns));
            return;
        }

        _work.ShowTranscript(transcript);
        _showWork();
    }

    [RelayCommand]
    private void ReRun(RunHistoryItemViewModel? item)
    {
        if (item is null || _work.IsRunning) return;

        // The goal is filled in and the run started, rather than only filled in: "run it again"
        // that needs a second click somewhere else is not what the button says.
        _work.Goal = item.Goal;
        _showWork();

        if (_work.StartCommand.CanExecute(null)) _work.StartCommand.Execute(null);
    }

    [RelayCommand]
    private void Delete(RunHistoryItemViewModel? item)
    {
        if (item is null) return;

        _services.History.Delete(item.Id);
        Runs.Remove(item);
        OnPropertyChanged(nameof(HasRuns));
    }

    private string Describe(RunStatus status) => status switch
    {
        RunStatus.Succeeded => L["history.status.succeeded"],
        RunStatus.Failed => L["history.status.failed"],
        _ => L["history.status.cancelled"],
    };
}
