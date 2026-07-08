using System.Globalization;
using Avalonia.Data.Converters;

namespace HelixExplorer.Converters;

public sealed class CutOpacityConverter : IValueConverter
{
    public static readonly CutOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 0.45 : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
