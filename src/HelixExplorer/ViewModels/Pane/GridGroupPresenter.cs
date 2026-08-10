using HelixExplorer.Core.Grouping;
using HelixExplorer.Core.Models;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Projects the pane's flat, dual-level sorted entries into the composite collection Grid view
/// renders: a <see cref="GroupHeaderViewModel"/> band per non-empty bucket followed by that
/// bucket's tiles, minus any bucket the user has collapsed.
/// </summary>
/// <remarks>
/// Headers are pooled by bucket key so repeated rebuilds hand back the same instances, which keeps
/// the reference-identity diff in <see cref="ObservableCollectionDiff"/> cheap and stops realized
/// containers from being torn down on every refresh. Buffers are reused for the same reason.
/// </remarks>
public sealed class GridGroupPresenter
{
    private readonly Dictionary<string, GroupHeaderViewModel> _headers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);
    private readonly List<object> _buffer = [];

    /// <summary>Drops pooled headers; call when the pane changes folder.</summary>
    public void Reset()
    {
        _headers.Clear();
        _emitted.Clear();
        _buffer.Clear();
    }

    /// <summary>
    /// Returns the presenter's reused buffer: valid only until the next <see cref="Build"/> call,
    /// so callers must copy or diff it immediately.
    /// </summary>
    public IReadOnlyList<object> Build(
        IReadOnlyList<EntryItemViewModel> entries,
        GroupByMode groupBy,
        DateTime utcNow,
        IReadOnlySet<string> collapsedKeys)
    {
        _buffer.Clear();

        if (groupBy == GroupByMode.None)
        {
            _headers.Clear();
            _emitted.Clear();
            foreach (var entry in entries)
                _buffer.Add(entry);

            return _buffer;
        }

        _emitted.Clear();
        GroupHeaderViewModel? current = null;
        var currentKey = string.Empty;
        var count = 0;

        foreach (var entry in entries)
        {
            var bucket = FileGrouping.GetBucket(entry.Entry, groupBy, utcNow);
            if (current is null || !string.Equals(bucket.Key, currentKey, StringComparison.Ordinal))
            {
                if (current is not null)
                    current.ItemCount = count;

                currentKey = bucket.Key;
                count = 0;
                current = GetOrCreateHeader(in bucket);
                current.IsCollapsed = collapsedKeys.Contains(bucket.Key);

                // Entries arrive bucket-sorted, so a key should never reappear. If it somehow does,
                // emitting the pooled header twice would put the same reference in the collection
                // twice and break the diff — fold the run into the header already emitted instead.
                if (_emitted.Add(bucket.Key))
                    _buffer.Add(current);
            }

            count++;
            if (!current.IsCollapsed)
                _buffer.Add(entry);
        }

        if (current is not null)
            current.ItemCount = count;

        PruneHeaders();
        return _buffer;
    }

    private GroupHeaderViewModel GetOrCreateHeader(in FileGroupBucket bucket)
    {
        if (_headers.TryGetValue(bucket.Key, out var header))
            return header;

        header = new GroupHeaderViewModel(bucket.Key, bucket.DisplayName);
        _headers[bucket.Key] = header;
        return header;
    }

    private void PruneHeaders()
    {
        if (_headers.Count == _emitted.Count)
            return;

        foreach (var key in _headers.Keys.Where(k => !_emitted.Contains(k)).ToList())
            _headers.Remove(key);
    }
}
