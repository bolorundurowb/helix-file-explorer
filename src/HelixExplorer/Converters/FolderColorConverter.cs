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
        // The folder colour now tints the vector glyph via <see cref="EntryIconBrushConverter"/> and
        // <see cref="FolderIconBrushConverter"/>; the grid tile itself stays transparent so the glyph
        // is the customisation rather than a solid block.
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
