using System.Collections.Concurrent;

namespace HelixExplorer.Core.Git;

/// <summary>
/// Coalesces repeated <c>git status</c> requests for the same repository root within a short time
/// window. File watchers and rapid navigation can trigger many refreshes for one repo; serving a
/// recent snapshot avoids spawning a git process for each.
/// </summary>
public sealed class GitStatusCache(TimeSpan ttl, Func<DateTime>? clock = null, int maxEntries = 128)
{
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly int _maxEntries = maxEntries > 0
        ? maxEntries
        : throw new ArgumentOutOfRangeException(nameof(maxEntries));

    internal int Count => _entries.Count;

    public bool TryGet(string root, out GitStatusSnapshot snapshot)
    {
        if (_entries.TryGetValue(root, out var entry))
        {
            if (_clock() - entry.Timestamp < ttl)
            {
                snapshot = entry.Snapshot;
                return true;
            }

            _entries.TryRemove(root, out _);
        }

        snapshot = GitStatusSnapshot.Empty;
        return false;
    }

    public void Store(string root, GitStatusSnapshot snapshot)
    {
        var entry = new Entry(snapshot, _clock());
        if (_entries.TryAdd(root, entry))
            _insertionOrder.Enqueue(root);
        else
            _entries[root] = entry;

        while (_entries.Count > _maxEntries && _insertionOrder.TryDequeue(out var oldest))
            _entries.TryRemove(oldest, out _);
    }

    public void Invalidate(string root) => _entries.TryRemove(root, out _);

    public void Clear()
    {
        _entries.Clear();
        while (_insertionOrder.TryDequeue(out _))
        {
        }
    }

    private readonly record struct Entry(GitStatusSnapshot Snapshot, DateTime Timestamp);
}
