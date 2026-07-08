using HelixExplorer.Core.Settings;

namespace HelixExplorer.Services;

public sealed class FolderColorService : IFolderColorService
{
    private readonly ISettingsStore _settingsStore;
    private readonly Dictionary<string, uint> _colors = new(StringComparer.OrdinalIgnoreCase);

    public FolderColorService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var settings = _settingsStore.Load();
        foreach (var (path, color) in settings.FolderColors)
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
        Persist();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveColor(string path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            return;

        if (!_colors.Remove(normalized))
            return;

        Persist();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyDictionary<string, uint> GetAll() => _colors;

    private void Persist()
    {
        var settings = _settingsStore.Load();
        settings.FolderColors = new Dictionary<string, uint>(_colors, StringComparer.OrdinalIgnoreCase);
        _settingsStore.Save(settings);
    }

    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.TrimEnd('\\', '/');
    }
}
