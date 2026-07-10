using System.Drawing;
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

        return TryGetShellIcon(request.Path, request.IsDirectory, size);
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
        catch
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

        if (result == IntPtr.Zero && shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            using var icon = Icon.FromHandle(shfi.hIcon);
            using var bitmap = icon.ToBitmap();
            using var scaled = ResizeToSquare(bitmap, size);
            return EncodePng(scaled);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shfi.hIcon != IntPtr.Zero)
                ShellIconNative.DestroyIcon(shfi.hIcon);
        }
    }

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
