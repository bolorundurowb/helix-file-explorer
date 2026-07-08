using System.Globalization;
using Avalonia.Data.Converters;
using HelixExplorer.Core.Formatting;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.Converters;

public sealed class FileSizeConverter : IValueConverter
{
    public static FileSizeConverter Instance { get; } = new();

    public SizeDisplayMode Mode { get; set; } = SizeDisplayMode.Binary;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            long size => FileSizeFormatter.Format(size, Mode, isDirectory: false),
            Core.Models.FileSystemEntry entry => FileSizeFormatter.Format(entry.SizeBytes, Mode, entry.IsDirectory),
            _ => string.Empty
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
