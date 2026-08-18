using VibeDesk.Domain;

namespace VibeDesk.Ui.Services;

/// <summary>
/// Chrome state that outlives navigation: sidebar collapse, the Clippy dock, theme, and which
/// document the assistant should treat as context. Scoped per circuit.
/// </summary>
public sealed class UiState
{
    public event Action? Changed;

    private bool _sidebarCollapsed;
    private bool _clippyOpen;
    private bool _drawerOpen;
    private string _theme = "system";

    public bool SidebarCollapsed
    {
        get => _sidebarCollapsed;
        set => Set(ref _sidebarCollapsed, value);
    }

    public bool ClippyOpen
    {
        get => _clippyOpen;
        set => Set(ref _clippyOpen, value);
    }

    /// <summary>Mobile sidebar drawer. Separate from collapse so the two don't fight on resize.</summary>
    public bool DrawerOpen
    {
        get => _drawerOpen;
        set => Set(ref _drawerOpen, value);
    }

    /// <summary>light | dark | system</summary>
    public string Theme
    {
        get => _theme;
        set => Set(ref _theme, value);
    }

    // ── assistant context ───────────────────────────────────────────────────
    // Set by each editor as it opens so Clippy can answer about what the user is actually looking at.

    public Guid? ActiveItemId { get; private set; }
    public string? ActiveItemName { get; private set; }
    public DriveItemType? ActiveItemType { get; private set; }

    /// <summary>drive | docs | sheets | slides | calendar</summary>
    public string ActiveApp { get; private set; } = "drive";

    public void SetContext(
        string app,
        Guid? itemId = null,
        string? itemName = null,
        DriveItemType? itemType = null)
    {
        // No notification here: this is called during a page's initialisation, and raising Changed
        // mid-render would re-enter the shell's render for no visible benefit.
        ActiveApp = app;
        ActiveItemId = itemId;
        ActiveItemName = itemName;
        ActiveItemType = itemType;
    }

    public void ToggleSidebar() => SidebarCollapsed = !SidebarCollapsed;
    public void ToggleClippy() => ClippyOpen = !ClippyOpen;
    public void ToggleDrawer() => DrawerOpen = !DrawerOpen;

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke();
    }
}
