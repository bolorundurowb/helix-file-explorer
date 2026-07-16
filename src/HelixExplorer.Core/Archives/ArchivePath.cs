namespace HelixExplorer.Core.Archives;

/// <summary>
/// Host path percent-encodes <c>!</c>/<c>%</c> so the <c>!</c> delimiter is unambiguous;
/// inner paths use forward slashes and may contain literal <c>!</c>.
/// </summary>
public static class ArchivePath
{
    public const string Scheme = "archive://";

    private static readonly HashSet<string> s_extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "rar", "tar", "gz", "bz2", "tgz", "txz", "xz"
    };

    public static bool IsVirtual(string path)
        => !string.IsNullOrEmpty(path)
           && path.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    public static bool IsArchiveFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        var ext = Path.GetExtension(path).TrimStart('.');
        return s_extensions.Contains(ext);
    }

    public static string Mount(string archiveFilePath)
    {
        var normalized = archiveFilePath.TrimEnd('\\', '/');
        return Scheme + EscapeHost(normalized) + "!";
    }

    public static string Combine(string archiveFilePath, string innerPath)
    {
        if (string.IsNullOrEmpty(innerPath))
            return Mount(archiveFilePath);

        return Mount(archiveFilePath) + innerPath.Replace('\\', '/');
    }

    public static bool TryParse(string virtualPath, out string archiveFile, out string innerPath)
    {
        if (!IsVirtual(virtualPath))
        {
            archiveFile = string.Empty;
            innerPath = string.Empty;
            return false;
        }

        var body = virtualPath[Scheme.Length..];
        var bang = body.IndexOf('!');
        if (bang < 0)
        {
            archiveFile = UnescapeHost(body);
            innerPath = string.Empty;
            return true;
        }

        archiveFile = UnescapeHost(body[..bang]);
        innerPath = body[(bang + 1)..];
        return true;
    }

    public static string NormalizeDirectory(string virtualPath)
    {
        if (!IsVirtual(virtualPath))
            return virtualPath;

        if (virtualPath.EndsWith('/') || virtualPath.EndsWith('\\'))
            return virtualPath;

        return virtualPath + "/";
    }

    public static string GetParent(string virtualPath)
    {
        if (!TryParse(virtualPath, out var archiveFile, out var inner))
            return virtualPath;

        var trimmedInner = inner.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(trimmedInner))
            return EnsureTrailingSeparator(archiveFile);

        var lastSlash = trimmedInner.LastIndexOf('/');
        if (lastSlash < 0)
            return Mount(archiveFile);

        return Combine(archiveFile, trimmedInner[..lastSlash] + "/");
    }

    public static IReadOnlyList<ArchiveBreadcrumb> GetBreadcrumbs(string virtualPath)
    {
        if (!TryParse(virtualPath, out var archiveFile, out var inner))
            return Array.Empty<ArchiveBreadcrumb>();

        var crumbs = new List<ArchiveBreadcrumb>
        {
            new(Path.GetFileName(archiveFile), Mount(archiveFile), false)
        };

        var trimmedInner = inner.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(trimmedInner))
        {
            crumbs[^1] = crumbs[^1] with { IsLast = true };
            return crumbs;
        }

        var segments = trimmedInner.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var accumulator = Mount(archiveFile);
        for (var i = 0; i < segments.Length; i++)
        {
            accumulator += segments[i];
            var isLast = i == segments.Length - 1 && !virtualPath.EndsWith('/');
            if (!isLast)
                accumulator += "/";

            crumbs.Add(new ArchiveBreadcrumb(segments[i], accumulator, isLast));
        }

        if (virtualPath.EndsWith('/') || virtualPath.EndsWith('\\'))
            crumbs[^1] = crumbs[^1] with { IsLast = true };

        return crumbs;
    }

    public static string EscapeHost(string archiveFilePath)
        => archiveFilePath.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("!", "%21", StringComparison.Ordinal);

    public static string UnescapeHost(string encodedHost)
        => encodedHost.Replace("%21", "!", StringComparison.Ordinal)
            .Replace("%25", "%", StringComparison.Ordinal);

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        if (path.Length == 2 && path[1] == ':')
            return path + Path.DirectorySeparatorChar;
        if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
            return path + Path.DirectorySeparatorChar;
        return path;
    }
}

public readonly record struct ArchiveBreadcrumb(string DisplayName, string Path, bool IsLast);
