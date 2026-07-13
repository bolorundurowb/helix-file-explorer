using System.Text.Json;
using System.Text.Json.Serialization;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class AppSettingsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void DefaultValues_MatchExpectedChromeDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal(ThemeMode.System, settings.Theme);
        Assert.Equal(UiFontFamily.System, settings.UiFont);
        Assert.Equal(200, settings.SidebarWidth);
        Assert.Equal(SizeDisplayMode.Binary, settings.SizeDisplay);
        Assert.True(settings.ShowFileExtensions);
        Assert.Equal(LayoutMode.Details, settings.DefaultViewMode);
        Assert.Equal(72, settings.DefaultThumbnailSize);
        Assert.False(settings.DefaultDualPane);
        Assert.Null(settings.AccentColorArgb);
        Assert.Empty(settings.PinnedPaths);
    }

    [Fact]
    public void JsonRoundTrip_PreservesChromePreferences()
    {
        var original = new AppSettings
        {
            Theme = ThemeMode.Dark,
            UiFont = UiFontFamily.DmSans,
            SidebarWidth = 280,
            SizeDisplay = SizeDisplayMode.Decimal,
            ShowHiddenFiles = true,
            DefaultViewMode = LayoutMode.Grid,
            DefaultThumbnailSize = 96,
            DefaultDualPane = true,
            AccentColorArgb = 0xFF107C10,
            PinnedPaths = ["C:\\Pinned", "D:\\Work"],
            WindowWidth = 1440,
            WindowHeight = 900,
            WindowX = 120,
            WindowY = 80,
            WindowMaximized = true
        };

        var json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);

        Assert.NotNull(loaded);
        Assert.Equal(ThemeMode.Dark, loaded.Theme);
        Assert.Equal(UiFontFamily.DmSans, loaded.UiFont);
        Assert.Equal(280, loaded.SidebarWidth);
        Assert.Equal(SizeDisplayMode.Decimal, loaded.SizeDisplay);
        Assert.True(loaded.ShowHiddenFiles);
        Assert.Equal(LayoutMode.Grid, loaded.DefaultViewMode);
        Assert.Equal(96, loaded.DefaultThumbnailSize);
        Assert.True(loaded.DefaultDualPane);
        Assert.Equal(0xFF107C10u, loaded.AccentColorArgb);
        Assert.Equal(2, loaded.PinnedPaths.Count);
        Assert.Equal(1440, loaded.WindowWidth);
        Assert.Equal(900, loaded.WindowHeight);
        Assert.Equal(120, loaded.WindowX);
        Assert.Equal(80, loaded.WindowY);
        Assert.True(loaded.WindowMaximized);
    }
}
