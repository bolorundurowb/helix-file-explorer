namespace HelixExplorer.Core.Settings;

public interface IFolderViewPreferencesService
{
    bool TryGet(string path, out FolderViewPreferences preferences);

    void Set(string path, FolderViewPreferences preferences);

    void Remove(string path);
}
