using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using HelixExplorer.Infrastructure;

namespace HelixExplorer.Services;

/// <summary>
/// Owns the application theme. Defers to Avalonia's
/// <see cref="Application.RequestedThemeVariant"/> for the base dark/light switch and
/// maintains a <see cref="Color"/> palette for accents and per-folder colours. The
/// accent is pushed into <see cref="Application.Resources"/> so every
/// <c>{DynamicResource HelixAccentBrush}</c> consumer updates live. State is persisted
/// to <c>theme.json</c>.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly Color s_defaultAccent = Color.FromArgb(0xFF, 0xFF, 0x8C, 0x00);
    private static readonly Color s_darkSurface = Color.FromArgb(0xFF, 0x1E, 0x1E, 0x24);
    private static readonly Color s_lightSurface = Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF5);
    private static readonly string s_configPath = AppPaths.File("theme.json");
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private readonly Dictionary<string, Color> _folderColors = new(StringComparer.OrdinalIgnoreCase);
    private Color _accent = s_defaultAccent;
    private ThemeVariant _current = ThemeVariant.Dark;
    private bool _followSystem = true;

    public ThemeService()
    {
        Load();
        // Reflect restored theme + accent into the running application.
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = _current;
            ApplyAccentResources();
        }
    }

    public ThemeVariant Current => _current;
    public Color Accent => _accent;
    public IReadOnlyDictionary<string, Color> FolderColors => _folderColors;

    public bool FollowSystemTheme
    {
        get => _followSystem;
        set { if (_followSystem != value) { _followSystem = value; Persist(); } }
    }

    public event EventHandler? ThemeChanged;

    public void SetTheme(ThemeVariant variant)
    {
        if (_current == variant) return;
        _current = variant;
        // An explicit choice from the UI opts out of following the system.
        _followSystem = false;
        if (Application.Current is { } app) app.RequestedThemeVariant = variant;
        ApplyAccentResources();
        Persist();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleTheme() =>
        SetTheme(_current == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark);

    public void SetAccent(Color color)
    {
        if (_accent == color) return;
        _accent = color;
        ApplyAccentResources();
        Persist();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFolderColor(string path, Color color)
    {
        if (string.IsNullOrEmpty(path)) return;
        _folderColors[path] = color;
        Persist();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFolderColor(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (_folderColors.Remove(path))
        {
            Persist();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TryGetFolderColor(string path, out Color color) => _folderColors.TryGetValue(path, out color);

    public Color GetFolderColor(string path, Color fallback) =>
        _folderColors.TryGetValue(path, out var c) ? c : fallback;

    public void NotifySystemThemeChanged(bool isDark)
    {
        if (!_followSystem) return;
        var variant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (_current == variant) return;
        _current = variant;
        if (Application.Current is { } app) app.RequestedThemeVariant = variant;
        ApplyAccentResources();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Surface colour appropriate to the current theme.</summary>
    public Color SurfaceColor => _current == ThemeVariant.Dark ? s_darkSurface : s_lightSurface;

    /// <summary>Pushes the accent colour into application resources so DynamicResource bindings refresh.</summary>
    private void ApplyAccentResources()
    {
        if (Application.Current is not { } app) return;
        app.Resources["HelixAccent"] = _accent;
        app.Resources["HelixAccentBrush"] = new SolidColorBrush(_accent);
    }

    // ---- persistence ----

    private sealed class ThemeConfig
    {
        public string Variant { get; set; } = "Dark";
        public bool FollowSystem { get; set; } = true;
        public uint AccentArgb { get; set; }
        public Dictionary<string, uint> FolderColors { get; set; } = new();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(s_configPath)) return;
            var cfg = JsonSerializer.Deserialize<ThemeConfig>(File.ReadAllText(s_configPath), s_json);
            if (cfg is null) return;

            _current = cfg.Variant.Equals("Light", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            _followSystem = cfg.FollowSystem;
            if (cfg.AccentArgb != 0) _accent = Color.FromUInt32(cfg.AccentArgb);
            foreach (var kv in cfg.FolderColors)
            {
                _folderColors[kv.Key] = Color.FromUInt32(kv.Value);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ThemeService.Load failed: {ex.Message}");
        }
    }

    private void Persist()
    {
        try
        {
            var cfg = new ThemeConfig
            {
                Variant = _current == ThemeVariant.Light ? "Light" : "Dark",
                FollowSystem = _followSystem,
                AccentArgb = _accent.ToUInt32(),
            };
            foreach (var kv in _folderColors) cfg.FolderColors[kv.Key] = kv.Value.ToUInt32();
            File.WriteAllText(s_configPath, JsonSerializer.Serialize(cfg, s_json));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ThemeService.Persist failed: {ex.Message}");
        }
    }
}
