using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Settings;

/// <summary>
/// Per-directory view overrides. When absent for a path, global defaults from <see cref="AppSettings"/> apply.
/// </summary>
public sealed class FolderViewPreferences
{
    public LayoutMode ViewMode { get; set; } = LayoutMode.Details;

    public SortColumn SortColumn { get; set; } = SortColumn.Name;

    public bool SortDescending { get; set; }

    public DirectorySortMode DirectorySort { get; set; } = DirectorySortMode.MixedWithFiles;

    public double ThumbnailSize { get; set; } = 72;

    /// <summary>Grid-only grouping. Other layouts ignore this and stay flat.</summary>
    public GroupByMode GroupBy { get; set; } = GroupByMode.None;

    /// <summary>
    /// Stable <see cref="FileGroupBucket.Key"/> values the user has collapsed. Kept per folder rather
    /// than per session so a collapsed group is still collapsed when the path is revisited.
    /// </summary>
    public List<string> CollapsedGroupKeys { get; set; } = [];
}
