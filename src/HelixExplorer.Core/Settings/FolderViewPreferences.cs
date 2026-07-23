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
}
