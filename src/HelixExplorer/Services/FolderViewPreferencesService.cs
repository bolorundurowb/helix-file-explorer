using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.Services;

public sealed class FolderViewPreferencesService : IFolderViewPreferencesService
{
    private readonly IFolderViewPreferencesStore _store;
    private readonly Dictionary<string, FolderViewPreferences> _prefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public FolderViewPreferencesService(IFolderViewPreferencesStore store)
    {
        _store = store;
    }

    public bool TryGet(string path, out FolderViewPreferences preferences)
    {
        preferences = new FolderViewPreferences();
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return false;

        lock (_gate)
        {
            if (_prefs.TryGetValue(normalized, out var cached))
            {
                preferences = Clone(cached);
                return true;
            }

            if (!_store.TryGet(normalized, out var fromStore))
                return false;

            // Write-through cache: populate on first miss so navigation never hits SQLite twice.
            _prefs[normalized] = Clone(fromStore);
            preferences = Clone(fromStore);
            return true;
        }
    }

    public void Set(string path, FolderViewPreferences preferences)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        lock (_gate)
        {
            var copy = Clone(preferences);
            _prefs[normalized] = copy;
            _store.Upsert(normalized, copy);
        }
    }

    public void Remove(string path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        lock (_gate)
        {
            _prefs.Remove(normalized);
            _store.Delete(normalized);
        }
    }

    private static FolderViewPreferences Clone(FolderViewPreferences source) => new()
    {
        ViewMode = source.ViewMode,
        SortColumn = source.SortColumn,
        SortDescending = source.SortDescending,
        DirectorySort = source.DirectorySort,
        ThumbnailSize = source.ThumbnailSize,
        GroupBy = source.GroupBy,
        CollapsedGroupKeys = [.. source.CollapsedGroupKeys]
    };

    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.TrimEnd('\\', '/');
    }
}
