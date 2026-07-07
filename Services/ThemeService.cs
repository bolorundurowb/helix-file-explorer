using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace HelixExplorer.Services;

/// <summary>
/// Owns the application theme. Defers to Avalonia's
/// <see cref="Application.RequestedThemeVariant"/> for the base dark/light switch
/// and maintains a <see cref="Color"/> palette for accents and per-folder colours.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly Color s_defaultAccent = Color.FromArgb(0xFF, 0xFF, 0x8C, 0x00);

    private static readonly Color s_darkSurface = Color.FromArgb(0xFF, 0x1E, 0x1E, 0x24);
    private static readonly Color s_lightSurface = Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF5);

    private readonly Dictionary<string, Color> _folderColors = new(StringComparer.OrdinalIgnoreCase);
    private Color _accent = s_defaultAccent;
    private ThemeVariant _current = ThemeVariant.Dark;

    public ThemeService()
    {
        // Pick up whatever Avalonia starts with so we don't fight the host.
        if (Application.Current is { } app)
        {
            _current = app.RequestedThemeVariant ?? ThemeVariant.Default;
        }
    }

    /// <inheritdoc />
    public ThemeVariant Current => _current;

    /// <inheritdoc />
    public Color Accent => _accent;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, Color> FolderColors => _folderColors;

    /// <inheritdoc />
    public event EventHandler? ThemeChanged;

    /// <inheritdoc />
    public void SetTheme(ThemeVariant variant)
    {
        if (_current == variant) return;
        _current = variant;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = variant;
        }
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ToggleTheme()
    {
        SetTheme(_current == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark);
    }

    /// <inheritdoc />
    public void SetAccent(Color color)
    {
        if (_accent == color) return;
        _accent = color;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SetFolderColor(string path, Color color)
    {
        if (string.IsNullOrEmpty(path)) return;
        _folderColors[path] = color;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool TryGetFolderColor(string path, out Color color) => _folderColors.TryGetValue(path, out color);

    /// <inheritdoc />
    public Color GetFolderColor(string path, Color fallback) => _folderColors.TryGetValue(path, out Color c) ? c : fallback;

    /// <summary>Surface colour appropriate to the current theme. Handy for code-behind converters.</summary>
    public Color SurfaceColor => _current == ThemeVariant.Dark ? s_darkSurface : s_lightSurface;

    internal void SaveDurably(string path)
    {
        Debug.WriteLine($"ThemeService persistence stub: would save {path}");
    }
}