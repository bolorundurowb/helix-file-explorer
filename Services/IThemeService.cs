using Avalonia.Media;
using Avalonia.Styling;

namespace HelixExplorer.Services;

/// <summary>Tracks active application theme, surface accent colour, and per-folder colour overrides.</summary>
public interface IThemeService
{
    ThemeVariant Current { get; }
    Color Accent { get; }
    IReadOnlyDictionary<string, Color> FolderColors { get; }

    /// <summary>When true, the app follows the OS light/dark setting automatically.</summary>
    bool FollowSystemTheme { get; set; }

    event EventHandler? ThemeChanged;

    void SetTheme(ThemeVariant variant);
    void ToggleTheme();
    void SetAccent(Color color);
    void SetFolderColor(string path, Color color);
    void RemoveFolderColor(string path);
    bool TryGetFolderColor(string path, out Color color);
    Color GetFolderColor(string path, Color fallback);

    /// <summary>Called by the OS theme listener. Applies the new variant when following the system.</summary>
    void NotifySystemThemeChanged(bool isDark);
}
