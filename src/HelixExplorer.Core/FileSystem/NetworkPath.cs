using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Pure helpers for Windows UNC paths so manual navigation to <c>\\</c>, <c>\\server</c>, and
/// <c>\\server\share</c> is predictable even when network discovery is unavailable.
/// </summary>
public static class NetworkPath
{
    /// <summary>The network root (<c>\\</c>).</summary>
    public const string Root = @"\\";

    /// <summary>True when <paramref name="path"/> starts with a UNC prefix (<c>\\</c> or <c>//</c>).</summary>
    public static bool IsUnc(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 2)
            return false;

        var a = path[0];
        var b = path[1];
        return (a == '\\' || a == '/') && (b == '\\' || b == '/');
    }

    /// <summary>True when the path is exactly the network root (<c>\\</c>, <c>//</c>, etc.).</summary>
    public static bool IsNetworkRoot(string? path)
    {
        if (!IsUnc(path))
            return false;

        for (var i = 2; i < path!.Length; i++)
        {
            if (path[i] is not ('\\' or '/'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Normalizes a user-typed UNC path: converts forward slashes to backslashes, collapses repeated
    /// separators inside the path, and trims a trailing separator (except for the bare root).
    /// Non-UNC input is returned unchanged.
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim();
        if (!IsUnc(trimmed))
            return trimmed;

        var body = trimmed[2..].Replace('/', '\\');

        // Collapse any run of separators inside the body into a single backslash.
        var parts = body.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return Root;

        return Root + string.Join('\\', parts);
    }

    /// <summary>Extracts the server name from a UNC path, or null when none is present (bare root).</summary>
    public static string? GetServer(string? path)
    {
        var normalized = Normalize(path);
        if (!IsUnc(normalized) || IsNetworkRoot(normalized))
            return null;

        var body = normalized[2..];
        var slash = body.IndexOf('\\');
        return slash < 0 ? body : body[..slash];
    }

    /// <summary>Extracts the share name (first segment after the server), or null when absent.</summary>
    public static string? GetShare(string? path)
    {
        var normalized = Normalize(path);
        if (!IsUnc(normalized) || IsNetworkRoot(normalized))
            return null;

        var body = normalized[2..];
        var slash = body.IndexOf('\\');
        if (slash < 0 || slash == body.Length - 1)
            return null;

        var rest = body[(slash + 1)..];
        var next = rest.IndexOf('\\');
        return next < 0 ? rest : rest[..next];
    }

    /// <summary>True when the path names a server root (<c>\\server</c>) without a share.</summary>
    public static bool IsServerRoot(string? path)
        => GetServer(path) is not null && GetShare(path) is null;

    /// <summary>True when the path is a UNC share root (<c>\\server\share</c>) or child path.</summary>
    public static bool HasShare(string? path) => GetShare(path) is not null;

    /// <summary>Builds the canonical <c>\\server</c> path for a server name.</summary>
    public static string ForServer(string server) => Root + server.Trim().Trim('\\', '/');

    /// <summary>
    /// De-duplicates and orders network locations by display name (case-insensitive), keeping the
    /// first occurrence of each distinct path.
    /// </summary>
    public static IReadOnlyList<NetworkLocationInfo> Deduplicate(IEnumerable<NetworkLocationInfo> locations)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<NetworkLocationInfo>();
        foreach (var location in locations)
        {
            if (string.IsNullOrWhiteSpace(location.Path))
                continue;

            if (seen.Add(Normalize(location.Path)))
                unique.Add(location);
        }

        unique.Sort(static (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return unique;
    }
}
