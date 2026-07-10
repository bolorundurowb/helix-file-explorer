using HelixExplorer.Core.Archives;

namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Centralized path classification, normalization, and comparison helpers for the file manager.
/// Handles physical paths, UNC paths, shell namespace paths, and archive virtual paths.
/// </summary>
public static class PathUtilities
{
    /// <summary>Classifies a path into one of the known path kinds.</summary>
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

    /// <summary>
    /// Determines whether <paramref name="path"/> refers to the same location as
    /// <paramref name="directory"/> or is contained within it.
    /// </summary>
    /// <remarks>
    /// Both paths are normalized before comparison. Only paths of the same <see cref="PathKind"/>
    /// can be related; paths of different kinds (e.g. a physical folder and an archive virtual
    /// folder) are never considered related.
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

    /// <summary>Compares two paths for equality using the appropriate normalization for their kind.</summary>
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

    /// <summary>
    /// Normalizes directory separators and resolves <c>.</c> and <c>..</c> segments where possible.
    /// Preserves drive roots and virtual path schemes.
    /// </summary>
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

    /// <summary>True if the path represents a Windows drive root such as <c>C:\</c>.</summary>
    public static bool IsDriveRoot(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        return normalized.Length == 2
               && char.IsLetter(normalized[0])
               && normalized[1] == ':';
    }

    /// <summary>True if the path is a UNC path such as <c>\\server\share</c>.</summary>
    public static bool IsUncPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.StartsWith(@"\\", StringComparison.Ordinal)
               && normalized.Length > 2;
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
        var dir = NormalizeArchivePath(directory);
        var candidate = NormalizeArchivePath(path);

        if (string.Equals(dir, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!dir.EndsWith('/'))
            dir += "/";

        return candidate.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
               && candidate.Length > dir.Length;
    }

    private static string NormalizePhysicalPath(string path)
    {
        try
        {
            // Resolves . and .. segments and normalizes separators. Does not require the path to exist.
            var full = Path.GetFullPath(path);
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
            ? ArchivePath.Scheme + normalizedArchive + "!"
            : ArchivePath.Scheme + normalizedArchive + "!" + normalizedInner + "/";
    }
}

/// <summary>Known path categories used by the file manager.</summary>
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
