namespace HelixExplorer.Core.Settings;

/// <summary>Merges user-pinned folder paths with built-in quick access entries.</summary>
public static class PinnedPathHelper
{
    public static IReadOnlyList<(string Path, string DisplayName)> MergeUserPins(
        IEnumerable<string> userPinnedPaths,
        IEnumerable<string> defaultPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Path, string DisplayName)>();

        foreach (var path in userPinnedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var normalized = Normalize(path);
            if (!seen.Add(normalized))
                continue;

            result.Add((normalized, Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? normalized));
        }

        foreach (var path in defaultPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var normalized = Normalize(path);
            if (!seen.Add(normalized))
                continue;

            result.Add((normalized, Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? normalized));
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

    private static string Normalize(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
