using System.Runtime.InteropServices;
using HelixExplorer.Core.Theming;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace HelixExplorer.Windows.Theming;

public sealed class WinHighContrastSource : IHighContrastSource
{
    public HighContrastPalette? TryReadActivePalette()
    {
        // SPI rather than the registry: the HighContrast registry flags can lag the live setting.
        var size = Marshal.SizeOf<HIGHCONTRAST>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new HIGHCONTRAST { cbSize = (uint)size }, buffer, fDeleteOld: false);
            if (!SystemParametersInfo(SPI.SPI_GETHIGHCONTRAST, (uint)size, buffer, 0)
                || !Marshal.PtrToStructure<HIGHCONTRAST>(buffer).dwFlags.HasFlag(HFC.HCF_HIGHCONTRASTON))
            {
                return null;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return new HighContrastPalette(
            Read(SystemColorIndex.COLOR_WINDOW),
            Read(SystemColorIndex.COLOR_WINDOWTEXT),
            Read(SystemColorIndex.COLOR_HIGHLIGHT),
            Read(SystemColorIndex.COLOR_HIGHLIGHTTEXT),
            Read(SystemColorIndex.COLOR_GRAYTEXT),
            Read(SystemColorIndex.COLOR_HOTLIGHT));
    }

    private static uint Read(SystemColorIndex index)
    {
        COLORREF color = GetSysColor(index);
        return 0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
    }
}
