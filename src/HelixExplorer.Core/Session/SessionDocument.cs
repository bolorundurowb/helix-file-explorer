using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Session;

/// <summary>
/// Root of the persisted workspace state (<c>session.json</c>): open tabs, the active tab,
/// and chrome preferences that should survive a restart.
/// </summary>
public sealed class SessionDocument
{
    public List<TabSnapshot> Tabs { get; set; } = [];
    public int ActiveTabIndex { get; set; }
    public bool SidebarOpen { get; set; } = true;
    public double SidebarWidth { get; set; } = 240;
    public List<string> RecentPaths { get; set; } = [];
}

/// <summary>Persisted state for a single tab, including dual-pane layout and tint.</summary>
public sealed class TabSnapshot
{
    public PaneSnapshot LeftPane { get; set; } = new();

    /// <summary>Present only when the tab was showing dual panes.</summary>
    public PaneSnapshot? RightPane { get; set; }

    public bool IsDualPane { get; set; }

    /// <summary>True when the right pane held focus at save time.</summary>
    public bool IsRightPaneActive { get; set; }

    public PaneSplitOrientation Orientation { get; set; } = PaneSplitOrientation.Vertical;

    /// <summary>Optional user-assigned tab tint stored as packed ARGB.</summary>
    public uint? TintArgb { get; set; }
}

/// <summary>Persisted per-pane navigation and presentation state.</summary>
public sealed class PaneSnapshot
{
    public string Path { get; set; } = string.Empty;
    public LayoutMode ViewMode { get; set; } = LayoutMode.Details;
    public SortColumn SortColumn { get; set; } = SortColumn.Name;
    public bool SortDescending { get; set; }
    public double ThumbnailSize { get; set; } = 72;
}
