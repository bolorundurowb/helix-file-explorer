using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// The ordered, allocation-conscious transformation stages that turn a raw directory listing into
/// publishable view models. Each stage is a pure function over explicit inputs (and, where pooling
/// matters, a caller-provided buffer), so every stage can be unit-tested and benchmarked in isolation
/// against synthetic listings. <see cref="PaneListingCoordinator"/> owns the long-lived buffers and
/// composes the stages in order: hidden filter → text filter → sort → aggregate → materialize.
/// </summary>
public static class ListingPipeline
{
    /// <summary>Stage 1 — drop hidden entries unless the view is showing them.</summary>
    public static void FilterHidden(
        IReadOnlyList<FileSystemEntry> source,
        bool showHiddenFiles,
        List<FileSystemEntry> destination)
    {
        destination.Clear();
        foreach (var entry in source)
        {
            if (showHiddenFiles || !entry.IsHidden)
                destination.Add(entry);
        }
    }

    /// <summary>Stage 2 — apply the live filter text (no-op when the filter is not visible).</summary>
    public static void FilterText(
        IReadOnlyList<FileSystemEntry> source,
        bool isFilterVisible,
        string filterText,
        List<FileSystemEntry> destination)
        => FileNameFilter.Apply(source, isFilterVisible ? filterText : null, destination);

    /// <summary>Stage 3 — order by the pane's grouped comparer, in place.</summary>
    public static void Sort(
        List<FileSystemEntry> entries,
        GroupByMode groupBy,
        DateTime groupingUtcNow,
        SortColumn sortColumn,
        bool sortDescending,
        DirectorySortMode directorySort)
        => entries.Sort(FileSystemEntryComparer.ForGrouped(groupBy, groupingUtcNow, sortColumn, sortDescending, directorySort));

    /// <summary>Stage 4 — compute the counts and size the pane surfaces for this listing.</summary>
    public static ListingAggregate Aggregate(
        IReadOnlyList<FileSystemEntry> visibleEntries,
        IReadOnlyList<FileSystemEntry> filteredEntries)
    {
        long sizeBytes = 0;
        foreach (var entry in filteredEntries)
        {
            if (!entry.IsDirectory)
                sizeBytes += entry.SizeBytes;
        }

        return new ListingAggregate(visibleEntries.Count, filteredEntries.Count, sizeBytes);
    }

    /// <summary>
    /// Stage 5 — recycle pooled view models (tagging git status) and collect the ones created new.
    /// The pool is a long-lived cache owned by the coordinator; passing it in keeps this stage pure
    /// in isolation while retaining the reuse that avoids re-fetching icons on filter widening.
    /// </summary>
    public static (IReadOnlyList<EntryItemViewModel> Entries, IReadOnlyList<EntryItemViewModel> VisualTargets) Materialize(
        IReadOnlyList<FileSystemEntry> sortedEntries,
        GitStatusSnapshot gitSnapshot,
        bool showFileExtensions,
        Dictionary<string, EntryItemViewModel> entryPool)
    {
        var entries = new List<EntryItemViewModel>(sortedEntries.Count);
        var visualTargets = new List<EntryItemViewModel>();

        foreach (var entry in sortedEntries)
        {
            var path = entry.FullPath;
            var gitStatus = gitSnapshot.GetStatusForPath(path);

            if (!entryPool.TryGetValue(path, out var item))
            {
                item = new EntryItemViewModel(entry, showFileExtensions, gitStatus);
                entryPool[path] = item;
                visualTargets.Add(item);
            }
            else
            {
                item.UpdateEntry(entry, showFileExtensions, gitStatus);
            }

            entries.Add(item);
        }

        return (entries, visualTargets);
    }
}

/// <summary>Aggregate of the visible/filtered listing stages.</summary>
public readonly record struct ListingAggregate(int TotalCount, int ItemCount, long SizeBytes);
