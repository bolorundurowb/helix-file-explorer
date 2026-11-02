using HelixExplorer.Core.Persistence;

namespace HelixExplorer.Services;

public sealed class FolderIconService : IFolderIconService
{
    private readonly IFolderIconStore _store;
    private readonly Lazy<Dictionary<string, string>> _icons;

    public FolderIconService(IFolderIconStore store)
    {
        _store = store;
        // Defer the SQLite load until the first lookup so singleton construction does not require
        // an already-initialised database (e.g. in headless/DI tests).
        _icons = new Lazy<Dictionary<string, string>>(LoadAll, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public event EventHandler<string>? IconsChanged;

    public bool TryGetIconFile(string path, out string iconFilePath)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized) || !_icons.Value.TryGetValue(normalized, out var value))
        {
            iconFilePath = string.Empty;
            return false;
        }

        iconFilePath = value;
        return true;
    }

    public void SetIcon(string path, string iconFilePath)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        _icons.Value[normalized] = iconFilePath;
        _store.Upsert(normalized, iconFilePath);
        IconsChanged?.Invoke(this, normalized);
    }

    public void RemoveIcon(string path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        if (!_icons.Value.Remove(normalized))
            return;

        _store.Delete(normalized);
        IconsChanged?.Invoke(this, normalized);
    }

    private Dictionary<string, string> LoadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, iconFile) in _store.LoadAll())
            result[path] = iconFile;
        return result;
    }

    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.TrimEnd('\\', '/');
    }
}
