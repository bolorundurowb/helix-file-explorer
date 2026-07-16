namespace HelixExplorer.Core.Settings;

public static class PinnedPathHelper
{
    public static IReadOnlyList<(string Path, string DisplayName)> MergeUserPins(
        IEnumerable<string> userPinnedPaths,
        IEnumerable<string> defaultPaths,
        IEnumerable<string>? unpinnedPaths = null)
    {
        var unpinned = new HashSet<string>(
            (unpinnedPaths ?? []).Select(Normalize),
            StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Path, string DisplayName)>();

        foreach (var path in userPinnedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var normalized = Normalize(path);
            if (!seen.Add(normalized))
                continue;

            result.Add((normalized, GetDisplayName(normalized)));
        }

        foreach (var path in defaultPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var normalized = Normalize(path);
            if (unpinned.Contains(normalized) || !seen.Add(normalized))
                continue;

            result.Add((normalized, GetDisplayName(normalized)));
        }

        return result;
    }

    public static bool IsPinned(IReadOnlyList<string> pinnedPaths, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = Normalize(path);
        foreach (var pinned in pinnedPaths)
        {
            if (string.Equals(Normalize(pinned), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsVisibleInSidebar(
        IReadOnlyList<string> pinnedPaths,
        IReadOnlyList<string> unpinnedPaths,
        IEnumerable<string> defaultPaths,
        string path)
    {
        var normalized = Normalize(path);
        if (IsPinned(pinnedPaths, normalized))
            return true;

        foreach (var unpinned in unpinnedPaths)
        {
            if (string.Equals(Normalize(unpinned), normalized, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var defaultPath in defaultPaths)
        {
            if (string.Equals(Normalize(defaultPath), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsPinnedOrDefault(
        IReadOnlyList<string> pinnedPaths,
        IReadOnlyList<string> unpinnedPaths,
        IEnumerable<string> defaultPaths,
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = Normalize(path);
        if (IsPinned(pinnedPaths, normalized))
            return true;

        foreach (var unpinned in unpinnedPaths)
        {
            if (string.Equals(Normalize(unpinned), normalized, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var defaultPath in defaultPaths)
        {
            if (string.Equals(Normalize(defaultPath), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetDisplayName(string normalized)
        => Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? normalized;

    private static string Normalize(string path)
        => Core.FileSystem.PathUtilities.NormalizePath(path);
}
