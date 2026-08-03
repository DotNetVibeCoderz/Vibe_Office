using System.Collections.ObjectModel;
using System.Diagnostics;
using AutoWork.Core.Agents;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>
/// The run view: take a request, show the plan, then show the work happening on the Work Tape.
///
/// Everything the orchestrator emits arrives on a background thread and is marshalled here, in
/// one place, so no other view model has to think about threading.
/// </summary>
public sealed partial class WorkViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _ticker;
    private readonly Stopwatch _stopwatch = new();

    private CancellationTokenSource? _cancellation;
    private TapeEntryViewModel? _currentStep;

    public WorkViewModel(AppServices services)
    {
        _services = services;

        _services.Approvals.Requested += OnApprovalRequested;
        _services.Approvals.Resolved += OnApprovalResolved;

        // Drives the elapsed readout. One second is enough — a faster tick would just burn
        // battery redrawing a number nobody reads that closely.
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => OnPropertyChanged(nameof(Elapsed));
    }

    public Strings L => _services.Strings;

    public ObservableCollection<TapeEntryViewModel> Tape { get; } = [];
    public ObservableCollection<ApprovalViewModel> Approvals { get; } = [];
    public ObservableCollection<string> PlanSteps { get; } = [];

    [ObservableProperty] private string _goal = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private string? _modelName;
    [ObservableProperty] private int _completedSteps;
    [ObservableProperty] private string? _planNotes;
    [ObservableProperty] private bool _verificationPassed = true;
    [ObservableProperty] private bool _hasVerification;
    [ObservableProperty] private AgentOrgan? _activeOrgan;
    [ObservableProperty] private string? _compactionNote;

    public bool HasTape => Tape.Count > 0;
    public bool HasPlan => PlanSteps.Count > 0;
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    public bool HasApprovals => Approvals.Count > 0;

    public bool HasModel => _services.Config.Current.ResolveExecutorModel() is not null;
    public bool CanStart => !IsRunning && !string.IsNullOrWhiteSpace(Goal) && HasModel;

    public string Elapsed => _stopwatch.Elapsed.TotalHours >= 1
        ? _stopwatch.Elapsed.ToString(@"h\:mm\:ss")
        : _stopwatch.Elapsed.ToString(@"m\:ss");

    /// <summary>Starter prompts, phrased as things a person would actually ask for.</summary>
    public IReadOnlyList<string> Examples => L.IsIndonesian
        ?
        [
            "Rapikan folder Downloads saya berdasarkan jenis file",
            "Baca semua PDF invoice di folder ini dan buat rekap Excel dengan total",
            "Ubah ukuran semua foto di folder Aset jadi maksimal 1600px",
            "Ringkas catatan rapat di folder ini jadi satu dokumen Word",
        ]
        :
        [
            "Organise my Downloads folder by file type",
            "Read every invoice PDF in this folder and build an Excel summary with totals",
            "Resize every photo in the Assets folder to 1600px on the long edge",
            "Turn the meeting notes in this folder into one Word document",
        ];

    partial void OnGoalChanged(string value) => StartCommand.NotifyCanExecuteChanged();
    partial void OnIsRunningChanged(bool value) => StartCommand.NotifyCanExecuteChanged();

    // ── Commands ──────────────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var goal = Goal.Trim();
        if (goal.Length == 0) return;

        Reset();

        IsRunning = true;
        _stopwatch.Restart();
        _ticker.Start();

        _cancellation = new CancellationTokenSource();

        var progress = new Progress<RunEvent>(Apply);

        try
        {
            await _services.Orchestrator.RunAsync(goal, progress, _cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Summary = "Stopped.";
        }
        catch (Exception ex)
        {
            Summary = ex.Message;
        }
        finally
        {
            IsRunning = false;
            _stopwatch.Stop();
            _ticker.Stop();
            OnPropertyChanged(nameof(Elapsed));
            OnPropertyChanged(nameof(HasSummary));

            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _cancellation?.Cancel();

        // Anything blocked on a consent prompt has to be released, or the agent thread parks
        // forever waiting for an answer that is never coming.
        _services.Approvals.ReleaseAll();
    }

    [RelayCommand]
    private void Clear()
    {
        if (IsRunning) return;
        Reset();
        Goal = "";
    }

    [RelayCommand]
    private void UseExample(string? example)
    {
        if (!string.IsNullOrWhiteSpace(example)) Goal = example;
    }

    private void Reset()
    {
        Tape.Clear();
        PlanSteps.Clear();
        Approvals.Clear();
        Summary = null;
        PlanNotes = null;
        CompactionNote = null;
        CompletedSteps = 0;
        HasVerification = false;
        ActiveOrgan = null;
        _currentStep = null;

        RaiseCollectionFlags();
    }

    // ── Event handling ────────────────────────────────────────────────────────────────────

    private void Apply(RunEvent evt)
    {
        switch (evt)
        {
            case RunStartedEvent started:
                ModelName = started.ModelDisplayName;
                break;

            case PlanReadyEvent ready:
                PlanNotes = ready.Plan.Notes;
                foreach (var step in ready.Plan.Steps)
                    PlanSteps.Add($"{step.Index}. {step.Title}");
                OnPropertyChanged(nameof(HasPlan));
                break;

            case StepStartedEvent step:
                _currentStep = new TapeEntryViewModel
                {
                    Kind = TapeKind.Step,
                    Organ = step.Organ,
                    Index = step.Index,
                    Stamp = step.Index.ToString("00"),
                    Title = step.Title,
                    Status = StepStatus.Running,
                };
                Tape.Add(_currentStep);
                ActiveOrgan = step.Organ;
                RaiseCollectionFlags();
                break;

            case StepFinishedEvent finished:
                var target = Tape.LastOrDefault(t => t.IsStep && t.Index == finished.Index);
                if (target is not null)
                {
                    target.Status = finished.Status;
                    target.Detail = finished.Detail;
                    target.Duration = FormatDuration(finished.ElapsedMs);
                }
                if (finished.Status == StepStatus.Succeeded) CompletedSteps++;
                ActiveOrgan = null;
                break;

            case ToolCallEvent call:
                ApplyToolCall(call);
                break;

            case CompactionEvent compaction:
                CompactionNote =
                    $"{compaction.TokensBefore:N0} → {compaction.TokensAfter:N0} tokens " +
                    $"({compaction.TurnsSummarised} turns summarised)";

                Tape.Add(new TapeEntryViewModel
                {
                    Kind = TapeKind.Compaction,
                    Organ = AgentOrgan.Brain,
                    Title = L["work.compacted"],
                    Detail = CompactionNote,
                    Status = StepStatus.Succeeded,
                });
                RaiseCollectionFlags();
                break;

            case SubAgentEvent sub:
                ApplySubAgent(sub);
                break;

            case AssistantMessageEvent message when _currentStep is not null:
                _currentStep.Detail = message.Text;
                break;

            case RunFinishedEvent done:
                Summary = done.Summary;
                CompletedSteps = done.TotalSteps;

                if (done.Verification is { } verification)
                {
                    HasVerification = true;
                    VerificationPassed = verification.Passed;

                    if (!verification.Passed && verification.UnmetCriteria.Count > 0)
                        Summary += "\n\nNot met:\n" + string.Join('\n', verification.UnmetCriteria.Select(c => "  · " + c));
                }

                OnPropertyChanged(nameof(HasSummary));
                ActiveOrgan = null;
                break;
        }
    }

    private void ApplyToolCall(ToolCallEvent call)
    {
        var step = _currentStep;
        if (step is null) return;

        ActiveOrgan = call.Organ;

        // The opening event has no result; the closing one completes the same row.
        var existing = step.Tools.LastOrDefault(t => t.Tool == call.Tool && t.Running);

        if (call.Result is null)
        {
            if (existing is not null) return;

            step.Tools.Add(new TapeToolViewModel
            {
                Tool = call.Tool,
                Arguments = call.Arguments,
                Organ = call.Organ,
            });

            return;
        }

        var row = existing ?? new TapeToolViewModel { Tool = call.Tool, Arguments = call.Arguments, Organ = call.Organ };
        if (existing is null) step.Tools.Add(row);

        row.Result = call.Result;
        row.Failed = call.Failed;
        row.Running = false;
        row.Duration = FormatDuration(call.ElapsedMs);
    }

    private void ApplySubAgent(SubAgentEvent sub)
    {
        var existing = Tape.LastOrDefault(t => t.Kind == TapeKind.SubAgent && t.Title == sub.Task);

        if (existing is null)
        {
            Tape.Add(new TapeEntryViewModel
            {
                Kind = TapeKind.SubAgent,
                Organ = AgentOrgan.Hands,
                Stamp = "∥",
                Title = sub.Task,
                Status = sub.Status,
                Detail = sub.Result,
            });
            RaiseCollectionFlags();
            return;
        }

        existing.Status = sub.Status;
        if (!string.IsNullOrWhiteSpace(sub.Result)) existing.Detail = sub.Result;
    }

    private void OnApprovalRequested(PendingApproval pending) =>
        Dispatcher.UIThread.Post(() =>
        {
            Approvals.Add(new ApprovalViewModel(pending, RaiseCollectionFlags));
            OnPropertyChanged(nameof(HasApprovals));
        });

    private void OnApprovalResolved(PendingApproval pending) =>
        Dispatcher.UIThread.Post(() =>
        {
            var match = Approvals.FirstOrDefault(a => a.Request.Id == pending.Request.Id);
            if (match is not null) Approvals.Remove(match);
            OnPropertyChanged(nameof(HasApprovals));
        });

    private void RaiseCollectionFlags()
    {
        OnPropertyChanged(nameof(HasTape));
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(HasApprovals));
        OnPropertyChanged(nameof(HasSummary));
    }

    public void RefreshModelAvailability()
    {
        OnPropertyChanged(nameof(HasModel));
        StartCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Examples));
    }

    private static string FormatDuration(long milliseconds) => milliseconds switch
    {
        < 1000 => $"{milliseconds} ms",
        < 60_000 => $"{milliseconds / 1000.0:0.0} s",
        _ => $"{milliseconds / 60_000}m {milliseconds % 60_000 / 1000}s",
    };
}
