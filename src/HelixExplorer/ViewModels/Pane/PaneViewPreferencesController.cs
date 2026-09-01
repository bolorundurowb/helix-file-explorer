using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.ViewModels.Pane;

public sealed class PaneViewPreferencesController(
    IFolderViewPreferencesService folderPreferences,
    AppSettingsCoordinator settings)
{
    public AppSettings CurrentSettings => settings.Load();

    public FolderViewPreferences Resolve(string path)
    {
        if (folderPreferences.TryGet(path, out var preferences))
            return preferences;

        var defaults = settings.Load();
        return new FolderViewPreferences
        {
            ViewMode = defaults.DefaultViewMode,
            SortColumn = SortColumn.Name,
            SortDescending = false,
            DirectorySort = defaults.DirectorySort,
            ThumbnailSize = defaults.DefaultThumbnailSize,
            GroupBy = GroupByMode.None
        };
    }

    public void Save(string path, FolderViewPreferences preferences)
        => folderPreferences.Set(path, preferences);
}
