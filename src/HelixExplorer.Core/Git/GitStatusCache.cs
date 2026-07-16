using System.Collections.Concurrent;

namespace HelixExplorer.Core.Git;

/// <summary>
/// Coalesces repeated <c>git status</c> requests for the same repository root within a short time
/// window. File watchers and rapid navigation can trigger many refreshes for one repo; serving a
/// recent snapshot avoids spawning a git process for each.
/// </summary>
public sealed class GitStatusCache(TimeSpan ttl, Func<DateTime>? clock = null)
{
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a cached snapshot for <paramref name="root"/> when one exists and is still fresh.</summary>
    public bool TryGet(string root, out GitStatusSnapshot snapshot)
    {
        if (_entries.TryGetValue(root, out var entry) && _clock() - entry.Timestamp < ttl)
        {
            snapshot = entry.Snapshot;
            return true;
        }

        snapshot = GitStatusSnapshot.Empty;
        return false;
    }

    public void Store(string root, GitStatusSnapshot snapshot)
        => _entries[root] = new Entry(snapshot, _clock());

    /// <summary>Drops the cached entry for a root (e.g. after a checkout or explicit refresh).</summary>
    public void Invalidate(string root) => _entries.TryRemove(root, out _);

    public void Clear() => _entries.Clear();

    private readonly record struct Entry(GitStatusSnapshot Snapshot, DateTime Timestamp);
}
