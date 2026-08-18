using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Domain;

namespace VibeDesk.Api.Hubs;

/// <summary>
/// Real-time co-editing. One group per Drive item, joined only after a permission check — group
/// membership *is* the authorisation boundary here, so a client that has not passed
/// <see cref="IPermissionService"/> never receives another editor's keystrokes.
/// </summary>
[Authorize]
public sealed class CollaborationHub(
    IPermissionService permissions,
    ICurrentUser currentUser,
    ILogger<CollaborationHub> logger) : Hub
{
    public static string GroupFor(Guid itemId) => $"item:{itemId:N}";

    /// <summary>Subscribes the caller to an item's change feed. Requires at least Viewer.</summary>
    public async Task JoinItem(Guid itemId)
    {
        // Throws NotFoundException when the caller has no access, which the API translates to 404 —
        // an item you cannot see must not be distinguishable from one that does not exist.
        await permissions.RequireAsync(itemId, PermissionRole.Viewer);

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(itemId));

        await Clients.OthersInGroup(GroupFor(itemId)).SendAsync(
            "PresenceJoined",
            new { itemId, userId = currentUser.Id, displayName = currentUser.DisplayName });
    }

    public async Task LeaveItem(Guid itemId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(itemId));

        await Clients.OthersInGroup(GroupFor(itemId)).SendAsync(
            "PresenceLeft",
            new { itemId, userId = currentUser.Id });
    }

    /// <summary>
    /// Broadcasts a cursor or selection position. Deliberately not persisted: presence is worthless
    /// the moment it is stale, and writing it would put a database round trip in the hot path.
    /// </summary>
    public Task Presence(Guid itemId, string? cursorJson) =>
        Clients.OthersInGroup(GroupFor(itemId)).SendAsync(
            "Presence",
            new
            {
                itemId,
                userId = currentUser.Id,
                displayName = currentUser.DisplayName,
                cursorJson,
            });

    /// <summary>
    /// Relays a typing patch to other editors. The authoritative save still goes through the REST
    /// endpoint with its revision check — this is a latency shortcut, never the source of truth.
    /// </summary>
    public async Task Typing(Guid itemId, long revision, string patchJson)
    {
        await permissions.RequireAsync(itemId, PermissionRole.Editor);

        await Clients.OthersInGroup(GroupFor(itemId)).SendAsync(
            "ContentChanged",
            new { itemId, revision, byUserId = currentUser.Id, patchJson });
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
        {
            logger.LogDebug(exception, "Collaboration connection {ConnectionId} dropped", Context.ConnectionId);
        }

        // Groups are cleaned up by SignalR; peers learn of the departure from their own transport.
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// The server-side half of <see cref="ICollaborationNotifier"/>. Registered by the API host so that
/// saves made through REST reach editors connected over the hub.
/// </summary>
public sealed class SignalRCollaborationNotifier(IHubContext<CollaborationHub> hub) : ICollaborationNotifier
{
    public Task ContentChangedAsync(
        Guid itemId,
        long revision,
        Guid byUserId,
        string? patchJson = null,
        CancellationToken ct = default) =>
        hub.Clients.Group(CollaborationHub.GroupFor(itemId)).SendAsync(
            "ContentChanged",
            new { itemId, revision, byUserId, patchJson },
            ct);

    public Task CommentsChangedAsync(Guid itemId, CancellationToken ct = default) =>
        hub.Clients.Group(CollaborationHub.GroupFor(itemId)).SendAsync(
            "CommentsChanged",
            new { itemId },
            ct);

    public Task PresenceAsync(
        Guid itemId,
        Guid userId,
        string displayName,
        string? cursorJson,
        CancellationToken ct = default) =>
        hub.Clients.Group(CollaborationHub.GroupFor(itemId)).SendAsync(
            "Presence",
            new { itemId, userId, displayName, cursorJson },
            ct);
}
