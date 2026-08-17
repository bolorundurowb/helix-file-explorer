using HelixExplorer.Core.Settings;

namespace HelixExplorer.Core.Persistence;

/// <summary>
/// Point-lookup store for per-folder view preferences, backed by SQLite.
/// Paths are pre-normalized by the caller (trimmed of trailing separators).
/// </summary>
public interface IFolderViewPreferencesStore
{
    bool TryGet(string normalizedPath, out FolderViewPreferences preferences);

    void Upsert(string normalizedPath, FolderViewPreferences preferences);

    void Delete(string normalizedPath);
}
