using HelixExplorer.Core.Models;
using HelixExplorer.Core.Session;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class PinnedPathHelperTests
{
    [Fact]
    public void MergeUserPins_PrefersUserOrderThenDefaults()
    {
        var user = new[] { @"D:\Projects", @"C:\Custom" };
        var defaults = new[] { @"C:\Users", @"D:\Projects" };

        var merged = PinnedPathHelper.MergeUserPins(user, defaults);

        Assert.Equal(3, merged.Count);
        Assert.Equal(@"D:\Projects", merged[0].Path);
        Assert.Equal(@"C:\Custom", merged[1].Path);
        Assert.Equal(@"C:\Users", merged[2].Path);
    }

    [Fact]
    public void IsPinned_IsCaseInsensitive()
    {
        var pins = new List<string> { @"C:\Work" };

        Assert.True(PinnedPathHelper.IsPinned(pins, @"c:\work"));
        Assert.False(PinnedPathHelper.IsPinned(pins, @"C:\Other"));
    }
}

public class AccentColorDefaultsTests
{
    [Fact]
    public void Resolve_UsesCustomWhenProvided()
    {
        Assert.Equal(0xFF123456u, AccentColorDefaults.Resolve(0xFF123456, isDarkTheme: false));
    }

    [Fact]
    public void Resolve_UsesThemeDefaultsWhenNull()
    {
        Assert.Equal(AccentColorDefaults.Light, AccentColorDefaults.Resolve(null, isDarkTheme: false));
        Assert.Equal(AccentColorDefaults.Dark, AccentColorDefaults.Resolve(null, isDarkTheme: true));
    }

    [Fact]
    public void FromHex_ParsesSixDigitHex()
    {
        Assert.Equal(0xFF0078D4u, AccentColorDefaults.FromHex("#0078D4"));
    }
}

public class SessionSettingsOwnershipTests
{
    [Fact]
    public void SessionDocument_StoresWorkspaceOnly()
    {
        var session = new SessionDocument
        {
            ActiveTabIndex = 2,
            RecentPaths = ["C:\\A", "C:\\B"]
        };

        Assert.Equal(2, session.ActiveTabIndex);
        Assert.Equal(2, session.RecentPaths.Count);
    }

    [Fact]
    public void AppSettings_StoresChromePreferences()
    {
        var settings = new AppSettings
        {
            SidebarOpen = false,
            SidebarWidth = 280,
            DefaultViewMode = Models.LayoutMode.Grid,
            DefaultThumbnailSize = 96,
            DefaultDualPane = true,
            AccentColorArgb = 0xFF107C10,
            PinnedPaths = ["C:\\Pinned"]
        };

        Assert.False(settings.SidebarOpen);
        Assert.Equal(280, settings.SidebarWidth);
        Assert.Equal(Models.LayoutMode.Grid, settings.DefaultViewMode);
        Assert.True(settings.DefaultDualPane);
        Assert.Single(settings.PinnedPaths);
    }
}
