using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels;

namespace HelixExplorer.ViewModels.Tests;

public sealed class WindowChromeTests
{
    [Fact]
    public void ClosedTabs_ReopenInReverseOrder_AndDropPastTheCap()
    {
        var history = new ClosedTabHistory();
        for (var i = 0; i < ClosedTabHistory.Capacity + 2; i++)
            history.Push(new ClosedTabSnapshot($@"C:\folder{i}", LayoutMode.Details));

        history.Count.Must().Be(ClosedTabHistory.Capacity);
        history.TryPop()!.Path.Must().Be($@"C:\folder{ClosedTabHistory.Capacity + 1}");
        history.TryPop()!.ViewMode.Must().Be(LayoutMode.Details);
    }

    [Fact]
    public void ClosedTabs_IgnoreBlankPaths_AndPopEmpty()
    {
        var history = new ClosedTabHistory();
        history.Push(new ClosedTabSnapshot("  ", LayoutMode.Grid));
        history.Count.Must().Be(0);
        history.TryPop().Must().BeNull();

        history.Push(new ClosedTabSnapshot(@"D:\work", LayoutMode.List));
        var reopened = history.TryPop();
        reopened!.Path.Must().Be(@"D:\work");
        reopened.ViewMode.Must().Be(LayoutMode.List);
        history.TryPop().Must().BeNull();
    }

    [Fact]
    public void FocusCycle_SkipsTheOtherPaneUntilSplit()
    {
        ChromeFocusCycle.Next(-1, hasOtherPane: false).Must().Be(ChromeFocusCycle.Sidebar);
        ChromeFocusCycle.Next(ChromeFocusCycle.ActivePane, hasOtherPane: false)
            .Must().Be(ChromeFocusCycle.StatusBar);
        ChromeFocusCycle.Next(ChromeFocusCycle.ActivePane, hasOtherPane: true)
            .Must().Be(ChromeFocusCycle.OtherPane);
        ChromeFocusCycle.Next(ChromeFocusCycle.StatusBar, hasOtherPane: true)
            .Must().Be(ChromeFocusCycle.Sidebar);
    }

    [Fact]
    public void ListingSummary_OmitsSizeWhenOnlyFoldersAreSelected()
    {
        ListingSummaryText.Items(1).Must().Be("1 item");
        ListingSummaryText.Items(12).Must().Be("12 items");

        var folder = Entry(@"C:\docs", "docs", isDirectory: true, size: 0);
        var file = Entry(@"C:\docs\a.txt", "a.txt", isDirectory: false, size: 2048);
        var unknown = Entry(@"C:\docs\b.txt", "b.txt", isDirectory: false, size: -1);

        ListingSummaryText.Selection(0, [file]).Must().BeNull();
        ListingSummaryText.Selection(1, [folder]).Must().Be("1 selected");
        ListingSummaryText.Selection(2, [folder, unknown]).Must().Be("2 selected");
        ListingSummaryText.Selection(2, [folder, file]).Must().Be("2 selected · 2.0 KiB");
        ListingSummaryText.Selection(1, [Entry(@"C:\empty.txt", "empty.txt", false, 0)])
            .Must().Be("1 selected · 0 B");
    }

    [Fact]
    public void SidebarColumn_CollapsesToZeroWithoutForgettingTheExpandedWidth()
    {
        var expanded = SidebarColumnLayout.Resolve(320, collapsed: false);
        expanded.Width.Must().Be(320);
        expanded.MinWidth.Must().Be(SidebarColumnLayout.ExpandedMin);
        expanded.MaxWidth.Must().Be(SidebarColumnLayout.ExpandedMax);

        var collapsed = SidebarColumnLayout.Resolve(320, collapsed: true);
        collapsed.Width.Must().Be(0);
        collapsed.MinWidth.Must().Be(0);
        collapsed.MaxWidth.Must().Be(0);
    }

    private static EntryItemViewModel Entry(string path, string name, bool isDirectory, long size)
        => new(new FileSystemEntry(path, name, isDirectory, size, DateTime.UnixEpoch, isDirectory ? string.Empty : ".txt"));
}
