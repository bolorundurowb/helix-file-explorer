using HelixExplorer.Core.Settings;

namespace HelixExplorer.Services;

public sealed class FolderViewPreferencesService : IFolderViewPreferencesService
{
    private readonly ISettingsStore _settingsStore;
    private readonly Dictionary<string, FolderViewPreferences> _prefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public FolderViewPreferencesService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var settings = _settingsStore.Load();
        foreach (var (path, prefs) in settings.FolderViewPreferences)
            _prefs[path] = Clone(prefs);
    }

    public bool TryGet(string path, out FolderViewPreferences preferences)
    {
        preferences = new FolderViewPreferences();
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return false;

        lock (_gate)
        {
            if (!_prefs.TryGetValue(normalized, out var stored))
                return false;

            preferences = Clone(stored);
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
            _prefs[normalized] = Clone(preferences);
            Persist();
        }
    }

    public void Remove(string path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        lock (_gate)
        {
            if (!_prefs.Remove(normalized))
                return;

            Persist();
        }
    }

    private void Persist()
    {
        var settings = _settingsStore.Load();
        settings.FolderViewPreferences = _prefs.ToDictionary(
            static kv => kv.Key,
            static kv => Clone(kv.Value),
            StringComparer.OrdinalIgnoreCase);
        _settingsStore.Save(settings);
    }

    private static FolderViewPreferences Clone(FolderViewPreferences source) => new()
    {
        ViewMode = source.ViewMode,
        SortColumn = source.SortColumn,
        SortDescending = source.SortDescending,
        DirectorySort = source.DirectorySort,
        ThumbnailSize = source.ThumbnailSize
    };

    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.TrimEnd('\\', '/');
    }
}
