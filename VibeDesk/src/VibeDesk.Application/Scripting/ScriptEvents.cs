namespace VibeDesk.Application.Scripting;

/// <summary>The event names a trigger can subscribe to, for the editor's picker.</summary>
public static class ScriptEvents
{
    public static readonly IReadOnlyList<(string Name, string Description)> All =
    [
        ("content.saved", "A document, spreadsheet or presentation was saved"),
        ("item.created", "A file or folder was created"),
        ("item.uploaded", "A file was uploaded"),
        ("item.renamed", "An item was renamed"),
        ("item.moved", "An item was moved"),
        ("item.copied", "An item was copied"),
        ("item.trashed", "An item was moved to trash"),
        ("item.restored", "An item was restored from trash"),
        ("item.shared", "An item was shared with someone"),
        ("item.unshared", "A share was revoked"),
        ("item.*", "Any Drive event"),
        ("comments.changed", "A comment or suggestion changed"),
        ("suggestion.accepted", "A suggestion was accepted into a document"),
        ("version.saved", "A version snapshot was taken"),
        ("version.restored", "A version was restored"),
        ("event.created", "A calendar event was created"),
        ("event.updated", "A calendar event was changed"),
        ("event.deleted", "A calendar event was deleted"),
        ("event.*", "Any calendar event change"),
        ("calendar.importedFromSheet", "Events were imported from a spreadsheet"),
    ];

    /// <summary>
    /// Matches a subscription against an event name. A trailing <c>*</c> matches a whole family, so
    /// <c>item.*</c> covers every Drive action without listing them.
    /// </summary>
    public static bool Matches(string? subscription, string eventName)
    {
        if (string.IsNullOrWhiteSpace(subscription)) return false;

        if (subscription.EndsWith('*'))
        {
            return eventName.StartsWith(subscription[..^1], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(subscription, eventName, StringComparison.OrdinalIgnoreCase);
    }
}
