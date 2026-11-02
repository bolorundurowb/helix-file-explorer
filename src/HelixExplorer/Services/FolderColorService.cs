using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.Services;

public sealed class FolderColorService : IFolderColorService
{
    private readonly IFolderColorStore _store;
    private readonly Lazy<Dictionary<string, uint>> _colors;

    public FolderColorService(IFolderColorStore store)
    {
        _store = store;
        // Defer the SQLite load until the first lookup so singleton construction does not require
        // an already-initialised database (e.g. in headless/DI tests).
        _colors = new Lazy<Dictionary<string, uint>>(LoadAll, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public event EventHandler? ColorsChanged;

    public bool TryGetColor(string path, out uint argb)
    {
        argb = 0;
        var normalized = Normalize(path);
        return !string.IsNullOrEmpty(normalized) && _colors.Value.TryGetValue(normalized, out argb);
    }

    public void SetColor(string path, uint argb)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        _colors.Value[normalized] = argb;
        _store.Upsert(normalized, argb);
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveColor(string path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        if (!_colors.Value.Remove(normalized))
            return;

        _store.Delete(normalized);
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private Dictionary<string, uint> LoadAll()
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, color) in _store.LoadAll())
            result[path] = color;
        return result;
    }

    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.TrimEnd('\\', '/');
    }
}
