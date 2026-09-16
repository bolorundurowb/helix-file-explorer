using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;

namespace HelixExplorer.ViewModels.Pane;

public sealed class PaneListingCoordinator
{
    private readonly Dictionary<string, EntryItemViewModel> _entryPool = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemEntry> _viewBuffer = new();
    private readonly List<FileSystemEntry> _visibleBuffer = new();

    public void ClearEntryPool() => _entryPool.Clear();

    public void RemoveFromPool(string path) => _entryPool.Remove(path);

    public ListingPublishResult ApplySortAndPublish(ListingPublishRequest request)
    {
        ListingPipeline.FilterHidden(request.AllEntries, request.ShowHiddenFiles, _visibleBuffer);
        ListingPipeline.FilterText(_visibleBuffer, request.IsFilterVisible, request.FilterText, _viewBuffer);
        ListingPipeline.Sort(
            _viewBuffer,
            request.GroupBy,
            request.GroupingUtcNow,
            request.SortColumn,
            request.SortDescending,
            request.DirectorySort);

        var aggregate = ListingPipeline.Aggregate(_visibleBuffer, _viewBuffer);
        var (entries, visualTargets) = ListingPipeline.Materialize(
            _viewBuffer,
            request.GitSnapshot,
            request.ShowFileExtensions,
            _entryPool);

        // Stale pool entries are retained until the pane navigates (ClearEntryPool) so filter
        // widening (e.g. backspace) reuses their view models and cached icons instead of
        // re-creating them and re-querying the shell.
        return new ListingPublishResult(
            entries,
            visualTargets,
            aggregate.TotalCount,
            aggregate.ItemCount,
            aggregate.SizeBytes);
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
    int TotalCount,
    int ItemCount,
    long ListingSizeBytes);
