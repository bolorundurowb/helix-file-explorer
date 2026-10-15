using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Services;

public sealed class AvaloniaAccentBrushService(IAccentColorSource accentColors) : IAccentBrushService
{
    private static readonly string[] AccentKeys =
    [
        "HelixAccentBrush",
        "HelixSelectionBarBrush",
        "HelixFocusBorderBrush",
        "HelixSettingsNavAccentBar",
        "HelixToggleOnBrush",
        "HelixProgressBrush",
        "HelixAccentForegroundBrush",
        "HelixSelectionFillBrush",
        "ToggleSwitchFillOn",
        "ToggleSwitchFillOnPointerOver",
        "ToggleSwitchFillOnPressed",
        "ToggleSwitchStrokeOn",
        "ToggleSwitchStrokeOnPointerOver",
        "ToggleSwitchStrokeOnPressed",
    ];

    public uint? CustomAccentArgb { get; private set; }

    public event Action? AccentChanged;

    public void ApplyCustomAccent(uint? argb)
    {
        CustomAccentArgb = argb;
        UpdateBrushes();
        AccentChanged?.Invoke();
    }

    private void UpdateBrushes()
    {
        if (Application.Current?.Resources is not ResourceDictionary resources)
            return;

        // Root keys outrank theme dictionaries, so a custom accent would hide the contrast colours.
        if (AvaloniaThemeService.IsHighContrast(AvaloniaThemeService.EffectiveVariant))
        {
            foreach (var key in AccentKeys)
                resources.Remove(key);
            return;
        }

        var isDark = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
        var system = CustomAccentArgb is null ? accentColors.TryReadAccentColor() : null;
        var accent = AccentColorDefaults.Resolve(CustomAccentArgb, system);
        var (a, r, g, b) = AccentColorDefaults.ToComponents(accent);
        var color = Color.FromArgb(a == 0 ? (byte)0xFF : a, r, g, b);
        var accentBrush = new SolidColorBrush(color);
        var foreground = AccentColorDefaults.ForegroundFor(accent);
        var (_, fr, fg, fb) = AccentColorDefaults.ToComponents(foreground);
        var tint = AccentColorDefaults.SelectionTintFor(accent, isDark);
        Color fillColor;
        if (tint.Neutral)
        {
            var channel = isDark ? (byte)255 : (byte)0;
            fillColor = Color.FromArgb(tint.Alpha, channel, channel, channel);
        }
        else
        {
            fillColor = Color.FromArgb(tint.Alpha, r, g, b);
        }

        resources["HelixAccentBrush"] = accentBrush;
        resources["HelixSelectionBarBrush"] = accentBrush;
        resources["HelixFocusBorderBrush"] = accentBrush;
        resources["HelixSettingsNavAccentBar"] = accentBrush;
        resources["HelixToggleOnBrush"] = accentBrush;
        resources["HelixProgressBrush"] = accentBrush;
        resources["HelixAccentForegroundBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, fr, fg, fb));
        resources["HelixSelectionFillBrush"] = new SolidColorBrush(fillColor);
        resources["ToggleSwitchFillOn"] = accentBrush;
        resources["ToggleSwitchFillOnPointerOver"] = accentBrush;
        resources["ToggleSwitchFillOnPressed"] = accentBrush;
        resources["ToggleSwitchStrokeOn"] = accentBrush;
        resources["ToggleSwitchStrokeOnPointerOver"] = accentBrush;
        resources["ToggleSwitchStrokeOnPressed"] = accentBrush;
    }
}
