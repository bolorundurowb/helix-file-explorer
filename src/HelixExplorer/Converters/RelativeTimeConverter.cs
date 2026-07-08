using System.Globalization;
using Avalonia.Data.Converters;
using HelixExplorer.Core.Formatting;

namespace HelixExplorer.Converters;

public sealed class RelativeTimeConverter : IValueConverter
{
    public static RelativeTimeConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return RelativeTimeFormatter.Format(dt);
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
