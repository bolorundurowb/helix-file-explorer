namespace HelixExplorer.Core.FileSystem;

public static class FileVisualRules
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff", ".heic", ".heif", ".avif"
    };

    public static bool SupportsThumbnail(string path)
        => ImageExtensions.Contains(Path.GetExtension(path));

    public static bool PreferThumbnail(string path, bool isDirectory, bool gridView)
        => gridView && !isDirectory && SupportsThumbnail(path);
}
