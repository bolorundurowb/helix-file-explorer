using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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

    private const string HighContrastKey = "HighContrast";

    // Both resolve the same "HighContrast" theme dictionary (variants compare by key); they differ only
    // in which dictionary supplies the resources a contrast theme does not remap.
    public static readonly ThemeVariant HighContrastLight = new(HighContrastKey, ThemeVariant.Light);
    public static readonly ThemeVariant HighContrastDark = new(HighContrastKey, ThemeVariant.Dark);

    private readonly IHighContrastSource? _highContrast;

    public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;

    public int ThemeRevision { get; private set; }

    /// <summary>
    /// Light, Dark, or contrast variant name brushes should resolve against. Updated once per apply
    /// so row bindings do not each read the OS theme.
    /// </summary>
    internal static ThemeVariant EffectiveVariant { get; private set; } = ThemeVariant.Light;

    public event Action<ThemeMode>? ThemeChanged;

    public AvaloniaThemeService(IHighContrastSource? highContrast = null)
    {
        _highContrast = highContrast;
        PublishRevision();
    }

    internal static bool IsHighContrast(ThemeVariant? variant) => Equals(variant?.Key, HighContrastKey);

    public void ApplyTheme(ThemeMode mode)
    {
        CurrentMode = mode;

        if (Application.Current is { } app)
        {
            if (_highContrast?.TryReadActivePalette() is { } palette)
            {
                ApplyHighContrastPalette(app, palette);
                var variant = palette.IsDarkSurface ? HighContrastDark : HighContrastLight;

                // The two contrast variants are equal by key, so a light-to-dark contrast switch would
                // not register as a change and the fallback dictionary would stay stale.
                if (IsHighContrast(app.RequestedThemeVariant)
                    && app.RequestedThemeVariant!.InheritVariant != variant.InheritVariant)
                {
                    app.RequestedThemeVariant = ThemeVariant.Default;
                }

                app.RequestedThemeVariant = variant;
            }
            else
            {
                app.RequestedThemeVariant = mode switch
                {
                    ThemeMode.Light => ThemeVariant.Light,
                    ThemeMode.Dark => ThemeVariant.Dark,
                    _ => ThemeVariant.Default
                };
            }
        }

        // Always bump, including when the mode is already System. RequestedThemeVariant stays
        // Default across an OS light/dark switch, so ActualThemeVariant does not notify.
        ThemeRevision++;
        PublishRevision();
        ThemeChanged?.Invoke(mode);
    }

    private static void ApplyHighContrastPalette(Application app, HighContrastPalette palette)
    {
        if (!app.Resources.ThemeDictionaries.TryGetValue(HighContrastDark, out var provider)
            || provider is not ResourceDictionary dictionary)
        {
            return;
        }

        // Recolour in place: the contrast dictionary aliases these brush instances, so one write
        // updates every mapped key without replacing resources under live DynamicResource bindings.
        SetColor(dictionary, "HelixSystemWindowBrush", palette.Window);
        SetColor(dictionary, "HelixSystemWindowTextBrush", palette.WindowText);
        SetColor(dictionary, "HelixSystemHighlightBrush", palette.Highlight);
        SetColor(dictionary, "HelixSystemHighlightTextBrush", palette.HighlightText);
        SetColor(dictionary, "HelixSystemGrayTextBrush", palette.GrayText);
        SetColor(dictionary, "HelixSystemHotlightBrush", palette.Hotlight);
    }

    private static void SetColor(ResourceDictionary dictionary, string key, uint argb)
    {
        if (dictionary.TryGetValue(key, out var value) && value is SolidColorBrush brush)
            brush.Color = Color.FromUInt32(argb);
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
        if (requested == ThemeVariant.Light || requested == ThemeVariant.Dark || IsHighContrast(requested))
            return requested!;

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
