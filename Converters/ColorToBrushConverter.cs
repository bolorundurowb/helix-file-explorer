using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HelixExplorer.Converters;

/// <summary>Converts a nullable <see cref="Color"/> to a brush; null becomes transparent.</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public static readonly ColorToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color c ? new SolidColorBrush(c) : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SolidColorBrush b ? b.Color : (object?)null!;
}
