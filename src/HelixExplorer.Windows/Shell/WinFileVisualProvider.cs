using System.Drawing;
using System.Drawing.Imaging;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Windows.Shell;

public sealed class WinFileVisualProvider : IFileVisualProvider
{
    public async ValueTask<FileVisualData?> GetAsync(FileVisualRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return null;

        return await Task.Run(() => GetSync(request), cancellationToken).ConfigureAwait(false);
    }

    private static FileVisualData? GetSync(FileVisualRequest request)
    {
        var size = Math.Clamp(request.Size, 16, 512);

        if (request.PreferThumbnail && !request.IsDirectory)
        {
            var thumbnail = TryGetImage(request.Path, size, Siigbf.ThumbnailOnly | Siigbf.BiggerSizeOk);
            if (thumbnail is not null)
                return thumbnail;
        }

        return TryGetImage(request.Path, size, Siigbf.IconOnly | Siigbf.BiggerSizeOk);
    }

    private static FileVisualData? TryGetImage(string path, int size, Siigbf flags)
    {
        var factoryGuid = ShellImageIid.IShellItemImageFactory;
        var hr = ShellImageNative.SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryGuid, out var factory);
        if (hr != 0 || factory is null)
            return null;

        IntPtr hBitmap;
        try
        {
            factory.GetImage(new NativeSize { cx = size, cy = size }, flags, out hBitmap);
        }
        catch
        {
            return null;
        }

        if (hBitmap == IntPtr.Zero)
            return null;

        try
        {
            using var bitmap = Image.FromHbitmap(hBitmap);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return new FileVisualData(stream.ToArray(), bitmap.Width, bitmap.Height);
        }
        catch
        {
            return null;
        }
        finally
        {
            ShellImageNative.DeleteObject(hBitmap);
        }
    }
}
