using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Sorting;

/// <summary>Singleton comparers — directories always precede files, then the requested column.</summary>
public static class FileSystemEntryComparer
{
    public static IComparer<FileSystemEntry> For(SortColumn column, bool descending) => column switch
    {
        SortColumn.Size => descending ? SizeDesc.Instance : SizeAsc.Instance,
        SortColumn.Modified => descending ? ModifiedDesc.Instance : ModifiedAsc.Instance,
        SortColumn.Type => descending ? TypeDesc.Instance : TypeAsc.Instance,
        _ => descending ? NameDesc.Instance : NameAsc.Instance
    };

    private static int CompareDirsFirst(in FileSystemEntry a, in FileSystemEntry b)
    {
        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;
        return 0;
    }

    private sealed class NameAsc : IComparer<FileSystemEntry>
    {
        public static readonly NameAsc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var dirs = CompareDirsFirst(in x, in y);
            return dirs != 0 ? dirs : StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }

    private sealed class NameDesc : IComparer<FileSystemEntry>
    {
        public static readonly NameDesc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var dirs = CompareDirsFirst(in x, in y);
            return dirs != 0 ? dirs : StringComparer.OrdinalIgnoreCase.Compare(y.Name, x.Name);
        }
    }

    private sealed class SizeAsc : IComparer<FileSystemEntry>
    {
        public static readonly SizeAsc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var dirs = CompareDirsFirst(in x, in y);
            if (dirs != 0) return dirs;
            var cmp = x.SizeBytes.CompareTo(y.SizeBytes);
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }

    private sealed class SizeDesc : IComparer<FileSystemEntry>
    {
        public static readonly SizeDesc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var dirs = CompareDirsFirst(in x, in y);
            if (dirs != 0) return dirs;
            var cmp = y.SizeBytes.CompareTo(x.SizeBytes);
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }

    private sealed class ModifiedAsc : IComparer<FileSystemEntry>
    {
        public static readonly ModifiedAsc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var dirs = CompareDirsFirst(in x, in y);
            if (dirs != 0) return dirs;
            var cmp = x.ModifiedUtc.CompareTo(y.ModifiedUtc);
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }

    private sealed class ModifiedDesc : IComparer<FileSystemEntry>
    {
        public static readonly ModifiedDesc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var dirs = CompareDirsFirst(in x, in y);
            if (dirs != 0) return dirs;
            var cmp = y.ModifiedUtc.CompareTo(x.ModifiedUtc);
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }

    private sealed class TypeAsc : IComparer<FileSystemEntry>
    {
        public static readonly TypeAsc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var dirs = CompareDirsFirst(in x, in y);
            if (dirs != 0) return dirs;
            var cmp = StringComparer.OrdinalIgnoreCase.Compare(x.Extension, y.Extension);
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }

    private sealed class TypeDesc : IComparer<FileSystemEntry>
    {
        public static readonly TypeDesc Instance = new();
        public int Compare(FileSystemEntry x, FileSystemEntry y)
        {
            var dirs = CompareDirsFirst(in x, in y);
            if (dirs != 0) return dirs;
            var cmp = StringComparer.OrdinalIgnoreCase.Compare(y.Extension, x.Extension);
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }
}
