using HelixExplorer.Core.Theming;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class UiFontCatalogTests
{
    [Fact]
    public void ResolveFontFamilySource_DmSans_UsesBundledCollection()
    {
        Assert.Equal("fonts:Helix#DM Sans", UiFontCatalog.ResolveFontFamilySource(UiFontFamily.DmSans));
    }

    [Fact]
    public void GetSystemFontFamilySource_MatchesCurrentPlatform()
    {
        var source = UiFontCatalog.GetSystemFontFamilySource();

        if (OperatingSystem.IsWindows())
            Assert.Equal("Segoe UI Variable Text, Segoe UI", source);
        else if (OperatingSystem.IsMacOS())
            Assert.Equal(".AppleSystemUIFont, SF Pro Text, Helvetica Neue", source);
        else
            Assert.Equal("Cantarell, Ubuntu, Noto Sans, fonts:Inter#Inter", source);
    }

    [Fact]
    public void Options_ContainSystemDmSansAndInterLabels()
    {
        Assert.Equal("System default", UiFontCatalog.GetDisplayName(UiFontFamily.System));
        Assert.Equal("DM Sans", UiFontCatalog.GetDisplayName(UiFontFamily.DmSans));
        Assert.Equal("Inter", UiFontCatalog.GetDisplayName(UiFontFamily.Inter));
    }

    [Fact]
    public void ResolveFontFamilySource_Inter_UsesAvaloniaInterCollection()
    {
        Assert.Equal("fonts:Inter#Inter", UiFontCatalog.ResolveFontFamilySource(UiFontFamily.Inter));
    }
}
