using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Pure helpers for Windows UNC paths so manual navigation to <c>\\</c>, <c>\\server</c>, and
/// <c>\\server\share</c> is predictable even when network discovery is unavailable.
/// </summary>
public static class NetworkPath
{
    public const string Root = @"\\";

    public static bool IsUnc(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 2)
            return false;

        var a = path[0];
        var b = path[1];
        return (a == '\\' || a == '/') && (b == '\\' || b == '/');
    }

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

    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim();
        if (!IsUnc(trimmed))
            return trimmed;

        var body = trimmed[2..].Replace('/', '\\');

        // Collapse runs so pasted //server//share and \\server\share\ match the same key.
        var parts = body.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return Root;

        return Root + string.Join('\\', parts);
    }

    public static string? GetServer(string? path)
    {
        var normalized = Normalize(path);
        if (!IsUnc(normalized) || IsNetworkRoot(normalized))
            return null;

        var body = normalized[2..];
        var slash = body.IndexOf('\\');
        return slash < 0 ? body : body[..slash];
    }

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

    public static bool IsServerRoot(string? path)
        => GetServer(path) is not null && GetShare(path) is null;

    public static bool HasShare(string? path) => GetShare(path) is not null;

    public static string ForServer(string server) => Root + server.Trim().Trim('\\', '/');

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
