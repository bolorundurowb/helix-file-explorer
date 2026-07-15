using HelixExplorer.Core.Collections;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

public sealed class WinFileSystemProvider(IShellFolderEnumerator shell, ILogger<WinFileSystemProvider> logger)
    : IFileSystemProvider
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Simple
    };

    public async ValueTask<DirectoryListing> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !DirectoryExists(path))
            return DirectoryListing.Empty;

        if (ShellPath.IsShellPath(path))
        {
            var shellEntries = await shell.EnumerateAsync(path, cancellationToken).ConfigureAwait(false);
            return new DirectoryListing(path, shellEntries);
        }

        var resolved = ResolvePath(path);
        if (NetworkPath.IsNetworkRoot(resolved))
        {
            var shellEntries = await shell.EnumerateAsync(ShellPath.Network, cancellationToken).ConfigureAwait(false);
            return new DirectoryListing(NetworkPath.Root, NormalizeShellNetworkEntries(shellEntries));
        }

        var entries = await Task.Run(() => Enumerate(resolved, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new DirectoryListing(resolved, entries);
    }

    public async ValueTask<SearchResult> SearchRecursiveAsync(
        string path,
        string query,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !DirectoryExists(path) || string.IsNullOrWhiteSpace(query))
            return SearchResult.Empty;

        var resolved = ResolvePath(path);
        return await Task.Run(() => SearchRecursive(resolved, query.Trim(), options, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (ShellPath.IsShellPath(path))
            return path;

        if (NetworkPath.IsUnc(path))
            return NetworkPath.Normalize(path);

        try
        {
            var full = Path.GetFullPath(path);
            if (full.Length == 2 && full[1] == ':')
                full += Path.DirectorySeparatorChar;
            return full;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ResolvePath failed for '{Path}'", path);
            return path;
        }
    }

    public bool DirectoryExists(string path)
        => ShellPath.IsShellPath(path)
           || NetworkPath.IsUnc(path)
           || (!string.IsNullOrEmpty(path) && Directory.Exists(path));

    public bool FileExists(string path) => !string.IsNullOrEmpty(path) && File.Exists(path);

    private static IReadOnlyList<FileSystemEntry> NormalizeShellNetworkEntries(IReadOnlyList<FileSystemEntry> entries)
    {
        var normalized = new List<FileSystemEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var path = NetworkPath.IsUnc(entry.FullPath)
                ? NetworkPath.Normalize(entry.FullPath)
                : NetworkPath.ForServer(entry.Name);

            normalized.Add(entry with { FullPath = path, IsDirectory = true, Extension = string.Empty });
        }

        normalized.Sort(FileSystemEntryComparer.For(SortColumn.Name, descending: false));
        return normalized;
    }

    private SearchResult SearchRecursive(string path, string query, SearchOptions options, CancellationToken token)
    {
        var results = new List<FileSystemEntry>(Math.Min(options.MaxResults, 256));
        var dirQueue = new Queue<(string Dir, int Depth)>();
        dirQueue.Enqueue((path, 0));

        // Stream entries (EnumerateFileSystemInfos) instead of materializing a full array per directory,
        // so a directory with tens of thousands of entries does not allocate a large transient array.
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = options.IncludeHiddenAndSystem
                ? (FileAttributes)0
                : FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false,
            MatchType = MatchType.Simple
        };

        var capped = false;

        while (dirQueue.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var (currentDir, depth) = dirQueue.Dequeue();

            try
            {
                foreach (var info in new DirectoryInfo(currentDir).EnumerateFileSystemInfos("*", opts))
                {
                    token.ThrowIfCancellationRequested();

                    var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                    var name = info.Name;
                    var matches = name.AsSpan().Contains(query.AsSpan(), StringComparison.OrdinalIgnoreCase);

                    // Queue subdirectories only while we are still under the depth bound.
                    if (isDir && depth < options.MaxDepth)
                        dirQueue.Enqueue((info.FullName, depth + 1));

                    if (!matches)
                        continue;

                    long size = 0;
                    DateTime modified = DateTime.MinValue;
                    try
                    {
                        modified = info.LastWriteTimeUtc;
                        if (!isDir && info is FileInfo fi)
                            size = fi.Length;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        logger.LogDebug(ex, "Failed to read metadata for '{Path}' during search", info.FullName);
                    }

                    var ext = isDir ? string.Empty : info.Extension;
                    var isHidden = (info.Attributes & FileAttributes.Hidden) != 0;
                    results.Add(new FileSystemEntry(info.FullName, info.Name, isDir, size, modified, ext, isHidden));

                    if (results.Count >= options.MaxResults)
                    {
                        capped = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogDebug(ex, "Skipping inaccessible directory '{Directory}' during search", currentDir);
                continue;
            }

            if (capped)
                break;
        }

        return new SearchResult(results, capped);
    }

    private IReadOnlyList<FileSystemEntry> Enumerate(string path, CancellationToken token)
    {
        using var entries = new ArrayPoolList<FileSystemEntry>(128);

        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var info in dir.EnumerateFileSystemInfos("*", Options))
            {
                token.ThrowIfCancellationRequested();

                var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                long size = 0;
                DateTime modified;
                try
                {
                    modified = info.LastWriteTimeUtc;
                    if (!isDir && info is FileInfo fi)
                        size = fi.Length;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    logger.LogWarning(ex, "Skipping inaccessible entry '{FullName}'", info.FullName);
                    modified = DateTime.MinValue;
                }

                var ext = isDir ? string.Empty : info.Extension;
                var isHidden = (info.Attributes & FileAttributes.Hidden) != 0;
                entries.Add(new FileSystemEntry(info.FullName, info.Name, isDir, size, modified, ext, isHidden));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            logger.LogError(ex, "Enumerate failed on '{Path}'", path);
        }

        var snapshot = entries.ToArray();
        Array.Sort(snapshot, FileSystemEntryComparer.For(SortColumn.Name, descending: false));
        return snapshot;
    }
}
