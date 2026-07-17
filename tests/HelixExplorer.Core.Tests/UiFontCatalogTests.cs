using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests;

public class UiFontCatalogTests
{
    [Fact]
    public void ResolveFontFamilySource_DmSans_UsesBundledCollection()
    {
        UiFontCatalog.ResolveFontFamilySource(UiFontFamily.DmSans).Must().Be("fonts:Helix#DM Sans");
    }

    [Fact]
    public void GetSystemFontFamilySource_MatchesCurrentPlatform()
    {
        var source = UiFontCatalog.GetSystemFontFamilySource();

        if (OperatingSystem.IsWindows())
            source.Must().Be("Segoe UI Variable Text, Segoe UI");
        else if (OperatingSystem.IsMacOS())
            source.Must().Be(".AppleSystemUIFont, SF Pro Text, Helvetica Neue");
        else
            source.Must().Be("Cantarell, Ubuntu, Noto Sans, fonts:Inter#Inter");
    }

    [Fact]
    public void Options_ContainSystemDmSansAndInterLabels()
    {
        UiFontCatalog.GetDisplayName(UiFontFamily.System).Must().Be("System default");
        UiFontCatalog.GetDisplayName(UiFontFamily.DmSans).Must().Be("DM Sans");
        UiFontCatalog.GetDisplayName(UiFontFamily.Inter).Must().Be("Inter");
    }

    [Fact]
    public void ResolveFontFamilySource_Inter_UsesAvaloniaInterCollection()
    {
        UiFontCatalog.ResolveFontFamilySource(UiFontFamily.Inter).Must().Be("fonts:Inter#Inter");
    }
}
