using System.Diagnostics;
using HelixExplorer.Core.Collections;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Low-allocation directory enumeration via a single
/// <see cref="DirectoryInfo.EnumerateFileSystemInfos"/> pass so attributes, size, and
/// timestamps come from the Win32 find data without extra per-entry stats.
/// </summary>
public sealed class WinFileSystemProvider : IFileSystemProvider
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

        var resolved = ResolvePath(path);
        var entries = await Task.Run(() => Enumerate(resolved, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new DirectoryListing(resolved, entries);
    }

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
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
            Debug.WriteLine($"WinFileSystemProvider.ResolvePath failed for '{path}': {ex.Message}");
            return path;
        }
    }

    public bool DirectoryExists(string path) => !string.IsNullOrEmpty(path) && Directory.Exists(path);

    public bool FileExists(string path) => !string.IsNullOrEmpty(path) && File.Exists(path);

    private static IReadOnlyList<FileSystemEntry> Enumerate(string path, CancellationToken token)
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
                    Debug.WriteLine($"Skipping inaccessible entry '{info.FullName}': {ex.Message}");
                    modified = DateTime.MinValue;
                }

                var ext = isDir ? string.Empty : info.Extension;
                var isHidden = (info.Attributes & FileAttributes.Hidden) != 0;
                entries.Add(new FileSystemEntry(info.FullName, info.Name, isDir, size, modified, ext, isHidden));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            Debug.WriteLine($"Enumerate failed on '{path}': {ex.Message}");
        }

        var snapshot = entries.ToArray();
        Array.Sort(snapshot, FileSystemEntryComparer.For(SortColumn.Name, descending: false));
        return snapshot;
    }
}
