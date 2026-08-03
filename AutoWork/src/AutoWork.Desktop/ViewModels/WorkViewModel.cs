using System.Collections.ObjectModel;
using System.Diagnostics;
using AutoWork.Core.Agents;
using AutoWork.Core.Automation;
using AutoWork.Core.Runs;
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
    private RunController? _controller;

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

    /// <summary>True from the click until the run actually reaches a step boundary and stops.</summary>
    [ObservableProperty] private bool _isPausing;

    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _correction = "";
    [ObservableProperty] private bool _correctionQueued;

    /// <summary>Set while the tape is showing a saved run rather than a live one.</summary>
    [ObservableProperty] private bool _isViewingHistory;

    /// <summary>Says which saved job started this run, and why. Null for a run you typed.</summary>
    [ObservableProperty] private string? _jobNote;

    public bool HasJobNote => !string.IsNullOrWhiteSpace(JobNote);

    private string? _nextJobNote;

    // ── Meter ─────────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private string? _tokenNote;
    [ObservableProperty] private string? _costNote;

    public bool HasUsage => !string.IsNullOrWhiteSpace(TokenNote);

    /// <summary>
    /// Shown only when a price is set on the model. There is no built-in price table, so a run
    /// against an unpriced model reports tokens and says nothing about money — which is honest,
    /// and better than a confident figure derived from a number nobody checked.
    /// </summary>
    public bool HasCost => !string.IsNullOrWhiteSpace(CostNote);

    public bool HasTape => Tape.Count > 0;
    public bool HasPlan => PlanSteps.Count > 0;
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    public bool HasApprovals => Approvals.Count > 0;

    public bool HasModel => _services.Config.Current.ResolveExecutorModel() is not null;
    public bool CanStart => !IsRunning && !string.IsNullOrWhiteSpace(Goal) && HasModel;

    /// <summary>Set while a past run is on the tape, so the readout shows its time, not zero.</summary>
    private TimeSpan? _frozenElapsed;

    public string Elapsed
    {
        get
        {
            var elapsed = _frozenElapsed ?? _stopwatch.Elapsed;

            return elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"m\:ss");
        }
    }

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
        _controller = new RunController();

        var progress = new Progress<RunEvent>(Apply);

        try
        {
            await _services.Orchestrator.RunAsync(goal, progress, _cancellation.Token, _controller).ConfigureAwait(true);
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
            _controller = null;

            IsPausing = false;
            IsPaused = false;
            CorrectionQueued = false;
        }
    }

    /// <summary>
    /// Starts a saved job as if the user had typed it.
    ///
    /// Deliberately the same path as a manual run — same tape, same consent cards, same history
    /// — because a scheduled run that behaves differently from one you watched is a scheduled run
    /// you cannot trust. It refuses while something else is going rather than queuing: two runs
    /// competing for one set of consent prompts is worse than a missed occurrence.
    /// </summary>
    public async Task RunJobAsync(SavedJob job, string reason)
    {
        if (IsRunning) return;

        Goal = job.Goal;
        _nextJobNote = string.Format(L["work.job.running"], job.DisplayName, reason);

        try
        {
            await StartAsync().ConfigureAwait(true);
        }
        finally
        {
            // Written back so the Jobs page can show what happened without keeping its own log.
            var updated = job.Clone();
            updated.LastRunAt = DateTimeOffset.Now;
            updated.LastOutcome = string.IsNullOrWhiteSpace(Summary) ? L["work.job.finished"] : Trim(Summary, 120);

            _services.Jobs.Save(updated);
        }
    }

    private static string Trim(string text, int max)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    [RelayCommand]
    private void Stop()
    {
        _cancellation?.Cancel();

        // A paused run is parked on the controller, not on the token, so releasing it is what
        // actually lets the cancellation be noticed.
        _controller?.Abandon();

        // Anything blocked on a consent prompt has to be released, or the agent thread parks
        // forever waiting for an answer that is never coming.
        _services.Approvals.ReleaseAll();
    }

    /// <summary>
    /// Asks the run to stop at the next step boundary. It cannot take effect immediately: a step
    /// in flight may have a tool call already sent, and abandoning that would leave the
    /// conversation — and possibly a half-written file — in a state nothing could resume from.
    /// </summary>
    [RelayCommand]
    private void Pause()
    {
        if (_controller is null || IsPaused) return;

        _controller.Pause();
        IsPausing = true;
    }

    [RelayCommand]
    private void Resume()
    {
        if (_controller is null) return;

        var correction = Correction.Trim();

        _controller.Resume(correction.Length == 0 ? null : correction);

        Correction = "";
        CorrectionQueued = false;
        IsPaused = false;
        IsPausing = false;
    }

    /// <summary>
    /// Hands over a correction without waiting for the run to stop. The orchestrator picks it up
    /// at the next boundary, so a user who spots the mistake early does not have to pause first.
    /// </summary>
    [RelayCommand]
    private void Steer()
    {
        var correction = Correction.Trim();
        if (_controller is null || correction.Length == 0) return;

        _controller.Steer(correction);

        Correction = "";
        CorrectionQueued = true;
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
        TokenNote = null;
        CostNote = null;
        CompletedSteps = 0;
        HasVerification = false;
        ActiveOrgan = null;
        IsViewingHistory = false;

        // Carried through the reset that starting a run performs, then consumed — so a job run
        // keeps its label and everything else clears it.
        JobNote = _nextJobNote;
        _nextJobNote = null;
        OnPropertyChanged(nameof(HasJobNote));

        Correction = "";
        CorrectionQueued = false;
        _currentStep = null;
        _frozenElapsed = null;

        RaiseCollectionFlags();
        OnPropertyChanged(nameof(HasUsage));
        OnPropertyChanged(nameof(HasCost));
    }

    /// <summary>
    /// Rebuilds the Work Tape from a saved run.
    ///
    /// It replays the stored events through the same handler a live run uses, rather than through
    /// a second read-only renderer. One renderer means a past run looks exactly like the run
    /// looked while it was happening — and there is no second place for the two to drift apart.
    /// </summary>
    public void ShowTranscript(RunTranscript transcript)
    {
        if (IsRunning) return;

        Reset();

        Goal = transcript.Entry.Goal;
        ModelName = transcript.Entry.ModelDisplayName;

        foreach (var evt in transcript.Events) Apply(evt);

        // Replay leaves the live-run flags set as they were at the moment each event fired.
        IsPaused = false;
        IsPausing = false;
        ActiveOrgan = null;
        _currentStep = null;

        IsViewingHistory = true;
        _frozenElapsed = TimeSpan.FromMilliseconds(transcript.Entry.ElapsedMs);

        OnPropertyChanged(nameof(Elapsed));
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

            // The running total, not the fragment: an update that arrives out of order — or a
            // view that started watching late — still shows the whole reply so far.
            case AssistantDeltaEvent delta when _currentStep is not null:
                _currentStep.Detail = delta.Text;
                break;

            case AssistantMessageEvent message when _currentStep is not null:
                _currentStep.Detail = message.Text;
                break;

            case UsageEvent usage:
                // "at least" when a provider answered some calls without reporting usage — the
                // total is then a floor, and presenting it as exact would be a small lie that
                // compounds over a long run.
                TokenNote = usage.CallsWithoutUsage > 0
                    ? string.Format(L["work.tokens.atleast"], usage.TotalTokens)
                    : $"{usage.TotalTokens:N0}";

                CostNote = usage.Cost is { } cost ? $"{cost:0.####} {usage.Currency}" : null;

                OnPropertyChanged(nameof(HasUsage));
                OnPropertyChanged(nameof(HasCost));
                break;

            case RunPausedEvent paused:
                IsPausing = false;
                IsPaused = true;
                ActiveOrgan = null;

                Tape.Add(new TapeEntryViewModel
                {
                    Kind = TapeKind.Note,
                    Organ = AgentOrgan.Brain,
                    Stamp = "‖",
                    Title = L["work.paused"],
                    Detail = paused.BeforeStep > 0 ? string.Format(L["work.paused.before"], paused.BeforeStep) : null,
                    Status = StepStatus.Pending,
                });
                RaiseCollectionFlags();
                break;

            case RunResumedEvent resumed:
                IsPaused = false;
                IsPausing = false;
                CorrectionQueued = false;

                // Only worth a row when the user actually said something; a bare resume is
                // already obvious from the next step appearing.
                if (!string.IsNullOrWhiteSpace(resumed.Correction))
                {
                    Tape.Add(new TapeEntryViewModel
                    {
                        Kind = TapeKind.Note,
                        Organ = AgentOrgan.Brain,
                        Stamp = "✎",
                        Title = L["work.corrected"],
                        Detail = resumed.Correction,
                        Status = StepStatus.Succeeded,
                    });
                    RaiseCollectionFlags();
                }
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
