using Avalonia.Media;
using Avalonia.Styling;

namespace HelixExplorer.Services;

/// <summary>Tracks active application theme, surface accent colour, and per-folder colour overrides.</summary>
public interface IThemeService
{
    ThemeVariant Current { get; }
    Color Accent { get; }
    IReadOnlyDictionary<string, Color> FolderColors { get; }

    event EventHandler? ThemeChanged;

    void SetTheme(ThemeVariant variant);
    void ToggleTheme();
    void SetAccent(Color color);
    void SetFolderColor(string path, Color color);
    bool TryGetFolderColor(string path, out Color color);
    Color GetFolderColor(string path, Color fallback);
}