namespace HelixExplorer.Models;

/// <summary>Serialisable snapshot of a single pane's view state.</summary>
public sealed class PaneState
{
    public string Path { get; set; } = string.Empty;
    /// <summary>Cast of <c>LayoutMode</c>.</summary>
    public int ViewMode { get; set; }
    public string SortColumn { get; set; } = "Name";
    public bool SortDescending { get; set; }
}

/// <summary>Serialisable snapshot of one tab (its two panes + layout).</summary>
public sealed class TabState
{
    public string Title { get; set; } = string.Empty;
    public bool IsDualPane { get; set; }
    public bool VerticalSplit { get; set; }
    /// <summary>0 = left pane active, 1 = right pane active.</summary>
    public int ActivePaneIndex { get; set; }
    public PaneState Left { get; set; } = new();
    public PaneState Right { get; set; } = new();
    /// <summary>Tab tint colour as packed ARGB. 0 means "no tint".</summary>
    public uint TintArgb { get; set; }
}

/// <summary>Top-level session document persisted to <c>session.json</c> between launches.</summary>
public sealed class SessionState
{
    public List<TabState> Tabs { get; set; } = new();
    public int ActiveTabIndex { get; set; }
    public List<string> RecentPaths { get; set; } = new();
    public bool SidebarOpen { get; set; } = true;
}
