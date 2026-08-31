using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.FileSystem.Undo;

/// <param name="RecycleItemPath">The <c>$R*</c> path holding the deleted item's contents.</param>
/// <param name="OriginalPath">Where the item lived before deletion, from the paired <c>$I*</c> file.</param>
public sealed record RecycleBinEntry(string RecycleItemPath, string OriginalPath, DateTime DeletedAtUtc);

/// <summary>
/// Pairs the paths a delete batch consumed with the recycle-bin entries it produced.
/// </summary>
/// <remarks>
/// The shell's <c>PostDeleteItem</c> event reports which source succeeded but not where it landed —
/// Vanara's <c>DestItem</c> is null for a recycle — so the only way to learn the <c>$R*</c> path is to
/// read the bin afterwards and match on original path. Kept pure and in Core so it can be tested
/// without hosting COM or touching a real recycle bin.
/// </remarks>
public static class RecycleBinMatcher
{
    /// <summary>
    /// Matches each deleted source to the bin entry it produced, one-to-one.
    /// </summary>
    /// <param name="deletedSources">
    /// Paths the shell reported as successfully deleted, in the order it reported them.
    /// </param>
    /// <param name="binEntries">Recycle-bin entries read after the batch completed.</param>
    /// <param name="batchStartUtc">
    /// Cutoff excluding entries that predate this batch. Callers should subtract a small skew, since
    /// the bin's timestamps come from the shell rather than the same clock read.
    /// </param>
    /// <returns>
    /// One change per source, in the input order. A source with no matching entry still yields a
    /// change, but with a null <see cref="FileOperationChange.RecycleItemPath"/> — the caller decides
    /// whether a partial mapping is undoable.
    /// </returns>
    public static IReadOnlyList<FileOperationChange> Match(
        IReadOnlyList<string> deletedSources,
        IReadOnlyList<RecycleBinEntry> binEntries,
        DateTime batchStartUtc)
    {
        if (deletedSources.Count == 0)
            return [];

        // Index candidates by original path so each source is an O(1) lookup rather than a scan of the
        // whole bin, which can hold thousands of entries.
        var candidates = new Dictionary<string, Queue<RecycleBinEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in binEntries
                     .Where(e => e.DeletedAtUtc >= batchStartUtc)
                     .GroupBy(e => PathUtilities.NormalizePath(e.OriginalPath), StringComparer.OrdinalIgnoreCase))
        {
            // Oldest first, so deleting two same-named items in one batch hands them out in the order
            // the shell created them rather than an arbitrary directory-enumeration order.
            candidates[group.Key] = new Queue<RecycleBinEntry>(group.OrderBy(e => e.DeletedAtUtc));
        }

        var changes = new List<FileOperationChange>(deletedSources.Count);
        foreach (var source in deletedSources)
        {
            var key = PathUtilities.NormalizePath(source);

            string? recycleItemPath = null;
            if (candidates.TryGetValue(key, out var queue) && queue.Count > 0)
                recycleItemPath = queue.Dequeue().RecycleItemPath;

            // Destination is where undo puts the item back, which for a delete is where it came from.
            changes.Add(new FileOperationChange(source, source, recycleItemPath));
        }

        return changes;
    }
}
