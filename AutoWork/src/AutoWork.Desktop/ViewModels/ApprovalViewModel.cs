using AutoWork.Core.Diff;
using AutoWork.Core.Security;
using AutoWork.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

/// <summary>
/// One line of a diff. The marker is exposed as three booleans rather than as an enum with a
/// converter, so the styling classes follow a live theme switch like everything else does.
/// </summary>
public sealed class DiffLineViewModel
{
    public DiffLineViewModel(DiffLine line) => Line = line;

    public DiffLine Line { get; }

    public string Text => Line.Text;

    public bool IsAdded => Line.Marker == DiffMarker.Added;
    public bool IsRemoved => Line.Marker == DiffMarker.Removed;
    public bool IsSkipped => Line.Marker == DiffMarker.Skipped;

    /// <summary>The gutter character, so a copied diff still reads correctly as text.</summary>
    public string Sign => Line.Marker switch
    {
        DiffMarker.Added => "+",
        DiffMarker.Removed => "−",
        DiffMarker.Skipped => "⋯",
        _ => " ",
    };
}

/// <summary>
/// An inline consent card rather than a modal dialog.
///
/// Modals train people to dismiss them. Showing the request in the flow of the tape — with the
/// actual command or file list visible, not summarised away — keeps the decision attached to
/// its context, which is the only way consent means anything.
/// </summary>
public sealed partial class ApprovalViewModel : ObservableObject
{
    private readonly PendingApproval _pending;
    private readonly Action _onAnswered;

    public ApprovalViewModel(PendingApproval pending, Action onAnswered)
    {
        _pending = pending;
        _onAnswered = onAnswered;
    }

    public ApprovalRequest Request => _pending.Request;

    public string Title => Request.Title;
    public string Detail => Request.Detail;
    public string Kind => Request.Kind.ToString();

    public bool HasDetail => !string.IsNullOrWhiteSpace(Request.Detail);
    public bool HasPaths => Request.AffectedPaths.Count > 0;

    public string Paths => string.Join('\n', Request.AffectedPaths);

    // ── Diff preview ──────────────────────────────────────────────────────────────────────

    public bool HasPreview => Request.Preview is not null;

    public bool HasDiffLines => Request.Preview is { Lines.Count: > 0 };

    public bool HasPreviewNote => !string.IsNullOrWhiteSpace(Request.Preview?.Note);

    public string PreviewNote => Request.Preview?.Note ?? "";

    /// <summary>"+12 −3". Empty when the preview is only a note.</summary>
    public string PreviewSummary => Request.Preview is { Lines.Count: > 0 } p ? p.Summary : "";

    public IReadOnlyList<DiffLineViewModel> DiffLines => Request.Preview is null
        ? []
        : [.. Request.Preview.Lines.Select(l => new DiffLineViewModel(l))];

    /// <summary>
    /// "Allow for this run" is offered only for repeatable, non-destructive kinds. Deleting and
    /// running commands are asked about every time — a standing grant is exactly the wrong
    /// affordance for an irreversible action.
    /// </summary>
    public bool CanAllowForRun => Request.Kind
        is ApprovalKind.WriteFiles
        or ApprovalKind.NetworkAccess
        or ApprovalKind.CaptureScreen;

    [RelayCommand]
    private void Allow() => Answer(ApprovalDecision.Approved);

    [RelayCommand]
    private void AllowForRun() => Answer(ApprovalDecision.ApprovedForRun);

    [RelayCommand]
    private void Deny() => Answer(ApprovalDecision.Denied);

    private void Answer(ApprovalDecision decision)
    {
        _pending.Answer(decision);
        _onAnswered();
    }
}
