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
            return new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));

        if (Application.Current?.TryGetResource("HelixEntryForegroundBrush", Application.Current.ActualThemeVariant, out var brush) == true)
            return brush;

        return Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}