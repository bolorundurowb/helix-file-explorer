using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.Services;

public sealed class FolderColorService : IFolderColorService
{
    private readonly IFolderColorStore _store;
    private readonly Dictionary<string, uint> _colors = new(StringComparer.OrdinalIgnoreCase);

    public FolderColorService(IFolderColorStore store)
    {
        _store = store;
        // Colors are looked up per visible item by converters; load once so listing never hits SQLite.
        foreach (var (path, color) in store.LoadAll())
            _colors[path] = color;
    }

    public event EventHandler? ColorsChanged;

    public bool TryGetColor(string path, out uint argb)
    {
        argb = 0;
        var normalized = Normalize(path);
        return !string.IsNullOrEmpty(normalized) && _colors.TryGetValue(normalized, out argb);
    }

    public void SetColor(string path, uint argb)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        _colors[normalized] = argb;
        _store.Upsert(normalized, argb);
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveColor(string path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        if (!_colors.Remove(normalized))
            return;

        _store.Delete(normalized);
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.TrimEnd('\\', '/');
    }
}
