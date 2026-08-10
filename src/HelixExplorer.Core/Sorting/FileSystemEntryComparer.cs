using HelixExplorer.Core.Grouping;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Sorting;

public static class FileSystemEntryComparer
{
    /// <summary>
    /// Dual-level ordering used whenever Group By is active: bucket order first, then the pane's
    /// normal sort inside each bucket. Applying it for every layout (not just Grid) keeps items from
    /// reshuffling when the user toggles between Grid and the flat views.
    /// </summary>
    public static IComparer<FileSystemEntry> ForGrouped(
        GroupByMode groupBy,
        DateTime utcNow,
        SortColumn column,
        bool descending,
        DirectorySortMode directorySort = DirectorySortMode.FoldersFirst)
    {
        var inner = For(column, descending, directorySort);
        return groupBy == GroupByMode.None ? inner : new GroupedBucketComparer(groupBy, utcNow, inner);
    }

    /// <summary>
    /// Directory sort mode defaults to <see cref="DirectorySortMode.FoldersFirst"/> to preserve
    /// historical behavior for callers that have not been updated yet.
    /// </summary>
    public static IComparer<FileSystemEntry> For(
        SortColumn column,
        bool descending,
        DirectorySortMode directorySort = DirectorySortMode.FoldersFirst)
    {
        if (directorySort is DirectorySortMode.FoldersFirst or DirectorySortMode.FilesFirst)
        {
            var filesFirst = directorySort == DirectorySortMode.FilesFirst;
            return column switch
            {
                SortColumn.Size => new GroupedComparer(SortColumn.Size, descending, filesFirst),
                SortColumn.Modified => new GroupedComparer(SortColumn.Modified, descending, filesFirst),
                SortColumn.Type => new GroupedComparer(SortColumn.Type, descending, filesFirst),
                _ => new GroupedComparer(SortColumn.Name, descending, filesFirst)
            };
        }

        return column switch
        {
            SortColumn.Size => descending ? MixedSizeDesc.Instance : MixedSizeAsc.Instance,
            SortColumn.Modified => descending ? MixedModifiedDesc.Instance : MixedModifiedAsc.Instance,
            SortColumn.Type => descending ? MixedTypeDesc.Instance : MixedTypeAsc.Instance,
            _ => descending ? MixedNameDesc.Instance : MixedNameAsc.Instance
        };
    }

    private static int CompareByKind(in FileSystemEntry a, in FileSystemEntry b, bool filesFirst)
    {
        if (a.IsDirectory == b.IsDirectory)
            return 0;

        if (filesFirst)
            return a.IsDirectory ? 1 : -1;

        return a.IsDirectory ? -1 : 1;
    }

    private static int CompareByColumn(SortColumn column, bool descending, in FileSystemEntry x, in FileSystemEntry y)
    {
        int cmp = column switch
        {
            SortColumn.Size => descending ? y.SizeBytes.CompareTo(x.SizeBytes) : x.SizeBytes.CompareTo(y.SizeBytes),
            SortColumn.Modified => descending ? y.ModifiedUtc.CompareTo(x.ModifiedUtc) : x.ModifiedUtc.CompareTo(y.ModifiedUtc),
            SortColumn.Type => descending
                ? StringComparer.OrdinalIgnoreCase.Compare(y.Extension, x.Extension)
                : StringComparer.OrdinalIgnoreCase.Compare(x.Extension, y.Extension),
            _ => descending
                ? StringComparer.OrdinalIgnoreCase.Compare(y.Name, x.Name)
                : StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name)
        };

        // Name is the tie-breaker for every non-name column; it always ascends so listings stay stable.
        if (cmp != 0 || column == SortColumn.Name)
            return cmp;

        return StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
    }

    /// <summary>
    /// Type grouping is the only mode whose bucket depends on a string, so its buckets are cached per
    /// extension. Without the cache a sort would rebuild the same label O(n log n) times.
    /// </summary>
    private sealed class GroupedBucketComparer(
        GroupByMode groupBy,
        DateTime utcNow,
        IComparer<FileSystemEntry> inner) : IComparer<FileSystemEntry>
    {
        private readonly Dictionary<string, FileGroupBucket> _typeBuckets =
            groupBy == GroupByMode.Type ? new Dictionary<string, FileGroupBucket>(StringComparer.OrdinalIgnoreCase) : null!;

        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var cmp = FileGrouping.CompareBuckets(Resolve(in x), Resolve(in y));
            return cmp != 0 ? cmp : inner.Compare(x, y);
        }

        private FileGroupBucket Resolve(in FileSystemEntry entry)
        {
            if (groupBy != GroupByMode.Type)
                return FileGrouping.GetBucket(in entry, groupBy, utcNow);

            if (entry.IsDirectory)
                return FileGrouping.TypeFolders;

            var extension = string.IsNullOrEmpty(entry.Extension) ? string.Empty : entry.Extension;
            if (_typeBuckets.TryGetValue(extension, out var cached))
                return cached;

            var bucket = FileGrouping.GetTypeBucket(in entry);
            _typeBuckets[extension] = bucket;
            return bucket;
        }
    }

    private sealed class GroupedComparer(SortColumn column, bool descending, bool filesFirst)
        : IComparer<FileSystemEntry>
    {
        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var kind = CompareByKind(in x, in y, filesFirst);
            return kind != 0 ? kind : CompareByColumn(column, descending, in x, in y);
        }
    }

    private sealed class MixedNameAsc : IComparer<FileSystemEntry>
    {
        public static readonly MixedNameAsc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y) => CompareByColumn(SortColumn.Name, false, in x, in y);
    }

    private sealed class MixedNameDesc : IComparer<FileSystemEntry>
    {
        public static readonly MixedNameDesc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y) => CompareByColumn(SortColumn.Name, true, in x, in y);
    }

    private sealed class MixedSizeAsc : IComparer<FileSystemEntry>
    {
        public static readonly MixedSizeAsc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y) => CompareByColumn(SortColumn.Size, false, in x, in y);
    }

    private sealed class MixedSizeDesc : IComparer<FileSystemEntry>
    {
        public static readonly MixedSizeDesc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y) => CompareByColumn(SortColumn.Size, true, in x, in y);
    }

    private sealed class MixedModifiedAsc : IComparer<FileSystemEntry>
    {
        public static readonly MixedModifiedAsc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y) => CompareByColumn(SortColumn.Modified, false, in x, in y);
    }

    private sealed class MixedModifiedDesc : IComparer<FileSystemEntry>
    {
        public static readonly MixedModifiedDesc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y) => CompareByColumn(SortColumn.Modified, true, in x, in y);
    }

    private sealed class MixedTypeAsc : IComparer<FileSystemEntry>
    {
        public static readonly MixedTypeAsc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y) => CompareByColumn(SortColumn.Type, false, in x, in y);
    }

    private sealed class MixedTypeDesc : IComparer<FileSystemEntry>
    {
        public static readonly MixedTypeDesc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y) => CompareByColumn(SortColumn.Type, true, in x, in y);
    }
}
