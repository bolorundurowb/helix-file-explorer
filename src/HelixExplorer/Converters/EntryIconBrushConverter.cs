using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Converters;

/// <summary>Distinctive icon tint colors, separate from git text coloring.</summary>
public sealed class EntryIconBrushConverter : IValueConverter
{
    public IFolderColorService? FolderColors { get; set; }

    private static readonly IBrush FolderBrush = Solid("#FFB900");
    private static readonly IBrush DefaultFileBrush = Solid("#0078D4");
    private static readonly IBrush NoColorBrush = Solid("#5C5C5C");

    // Precomputed so the hot binding path (one evaluation per row/tile on every recycle during
    // scroll) never allocates a brush or parses a color. Keyed case-insensitively to match the
    // extension strings produced by enumeration.
    private static readonly IReadOnlyDictionary<string, IBrush> FileBrushes =
        new Dictionary<string, IBrush>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = Solid("#E74856"),
            [".jpeg"] = Solid("#E74856"),
            [".png"] = Solid("#E74856"),
            [".gif"] = Solid("#E74856"),
            [".bmp"] = Solid("#E74856"),
            [".webp"] = Solid("#E74856"),
            [".ico"] = Solid("#E74856"),
            [".tif"] = Solid("#E74856"),
            [".tiff"] = Solid("#E74856"),
            [".heic"] = Solid("#E74856"),
            [".heif"] = Solid("#E74856"),
            [".avif"] = Solid("#E74856"),

            [".mp4"] = Solid("#8764B8"),
            [".mkv"] = Solid("#8764B8"),
            [".avi"] = Solid("#8764B8"),
            [".mov"] = Solid("#8764B8"),
            [".wmv"] = Solid("#8764B8"),
            [".webm"] = Solid("#8764B8"),

            [".mp3"] = Solid("#FF8C00"),
            [".wav"] = Solid("#FF8C00"),
            [".flac"] = Solid("#FF8C00"),
            [".aac"] = Solid("#FF8C00"),
            [".ogg"] = Solid("#FF8C00"),
            [".m4a"] = Solid("#FF8C00"),

            [".pdf"] = Solid("#D13438"),
            [".doc"] = Solid("#2B579A"),
            [".docx"] = Solid("#2B579A"),
            [".rtf"] = Solid("#2B579A"),
            [".xls"] = Solid("#107C41"),
            [".xlsx"] = Solid("#107C41"),
            [".csv"] = Solid("#107C41"),
            [".ppt"] = Solid("#C43E1C"),
            [".pptx"] = Solid("#C43E1C"),

            [".zip"] = Solid("#CA5010"),
            [".rar"] = Solid("#CA5010"),
            [".7z"] = Solid("#CA5010"),
            [".tar"] = Solid("#CA5010"),
            [".gz"] = Solid("#CA5010"),

            [".cs"] = Solid("#68217A"),
            [".csproj"] = Solid("#68217A"),
            [".sln"] = Solid("#68217A"),

            [".js"] = Solid("#F7DF1E"),
            [".ts"] = Solid("#F7DF1E"),
            [".jsx"] = Solid("#F7DF1E"),
            [".tsx"] = Solid("#F7DF1E"),
            [".mjs"] = Solid("#F7DF1E"),

            [".json"] = Solid("#0078D4"),
            [".yaml"] = Solid("#0078D4"),
            [".yml"] = Solid("#0078D4"),
            [".xml"] = Solid("#0078D4"),

            [".html"] = Solid("#E81123"),
            [".htm"] = Solid("#E81123"),
            [".css"] = Solid("#E81123"),
            [".scss"] = Solid("#E81123"),

            [".exe"] = Solid("#0078D4"),
            [".msi"] = Solid("#0078D4"),
            [".bat"] = Solid("#0078D4"),
            [".cmd"] = Solid("#0078D4"),

            [".txt"] = Solid("#605E5C"),
            [".md"] = Solid("#605E5C"),
            [".log"] = Solid("#605E5C"),
        };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is EntryItemViewModel item)
            return BrushFor(item.IsDirectory, item.Extension, item.FullPath);

        if (value is FileSystemEntry entry)
            return BrushFor(entry.IsDirectory, entry.Extension, entry.FullPath);

        return NoColorBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private IBrush BrushFor(bool isDirectory, string extension, string fullPath)
    {
        if (isDirectory)
        {
            if (FolderColors?.TryGetColor(fullPath, out var argb) == true)
                return BrushFromArgb(argb, fallbackAlpha: 255);

            return FolderBrush;
        }

        return FileBrushes.GetValueOrDefault(extension, DefaultFileBrush);
    }

    private static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));

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
