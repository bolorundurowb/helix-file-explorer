using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;

namespace HelixExplorer.ViewModels.Pane;

public sealed class PaneListingCoordinator
{
    private readonly Dictionary<string, EntryItemViewModel> _entryPool = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemEntry> _viewBuffer = new();
    private readonly List<FileSystemEntry> _visibleBuffer = new();

    public IReadOnlyDictionary<string, EntryItemViewModel> EntryPool => _entryPool;

    /// <summary>
    /// Drops every pooled entry and hands them back so the caller can release each one's cached
    /// visual (<see cref="Services.FileVisualService.Release"/>). A bare <c>Clear()</c> used to just
    /// drop the dictionary entries, permanently leaking every entry's decoded icon/thumbnail bitmap:
    /// nothing else in the app ever releases that reference once the ViewModel becomes unreachable,
    /// and a leaked reference count blocks the visual cache from ever disposing the bitmap, even after
    /// LRU-evicting it from its own lookup table.
    /// </summary>
    public IReadOnlyList<EntryItemViewModel> ClearEntryPool()
    {
        if (_entryPool.Count == 0)
            return [];

        var evicted = _entryPool.Values.ToList();
        _entryPool.Clear();
        return evicted;
    }

    /// <summary>Removes one pooled entry, returning it (if present) so its cached visual can be released.</summary>
    public EntryItemViewModel? RemoveFromPool(string path)
        => _entryPool.Remove(path, out var item) ? item : null;

    public ListingPublishResult ApplySortAndPublish(ListingPublishRequest request)
    {
        _visibleBuffer.Clear();
        foreach (var entry in request.AllEntries)
        {
            if (!request.ShowHiddenFiles && entry.IsHidden)
                continue;

            _visibleBuffer.Add(entry);
        }

        var totalCount = _visibleBuffer.Count;

        FileNameFilter.Apply(_visibleBuffer, request.IsFilterVisible ? request.FilterText : null, _viewBuffer);
        _viewBuffer.Sort(FileSystemEntryComparer.ForGrouped(
            request.GroupBy,
            request.GroupingUtcNow,
            request.SortColumn,
            request.SortDescending,
            request.DirectorySort));

        long listingSizeBytes = 0;
        foreach (var entry in _viewBuffer)
        {
            if (!entry.IsDirectory)
                listingSizeBytes += entry.SizeBytes;
        }

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visualTargets = new List<EntryItemViewModel>();
        var nextEntries = new List<EntryItemViewModel>(_viewBuffer.Count);

        foreach (var entry in _viewBuffer)
        {
            var path = entry.FullPath;
            usedPaths.Add(path);
            var gitStatus = request.GitSnapshot.GetStatusForPath(path);

            if (!_entryPool.TryGetValue(path, out var item))
            {
                item = new EntryItemViewModel(entry, request.ShowFileExtensions, gitStatus);
                _entryPool[path] = item;
                visualTargets.Add(item);
            }
            else
            {
                item.UpdateEntry(entry, request.ShowFileExtensions, gitStatus);
            }

            nextEntries.Add(item);
        }

        var evicted = new List<EntryItemViewModel>();
        foreach (var stale in _entryPool.Keys.Where(k => !usedPaths.Contains(k)).ToList())
        {
            if (_entryPool.Remove(stale, out var item))
                evicted.Add(item);
        }

        return new ListingPublishResult(
            nextEntries,
            visualTargets,
            evicted,
            totalCount,
            _viewBuffer.Count,
            listingSizeBytes);
    }
}

public sealed class ListingPublishRequest
{
    public required IReadOnlyList<FileSystemEntry> AllEntries { get; init; }

    public required GitStatusSnapshot GitSnapshot { get; init; }

    public required bool ShowHiddenFiles { get; init; }

    public required bool ShowFileExtensions { get; init; }

    public required bool IsFilterVisible { get; init; }

    public required string FilterText { get; init; }

    public required SortColumn SortColumn { get; init; }

    public required bool SortDescending { get; init; }

    public DirectorySortMode DirectorySort { get; init; } = DirectorySortMode.FoldersFirst;

    public GroupByMode GroupBy { get; init; } = GroupByMode.None;

    /// <summary>
    /// Instant used to resolve relative date buckets. Carried on the request so the sort and the
    /// presentation rebuild that follows it agree on where "today" ends.
    /// </summary>
    public DateTime GroupingUtcNow { get; init; } = DateTime.UtcNow;
}

public readonly record struct ListingPublishResult(
    IReadOnlyList<EntryItemViewModel> Entries,
    IReadOnlyList<EntryItemViewModel> VisualTargets,
    IReadOnlyList<EntryItemViewModel> EvictedEntries,
    int TotalCount,
    int ItemCount,
    long ListingSizeBytes);
