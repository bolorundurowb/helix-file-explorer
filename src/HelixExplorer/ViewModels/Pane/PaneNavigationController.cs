using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>How a pane arrived at a folder, which decides what should be focused there.</summary>
public enum PaneNavigationKind
{
    /// <summary>New navigation: Up/breadcrumb highlights the branch just left.</summary>
    Forward,

    /// <summary>Back/Forward: replay where the user was in that folder.</summary>
    History
}

public sealed class PaneNavigationController(IFileSystemProvider fileSystem, IArchiveProvider archive)
{
    /// <summary>
    /// Focus memory is session state for browsing comfort, so it stays in memory and stays bounded —
    /// a long session must not accumulate an entry per folder visited.
    /// </summary>
    private const int MaxRememberedFolders = 64;

    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private readonly Dictionary<string, string> _focusByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _focusOrder = new();

    public bool CanGoBack => _backStack.Count > 0;

    public bool CanGoForward => _forwardStack.Count > 0;

    /// <summary>
    /// Records the entry a pane was focused on in <paramref name="path"/>. A null or empty
    /// <paramref name="entryPath"/> forgets the folder rather than storing a meaningless anchor.
    /// </summary>
    public void RememberFocus(string? path, string? entryPath)
    {
        var key = PathUtilities.NormalizePath(path);
        if (key.Length == 0)
            return;

        if (string.IsNullOrEmpty(entryPath))
        {
            _focusByPath.Remove(key);
            return;
        }

        if (!_focusByPath.TryAdd(key, entryPath))
        {
            _focusByPath[key] = entryPath;
            return;
        }

        _focusOrder.Enqueue(key);
        while (_focusOrder.Count > MaxRememberedFolders)
        {
            var evicted = _focusOrder.Dequeue();
            if (!string.Equals(evicted, key, StringComparison.OrdinalIgnoreCase))
                _focusByPath.Remove(evicted);
        }
    }

    public string? GetRememberedFocus(string? path)
    {
        var key = PathUtilities.NormalizePath(path);
        return key.Length == 0 ? null : _focusByPath.GetValueOrDefault(key);
    }

    /// <summary>
    /// The entry that should be focused on entering <paramref name="destination"/> from
    /// <paramref name="origin"/>, or null when there is nothing sensible to focus.
    /// </summary>
    public string? ResolveFocusOnEnter(PaneNavigationKind kind, string? origin, string? destination)
    {
        if (string.IsNullOrEmpty(destination))
            return null;

        // Back/Forward replays the user's own position; only a fresh navigation upwards should
        // override it with the folder that was just left.
        if (kind == PaneNavigationKind.History)
            return GetRememberedFocus(destination);

        return TryGetChildOnPath(destination, origin) ?? GetRememberedFocus(destination);
    }

    /// <summary>
    /// The immediate child of <paramref name="ancestor"/> on the way down to
    /// <paramref name="descendant"/>, so jumping up several levels (breadcrumb) still highlights the
    /// branch the user came from. Null when the two are unrelated or equal.
    /// </summary>
    public static string? TryGetChildOnPath(string? ancestor, string? descendant)
    {
        if (string.IsNullOrEmpty(ancestor)
            || string.IsNullOrEmpty(descendant)
            || PathUtilities.PathsEqual(ancestor, descendant)
            || !PathUtilities.IsSameOrChildPath(ancestor, descendant))
        {
            return null;
        }

        var candidate = descendant;
        while (true)
        {
            var parent = GetParentPath(candidate);
            if (string.IsNullOrEmpty(parent) || PathUtilities.PathsEqual(parent, candidate))
                return null;

            if (PathUtilities.PathsEqual(parent, ancestor))
                return candidate;

            candidate = parent;
        }
    }

    private static string? GetParentPath(string path)
    {
        if (ArchivePath.IsVirtual(path))
            return ArchivePath.GetParent(path);

        if (ShellPath.IsShellPath(path))
            return null;

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Directory.GetParent(trimmed);
        return parent is null ? null : EnsureTrailingSeparator(parent.FullName);
    }

    /// <summary>
    /// Whether Up should be enabled for the current location (drive/network/server roots cannot go up).
    /// </summary>
    public static bool CanNavigateUp(string? path, bool isHome, bool isShellNamespace)
    {
        if (isHome || string.IsNullOrEmpty(path))
            return false;

        // Shell namespaces (e.g. Recycle Bin) navigate Home on Up.
        if (isShellNamespace)
            return true;

        if (ArchivePath.IsVirtual(path))
            return true;

        if (PathUtilities.IsDriveRoot(path))
            return false;

        if (NetworkPath.IsNetworkRoot(path) || NetworkPath.IsServerRoot(path))
            return false;

        if (NetworkPath.IsUnc(path))
            return NetworkPath.HasShare(path);

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Directory.GetParent(trimmed) is not null;
    }

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
