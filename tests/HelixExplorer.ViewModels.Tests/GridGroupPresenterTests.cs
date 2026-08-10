using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels.Tests;

public class GridGroupPresenterTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 13, 30, 0, DateTimeKind.Utc);

    private static readonly IReadOnlySet<string> NothingCollapsed = new HashSet<string>(StringComparer.Ordinal);

    private static EntryItemViewModel Entry(string name, long size = 1, bool isDirectory = false)
        => new(new FileSystemEntry(
            @"C:\root\" + name,
            name,
            isDirectory,
            size,
            Now,
            isDirectory ? string.Empty : Path.GetExtension(name)));

    /// <summary>Mirrors what the pane feeds the presenter: entries are always grouped-sorted first.</summary>
    private static List<EntryItemViewModel> Sorted(GroupByMode mode, params EntryItemViewModel[] entries)
    {
        var comparer = FileSystemEntryComparer.ForGrouped(
            mode, Now, SortColumn.Name, descending: false, DirectorySortMode.MixedWithFiles);
        return [.. entries.OrderBy(e => e.Entry, comparer)];
    }

    [Fact]
    public void Build_None_ReturnsEntriesUntouched()
    {
        var entries = new List<EntryItemViewModel> { Entry("a.txt"), Entry("b.txt") };

        var items = new GridGroupPresenter().Build(entries, GroupByMode.None, Now, NothingCollapsed);

        items.Count.Must().Be(2);
        items.OfType<GroupHeaderViewModel>().Must().BeEmpty();
        ReferenceEquals(items[0], entries[0]).Must().BeTrue();
    }

    [Fact]
    public void Build_EmitsOneHeaderPerNonEmptyBucket_WithCounts()
    {
        var entries = Sorted(GroupByMode.Name, Entry("alpha.txt"), Entry("beta.txt"), Entry("zulu.txt"));

        var items = new GridGroupPresenter().Build(entries, GroupByMode.Name, Now, NothingCollapsed);

        var headers = items.OfType<GroupHeaderViewModel>().ToList();
        headers.Select(h => h.Key).Must().BeSequenceEqual(new[] { "name_a_h", "name_q_z" });
        headers[0].ItemCount.Must().Be(2);
        headers[1].ItemCount.Must().Be(1);
        headers.Select(h => h.Title).Must().BeSequenceEqual(new[] { "A–H", "Q–Z" });

        // Header, its two entries, header, its one entry.
        items.Count.Must().Be(5);
    }

    [Fact]
    public void Build_EmptyBuckets_AreOmitted()
    {
        var entries = Sorted(GroupByMode.Name, Entry("alpha.txt"));

        var items = new GridGroupPresenter().Build(entries, GroupByMode.Name, Now, NothingCollapsed);

        items.OfType<GroupHeaderViewModel>().Select(h => h.Key).Must().BeSequenceEqual(new[] { "name_a_h" });
    }

    [Fact]
    public void Build_CollapsedKey_KeepsHeaderAndDropsChildren()
    {
        var entries = Sorted(GroupByMode.Name, Entry("alpha.txt"), Entry("beta.txt"), Entry("zulu.txt"));
        var collapsed = new HashSet<string>(StringComparer.Ordinal) { "name_a_h" };

        var items = new GridGroupPresenter().Build(entries, GroupByMode.Name, Now, collapsed);

        var headers = items.OfType<GroupHeaderViewModel>().ToList();
        headers.Select(h => h.Key).Must().BeSequenceEqual(new[] { "name_a_h", "name_q_z" });
        headers[0].IsCollapsed.Must().BeTrue();
        headers[0].IsExpanded.Must().BeFalse();

        // The collapsed group still reports its real size even though its tiles are hidden.
        headers[0].ItemCount.Must().Be(2);

        var visible = items.OfType<EntryItemViewModel>().Select(e => e.Name).ToList();
        visible.Must().BeSequenceEqual(new[] { "zulu.txt" });
    }

    [Fact]
    public void Build_ExpandingAgain_RestoresChildren()
    {
        var presenter = new GridGroupPresenter();
        var entries = Sorted(GroupByMode.Name, Entry("alpha.txt"), Entry("zulu.txt"));
        var collapsed = new HashSet<string>(StringComparer.Ordinal) { "name_a_h" };

        presenter.Build(entries, GroupByMode.Name, Now, collapsed).Count.Must().Be(3);

        collapsed.Clear();
        var items = presenter.Build(entries, GroupByMode.Name, Now, collapsed);

        items.Count.Must().Be(4);
        items.OfType<GroupHeaderViewModel>().First().IsCollapsed.Must().BeFalse();
    }

    [Fact]
    public void Build_ReusesHeaderInstances_AcrossRebuilds()
    {
        var presenter = new GridGroupPresenter();
        var entries = Sorted(GroupByMode.Name, Entry("alpha.txt"));

        var first = presenter.Build(entries, GroupByMode.Name, Now, NothingCollapsed).OfType<GroupHeaderViewModel>().Single();
        var second = presenter.Build(entries, GroupByMode.Name, Now, NothingCollapsed).OfType<GroupHeaderViewModel>().Single();

        ReferenceEquals(first, second).Must().BeTrue();
    }

    [Fact]
    public void Build_AfterReset_CreatesFreshHeaders()
    {
        var presenter = new GridGroupPresenter();
        var entries = Sorted(GroupByMode.Name, Entry("alpha.txt"));

        var first = presenter.Build(entries, GroupByMode.Name, Now, NothingCollapsed).OfType<GroupHeaderViewModel>().Single();
        presenter.Reset();
        var second = presenter.Build(entries, GroupByMode.Name, Now, NothingCollapsed).OfType<GroupHeaderViewModel>().Single();

        ReferenceEquals(first, second).Must().BeFalse();
    }

    [Fact]
    public void Build_TracksUnderlyingEntryChanges()
    {
        var presenter = new GridGroupPresenter();
        var entries = Sorted(GroupByMode.Name, Entry("alpha.txt"), Entry("beta.txt"));

        presenter.Build(entries, GroupByMode.Name, Now, NothingCollapsed).Count.Must().Be(3);

        // A refresh that adds an entry in a new bucket must add that bucket's header too.
        entries = Sorted(GroupByMode.Name, Entry("alpha.txt"), Entry("beta.txt"), Entry("zulu.txt"));
        var items = presenter.Build(entries, GroupByMode.Name, Now, NothingCollapsed);

        items.Count.Must().Be(5);
        items.OfType<GroupHeaderViewModel>().Select(h => h.Key).Must().BeSequenceEqual(new[] { "name_a_h", "name_q_z" });

        // ...and a refresh that empties a bucket must drop its header.
        entries = Sorted(GroupByMode.Name, Entry("zulu.txt"));
        items = presenter.Build(entries, GroupByMode.Name, Now, NothingCollapsed);

        items.Count.Must().Be(2);
        items.OfType<GroupHeaderViewModel>().Select(h => h.Key).Must().BeSequenceEqual(new[] { "name_q_z" });
    }

    [Fact]
    public void Build_ByType_PutsFoldersBandFirst()
    {
        var entries = Sorted(
            GroupByMode.Type,
            Entry("notes.md"),
            Entry("pics", isDirectory: true),
            Entry("archive.zip"));

        var items = new GridGroupPresenter().Build(entries, GroupByMode.Type, Now, NothingCollapsed);

        items.OfType<GroupHeaderViewModel>().Select(h => h.Key)
            .Must().BeSequenceEqual(new[] { "type_folder", "type_md", "type_zip" });
        (items[0] as GroupHeaderViewModel)!.Title.Must().Be("Folders");
    }

    [Fact]
    public void Build_SwitchingModes_ReplacesHeadersEntirely()
    {
        var presenter = new GridGroupPresenter();
        var byName = Sorted(GroupByMode.Name, Entry("alpha.txt"), Entry("zulu.txt"));
        presenter.Build(byName, GroupByMode.Name, Now, NothingCollapsed);

        var bySize = Sorted(GroupByMode.Size, Entry("alpha.txt"), Entry("zulu.txt"));
        var items = presenter.Build(bySize, GroupByMode.Size, Now, NothingCollapsed);

        items.OfType<GroupHeaderViewModel>().Select(h => h.Key).Must().BeSequenceEqual(new[] { "size_tiny" });
    }
}

