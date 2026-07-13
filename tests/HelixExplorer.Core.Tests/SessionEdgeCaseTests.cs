using HelixExplorer.Core.Models;
using HelixExplorer.Core.Session;
using Xunit;

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
            Assert.Equal(storedIndex, loaded.ActiveTabIndex);
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

            Assert.Equal(20, loaded.RecentPaths.Count);
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

            var tab = Assert.Single(loaded.Tabs);
            Assert.True(tab.IsDualPane);
            Assert.True(tab.IsRightPaneActive);
            Assert.Equal(PaneSplitOrientation.Horizontal, tab.Orientation);
            Assert.Equal(@"C:\Left", tab.LeftPane.Path);
            Assert.Equal(LayoutMode.Grid, tab.LeftPane.ViewMode);
            Assert.NotNull(tab.RightPane);
            Assert.Equal(@"D:\Right", tab.RightPane!.Path);
            Assert.Equal(LayoutMode.Details, tab.RightPane.ViewMode);
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

            Assert.Empty(loaded.Tabs);
            Assert.Empty(loaded.RecentPaths);
            Assert.Equal(0, loaded.ActiveTabIndex);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
