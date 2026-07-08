using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Converters;

/// <summary>Maps a <see cref="FileSystemEntry"/> (or its IsDirectory flag) to a folder/file glyph.</summary>
public sealed class EntryIconConverter : IValueConverter
{
    private const string FolderPath =
        "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z";

    private const string FilePath =
        "M13,9V3.5L18.5,9M6,2C4.89,2 4,2.89 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2H6Z";

    private static readonly Geometry Folder = Geometry.Parse(FolderPath);
    private static readonly Geometry File = Geometry.Parse(FilePath);

    public static EntryIconConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDirectory = value switch
        {
            FileSystemEntry entry => entry.IsDirectory,
            bool flag => flag,
            _ => false
        };

        return isDirectory ? Folder : File;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
