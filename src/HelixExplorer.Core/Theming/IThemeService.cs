namespace HelixExplorer.Core.Theming;

public interface IThemeService
{
    ThemeMode CurrentMode { get; }
    void ApplyTheme(ThemeMode mode);
    event Action<ThemeMode>? ThemeChanged;
}
