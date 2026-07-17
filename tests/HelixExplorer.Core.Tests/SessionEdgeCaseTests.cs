using HelixExplorer.Core.Models;
using HelixExplorer.Core.Session;

namespace HelixExplorer.Core.Tests;

public sealed class SessionEdgeCaseTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public void RoundTrip_PreservesActiveTabIndexRawValue(int storedIndex)
    {
        var path = Path.Combine(Path.GetTempPath(), $"helix-session-clamp-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSessionStore(path);
            store.Save(new SessionDocument
            {
                ActiveTabIndex = storedIndex,
                Tabs = { new TabSnapshot { LeftPane = new PaneSnapshot { Path = @"C:\" } } }
            });

            var loaded = new JsonSessionStore(path).Load();
            loaded.ActiveTabIndex.Must().Be(storedIndex);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void RoundTrip_PreservesRecentPathsCount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"helix-session-recent-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSessionStore(path);
            var document = new SessionDocument
            {
                RecentPaths = Enumerable.Range(0, 20).Select(i => $"C:\\Recent{i}").ToList(),
                Tabs = { new TabSnapshot { LeftPane = new PaneSnapshot { Path = @"C:\" } } }
            };

            store.Save(document);
            var loaded = new JsonSessionStore(path).Load();

            loaded.RecentPaths.Count.Must().Be(20);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void RoundTrip_PreservesDualPaneState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"helix-session-dual-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSessionStore(path);
            var document = new SessionDocument
            {
                Tabs =
                {
                    new TabSnapshot
                    {
                        IsDualPane = true,
                        IsRightPaneActive = true,
                        Orientation = PaneSplitOrientation.Horizontal,
                        LeftPane = new PaneSnapshot { Path = @"C:\Left", ViewMode = LayoutMode.Grid },
                        RightPane = new PaneSnapshot { Path = @"D:\Right", ViewMode = LayoutMode.Details }
                    }
                }
            };

            store.Save(document);
            var loaded = new JsonSessionStore(path).Load();

            loaded.Tabs.Must().HaveCount(1);
            var tab = loaded.Tabs[0];
            tab.IsDualPane.Must().BeTrue();
            tab.IsRightPaneActive.Must().BeTrue();
            tab.Orientation.Must().Be(PaneSplitOrientation.Horizontal);
            tab.LeftPane.Path.Must().Be(@"C:\Left");
            tab.LeftPane.ViewMode.Must().Be(LayoutMode.Grid);
            tab.RightPane.Must().NotBeNull();
            tab.RightPane!.Path.Must().Be(@"D:\Right");
            tab.RightPane.ViewMode.Must().Be(LayoutMode.Details);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Save_CorruptedFile_ReturnsEmptyDocument()
    {
        var path = Path.Combine(Path.GetTempPath(), $"helix-session-corrupt-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not valid json");
            var loaded = new JsonSessionStore(path).Load();

            loaded.Tabs.Must().BeEmpty();
            loaded.RecentPaths.Must().BeEmpty();
            loaded.ActiveTabIndex.Must().Be(0);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
