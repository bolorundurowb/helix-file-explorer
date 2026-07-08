using Avalonia;
using Avalonia.Styling;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;

    public event Action<ThemeMode>? ThemeChanged;

    public void ApplyTheme(ThemeMode mode)
    {
        CurrentMode = mode;

        if (Application.Current is null)
            return;

        var variant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        Application.Current.RequestedThemeVariant = variant;
        ThemeChanged?.Invoke(mode);
    }
}
