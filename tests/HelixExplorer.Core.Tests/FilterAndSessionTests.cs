using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Session;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class FileNameFilterTests
{
    private static FileSystemEntry Entry(string name)
        => new($@"C:\{name}", name, false, 0, DateTime.UtcNow, Path.GetExtension(name));

    [Theory]
    [InlineData("Report.docx", "report", true)]
    [InlineData("Report.docx", "DOCX", true)]
    [InlineData("Report.docx", "r", true)]
    [InlineData("Report.docx", "R", true)]
    [InlineData("Report.docx", "xls", false)]
    [InlineData("Report.docx", "z", false)]
    [InlineData("Report.docx", "", true)]
    [InlineData("Report.docx", "   ", true)]
    public void Matches_IsCaseInsensitiveSubstring(string name, string query, bool expected)
    {
        Assert.Equal(expected, FileNameFilter.Matches(Entry(name), query));
    }

    [Fact]
    public void Apply_EmptyQuery_ReturnsAll()
    {
        var source = new[] { Entry("a.txt"), Entry("b.txt"), Entry("c.md") };
        var dest = new List<FileSystemEntry>();

        var count = FileNameFilter.Apply(source, null, dest);

        Assert.Equal(3, count);
        Assert.Equal(3, dest.Count);
    }

    [Fact]
    public void Apply_FiltersAndPreservesOrder()
    {
        var source = new[] { Entry("alpha.txt"), Entry("beta.log"), Entry("gamma.txt") };
        var dest = new List<FileSystemEntry>();

        FileNameFilter.Apply(source, "txt", dest);

        Assert.Equal(2, dest.Count);
        Assert.Equal("alpha.txt", dest[0].Name);
        Assert.Equal("gamma.txt", dest[1].Name);
    }

    [Fact]
    public void Apply_ReusesDestinationBuffer()
    {
        var dest = new List<FileSystemEntry> { Entry("stale.txt") };
        var source = new[] { Entry("keep.md") };

        FileNameFilter.Apply(source, "keep", dest);

        Assert.Single(dest);
        Assert.Equal("keep.md", dest[0].Name);
    }

    [Fact]
    public void Apply_10kEntries_CompletesQuickly()
    {
        var source = new List<FileSystemEntry>(10_000);
        for (var i = 0; i < 10_000; i++)
            source.Add(Entry($"file-{i:D5}.txt"));

        var dest = new List<FileSystemEntry>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var count = FileNameFilter.Apply(source, "123", dest);
        sw.Stop();

        Assert.True(count > 0);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Filter took {sw.ElapsedMilliseconds}ms");
    }
}

public class SessionStoreTests
{
    [Fact]
    public void RoundTrip_PreservesTabsAndRecentPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), $"helix-session-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSessionStore(path);
            var document = new SessionDocument
            {
                ActiveTabIndex = 1,
                RecentPaths = ["C:\\Recent", "D:\\Other"],
                Tabs =
                {
                    new TabSnapshot
                    {
                        LeftPane = new PaneSnapshot { Path = @"C:\Users", ViewMode = LayoutMode.List, ThumbnailSize = 96 }
                    },
                    new TabSnapshot
                    {
                        IsDualPane = true,
                        IsRightPaneActive = true,
                        Orientation = PaneSplitOrientation.Horizontal,
                        TintArgb = 0xFF0078D4,
                        LeftPane = new PaneSnapshot { Path = @"C:\", ViewMode = LayoutMode.Grid, SortColumn = SortColumn.Size, SortDescending = true },
                        RightPane = new PaneSnapshot { Path = @"D:\", ViewMode = LayoutMode.Details }
                    }
                }
            };

            store.Save(document);
            var loaded = new JsonSessionStore(path).Load();

            Assert.Equal(1, loaded.ActiveTabIndex);
            Assert.Equal(2, loaded.RecentPaths.Count);
            Assert.Equal(2, loaded.Tabs.Count);

            var first = loaded.Tabs[0];
            Assert.Equal(@"C:\Users", first.LeftPane.Path);
            Assert.Equal(LayoutMode.List, first.LeftPane.ViewMode);
            Assert.Equal(96, first.LeftPane.ThumbnailSize);
            Assert.False(first.IsDualPane);
            Assert.Null(first.RightPane);

            var second = loaded.Tabs[1];
            Assert.True(second.IsDualPane);
            Assert.True(second.IsRightPaneActive);
            Assert.Equal(PaneSplitOrientation.Horizontal, second.Orientation);
            Assert.Equal(0xFF0078D4u, second.TintArgb);
            Assert.Equal(SortColumn.Size, second.LeftPane.SortColumn);
            Assert.True(second.LeftPane.SortDescending);
            Assert.NotNull(second.RightPane);
            Assert.Equal(@"D:\", second.RightPane!.Path);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyDocument()
    {
        var path = Path.Combine(Path.GetTempPath(), $"helix-missing-{Guid.NewGuid():N}.json");
        var loaded = new JsonSessionStore(path).Load();

        Assert.Empty(loaded.Tabs);
        Assert.Empty(loaded.RecentPaths);
    }

    [Fact]
    public void Save_OverwritesExistingFileAtomically()
    {
        var path = Path.Combine(Path.GetTempPath(), $"helix-overwrite-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSessionStore(path);
            store.Save(new SessionDocument { ActiveTabIndex = 0 });
            store.Save(new SessionDocument { ActiveTabIndex = 5 });

            Assert.Equal(5, new JsonSessionStore(path).Load().ActiveTabIndex);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
