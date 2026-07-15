using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>Back/forward stacks and path resolution for pane navigation.</summary>
public sealed class PaneNavigationController(IFileSystemProvider fileSystem, IArchiveProvider archive)
{
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    public bool CanGoBack => _backStack.Count > 0;

    public bool CanGoForward => _forwardStack.Count > 0;

    public string ResolveDestination(string path, string currentPath)
    {
        if (ArchivePath.IsVirtual(path))
            return ArchivePath.NormalizeDirectory(path);

        if (ShellPath.IsShellPath(path))
            return path;

        if (path == "..")
        {
            if (string.IsNullOrEmpty(currentPath))
                return currentPath;

            if (ArchivePath.IsVirtual(currentPath))
                return ArchivePath.GetParent(currentPath);

            var parent = Directory.GetParent(currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return parent is null
                ? currentPath
                : EnsureTrailingSeparator(parent.FullName);
        }

        var resolved = fileSystem.ResolvePath(path);
        if (archive.IsArchiveFile(resolved))
            return ArchivePath.Mount(resolved);

        return EnsureTrailingSeparator(resolved);
    }

    public NavigationTransition RecordForward(string currentPath, string resolved)
    {
        if (!string.IsNullOrEmpty(currentPath))
        {
            _backStack.Push(currentPath);
            _forwardStack.Clear();
        }

        return new NavigationTransition(resolved, CanGoBack, CanGoForward);
    }

    public NavigationTransition? GoBack(string currentPath)
    {
        if (_backStack.Count == 0)
            return null;

        _forwardStack.Push(currentPath);
        var path = _backStack.Pop();
        return new NavigationTransition(path, CanGoBack, CanGoForward);
    }

    public NavigationTransition? GoForward(string currentPath)
    {
        if (_forwardStack.Count == 0)
            return null;

        _backStack.Push(currentPath);
        var path = _forwardStack.Pop();
        return new NavigationTransition(path, CanGoBack, CanGoForward);
    }

    public static IReadOnlyList<BreadcrumbSegment> BuildBreadcrumbs(string path)
    {
        var breadcrumbs = new List<BreadcrumbSegment>();
        if (string.IsNullOrEmpty(path))
            return breadcrumbs;

        if (ArchivePath.IsVirtual(path))
        {
            foreach (var crumb in ArchivePath.GetBreadcrumbs(path))
            {
                breadcrumbs.Add(new BreadcrumbSegment(
                    DisplayName: crumb.DisplayName,
                    Path: crumb.Path,
                    IsLast: crumb.IsLast));
            }

            return breadcrumbs;
        }

        if (ShellPath.IsRecycleBin(path))
        {
            breadcrumbs.Add(new BreadcrumbSegment("Recycle Bin", path, IsLast: true));
            return breadcrumbs;
        }

        if (NetworkPath.IsUnc(path))
            return BuildNetworkBreadcrumbs(path);

        var parts = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var accumulator = string.Empty;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isDrive = part.Length == 2 && part[1] == ':';
            accumulator = isDrive
                ? part + Path.DirectorySeparatorChar
                : Path.Combine(accumulator.TrimEnd(Path.DirectorySeparatorChar), part) + Path.DirectorySeparatorChar;

            breadcrumbs.Add(new BreadcrumbSegment(
                DisplayName: part,
                Path: accumulator,
                IsLast: i == parts.Length - 1));
        }

        return breadcrumbs;
    }

    private static IReadOnlyList<BreadcrumbSegment> BuildNetworkBreadcrumbs(string path)
    {
        var normalized = NetworkPath.Normalize(path);
        var breadcrumbs = new List<BreadcrumbSegment>
        {
            new("Network", NetworkPath.Root, NetworkPath.IsNetworkRoot(normalized))
        };

        var server = NetworkPath.GetServer(normalized);
        if (server is null)
            return breadcrumbs;

        var serverPath = NetworkPath.ForServer(server);
        var share = NetworkPath.GetShare(normalized);
        breadcrumbs.Add(new BreadcrumbSegment(server, serverPath, share is null));

        if (share is null)
            return breadcrumbs;

        var body = normalized[2..];
        var parts = body.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var accumulator = serverPath;
        for (var i = 1; i < parts.Length; i++)
        {
            accumulator += "\\" + parts[i];
            breadcrumbs.Add(new BreadcrumbSegment(parts[i], accumulator, i == parts.Length - 1));
        }

        return breadcrumbs;
    }

    public static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        if (NetworkPath.IsNetworkRoot(path) || NetworkPath.IsServerRoot(path))
            return NetworkPath.Normalize(path);
        if (path.Length == 2 && path[1] == ':')
            return path + Path.DirectorySeparatorChar;
        if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
            return path + Path.DirectorySeparatorChar;
        return path;
    }
}

public readonly record struct NavigationTransition(string Path, bool CanGoBack, bool CanGoForward);
