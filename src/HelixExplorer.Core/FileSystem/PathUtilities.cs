using HelixExplorer.Core.Archives;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public static class PathUtilities
{
    public static PathKind Classify(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return PathKind.Empty;

        if (string.Equals(path, PaneConstants.HomeRoute, StringComparison.Ordinal))
            return PathKind.Home;

        if (ArchivePath.IsVirtual(path))
            return PathKind.Archive;

        if (ShellPath.IsRecycleBin(path))
            return PathKind.RecycleBin;

        if (ShellPath.IsShellPath(path))
            return PathKind.Shell;

        if (IsUncPath(path))
            return PathKind.Unc;

        return PathKind.Physical;
    }

    /// <remarks>
    /// Only paths of the same <see cref="PathKind"/> can be related; a physical folder and an
    /// archive virtual folder are never considered related.
    /// </remarks>
    public static bool IsSameOrChildPath(string directory, string path)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(path))
            return false;

        var kind = Classify(directory);
        if (kind != Classify(path))
            return false;

        return kind switch
        {
            PathKind.Archive => IsSameOrChildArchivePath(directory, path),
            PathKind.Shell or PathKind.RecycleBin =>
                string.Equals(directory.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase),
            PathKind.Unc or PathKind.Physical => IsSameOrChildPhysicalPath(directory, path),
            _ => false
        };
    }

    public static bool PathsEqual(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b);

        var kindA = Classify(a);
        var kindB = Classify(b);
        if (kindA != kindB)
            return false;

        return kindA switch
        {
            PathKind.Archive =>
                string.Equals(NormalizeArchivePath(a), NormalizeArchivePath(b), StringComparison.OrdinalIgnoreCase),
            PathKind.Shell or PathKind.RecycleBin =>
                string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase),
            PathKind.Unc or PathKind.Physical =>
                string.Equals(NormalizePhysicalPath(a), NormalizePhysicalPath(b), StringComparison.OrdinalIgnoreCase),
            PathKind.Home or PathKind.Empty => true,
            _ => string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
        };
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var kind = Classify(path);
        return kind switch
        {
            PathKind.Archive => NormalizeArchivePath(path),
            PathKind.Shell or PathKind.RecycleBin => path,
            PathKind.Unc or PathKind.Physical => NormalizePhysicalPath(path),
            _ => path
        };
    }

    public static bool IsDriveRoot(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        return normalized.Length == 2
               && char.IsLetter(normalized[0])
               && normalized[1] == ':';
    }

    public static bool IsUncPath(string? path)
        => NetworkPath.IsUnc(path);

    /// <summary>
    /// True when both paths resolve to the same drive root (or the same UNC share root).
    /// <see cref="Directory.Move"/> requires this; otherwise the caller should copy then delete.
    /// </summary>
    public static bool IsSameVolume(string source, string destination)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(destination))
            return false;

        try
        {
            var sourceRoot = Path.GetPathRoot(Path.GetFullPath(source));
            var destRoot = Path.GetPathRoot(Path.GetFullPath(destination));
            return !string.IsNullOrEmpty(sourceRoot)
                   && string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Orders paths so that a nested path always precedes the path that contains it, leaving unrelated
    /// paths in their original relative order.
    /// </summary>
    /// <remarks>
    /// Undo deletes children before parents: recycling a parent first would take its children with it,
    /// and the child's own recycle would then fail on a path that no longer exists. Sorting by segment
    /// depth achieves this without an O(n²) containment check, because a contained path always has more
    /// segments than its container. The sort is stable, so siblings keep the caller's order.
    /// </remarks>
    public static IReadOnlyList<string> OrderDeepestFirst(IReadOnlyList<string> paths)
    {
        if (paths.Count < 2)
            return paths;

        return [.. paths.OrderByDescending(SegmentDepth)];
    }

    /// <summary>
    /// Inverse of <see cref="OrderDeepestFirst"/>: containers before the paths nested inside them.
    /// </summary>
    /// <remarks>
    /// Restoring from the recycle bin needs the opposite order to deleting — a child cannot be restored
    /// into a parent directory that has not been recreated yet.
    /// </remarks>
    public static IReadOnlyList<string> OrderShallowestFirst(IReadOnlyList<string> paths)
    {
        if (paths.Count < 2)
            return paths;

        return [.. paths.OrderBy(SegmentDepth)];
    }

    private static int SegmentDepth(string path)
    {
        var normalized = NormalizePath(path);
        if (string.IsNullOrEmpty(normalized))
            return 0;

        var depth = 0;
        foreach (var c in normalized)
        {
            if (c is '\\' or '/')
                depth++;
        }

        return depth;
    }

    private static bool IsSameOrChildPhysicalPath(string directory, string path)
    {
        var dir = NormalizePhysicalPath(directory);
        var candidate = NormalizePhysicalPath(path);

        if (string.Equals(dir, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!dir.EndsWith(Path.DirectorySeparatorChar))
            dir += Path.DirectorySeparatorChar;

        return candidate.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
               && candidate.Length > dir.Length;
    }

    private static bool IsSameOrChildArchivePath(string directory, string path)
    {
        if (!ArchivePath.TryParse(directory, out var dirArchive, out var dirInner) ||
            !ArchivePath.TryParse(path, out var pathArchive, out var pathInner))
        {
            return false;
        }

        if (!PathsEqual(dirArchive, pathArchive))
            return false;

        var dirInnerNorm = dirInner.Replace('\\', '/').Trim('/');
        var pathInnerNorm = pathInner.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(dirInnerNorm))
            return true;

        if (string.IsNullOrEmpty(pathInnerNorm))
            return false;

        if (string.Equals(dirInnerNorm, pathInnerNorm, StringComparison.OrdinalIgnoreCase))
            return true;

        return pathInnerNorm.StartsWith(dirInnerNorm + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePhysicalPath(string path)
    {
        try
        {
            var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            // Path.GetFullPath interprets a lone "C:" as the current directory on that drive.
            // Treat it as the drive root instead.
            if (normalized.Length == 2 && normalized[1] == ':')
                normalized += Path.DirectorySeparatorChar;

            var full = Path.GetFullPath(normalized);
            if (IsDriveRoot(full))
                return full;

            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            // Path.GetFullPath can throw for paths containing invalid characters. Fall back to a
            // lightweight normalization that at least makes separators consistent.
            var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                                 .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (normalized.Length == 2 && normalized[1] == ':')
                normalized += Path.DirectorySeparatorChar;

            return normalized;
        }
    }

    private static string NormalizeArchivePath(string path)
    {
        if (!ArchivePath.TryParse(path, out var archiveFile, out var inner))
            return path;

        var normalizedArchive = NormalizePhysicalPath(archiveFile).Replace(Path.DirectorySeparatorChar, '/');
        var normalizedInner = inner.Replace(Path.DirectorySeparatorChar, '/').Trim('/');

        return string.IsNullOrEmpty(normalizedInner)
            ? ArchivePath.Mount(normalizedArchive)
            : ArchivePath.Combine(normalizedArchive, normalizedInner + "/");
    }
}

public enum PathKind
{
    Empty,
    Home,
    Physical,
    Shell,
    RecycleBin,
    Archive,
    Unc
}
