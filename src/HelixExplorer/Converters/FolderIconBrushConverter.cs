using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.Converters;

/// <summary>Solid folder tint for sidebar folder icons (custom color or default yellow).</summary>
public sealed class FolderIconBrushConverter : IValueConverter
{
    public IFolderColorService? FolderColors { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path
            && !string.IsNullOrEmpty(path)
            && FolderColors?.TryGetColor(path, out var argb) == true)
        {
            return BrushFromArgb(argb);
        }

        return new SolidColorBrush(Color.Parse("#FFB900"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static IBrush BrushFromArgb(uint argb)
    {
        var alpha = (byte)((argb >> 24) & 0xFF);
        if (alpha == 0)
            alpha = 255;

        return new SolidColorBrush(Color.FromArgb(
            alpha,
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF)));
    }
}
