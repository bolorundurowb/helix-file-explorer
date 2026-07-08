using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Settings;
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
    public void MergeUserPins_SkipsUnpinnedDefaults()
    {
        var user = Array.Empty<string>();
        var defaults = new[] { @"C:\Users", @"C:\Desktop" };
        var unpinned = new[] { @"C:\Desktop" };

        var merged = PinnedPathHelper.MergeUserPins(user, defaults, unpinned);

        Assert.Single(merged);
        Assert.Equal(@"C:\Users", merged[0].Path);
    }

    [Fact]
    public void IsPinnedOrDefault_ReturnsFalseForUnpinnedDefault()
    {
        var pinned = new List<string>();
        var unpinned = new List<string> { @"C:\Desktop" };
        var defaults = new[] { @"C:\Desktop", @"C:\Users" };

        Assert.False(PinnedPathHelper.IsPinnedOrDefault(pinned, unpinned, defaults, @"C:\Desktop"));
        Assert.True(PinnedPathHelper.IsPinnedOrDefault(pinned, unpinned, defaults, @"C:\Users"));
    }

    [Fact]
    public void IsVisibleInSidebar_MatchesMergedSidebarState()
    {
        var pinned = new List<string> { @"D:\Work" };
        var unpinned = new List<string> { @"C:\Desktop" };
        var defaults = new[] { @"C:\Desktop", @"C:\Users" };

        Assert.True(PinnedPathHelper.IsVisibleInSidebar(pinned, unpinned, defaults, @"D:\Work"));
        Assert.False(PinnedPathHelper.IsVisibleInSidebar(pinned, unpinned, defaults, @"C:\Desktop"));
        Assert.True(PinnedPathHelper.IsVisibleInSidebar(pinned, unpinned, defaults, @"C:\Users"));
    }

    [Fact]
    public void IsPinned_IsCaseInsensitive()
    {
        var pins = new List<string> { @"C:\Work" };

        Assert.True(PinnedPathHelper.IsPinned(pins, @"c:\work"));
        Assert.False(PinnedPathHelper.IsPinned(pins, @"C:\Other"));
    }
}

public class ClipboardCutStateTests
{
    [Fact]
    public void IsPathCut_IsCaseInsensitive()
    {
        var clipboard = new InternalClipboardService();
        clipboard.SetCut([@"C:\Work\File.txt"], @"C:\Work");

        Assert.True(ClipboardCutState.IsPathCut(clipboard, @"c:\work\file.txt"));
        Assert.False(ClipboardCutState.IsPathCut(clipboard, @"C:\Other"));
    }

    [Fact]
    public void IsPathCut_ReturnsFalseForCopyOperation()
    {
        var clipboard = new InternalClipboardService();
        clipboard.SetCopy([@"C:\Work\File.txt"], @"C:\Work");

        Assert.False(ClipboardCutState.IsPathCut(clipboard, @"C:\Work\File.txt"));
    }
}
