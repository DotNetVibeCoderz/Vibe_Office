namespace VibeDesk.Ui.Services;

public enum ToastKind { Success, Error, Warn, Info }

public sealed record Toast(Guid Id, ToastKind Kind, string Message, int DurationMs);

/// <summary>
/// Transient user feedback. Scoped per circuit so one user's toasts never reach another's browser.
/// </summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];

    public IReadOnlyList<Toast> Toasts => _toasts;

    public event Action? Changed;

    public void Success(string message, int durationMs = 3200) => Show(ToastKind.Success, message, durationMs);
    public void Info(string message, int durationMs = 3200) => Show(ToastKind.Info, message, durationMs);
    public void Warn(string message, int durationMs = 4500) => Show(ToastKind.Warn, message, durationMs);

    /// <summary>Errors linger: the user needs time to read what went wrong.</summary>
    public void Error(string message, int durationMs = 6000) => Show(ToastKind.Error, message, durationMs);

    /// <summary>
    /// Turns an exception into a message the user can act on. Our own exception types carry
    /// user-facing text; anything else is unexpected and gets a generic line instead of a stack trace.
    /// </summary>
    public void Error(Exception exception)
    {
        var message = exception switch
        {
            Application.Abstractions.ValidationException v => v.Message,
            Application.Abstractions.ForbiddenException f => f.Message,
            Application.Abstractions.NotFoundException n => n.Message,
            UnauthorizedAccessException => "Please sign in and try again.",
            _ => "Something went wrong. Please try again.",
        };

        Error(message);
    }

    private void Show(ToastKind kind, string message, int durationMs)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var toast = new Toast(Guid.NewGuid(), kind, message, durationMs);
        _toasts.Add(toast);
        Changed?.Invoke();

        // Auto-dismiss without blocking the caller. Awaiting here would stall whatever raised it.
        _ = DismissLaterAsync(toast);
    }

    private async Task DismissLaterAsync(Toast toast)
    {
        await Task.Delay(toast.DurationMs);
        Dismiss(toast.Id);
    }

    public void Dismiss(Guid id)
    {
        var removed = _toasts.RemoveAll(t => t.Id == id);
        if (removed > 0) Changed?.Invoke();
    }
}
