using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.Converters;

public sealed class FolderColorConverter : IValueConverter
{
    public IFolderColorService? FolderColors { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (FolderColors is null || value is not string path || string.IsNullOrEmpty(path))
            return Brushes.Transparent;

        if (!FolderColors.TryGetColor(path, out var argb))
            return Brushes.Transparent;

        var alpha = (byte)((argb >> 24) & 0xFF);
        if (alpha == 0)
            alpha = 64;

        return new SolidColorBrush(Color.FromArgb(alpha, (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF)));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
