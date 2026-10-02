using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using static Vanara.PInvoke.ComCtl32;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

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
        var shfi = new SHFILEINFO();
        var useAttributes = !File.Exists(path) && !Directory.Exists(path);
        var attributes = isDirectory
            ? FileAttributes.Directory
            : FileAttributes.Normal;

        var flags = SHGFI.SHGFI_ICON
                    | (size > 32 ? SHGFI.SHGFI_LARGEICON : SHGFI.SHGFI_SMALLICON);
        if (useAttributes)
            flags |= SHGFI.SHGFI_USEFILEATTRIBUTES;

        var result = SHGetFileInfo(
            path,
            useAttributes ? attributes : 0,
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
            using var scaled = ScaleShellIcon(bitmap, size);
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

        var hr = SHGetImageList(GetImageListSize(size), out IImageList? imageList);
        if (hr.Failed || imageList is null)
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
                using var scaled = ScaleShellIcon(bitmap, size);
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
        finally
        {
            Marshal.ReleaseComObject(imageList);
        }
    }

    private static bool TryGetShellIconIndex(string path, bool isDirectory, out int iconIndex)
    {
        iconIndex = 0;

        var shfi = new SHFILEINFO();
        var useAttributes = !File.Exists(path) && !Directory.Exists(path);
        var attributes = isDirectory
            ? FileAttributes.Directory
            : FileAttributes.Normal;

        var flags = SHGFI.SHGFI_SYSICONINDEX;
        if (useAttributes)
            flags |= SHGFI.SHGFI_USEFILEATTRIBUTES;

        var result = SHGetFileInfo(
            path,
            useAttributes ? attributes : 0,
            ref shfi,
            SHFILEINFO.Size,
            flags);

        if (result == IntPtr.Zero)
            return false;

        iconIndex = shfi.iIcon;
        return iconIndex >= 0;
    }

    private static SHIL GetImageListSize(int size)
        => size switch
        {
            > 48 => SHIL.SHIL_JUMBO,
            > 32 => SHIL.SHIL_EXTRALARGE,
            > 16 => SHIL.SHIL_LARGE,
            _ => SHIL.SHIL_SMALL
        };

    private static FileStream OpenReadShared(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// Jumbo shell slots (grid view) store a custom folder icon as a small glyph in the
    /// corner of a large transparent bitmap when the icon file has no jumbo frame.
    /// Scaling that canvas keeps the transparent padding and draws the folder tiny next
    /// to system icons, whose artwork already fills the slot. Crop to the glyph first.
    /// </summary>
    private static Bitmap ScaleShellIcon(Bitmap bitmap, int size)
    {
        var cropped = TryCropSparseGlyph(bitmap);
        try
        {
            return ResizeToSquare(cropped ?? bitmap, size);
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    private static Bitmap? TryCropSparseGlyph(Bitmap bitmap)
    {
        if (bitmap.Width < 2 || bitmap.Height < 2)
            return null;

        var bounds = FindOpaqueBounds(bitmap);
        if (bounds is not { } glyph)
            return null;

        // System folder artwork fills most of the slot (about 0.7 on the short side).
        // A jumbo placeholder for a 32px custom icon sits near 0.2. Half the canvas
        // separates those without cropping icons that already have a real large frame.
        if (glyph.Width >= bitmap.Width * 0.5 && glyph.Height >= bitmap.Height * 0.5)
            return null;

        return bitmap.Clone(glyph, PixelFormat.Format32bppArgb);
    }

    private static Rectangle? FindOpaqueBounds(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        BitmapData data;
        try
        {
            data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
        }
        catch (Exception)
        {
            return null;
        }

        try
        {
            // Scan0 is the top row; stride may be negative. Copy one row at a time so
            // the bounds stay in bitmap coordinates either way.
            var row = new byte[width * 4];
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (var x = 0; x < width; x++)
                {
                    if (row[(x * 4) + 3] < 32)
                        continue;

                    if (x < minX)
                        minX = x;
                    if (y < minY)
                        minY = y;
                    if (x > maxX)
                        maxX = x;
                    if (y > maxY)
                        maxY = y;
                }
            }

            if (maxX < 0)
                return null;

            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

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
