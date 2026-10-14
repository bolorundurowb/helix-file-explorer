using Avalonia;
using Avalonia.Styling;
using HelixExplorer.Core.Theming;
using HelixExplorer.Windows.Theming;

namespace HelixExplorer.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    /// <summary>
    /// Name foreground bindings include this resource so they re-run after a theme apply.
    /// Re-applying <see cref="ThemeMode.System"/> assigns <see cref="ThemeVariant.Default"/> again,
    /// which does not change <c>ActualThemeVariant</c>.
    /// </summary>
    public const string ThemeRevisionResourceKey = "HelixThemeRevision";

    public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;

    public int ThemeRevision { get; private set; }

    /// <summary>
    /// Light or Dark variant name brushes should resolve against. Updated once per apply so row
    /// bindings do not each read the OS theme.
    /// </summary>
    internal static ThemeVariant EffectiveVariant { get; private set; } = ThemeVariant.Light;

    public event Action<ThemeMode>? ThemeChanged;

    public AvaloniaThemeService()
    {
        PublishRevision();
    }

    public void ApplyTheme(ThemeMode mode)
    {
        CurrentMode = mode;

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }

        // Always bump, including when the mode is already System. RequestedThemeVariant stays
        // Default across an OS light/dark switch, so ActualThemeVariant does not notify.
        ThemeRevision++;
        PublishRevision();
        ThemeChanged?.Invoke(mode);
    }

    private void PublishRevision()
    {
        EffectiveVariant = ResolveEffectiveVariant();
        if (Application.Current is null)
            return;

        Application.Current.Resources[ThemeRevisionResourceKey] = ThemeRevision;
    }

    private static ThemeVariant ResolveEffectiveVariant()
    {
        var requested = Application.Current?.RequestedThemeVariant;
        if (requested == ThemeVariant.Light || requested == ThemeVariant.Dark)
            return requested;

        // System mode leaves the request at Default. ActualThemeVariant does not track the OS
        // flip, so the app theme bit is the variant name brushes must use.
        return WinThemeWatcher.TryReadAppsUseLightTheme() switch
        {
            0 => ThemeVariant.Dark,
            1 => ThemeVariant.Light,
            _ => EffectiveVariant
        };
    }
}
