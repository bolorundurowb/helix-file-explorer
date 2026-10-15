using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests;

public class HighContrastPaletteTests
{
    [Fact]
    public void BlackWindow_IsDarkSurface()
    {
        var palette = new HighContrastPalette(0xFF000000, 0xFFFFFFFF, 0xFF1AEBFF, 0xFF000000, 0xFF3FF23F, 0xFFFFFF00);
        palette.IsDarkSurface.Must().BeTrue();
    }

    [Fact]
    public void WhiteWindow_IsLightSurface()
    {
        var palette = new HighContrastPalette(0xFFFFFFFF, 0xFF000000, 0xFF37006E, 0xFFFFFFFF, 0xFF600000, 0xFF00009F);
        palette.IsDarkSurface.Must().BeFalse();
    }

    [Fact]
    public void DesertWindow_IsLightSurface()
    {
        var palette = new HighContrastPalette(0xFFFFFAEF, 0xFF3D3D3D, 0xFF903909, 0xFFFFF5E3, 0xFF676767, 0xFF8E2E00);
        palette.IsDarkSurface.Must().BeFalse();
    }
}
