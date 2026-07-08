using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Services;

public sealed class AvaloniaAccentBrushService : IAccentBrushService
{
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

        var isDark = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
        var accent = AccentColorDefaults.Resolve(CustomAccentArgb, isDark);
        var (a, r, g, b) = AccentColorDefaults.ToComponents(accent);
        var color = Color.FromArgb(a, r, g, b);

        resources["HelixAccentBrush"] = new SolidColorBrush(color);
        resources["HelixSelectionBarBrush"] = new SolidColorBrush(color);
        resources["HelixFocusBorderBrush"] = new SolidColorBrush(color);
        resources["HelixSelectionFillBrush"] = new SolidColorBrush(Color.FromArgb(32, r, g, b));
    }
}
