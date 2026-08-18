using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Scripting.Triggers;

/// <summary>
/// Turns every audit entry into a candidate script trigger.
/// </summary>
/// <remarks>
/// Decorating <see cref="IActivityService"/> rather than editing Drive, Calendar and the rest is the
/// whole trick here: those services already log <c>item.created</c>, <c>item.shared</c>,
/// <c>event.created</c> and the rest on exactly the actions worth reacting to. One registration adds
/// event triggers to all of them, and none of them has to learn that scripts exist.
/// </remarks>
public sealed class ScriptAwareActivityService(
    IActivityService inner,
    IScriptEventDispatcher dispatcher) : IActivityService
{
    public async Task LogAsync(
        string action, Guid? driveItemId = null, string? detail = null, CancellationToken ct = default)
    {
        await inner.LogAsync(action, driveItemId, detail, ct).ConfigureAwait(false);

        // After, not before: a trigger fires on something that happened, and the dispatcher swallows
        // its own failures so an automation cannot break the action that produced it.
        await dispatcher.DispatchAsync(action, driveItemId, detail, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ActivityDto>> ForItemAsync(
        Guid itemId, int take = 50, CancellationToken ct = default) =>
        inner.ForItemAsync(itemId, take, ct);

    public Task<IReadOnlyList<ActivityDto>> ForUserAsync(int take = 50, CancellationToken ct = default) =>
        inner.ForUserAsync(take, ct);
}

/// <summary>
/// Raises <c>content.saved</c> when a document's body changes.
/// </summary>
/// <remarks>
/// This one is not covered by the activity log: <see cref="IDocumentContentService"/> writes content
/// without an audit entry, and "the file was edited" is the trigger people reach for first. The
/// collaboration notifier is already called on every accepted save, so decorating it costs nothing
/// and needs no change to the save path.
/// </remarks>
public sealed class ScriptAwareCollaborationNotifier(
    ICollaborationNotifier inner,
    IScriptEventDispatcher dispatcher) : ICollaborationNotifier
{
    public async Task ContentChangedAsync(
        Guid itemId,
        long revision,
        Guid byUserId,
        string? patchJson = null,
        CancellationToken ct = default)
    {
        await inner.ContentChangedAsync(itemId, revision, byUserId, patchJson, ct).ConfigureAwait(false);

        await dispatcher
            .DispatchAsync("content.saved", itemId, $"revision {revision}", ct)
            .ConfigureAwait(false);
    }

    public async Task CommentsChangedAsync(Guid itemId, CancellationToken ct = default)
    {
        await inner.CommentsChangedAsync(itemId, ct).ConfigureAwait(false);

        await dispatcher.DispatchAsync("comments.changed", itemId, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Not a trigger source. Presence fires on every cursor move, and a script that ran on each of
    /// them would be a denial-of-service against its own workspace.
    /// </summary>
    public Task PresenceAsync(
        Guid itemId,
        Guid userId,
        string displayName,
        string? cursorJson,
        CancellationToken ct = default) =>
        inner.PresenceAsync(itemId, userId, displayName, cursorJson, ct);
}
