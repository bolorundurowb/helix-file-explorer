using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HelixExplorer.Converters;

public sealed class BoolToFailureBrushConverter : IValueConverter
{
    public static BoolToFailureBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
        {
            if (Application.Current?.TryGetResource("HelixDangerBrush", Application.Current.ActualThemeVariant, out var danger) == true)
                return danger;

            return new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
        }

        if (Application.Current?.TryGetResource("HelixEntryForegroundBrush", Application.Current.ActualThemeVariant, out var brush) == true)
            return brush;

        return Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}