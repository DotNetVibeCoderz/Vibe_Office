using System.Collections.ObjectModel;
using AutoWork.Core.Agents;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoWork.Desktop.ViewModels;

public enum TapeKind
{
    Step,
    Note,
    Compaction,
    SubAgent,
    Result,
}

/// <summary>One tool call, shown nested under the step that made it.</summary>
public sealed partial class TapeToolViewModel : ObservableObject
{
    [ObservableProperty] private string _tool = "";
    [ObservableProperty] private string _arguments = "";
    [ObservableProperty] private string? _result;
    [ObservableProperty] private bool _failed;
    [ObservableProperty] private bool _running = true;
    [ObservableProperty] private string _duration = "";

    public AgentOrgan Organ { get; init; }

    // Styling classes rather than a converter, so the hue follows a live theme switch.
    public bool IsThink => Organ == AgentOrgan.Brain;
    public bool IsSee => Organ == AgentOrgan.Eyes;
    public bool IsAct => Organ == AgentOrgan.Hands;
}

/// <summary>
/// A row on the Work Tape — the vertical spine that is the run view's whole story.
///
/// Steps are numbered because agent steps genuinely are a sequence, and the order carries
/// information the reader needs: what had to happen before what. Each row is stamped with the
/// organ responsible, which is what makes a glance at the tape tell you whether AutoWork is
/// currently thinking, looking, or touching your files.
/// </summary>
public sealed partial class TapeEntryViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _detail;
    [ObservableProperty] private StepStatus _status = StepStatus.Running;
    [ObservableProperty] private string _duration = "";

    public required TapeKind Kind { get; init; }
    public required AgentOrgan Organ { get; init; }

    /// <summary>The mono stamp down the spine: "01", "02". Empty for non-step rows.</summary>
    public string Stamp { get; init; } = "";

    public int Index { get; init; }

    public TapeEntryViewModel() =>
        Tools.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTools));

    public ObservableCollection<TapeToolViewModel> Tools { get; } = [];

    public bool IsThink => Organ == AgentOrgan.Brain;
    public bool IsSee => Organ == AgentOrgan.Eyes;
    public bool IsAct => Organ == AgentOrgan.Hands;

    public bool IsRunning => Status == StepStatus.Running;
    public bool IsDone => Status == StepStatus.Succeeded;
    public bool IsFailed => Status == StepStatus.Failed;

    public bool IsStep => Kind == TapeKind.Step;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool HasTools => Tools.Count > 0;

    partial void OnStatusChanged(StepStatus value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsFailed));
    }

    partial void OnDetailChanged(string? value) => OnPropertyChanged(nameof(HasDetail));
}
