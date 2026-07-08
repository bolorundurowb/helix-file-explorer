using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace HelixExplorer.Converters;

/// <summary>
/// Maps a bool to a border <see cref="Thickness"/>. True → uniform thickness from the
/// parameter (default 2), false → 0. Used to draw the accent border around the active pane.
/// </summary>
public sealed class BoolToThicknessConverter : IValueConverter
{
    public static readonly BoolToThicknessConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double on = 2;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double p))
        {
            on = p;
        }
        return value is true ? new Thickness(on) : new Thickness(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
