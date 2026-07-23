using HelixExplorer.Core.Collections;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Search;
using HelixExplorer.Core.Sorting;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

public sealed class WinFileSystemProvider(
    IShellFolderEnumerator shell,
    INetworkConnectionService networkConnections,
    ILogger<WinFileSystemProvider> logger)
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

        if (NetworkPath.IsServerRoot(resolved))
        {
            var shares = await EnumerateServerSharesAsync(resolved, cancellationToken).ConfigureAwait(false);
            return new DirectoryListing(resolved, shares);
        }

        var entries = await EnumeratePathAsync(resolved, cancellationToken).ConfigureAwait(false);
        return new DirectoryListing(resolved, entries);
    }

    private async Task<IReadOnlyList<FileSystemEntry>> EnumerateServerSharesAsync(
        string serverRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                () => WinNetworkShareEnumerator.EnumerateShares(serverRoot, logger, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            if (!await networkConnections.EnsureConnectedAsync(serverRoot, cancellationToken).ConfigureAwait(false))
                throw;

            return await Task.Run(
                () => WinNetworkShareEnumerator.EnumerateShares(serverRoot, logger, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<FileSystemEntry>> EnumeratePathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => Enumerate(path, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || IsAccessDeniedIo(ex))
        {
            if (!NetworkPath.IsUnc(path))
                throw;

            if (!await networkConnections.EnsureConnectedAsync(path, cancellationToken).ConfigureAwait(false))
                throw;

            return await Task.Run(() => Enumerate(path, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsAccessDeniedIo(Exception ex)
        => ex is IOException io
           && (io.Message.Contains("denied", StringComparison.OrdinalIgnoreCase)
               || io.Message.Contains("access", StringComparison.OrdinalIgnoreCase));

    public async ValueTask<SearchResult> SearchRecursiveAsync(
        string path,
        string query,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !DirectoryExists(path) || string.IsNullOrWhiteSpace(query))
            return SearchResult.Empty;

        var resolved = ResolvePath(path);
        return await SearchRecursiveAsyncCore(resolved, query.Trim(), options, cancellationToken).ConfigureAwait(false);
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

    private async Task<SearchResult> SearchRecursiveAsyncCore(
        string path,
        string query,
        SearchOptions options,
        CancellationToken token)
    {
        var results = new List<FileSystemEntry>(Math.Min(options.MaxResults, 256));
        var dirQueue = new Queue<(string Dir, int Depth)>();
        dirQueue.Enqueue((path, 0));
        var scanContent = options.SearchFileContents && !GlobMatcher.HasGlobMetacharacters(query);

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
        var rootPrefixLength = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;

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
                    if (isDir && depth < options.MaxDepth)
                        dirQueue.Enqueue((info.FullName, depth + 1));

                    var relative = info.FullName.Length > rootPrefixLength
                        ? info.FullName[(rootPrefixLength + 1)..]
                        : info.Name;
                    var nameMatches = EntryNameMatcher.Matches(info.Name, query)
                                      || EntryNameMatcher.Matches(relative.Replace('\\', '/'), query);

                    var contentMatches = false;
                    if (!nameMatches && scanContent && !isDir
                        && TextFileClassifier.IsLikelyTextExtension(info.Extension))
                    {
                        contentMatches = await FileContentSearcher.ContainsAsync(
                            info.FullName,
                            query,
                            options.MaxContentBytes,
                            token).ConfigureAwait(false);
                    }

                    if (!nameMatches && !contentMatches)
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
            // Surface UNC failures so the pane can show Access denied / unavailable instead of an empty folder.
            if (NetworkPath.IsUnc(path))
                throw;
        }

        var snapshot = entries.ToArray();
        Array.Sort(snapshot, FileSystemEntryComparer.For(SortColumn.Name, descending: false));
        return snapshot;
    }
}
