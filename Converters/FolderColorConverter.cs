using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HelixExplorer.Services;

namespace HelixExplorer.Converters;

/// <summary>
/// Looks up the user's per-folder tint (set via right-click → colour picker) for a path.
/// Returns a brush when a tint exists, otherwise transparent so the default styling shows.
/// </summary>
public sealed class FolderColorConverter : IValueConverter
{
    public static readonly FolderColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path) &&
            ServiceLocator.Theme.TryGetFolderColor(path, out var color))
        {
            return new SolidColorBrush(color);
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
