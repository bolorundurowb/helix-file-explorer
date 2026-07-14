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
        var entries = await Task.Run(() => Enumerate(resolved, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new DirectoryListing(resolved, entries);
    }

    public async ValueTask<IReadOnlyList<FileSystemEntry>> SearchRecursiveAsync(string path, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !DirectoryExists(path) || string.IsNullOrWhiteSpace(query))
            return Array.Empty<FileSystemEntry>();

        var resolved = ResolvePath(path);
        return await Task.Run(() => SearchRecursive(resolved, query.Trim(), cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (ShellPath.IsShellPath(path))
            return path;

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
        => ShellPath.IsShellPath(path) || (!string.IsNullOrEmpty(path) && Directory.Exists(path));

    public bool FileExists(string path) => !string.IsNullOrEmpty(path) && File.Exists(path);

    private IReadOnlyList<FileSystemEntry> SearchRecursive(string path, string query, CancellationToken token)
    {
        var results = new List<FileSystemEntry>();
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(path);

        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
            MatchType = MatchType.Simple
        };

        while (dirQueue.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var currentDir = dirQueue.Dequeue();

            FileSystemInfo[] infos;
            try
            {
                var dirInfo = new DirectoryInfo(currentDir);
                infos = dirInfo.GetFileSystemInfos("*", opts);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogDebug(ex, "Skipping inaccessible directory '{Directory}' during search", currentDir);
                continue;
            }

            foreach (var info in infos)
            {
                token.ThrowIfCancellationRequested();

                var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                var name = info.Name;
                var matches = name.AsSpan().Contains(query.AsSpan(), StringComparison.OrdinalIgnoreCase);

                if (isDir)
                    dirQueue.Enqueue(info.FullName);

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
            }
        }

        return results;
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
