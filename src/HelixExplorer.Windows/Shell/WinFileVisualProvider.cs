using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Windows.Shell;

public sealed class WinFileVisualProvider : IFileVisualProvider
{
    public async ValueTask<FileVisualData?> GetAsync(FileVisualRequest request, CancellationToken cancellationToken)
    {
        if (!CanQueryShell(request.Path))
            return null;

        return await Task.Run(() => GetSync(request), cancellationToken).ConfigureAwait(false);
    }

    private static bool CanQueryShell(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (ArchivePath.IsVirtual(path))
            return false;

        return !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
    }

    private static FileVisualData? GetSync(FileVisualRequest request)
    {
        var size = Math.Clamp(request.Size, 16, 512);

        if (request.PreferThumbnail && !request.IsDirectory)
        {
            var thumbnail = TryLoadImageThumbnail(request.Path, size);
            if (thumbnail is not null)
                return thumbnail;
        }

        return TryGetShellIconFromImageList(request.Path, request.IsDirectory, size)
               ?? TryGetShellIcon(request.Path, request.IsDirectory, size);
    }

    private static FileVisualData? TryLoadImageThumbnail(string path, int size)
    {
        if (!FileVisualRules.SupportsThumbnail(path) || !File.Exists(path))
            return null;

        try
        {
            using var stream = OpenReadShared(path);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            using var scaled = ResizeToSquare(image, size);
            return EncodePng(scaled);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static FileVisualData? TryGetShellIcon(string path, bool isDirectory, int size)
    {
        var shfi = new ShellFileInfo();
        var useAttributes = !File.Exists(path) && !Directory.Exists(path);
        var attributes = isDirectory
            ? ShellFileAttributes.Directory
            : ShellFileAttributes.Normal;

        var flags = ShellGetFileInfoFlags.Icon
                    | (size > 32 ? ShellGetFileInfoFlags.LargeIcon : ShellGetFileInfoFlags.SmallIcon);
        if (useAttributes)
            flags |= ShellGetFileInfoFlags.UseFileAttributes;

        var result = ShellIconNative.SHGetFileInfo(
            path,
            useAttributes ? attributes : 0,
            ref shfi,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            flags);

        // Bail out if the call failed OR no icon handle came back. Using || (not &&) is essential:
        // a non-zero result with a zero hIcon would otherwise reach Icon.FromHandle(IntPtr.Zero).
        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            using var icon = Icon.FromHandle(shfi.hIcon);
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
            if (shfi.hIcon != IntPtr.Zero)
                ShellIconNative.DestroyIcon(shfi.hIcon);
        }
    }

    private static FileVisualData? TryGetShellIconFromImageList(string path, bool isDirectory, int size)
    {
        if (!TryGetShellIconIndex(path, isDirectory, out var iconIndex))
            return null;

        var imageListSize = GetImageListSize(size);
        var iid = ShellIconNative.IID_IImageList;
        var hr = ShellIconNative.SHGetImageList(imageListSize, ref iid, out var imageList);
        if (hr < 0 || imageList is null)
            return null;

        var hIcon = IntPtr.Zero;
        try
        {
            hr = imageList.GetIcon(iconIndex, ImageListDrawFlags.Transparent, ref hIcon);
            if (hr < 0 || hIcon == IntPtr.Zero)
                return null;

            using var icon = Icon.FromHandle(hIcon);
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
            if (hIcon != IntPtr.Zero)
                ShellIconNative.DestroyIcon(hIcon);

            Marshal.ReleaseComObject(imageList);
        }
    }

    private static bool TryGetShellIconIndex(string path, bool isDirectory, out int iconIndex)
    {
        iconIndex = 0;

        var shfi = new ShellFileInfo();
        var useAttributes = !File.Exists(path) && !Directory.Exists(path);
        var attributes = isDirectory
            ? ShellFileAttributes.Directory
            : ShellFileAttributes.Normal;

        var flags = ShellGetFileInfoFlags.SysIconIndex;
        if (useAttributes)
            flags |= ShellGetFileInfoFlags.UseFileAttributes;

        var result = ShellIconNative.SHGetFileInfo(
            path,
            useAttributes ? attributes : 0,
            ref shfi,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            flags);

        if (result == IntPtr.Zero)
            return false;

        iconIndex = shfi.iIcon;
        return iconIndex >= 0;
    }

    private static ShellImageListSize GetImageListSize(int size)
        => size switch
        {
            > 48 => ShellImageListSize.Jumbo,
            > 32 => ShellImageListSize.ExtraLarge,
            > 16 => ShellImageListSize.Large,
            _ => ShellImageListSize.Small
        };

    private static FileStream OpenReadShared(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

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
}
