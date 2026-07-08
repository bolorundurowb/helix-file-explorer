using System.Globalization;
using Avalonia.Data.Converters;

namespace HelixExplorer.Converters;

/// <summary>Multiplies a double by the factor supplied as the converter parameter (default 1).</summary>
public sealed class DoubleScaleConverter : IValueConverter
{
    public static readonly DoubleScaleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var v = value switch
        {
            double d => d,
            int i => i,
            _ => 0
        };
        double factor = 1;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
        {
            factor = f;
        }
        return v * factor;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
