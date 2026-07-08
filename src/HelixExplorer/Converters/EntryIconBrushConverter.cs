using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Converters;

/// <summary>Maps entries to distinctive icon tint colors (separate from git text coloring).</summary>
public sealed class EntryIconBrushConverter : IValueConverter
{
    public IFolderColorService? FolderColors { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is EntryItemViewModel item)
            return BrushFor(item.IsDirectory, item.Extension, item.FullPath);

        if (value is FileSystemEntry entry)
            return BrushFor(entry.IsDirectory, entry.Extension, entry.FullPath);

        return new SolidColorBrush(Color.Parse("#5C5C5C"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private IBrush BrushFor(bool isDirectory, string extension, string fullPath)
    {
        if (isDirectory)
        {
            if (FolderColors?.TryGetColor(fullPath, out var argb) == true)
                return BrushFromArgb(argb, fallbackAlpha: 255);

            return new SolidColorBrush(Color.Parse("#FFB900"));
        }

        var hex = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tif" or ".tiff" or ".heic" or ".heif" or ".avif"
                => "#E74856",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm"
                => "#8764B8",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a"
                => "#FF8C00",
            ".pdf"
                => "#D13438",
            ".doc" or ".docx" or ".rtf"
                => "#2B579A",
            ".xls" or ".xlsx" or ".csv"
                => "#107C41",
            ".ppt" or ".pptx"
                => "#C43E1C",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz"
                => "#CA5010",
            ".cs" or ".csproj" or ".sln"
                => "#68217A",
            ".js" or ".ts" or ".jsx" or ".tsx" or ".mjs"
                => "#F7DF1E",
            ".json" or ".yaml" or ".yml" or ".xml"
                => "#0078D4",
            ".html" or ".htm" or ".css" or ".scss"
                => "#E81123",
            ".exe" or ".msi" or ".bat" or ".cmd"
                => "#0078D4",
            ".txt" or ".md" or ".log"
                => "#605E5C",
            _ => "#0078D4"
        };

        return new SolidColorBrush(Color.Parse(hex));
    }

    private static IBrush BrushFromArgb(uint argb, byte fallbackAlpha)
    {
        var alpha = (byte)((argb >> 24) & 0xFF);
        if (alpha == 0)
            alpha = fallbackAlpha;

        return new SolidColorBrush(Color.FromArgb(
            alpha,
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF)));
    }
}
