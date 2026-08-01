using AppKit;
using CoreGraphics;
using Foundation;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.macOS.Shell;

public sealed class MacFileVisualProvider : IFileVisualProvider
{
    private readonly NSCache _iconCache = new();
    private readonly object _cacheLock = new();

    public async ValueTask<FileVisualData?> GetAsync(FileVisualRequest request, CancellationToken cancellationToken)
    {
        if (!CanProvideVisual(request.Path))
            return null;

        return await Task.Run(() => GetVisualSync(request), cancellationToken).ConfigureAwait(false);
    }

    private static bool CanProvideVisual(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (ArchivePath.IsVirtual(path))
            return false;
        return true;
    }

    private FileVisualData? GetVisualSync(FileVisualRequest request)
    {
        var size = Math.Clamp(request.Size, 16, 512);

        if (request.PreferThumbnail && !request.IsDirectory)
        {
            var thumbnail = TryLoadThumbnail(request.Path, size);
            if (thumbnail is not null)
                return thumbnail;
        }

        return TryGetWorkspaceIcon(request.Path, request.IsDirectory, size);
    }

    private FileVisualData? TryLoadThumbnail(string path, int size)
    {
        if (!FileVisualRules.SupportsThumbnail(path) || !File.Exists(path))
            return null;

        try
        {
            using var image = new NSImage(path);
            if (!image.IsValid)
                return null;

            using var resized = ResizeImage(image, size);
            return EncodeAsPng(resized);
        }
        catch
        {
            return null;
        }
    }

    private FileVisualData? TryGetWorkspaceIcon(string path, bool isDirectory, int size)
    {
        try
        {
            var key = $"{(isDirectory ? "d:" : "f:")}{(isDirectory ? "__" : Path.GetExtension(path))}";
            var cacheKey = new NSString(key);

            lock (_cacheLock)
            {
                var cached = _iconCache.ObjectForKey(cacheKey) as NSData;
                if (cached is not null)
                    return new FileVisualData(cached.ToArray());
            }

            using var icon = isDirectory
                ? NSWorkspace.SharedWorkspace.IconForFile("/")
                : (!string.IsNullOrWhiteSpace(path) && File.Exists(path)
                    ? NSWorkspace.SharedWorkspace.IconForFile(path)
                    : null);

            if (icon is null)
                return null;

            using var resized = ResizeImage(icon, size);
            var result = EncodeAsPng(resized);
            if (result is null)
                return null;

            CacheIcon(cacheKey, result);
            return result;
        }
        catch
        {
            return null;
        }
    }

    private void CacheIcon(NSString key, FileVisualData data)
    {
        var nsData = NSData.FromArray(data.Png);
        lock (_cacheLock)
        {
            _iconCache.SetObjectForKey(nsData, key);
        }
    }

    private static NSImage ResizeImage(NSImage source, int size)
    {
        var resized = new NSImage { Size = new CGSize(size, size) };
        resized.LockFocus();
        source.Draw(new CGRect(0, 0, size, size), CGRect.Null, NSCompositingOperation.SourceOver, 1.0f);
        resized.UnlockFocus();
        return resized;
    }

    private static FileVisualData? EncodeAsPng(NSImage image)
    {
        if (image.CGImage is null)
            return null;

        using var rep = new NSBitmapImageRep(image.CGImage);
        var pngData = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
        return pngData is not null ? new FileVisualData(pngData.ToArray()) : null;
    }
}