public class GroupedListingPublishTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 13, 30, 0, DateTimeKind.Utc);

    private static FileSystemEntry File(string name)
        => new(@"C:\root\" + name, name, false, 1, Now, Path.GetExtension(name));

    private static ListingPublishRequest Request(GroupByMode groupBy, params FileSystemEntry[] entries)
        => new()
        {
            AllEntries = entries,
            GitSnapshot = GitStatusSnapshot.Empty,
            ShowHiddenFiles = false,
            ShowFileExtensions = true,
            IsFilterVisible = false,
            FilterText = string.Empty,
            SortColumn = SortColumn.Name,
            SortDescending = false,
            DirectorySort = DirectorySortMode.MixedWithFiles,
            GroupBy = groupBy,
            GroupingUtcNow = Now
        };

    [Fact]
    public void ApplySortAndPublish_None_KeepsPlainNameOrder()
    {
        var result = new PaneListingCoordinator().ApplySortAndPublish(
            Request(GroupByMode.None, File("zulu.txt"), File("alpha.txt")));

        result.Entries.Select(e => e.Name).Must().BeSequenceEqual(new[] { "alpha.txt", "zulu.txt" });
    }

    [Fact]
    public void ApplySortAndPublish_Grouped_AppliesBucketOrderToTheFlatListing()
    {
        // The flat listing itself is bucket-ordered so switching layouts never reshuffles tiles.
        var result = new PaneListingCoordinator().ApplySortAndPublish(
            Request(GroupByMode.Name, File("alpha.txt"), File("zulu.txt"), File("1st.txt")));

        result.Entries.Select(e => e.Name).Must().BeSequenceEqual(new[] { "1st.txt", "alpha.txt", "zulu.txt" });
    }
}
