using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia;

namespace HelixExplorer.Converters;

/// <summary>Maps selected=true to HelixSidebarSelectedBrush, otherwise Transparent.</summary>
public sealed class BoolToSidebarBrushConverter : IValueConverter
{
    public static BoolToSidebarBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
            return Brushes.Transparent;

        if (Application.Current?.TryGetResource("HelixSidebarSelectedBrush",
                Application.Current.ActualThemeVariant, out var brush) == true)
            return brush;

        return new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x78, 0xD4));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
