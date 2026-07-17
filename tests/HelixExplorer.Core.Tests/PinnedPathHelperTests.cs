using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.Core.Tests;

public class PinnedPathHelperTests
{
    [Fact]
    public void MergeUserPins_PrefersUserOrderThenDefaults()
    {
        var user = new[] { @"D:\Projects", @"C:\Custom" };
        var defaults = new[] { @"C:\Users", @"D:\Projects" };

        var merged = PinnedPathHelper.MergeUserPins(user, defaults);

        merged.Count.Must().Be(3);
        merged[0].Path.Must().Be(@"D:\Projects");
        merged[1].Path.Must().Be(@"C:\Custom");
        merged[2].Path.Must().Be(@"C:\Users");
    }

    [Fact]
    public void MergeUserPins_SkipsUnpinnedDefaults()
    {
        var user = Array.Empty<string>();
        var defaults = new[] { @"C:\Users", @"C:\Desktop" };
        var unpinned = new[] { @"C:\Desktop" };

        var merged = PinnedPathHelper.MergeUserPins(user, defaults, unpinned);

        merged.Must().HaveCount(1);
        merged[0].Path.Must().Be(@"C:\Users");
    }

    [Fact]
    public void IsPinnedOrDefault_ReturnsFalseForUnpinnedDefault()
    {
        var pinned = new List<string>();
        var unpinned = new List<string> { @"C:\Desktop" };
        var defaults = new[] { @"C:\Desktop", @"C:\Users" };

        PinnedPathHelper.IsPinnedOrDefault(pinned, unpinned, defaults, @"C:\Desktop").Must().BeFalse();
        PinnedPathHelper.IsPinnedOrDefault(pinned, unpinned, defaults, @"C:\Users").Must().BeTrue();
    }

    [Fact]
    public void IsVisibleInSidebar_MatchesMergedSidebarState()
    {
        var pinned = new List<string> { @"D:\Work" };
        var unpinned = new List<string> { @"C:\Desktop" };
        var defaults = new[] { @"C:\Desktop", @"C:\Users" };

        PinnedPathHelper.IsVisibleInSidebar(pinned, unpinned, defaults, @"D:\Work").Must().BeTrue();
        PinnedPathHelper.IsVisibleInSidebar(pinned, unpinned, defaults, @"C:\Desktop").Must().BeFalse();
        PinnedPathHelper.IsVisibleInSidebar(pinned, unpinned, defaults, @"C:\Users").Must().BeTrue();
    }

    [Fact]
    public void IsPinned_IsCaseInsensitive()
    {
        var pins = new List<string> { @"C:\Work" };

        PinnedPathHelper.IsPinned(pins, @"c:\work").Must().BeTrue();
        PinnedPathHelper.IsPinned(pins, @"C:\Other").Must().BeFalse();
    }
}

public class ClipboardCutStateTests
{
    [Fact]
    public void IsPathCut_IsCaseInsensitive()
    {
        var clipboard = new InternalClipboardService();
        clipboard.SetCut([@"C:\Work\File.txt"], @"C:\Work");

        ClipboardCutState.IsPathCut(clipboard, @"c:\work\file.txt").Must().BeTrue();
        ClipboardCutState.IsPathCut(clipboard, @"C:\Other").Must().BeFalse();
    }

    [Fact]
    public void IsPathCut_ReturnsFalseForCopyOperation()
    {
        var clipboard = new InternalClipboardService();
        clipboard.SetCopy([@"C:\Work\File.txt"], @"C:\Work");

        ClipboardCutState.IsPathCut(clipboard, @"C:\Work\File.txt").Must().BeFalse();
    }
}
