using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using Vanara.PInvoke;
using static Vanara.PInvoke.ComCtl32;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

namespace HelixExplorer.Windows.Shell;

public sealed class WinFileVisualProvider : IFileVisualProvider
{
    private static readonly ConcurrentDictionary<IconCacheKey, FileVisualData?> GenericIconCache = new();
    private static readonly Dictionary<SHIL, IImageList> ImageLists = [];

    public async ValueTask<FileVisualData?> GetAsync(FileVisualRequest request, CancellationToken cancellationToken)
    {
        if (!CanQueryShell(request.Path))
            return null;

        return await STATask.Run(() => GetSync(request, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static bool CanQueryShell(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (ArchivePath.IsVirtual(path))
            return false;

        return !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
    }

    private static FileVisualData? GetSync(FileVisualRequest request, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(request.Size, 16, 512);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.PreferThumbnail && !request.IsDirectory)
        {
            var thumbnail = TryLoadShellThumbnail(request.Path, size, cancellationToken);
            if (thumbnail is not null)
                return thumbnail;
        }

        var key = IconCacheKey.Create(request.Path, request.IsDirectory, size);
        return GenericIconCache.GetOrAdd(
            key,
            static cacheKey => TryGetShellIconFromImageList(cacheKey.IdentityPath, cacheKey.IsDirectory, cacheKey.Size)
                              ?? TryGetShellIcon(cacheKey.IdentityPath, cacheKey.IsDirectory, cacheKey.Size));
    }

    private static FileVisualData? TryLoadShellThumbnail(
        string path,
        int size,
        CancellationToken cancellationToken)
    {
        if (!FileVisualRules.SupportsThumbnail(path) || !File.Exists(path))
            return null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestedSize = new SIZE(size, size);
            var hr = ShellUtil.LoadImageFromImageFactory(
                path,
                ref requestedSize,
                SIIGBF.SIIGBF_RESIZETOFIT | SIIGBF.SIIGBF_THUMBNAILONLY,
                out var bitmapHandle);
            using (bitmapHandle)
            {
                if (hr.Failed || bitmapHandle.IsInvalid)
                    return null;

                cancellationToken.ThrowIfCancellationRequested();
                using var image = Image.FromHbitmap(bitmapHandle.DangerousGetHandle());
                using var scaled = ResizeToSquare(image, size);
                return EncodePng(scaled);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return null;
        }
    }

    private static FileVisualData? TryGetShellIcon(string path, bool isDirectory, int size)
    {
        var shfi = new SHFILEINFO();
        var attributes = isDirectory
            ? FileAttributes.Directory
            : FileAttributes.Normal;

        var flags = SHGFI.SHGFI_ICON
                    | SHGFI.SHGFI_USEFILEATTRIBUTES
                    | (size > 32 ? SHGFI.SHGFI_LARGEICON : SHGFI.SHGFI_SMALLICON);

        var result = SHGetFileInfo(
            path,
            attributes,
            ref shfi,
            SHFILEINFO.Size,
            flags);

        // Bail out if the call failed OR no icon handle came back. Using || (not &&) is essential:
        // a non-zero result with a zero hIcon would otherwise reach Icon.FromHandle(IntPtr.Zero).
        if (result == IntPtr.Zero || shfi.hIcon.IsNull)
            return null;

        try
        {
            using var icon = Icon.FromHandle((IntPtr)shfi.hIcon);
            using var bitmap = icon.ToBitmap();
            using var scaled = ResizeToSquare(bitmap, size);
            return EncodePng(scaled);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (!shfi.hIcon.IsNull)
                DestroyIcon(shfi.hIcon);
        }
    }

    private static FileVisualData? TryGetShellIconFromImageList(string path, bool isDirectory, int size)
    {
        if (!TryGetShellIconIndex(path, isDirectory, out var iconIndex))
            return null;

        var imageList = GetCachedImageList(GetImageListSize(size));
        if (imageList is null)
            return null;

        try
        {
            var hIcon = imageList.GetIcon(iconIndex, IMAGELISTDRAWFLAGS.ILD_TRANSPARENT);
            if (hIcon.IsNull)
                return null;

            try
            {
                using var icon = Icon.FromHandle((IntPtr)hIcon);
                using var bitmap = icon.ToBitmap();
                using var scaled = ResizeToSquare(bitmap, size);
                return EncodePng(scaled);
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryGetShellIconIndex(string path, bool isDirectory, out int iconIndex)
    {
        iconIndex = 0;

        var shfi = new SHFILEINFO();
        var attributes = isDirectory
            ? FileAttributes.Directory
            : FileAttributes.Normal;

        var flags = SHGFI.SHGFI_SYSICONINDEX | SHGFI.SHGFI_USEFILEATTRIBUTES;

        var result = SHGetFileInfo(
            path,
            attributes,
            ref shfi,
            SHFILEINFO.Size,
            flags);

        if (result == IntPtr.Zero)
            return false;

        iconIndex = shfi.iIcon;
        return iconIndex >= 0;
    }

    private static IImageList? GetCachedImageList(SHIL size)
    {
        if (ImageLists.TryGetValue(size, out var cached))
            return cached;

        var hr = SHGetImageList(size, out IImageList? imageList);
        if (hr.Failed || imageList is null)
            return null;

        ImageLists.Add(size, imageList);
        return imageList;
    }

    private static SHIL GetImageListSize(int size)
        => size switch
        {
            > 48 => SHIL.SHIL_JUMBO,
            > 32 => SHIL.SHIL_EXTRALARGE,
            > 16 => SHIL.SHIL_LARGE,
            _ => SHIL.SHIL_SMALL
        };

    private static Bitmap ResizeToSquare(Image source, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var scale = Math.Min((float)size / source.Width, (float)size / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var x = (size - width) / 2;
        var y = (size - height) / 2;
        graphics.DrawImage(source, x, y, width, height);
        return bitmap;
    }

    private static FileVisualData EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new FileVisualData(stream.ToArray());
    }

    private readonly record struct IconCacheKey(string IdentityPath, bool IsDirectory, int Size)
    {
        public static IconCacheKey Create(string path, bool isDirectory, int size)
        {
            if (isDirectory)
                return new IconCacheKey("folder", true, size);

            var extension = Path.GetExtension(path);
            var identity = string.IsNullOrEmpty(extension)
                ? "file"
                : "file" + extension.ToLowerInvariant();
            return new IconCacheKey(identity, false, size);
        }
    }
}
