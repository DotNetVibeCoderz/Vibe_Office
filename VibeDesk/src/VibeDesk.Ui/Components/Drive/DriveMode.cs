namespace VibeDesk.Ui.Components.Drive;

/// <summary>
/// Which Drive view is on screen. The available actions differ per view — a trashed item offers
/// restore rather than share — so the card and row components branch on this.
/// </summary>
public enum DriveMode
{
    Folder = 0,
    Shared = 1,
    Starred = 2,
    Trash = 3,
}
