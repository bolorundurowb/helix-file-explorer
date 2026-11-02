using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

/// <summary>
/// Copies a user-picked image into Helix-owned storage as a PNG.
/// The listing then does not depend on the original file staying where it was.
/// </summary>
internal static class FolderIconFiles
{
    public static async Task<string> CopyAsPngAsync(string sourcePath)
    {
        Directory.CreateDirectory(AppPaths.FolderIconsDirectory);
        var destPath = Path.Combine(AppPaths.FolderIconsDirectory, $"{Guid.NewGuid():N}.png");

        try
        {
            await using var readStream = File.OpenRead(sourcePath);
            // Bitmap decode and encode are UI-thread work. A file that cannot be decoded throws
            // before the caller replaces the stored icon.
            var bitmap = await Dispatcher.UIThread.InvokeAsync(() => new Bitmap(readStream));
            using (bitmap)
            {
                await using var writeStream = File.Create(destPath);
                await Dispatcher.UIThread.InvokeAsync(() => bitmap.Save(writeStream, new PngBitmapEncoderOptions()));
            }

            return destPath;
        }
        catch
        {
            Delete(destPath);
            throw;
        }
    }

    public static void Delete(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try { File.Delete(path); }
        catch { /* The copied icon is in Helix-owned storage; best-effort cleanup is enough. */ }
    }
}
