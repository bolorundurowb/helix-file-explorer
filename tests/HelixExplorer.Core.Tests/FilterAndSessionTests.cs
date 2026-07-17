using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Session;

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
        FileNameFilter.Matches(Entry(name), query).Must().Be(expected);
    }

    [Fact]
    public void Apply_EmptyQuery_ReturnsAll()
    {
        var source = new[] { Entry("a.txt"), Entry("b.txt"), Entry("c.md") };
        var dest = new List<FileSystemEntry>();

        var count = FileNameFilter.Apply(source, null, dest);

        count.Must().Be(3);
        dest.Count.Must().Be(3);
    }

    [Fact]
    public void Apply_FiltersAndPreservesOrder()
    {
        var source = new[] { Entry("alpha.txt"), Entry("beta.log"), Entry("gamma.txt") };
        var dest = new List<FileSystemEntry>();

        FileNameFilter.Apply(source, "txt", dest);

        dest.Count.Must().Be(2);
        dest[0].Name.Must().Be("alpha.txt");
        dest[1].Name.Must().Be("gamma.txt");
    }

    [Fact]
    public void Apply_ReusesDestinationBuffer()
    {
        var dest = new List<FileSystemEntry> { Entry("stale.txt") };
        var source = new[] { Entry("keep.md") };

        FileNameFilter.Apply(source, "keep", dest);

        dest.Must().HaveCount(1);
        dest[0].Name.Must().Be("keep.md");
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

        count.Must().BeGreaterThan(0);
        sw.ElapsedMilliseconds.Must().BeLessThan(500);
    }

    [Fact]
    public void Apply_SupportsGlobPatterns()
    {
        var source = new[] { Entry("alpha.pdf"), Entry("beta.txt"), Entry("gamma.PDF") };
        var dest = new List<FileSystemEntry>();

        FileNameFilter.Apply(source, "*.pdf", dest);

        dest.Count.Must().Be(2);
        dest.Must().Contain(e => e.Name == "alpha.pdf");
        dest.Must().Contain(e => e.Name == "gamma.PDF");
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

            loaded.ActiveTabIndex.Must().Be(1);
            loaded.RecentPaths.Count.Must().Be(2);
            loaded.Tabs.Count.Must().Be(2);

            var first = loaded.Tabs[0];
            first.LeftPane.Path.Must().Be(@"C:\Users");
            first.LeftPane.ViewMode.Must().Be(LayoutMode.List);
            first.LeftPane.ThumbnailSize.Must().Be(96);
            first.IsDualPane.Must().BeFalse();
            first.RightPane.Must().BeNull();

            var second = loaded.Tabs[1];
            second.IsDualPane.Must().BeTrue();
            second.IsRightPaneActive.Must().BeTrue();
            second.Orientation.Must().Be(PaneSplitOrientation.Horizontal);
            second.TintArgb.Must().Be(0xFF0078D4u);
            second.LeftPane.SortColumn.Must().Be(SortColumn.Size);
            second.LeftPane.SortDescending.Must().BeTrue();
            second.RightPane.Must().NotBeNull();
            second.RightPane!.Path.Must().Be(@"D:\");
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

        loaded.Tabs.Must().BeEmpty();
        loaded.RecentPaths.Must().BeEmpty();
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

            new JsonSessionStore(path).Load().ActiveTabIndex.Must().Be(5);
            File.Exists(path + ".tmp").Must().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
