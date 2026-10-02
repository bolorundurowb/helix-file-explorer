using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Windows.Shell;

namespace HelixExplorer.ViewModels.Tests;

public sealed class CustomFolderIconTests
{
    [Fact]
    public async Task GridSize_CustomFolderIcon_MatchesSystemFolderGlyph()
    {
        var root = Path.Combine(Path.GetTempPath(), "helix-icon-" + Guid.NewGuid().ToString("N"));
        var plain = Path.Combine(root, "plain");
        var custom = Path.Combine(root, "custom");
        Directory.CreateDirectory(plain);
        Directory.CreateDirectory(custom);

        try
        {
            var icoPath = Path.Combine(root, "tiny.ico");
            WritePngIco(icoPath, 32);
            var desktopIni = Path.Combine(custom, "desktop.ini");
            await File.WriteAllTextAsync(desktopIni, $"[.ShellClassInfo]{Environment.NewLine}IconResource={icoPath},0{Environment.NewLine}");
            File.SetAttributes(desktopIni, FileAttributes.Hidden | FileAttributes.System);
            new DirectoryInfo(custom).Attributes |= FileAttributes.ReadOnly;
            SHChangeNotify(0x00002000, 0x0005, custom, IntPtr.Zero);

            var provider = new WinFileVisualProvider();
            var plainGlyph = await GlyphAsync(provider, plain, 96);
            var customGlyph = await GlyphAsync(provider, custom, 96);
            var customList = await GlyphAsync(provider, custom, 20);

            // The custom icon is a red disc. A yellow system folder means desktop.ini was ignored.
            customGlyph.RedPixels.Must().BeGreaterThan(80);
            plainGlyph.RedPixels.Must().BeLessThan(20);

            // System folder artwork already fills the tile. Cropping must not shrink it.
            plainGlyph.OpaqueMin.Must().BeGreaterThan(50);

            // Before the fix this glyph was ~16px inside the 96px tile.
            customGlyph.OpaqueMin.Must().BeGreaterThan(64);

            // List view asks for a small image list, where the 32px icon already fills the slot.
            customList.OpaqueMin.Must().BeGreaterThan(10);
        }
        finally
        {
            if (Directory.Exists(custom))
                new DirectoryInfo(custom).Attributes &= ~FileAttributes.ReadOnly;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<Glyph> GlyphAsync(WinFileVisualProvider provider, string path, int size)
    {
        var data = await provider.GetAsync(new FileVisualRequest(path, true, size, false), CancellationToken.None);
        data.Must().NotBeNull();
        using var stream = new MemoryStream(data!.Png);
        using var bitmap = new Bitmap(stream);
        return Measure(bitmap);
    }

    private static Glyph Measure(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var rowBytes = Math.Abs(stride);
            var bytes = new byte[rowBytes * bitmap.Height];
            var origin = stride >= 0 ? data.Scan0 : IntPtr.Add(data.Scan0, stride * (bitmap.Height - 1));
            Marshal.Copy(origin, bytes, 0, bytes.Length);

            var minX = bitmap.Width;
            var minY = bitmap.Height;
            var maxX = -1;
            var maxY = -1;
            var red = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = y * rowBytes;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var offset = row + (x * 4);
                    var b = bytes[offset];
                    var g = bytes[offset + 1];
                    var r = bytes[offset + 2];
                    var a = bytes[offset + 3];
                    if (a < 32)
                        continue;

                    if (r > 180 && g < 90 && b < 90)
                        red++;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            var opaqueMin = maxX < 0 ? 0 : Math.Min(maxX - minX + 1, maxY - minY + 1);
            return new Glyph(opaqueMin, red);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void WritePngIco(string path, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 220, 40, 40));
            graphics.FillEllipse(brush, 1, 1, size - 2, size - 2);
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        var pngBytes = png.ToArray();

        using var output = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)size);
        writer.Write((byte)size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(pngBytes.Length);
        writer.Write(22);
        writer.Write(pngBytes);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

    private readonly record struct Glyph(int OpaqueMin, int RedPixels);
}
