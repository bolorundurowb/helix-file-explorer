using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels.Tests;

public sealed class ListingPipelineTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 13, 30, 0, DateTimeKind.Utc);

    private static FileSystemEntry Entry(string name, bool hidden = false, long size = 1, bool isDirectory = false)
        => new(
            @"C:\root\" + name,
            name,
            isDirectory,
            isDirectory ? 0 : size,
            Now,
            isDirectory ? string.Empty : Path.GetExtension(name),
            hidden);

    [Fact]
    public void FilterHidden_DropsHiddenEntries()
    {
        var source = new List<FileSystemEntry> { Entry("a.txt"), Entry("b.txt", hidden: true), Entry("c.txt") };
        var destination = new List<FileSystemEntry>();

        ListingPipeline.FilterHidden(source, showHiddenFiles: false, destination);

        destination.Select(e => e.Name).Must().BeSequenceEqual(new[] { "a.txt", "c.txt" });
    }

    [Fact]
    public void FilterHidden_KeepsHiddenWhenRequested()
    {
        var source = new List<FileSystemEntry> { Entry("a.txt"), Entry("b.txt", hidden: true) };
        var destination = new List<FileSystemEntry>();

        ListingPipeline.FilterHidden(source, showHiddenFiles: true, destination);

        destination.Count.Must().Be(2);
    }

    [Fact]
    public void FilterText_AppliesFilter()
    {
        var source = new List<FileSystemEntry> { Entry("alpha.txt"), Entry("beta.txt") };
        var destination = new List<FileSystemEntry>();

        ListingPipeline.FilterText(source, isFilterVisible: true, "alpha", destination);

        destination.Select(e => e.Name).Must().BeSequenceEqual(new[] { "alpha.txt" });
    }

    [Fact]
    public void FilterText_IsNoopWhenFilterHidden()
    {
        var source = new List<FileSystemEntry> { Entry("alpha.txt"), Entry("beta.txt") };
        var destination = new List<FileSystemEntry>();

        ListingPipeline.FilterText(source, isFilterVisible: false, "alpha", destination);

        destination.Count.Must().Be(2);
    }

    [Fact]
    public void Sort_OrdersByName()
    {
        var entries = new List<FileSystemEntry> { Entry("zulu.txt"), Entry("alpha.txt"), Entry("1st.txt") };

        ListingPipeline.Sort(entries, GroupByMode.None, Now, SortColumn.Name, sortDescending: false, DirectorySortMode.MixedWithFiles);

        entries.Select(e => e.Name).Must().BeSequenceEqual(new[] { "1st.txt", "alpha.txt", "zulu.txt" });
    }

    [Fact]
    public void Aggregate_ComputesCountsAndSize()
    {
        var visible = new List<FileSystemEntry> { Entry("a.txt", size: 10), Entry("b.txt", size: 20), Entry("dir", isDirectory: true) };
        var filtered = new List<FileSystemEntry> { Entry("a.txt", size: 10), Entry("dir", isDirectory: true) };

        var aggregate = ListingPipeline.Aggregate(visible, filtered);

        aggregate.TotalCount.Must().Be(3);
        aggregate.ItemCount.Must().Be(2);
        aggregate.SizeBytes.Must().Be(10);
    }

    [Fact]
    public void Materialize_RecyclesPooledViewModels()
    {
        var pool = new Dictionary<string, EntryItemViewModel>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<FileSystemEntry> { Entry("alpha.txt"), Entry("zulu.txt") };

        var first = ListingPipeline.Materialize(entries, GitStatusSnapshot.Empty, showFileExtensions: true, pool);
        first.VisualTargets.Count.Must().Be(2);
        var alpha = first.Entries[0];

        var second = ListingPipeline.Materialize(entries, GitStatusSnapshot.Empty, showFileExtensions: true, pool);

        second.VisualTargets.Must().BeEmpty();
        ReferenceEquals(second.Entries[0], alpha).Must().BeTrue();
    }
}
