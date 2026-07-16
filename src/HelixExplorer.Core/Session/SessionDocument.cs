using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Session;

/// <summary>
/// Chrome preferences (sidebar, theme, etc.) live in <see cref="Settings.AppSettings"/>, not here.
/// </summary>
public sealed class SessionDocument
{
    public List<TabSnapshot> Tabs { get; set; } = [];
    public int ActiveTabIndex { get; set; }
    public List<string> RecentPaths { get; set; } = [];
    public List<string> RecentFiles { get; set; } = [];
}

public sealed class TabSnapshot
{
    public PaneSnapshot LeftPane { get; set; } = new();
    public PaneSnapshot? RightPane { get; set; }
    public bool IsDualPane { get; set; }
    public bool IsRightPaneActive { get; set; }
    public PaneSplitOrientation Orientation { get; set; } = PaneSplitOrientation.Vertical;
    public uint? TintArgb { get; set; }
}

public sealed class PaneSnapshot
{
    public string Path { get; set; } = string.Empty;
    public LayoutMode ViewMode { get; set; } = LayoutMode.Details;
    public SortColumn SortColumn { get; set; } = SortColumn.Name;
    public bool SortDescending { get; set; }
    public DirectorySortMode DirectorySort { get; set; } = DirectorySortMode.MixedWithFiles;
    public double ThumbnailSize { get; set; } = 72;
}
