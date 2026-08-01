using HelixExplorer.Core.Collections;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.FileSystem;

public sealed class MacTrashEnumerator(ILogger<MacTrashEnumerator> logger) : IShellFolderEnumerator
{
    private FileSystemWatcher? _watcher;
    private readonly object _watcherLock = new();

    public event EventHandler? RecycleBinChanged;

    public async ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default)
    {
        if (IsTrashPath(shellPath))
        {
            return await EnumerateTrashAsync(ct).ConfigureAwait(false);
        }

        // For regular paths, use standard enumeration
        return await Task.Run(() => EnumeratePath(shellPath, ct), ct).ConfigureAwait(false);
    }

    private IReadOnlyList<FileSystemEntry> EnumeratePath(string path, CancellationToken ct)
    {
        using var entries = new ArrayPoolList<FileSystemEntry>(128);
        try
        {
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false
            };

            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", opts))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(entry);
                    var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                    var size = isDir ? 0L : info.Length;
                    var modified = info.LastWriteTimeUtc;
                    var ext = isDir ? string.Empty : info.Extension;
                    var isHidden = (info.Attributes & FileAttributes.Hidden) != 0;
                    entries.Add(new FileSystemEntry(info.FullName, info.Name, isDir, size, modified, ext, isHidden));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    logger.LogDebug(ex, "Skipping entry '{Entry}'", entry);
                }
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

    private async Task<IReadOnlyList<FileSystemEntry>> EnumerateTrashAsync(CancellationToken ct)
    {
        var trashPath = GetTrashPath();
        var filesPath = Path.Combine(trashPath, "files");
        var infoPath = Path.Combine(trashPath, "info");

        using var entries = new ArrayPoolList<FileSystemEntry>(64);

        if (Directory.Exists(filesPath))
        {
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(filesPath))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(entry);
                        var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                        var size = isDir ? 0L : info.Length;
                        var modified = info.LastWriteTimeUtc;
                        var ext = isDir ? string.Empty : info.Extension;
                        var originalPath = GetOriginalPathFromInfo(entry, infoPath);
                        entries.Add(new FileSystemEntry(
                            info.FullName,
                            info.Name,
                            isDir,
                            size,
                            modified,
                            ext,
                            false,
                            originalPath));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        logger.LogDebug(ex, "Skipping trash entry '{Entry}'", entry);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                logger.LogError(ex, "Enumerate failed on trash");
            }
        }

        var snapshot = entries.ToArray();
        Array.Sort(snapshot, FileSystemEntryComparer.For(SortColumn.Name, descending: false));
        return snapshot;
    }

    private static string? GetOriginalPathFromInfo(string trashEntryPath, string infoPath)
    {
        var fileName = Path.GetFileName(trashEntryPath);
        var infoFile = Path.Combine(infoPath, fileName + ".trashinfo");
        if (File.Exists(infoFile))
        {
            try
            {
                var content = File.ReadAllText(infoFile);
                var pathLine = content.Split('\n').FirstOrDefault(l => l.StartsWith("Path=", StringComparison.Ordinal));
                if (pathLine is not null)
                    return pathLine["Path=".Length..];
            }
            catch { }
        }
        return null;
    }

    public async ValueTask RestoreAsync(string itemPath, string? destinationPath = null, CancellationToken ct = default)
    {
        if (!File.Exists(itemPath) && !Directory.Exists(itemPath))
            return;

        var originalPath = destinationPath ?? GetOriginalPathFromInfo(itemPath, Path.Combine(GetTrashPath(), "info"));
        var dest = originalPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", Path.GetFileName(itemPath));

        if (Directory.Exists(itemPath))
            Directory.Move(itemPath, dest);
        else
            File.Move(itemPath, dest);

        OnRecycleBinChanged();
    }

    public async ValueTask EmptyRecycleBinAsync(CancellationToken ct = default)
    {
        var trashPath = GetTrashPath();
        var filesPath = Path.Combine(trashPath, "files");
        var infoPath = Path.Combine(trashPath, "info");

        if (Directory.Exists(filesPath))
            Directory.Delete(filesPath, true);
        if (Directory.Exists(infoPath))
            Directory.Delete(infoPath, true);

        Directory.CreateDirectory(filesPath);
        Directory.CreateDirectory(infoPath);

        OnRecycleBinChanged();
    }

    public async ValueTask<(long ItemCount, long TotalSize)> QueryRecycleBinAsync(CancellationToken ct = default)
    {
        var filesPath = Path.Combine(GetTrashPath(), "files");
        if (!Directory.Exists(filesPath))
            return (0, 0);

        long count = 0, size = 0;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(filesPath, "*", new EnumerationOptions { RecurseSubdirectories = true }))
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(entry))
                {
                    count++;
                    size += new FileInfo(entry).Length;
                }
                else if (Directory.Exists(entry))
                {
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "QueryRecycleBin failed");
        }
        return (count, size);
    }

    public bool HasRecycleBinItems()
    {
        var filesPath = Path.Combine(GetTrashPath(), "files");
        return Directory.Exists(filesPath) && Directory.EnumerateFileSystemEntries(filesPath).Any();
    }

    public void StartRecycleBinWatcher()
    {
        lock (_watcherLock)
        {
            if (_watcher is not null)
                return;

            var filesPath = Path.Combine(GetTrashPath(), "files");
            if (!Directory.Exists(filesPath))
                return;

            _watcher = new FileSystemWatcher(filesPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += (_, _) => OnRecycleBinChanged();
            _watcher.Deleted += (_, _) => OnRecycleBinChanged();
            _watcher.Renamed += (_, _) => OnRecycleBinChanged();
        }
    }

    public void StopRecycleBinWatcher()
    {
        lock (_watcherLock)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void OnRecycleBinChanged() => RecycleBinChanged?.Invoke(this, EventArgs.Empty);

    private static string GetTrashPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");

    private static bool IsTrashPath(string path) => path.StartsWith(GetTrashPath(), StringComparison.OrdinalIgnoreCase);
}