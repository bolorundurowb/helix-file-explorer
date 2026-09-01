using System.Collections.Frozen;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Converters;

/// <summary>Distinctive icon tint colors, separate from git text coloring.</summary>
public sealed class EntryIconBrushConverter : IValueConverter
{
    private static readonly IImmutableSolidColorBrush DefaultFile = new ImmutableSolidColorBrush(0xFF0078D4);
    private static readonly IImmutableSolidColorBrush DefaultFolder = new ImmutableSolidColorBrush(0xFFFFB900);
    private static readonly IImmutableSolidColorBrush Fallback = new ImmutableSolidColorBrush(0xFF5C5C5C);

    private static readonly FrozenDictionary<string, IImmutableSolidColorBrush> ExtensionBrushes =
        new Dictionary<string, IImmutableSolidColorBrush>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".jpeg"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".png"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".gif"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".bmp"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".webp"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".ico"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".tif"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".tiff"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".heic"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".heif"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".avif"] = new ImmutableSolidColorBrush(0xFFE74856),
            [".mp4"] = new ImmutableSolidColorBrush(0xFF8764B8),
            [".mkv"] = new ImmutableSolidColorBrush(0xFF8764B8),
            [".avi"] = new ImmutableSolidColorBrush(0xFF8764B8),
            [".mov"] = new ImmutableSolidColorBrush(0xFF8764B8),
            [".wmv"] = new ImmutableSolidColorBrush(0xFF8764B8),
            [".webm"] = new ImmutableSolidColorBrush(0xFF8764B8),
            [".mp3"] = new ImmutableSolidColorBrush(0xFFFF8C00),
            [".wav"] = new ImmutableSolidColorBrush(0xFFFF8C00),
            [".flac"] = new ImmutableSolidColorBrush(0xFFFF8C00),
            [".aac"] = new ImmutableSolidColorBrush(0xFFFF8C00),
            [".ogg"] = new ImmutableSolidColorBrush(0xFFFF8C00),
            [".m4a"] = new ImmutableSolidColorBrush(0xFFFF8C00),
            [".pdf"] = new ImmutableSolidColorBrush(0xFFD13438),
            [".doc"] = new ImmutableSolidColorBrush(0xFF2B579A),
            [".docx"] = new ImmutableSolidColorBrush(0xFF2B579A),
            [".rtf"] = new ImmutableSolidColorBrush(0xFF2B579A),
            [".xls"] = new ImmutableSolidColorBrush(0xFF107C41),
            [".xlsx"] = new ImmutableSolidColorBrush(0xFF107C41),
            [".csv"] = new ImmutableSolidColorBrush(0xFF107C41),
            [".ppt"] = new ImmutableSolidColorBrush(0xFFC43E1C),
            [".pptx"] = new ImmutableSolidColorBrush(0xFFC43E1C),
            [".zip"] = new ImmutableSolidColorBrush(0xFFCA5010),
            [".rar"] = new ImmutableSolidColorBrush(0xFFCA5010),
            [".7z"] = new ImmutableSolidColorBrush(0xFFCA5010),
            [".tar"] = new ImmutableSolidColorBrush(0xFFCA5010),
            [".gz"] = new ImmutableSolidColorBrush(0xFFCA5010),
            [".cs"] = new ImmutableSolidColorBrush(0xFF68217A),
            [".csproj"] = new ImmutableSolidColorBrush(0xFF68217A),
            [".sln"] = new ImmutableSolidColorBrush(0xFF68217A),
            [".js"] = new ImmutableSolidColorBrush(0xFFF7DF1E),
            [".ts"] = new ImmutableSolidColorBrush(0xFFF7DF1E),
            [".jsx"] = new ImmutableSolidColorBrush(0xFFF7DF1E),
            [".tsx"] = new ImmutableSolidColorBrush(0xFFF7DF1E),
            [".mjs"] = new ImmutableSolidColorBrush(0xFFF7DF1E),
            [".json"] = new ImmutableSolidColorBrush(0xFF0078D4),
            [".yaml"] = new ImmutableSolidColorBrush(0xFF0078D4),
            [".yml"] = new ImmutableSolidColorBrush(0xFF0078D4),
            [".xml"] = new ImmutableSolidColorBrush(0xFF0078D4),
            [".html"] = new ImmutableSolidColorBrush(0xFFE81123),
            [".htm"] = new ImmutableSolidColorBrush(0xFFE81123),
            [".css"] = new ImmutableSolidColorBrush(0xFFE81123),
            [".scss"] = new ImmutableSolidColorBrush(0xFFE81123),
            [".exe"] = new ImmutableSolidColorBrush(0xFF0078D4),
            [".msi"] = new ImmutableSolidColorBrush(0xFF0078D4),
            [".bat"] = new ImmutableSolidColorBrush(0xFF0078D4),
            [".cmd"] = new ImmutableSolidColorBrush(0xFF0078D4),
            [".txt"] = new ImmutableSolidColorBrush(0xFF605E5C),
            [".md"] = new ImmutableSolidColorBrush(0xFF605E5C),
            [".log"] = new ImmutableSolidColorBrush(0xFF605E5C),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public IFolderColorService? FolderColors { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is EntryItemViewModel item)
            return BrushFor(item.IsDirectory, item.Extension, item.FullPath);

        if (value is FileSystemEntry entry)
            return BrushFor(entry.IsDirectory, entry.Extension, entry.FullPath);

        return Fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private IBrush BrushFor(bool isDirectory, string extension, string fullPath)
    {
        if (isDirectory)
        {
            if (FolderColors?.TryGetColor(fullPath, out var argb) == true)
                return BrushFromArgb(argb, fallbackAlpha: 255);

            return DefaultFolder;
        }

        return ExtensionBrushes.GetValueOrDefault(extension, DefaultFile);
    }

    private static IBrush BrushFromArgb(uint argb, byte fallbackAlpha)
    {
        var alpha = (byte)((argb >> 24) & 0xFF);
        if (alpha == 0)
            alpha = fallbackAlpha;

        return new ImmutableSolidColorBrush(Color.FromArgb(
            alpha,
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF)));
    }
}
