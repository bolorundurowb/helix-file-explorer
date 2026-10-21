using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests;

public class UiFontCatalogTests
{
    [Fact]
    public void Options_shouldListTheFinalFiveInOrder()
    {
        UiFontCatalog.Options.Count.Must().Be(5);

        UiFontCatalog.Options[0].Value.Must().Be(UiFontFamily.System);
        UiFontCatalog.Options[0].Label.Must().Be("System default");
        UiFontCatalog.Options[1].Value.Must().Be(UiFontFamily.Inter);
        UiFontCatalog.Options[1].Label.Must().Be("Inter");
        UiFontCatalog.Options[2].Value.Must().Be(UiFontFamily.SourceSans3);
        UiFontCatalog.Options[2].Label.Must().Be("Source Sans 3");
        UiFontCatalog.Options[3].Value.Must().Be(UiFontFamily.IbmPlexSans);
        UiFontCatalog.Options[3].Label.Must().Be("IBM Plex Sans");
        UiFontCatalog.Options[4].Value.Must().Be(UiFontFamily.GeistSans);
        UiFontCatalog.Options[4].Label.Must().Be("Geist Sans");
    }

    [Fact]
    public void GetSystemFontFamilySource_shouldMatchTheCurrentPlatform()
    {
        var source = UiFontCatalog.GetSystemFontFamilySource();

        if (OperatingSystem.IsWindows())
            source.Must().Be("Segoe UI Variable Text, Segoe UI");
        else if (OperatingSystem.IsMacOS())
            source.Must().Be(".AppleSystemUIFont, SF Pro Text, Helvetica Neue");
        else
            source.Must().Be("Cantarell, Ubuntu, Noto Sans, fonts:Inter#Inter");
    }

    [Theory]
    [InlineData(UiFontFamily.Inter, "fonts:Inter#Inter")]
    [InlineData(UiFontFamily.SourceSans3, "fonts:HelixSourceSans#Source Sans 3")]
    [InlineData(UiFontFamily.IbmPlexSans, "fonts:HelixPlex#IBM Plex Sans")]
    [InlineData(UiFontFamily.GeistSans, "fonts:HelixGeist#Geist")]
    public void ResolveFontFamilySource_shouldReturnTheBundledCollection_whenTheFontIsNotSystem(
        UiFontFamily font,
        string expected)
    {
        UiFontCatalog.ResolveFontFamilySource(font).Must().Be(expected);
    }

    [Fact]
    public void ResolveFontFamilySource_shouldUseThePlatformStack_whenTheFontIsSystem()
    {
        UiFontCatalog.ResolveFontFamilySource(UiFontFamily.System)
            .Must().Be(UiFontCatalog.GetSystemFontFamilySource());
    }
}
