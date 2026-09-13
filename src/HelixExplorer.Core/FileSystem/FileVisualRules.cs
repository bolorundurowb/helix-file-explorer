namespace HelixExplorer.Core.FileSystem;

public static class FileVisualRules
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff", ".heic", ".heif", ".avif"
    };

    /// <summary>
    /// Extensions whose icon is read from the file itself (embedded executable icon, shortcut
    /// target, icon/cursor resource) rather than the registered default for the extension. These
    /// must be keyed by full path; everything else can share a per-extension icon.
    /// </summary>
    private static readonly HashSet<string> PerFileIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".ocx", ".cpl", ".scr", ".msc", ".com",
        ".lnk", ".url", ".ico", ".ani", ".cur", ".msi"
    };

    public static bool SupportsThumbnail(string path)
        => ImageExtensions.Contains(Path.GetExtension(path));

    public static bool PreferThumbnail(string path, bool isDirectory, bool gridView)
        => gridView && !isDirectory && SupportsThumbnail(path);

    public static bool HasPerFileIcon(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Length > 0 && PerFileIconExtensions.Contains(extension);
    }
}
