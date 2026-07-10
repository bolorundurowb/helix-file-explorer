using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using HelixExplorer.ViewModels;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>Sorts, filters, and maps directory entries to view models.</summary>
public sealed class PaneListingCoordinator
{
    private readonly Dictionary<string, EntryItemViewModel> _entryPool = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemEntry> _viewBuffer = new();
    private readonly List<FileSystemEntry> _visibleBuffer = new();

    public IReadOnlyDictionary<string, EntryItemViewModel> EntryPool => _entryPool;

    public void ClearEntryPool() => _entryPool.Clear();

    public void RemoveFromPool(string path) => _entryPool.Remove(path);

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
        _viewBuffer.Sort(FileSystemEntryComparer.For(request.SortColumn, request.SortDescending));

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

        foreach (var stale in _entryPool.Keys.Where(k => !usedPaths.Contains(k)).ToList())
            _entryPool.Remove(stale);

        return new ListingPublishResult(
            nextEntries,
            visualTargets,
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
}

public readonly record struct ListingPublishResult(
    IReadOnlyList<EntryItemViewModel> Entries,
    IReadOnlyList<EntryItemViewModel> VisualTargets,
    int TotalCount,
    int ItemCount,
    long ListingSizeBytes);
