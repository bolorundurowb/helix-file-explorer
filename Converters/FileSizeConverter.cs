using System.Globalization;
using Avalonia.Data.Converters;

namespace HelixExplorer.Converters;

/// <summary>Converts a byte count into a human-readable string respecting the user's
/// binary/decimal preference stored in <see cref="ServiceLocator.Settings"/>.</summary>
public sealed class FileSizeConverter : IValueConverter
{
    public static readonly FileSizeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long size) return value;
        if (size < 0) return "--";

        var mode = ServiceLocator.Settings.FileSizeDisplayMode;
        return mode == SizeDisplayMode.Decimal ? FormatDecimal(size) : FormatBinary(size);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static string FormatBinary(long size)
    {
        if (size < 1024) return $"{size} B";
        if (size < 1048576) return $"{size / 1024.0:F1} KiB";
        if (size < 1073741824) return $"{size / 1048576.0:F1} MiB";
        if (size < 1099511627776L) return $"{size / 1073741824.0:F2} GiB";
        return $"{size / 1099511627776.0:F2} TiB";
    }

    private static string FormatDecimal(long size)
    {
        if (size < 1000) return $"{size} B";
        if (size < 1000000) return $"{size / 1000.0:F1} KB";
        if (size < 1000000000) return $"{size / 1000000.0:F1} MB";
        if (size < 1000000000000L) return $"{size / 1000000000.0:F2} GB";
        return $"{size / 1000000000000.0:F2} TB";
    }
}