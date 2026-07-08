using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using HelixExplorer.Infrastructure;
using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>
/// Low-allocation directory enumeration. Uses
/// <see cref="DirectoryInfo.EnumerateFileSystemInfos(string, EnumerationOptions)"/> so the
/// attributes, size, and timestamps come straight from the single Win32 find pass — no
/// extra <c>GetAttributes</c>/<c>GetLastWriteTime</c>/<c>FileInfo</c> stat calls per entry.
/// Results accumulate in a pooled buffer (<see cref="ArrayPoolList{T}"/>) and are snapshotted
/// once at the end.
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    private static readonly EnumerationOptions s_options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Simple
    };

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FileSystemEntry>> GetDirectoryContentsAsync(string path, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(path) || !DirectoryExists(path))
        {
            return Array.Empty<FileSystemEntry>();
        }

        // Run enumeration on the thread pool; snapshot the listing outside the UI thread.
        return await Task.Run(() => Enumerate(path, token), token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        // Preserve archive:// virtual scheme.
        if (path.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            int schemeEnd = path.IndexOf('!', StringComparison.Ordinal);
            if (schemeEnd < 0) return Normalize(path);
            string scheme = path[..schemeEnd];
            string remainder = path[(schemeEnd + 1)..];
            return scheme + "!" + Normalize(remainder).TrimStart('\\', '/');
        }

        return Normalize(path);
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FileSystemService.ResolvePath failed for '{path}': {ex.Message}");
            return path;
        }
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path) => !string.IsNullOrEmpty(path) && Directory.Exists(path);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsArchivePath(string path) =>
        path.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<FileSystemEntry> Enumerate(string path, CancellationToken token)
    {
        if (IsArchivePath(path))
        {
            // Archive paths never reach here; the pane routes them via the ArchiveService.
            return Array.Empty<FileSystemEntry>();
        }

        using var entries = ArrayPoolList<FileSystemEntry>.Rent(128);

        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var info in dir.EnumerateFileSystemInfos("*", s_options))
            {
                token.ThrowIfCancellationRequested();

                bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
                long size = 0L;
                DateTime modified;
                try
                {
                    modified = info.LastWriteTimeUtc;
                    if (!isDir && info is FileInfo fi) size = fi.Length;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    Debug.WriteLine($"Skipping inaccessible entry '{info.FullName}': {ex.Message}");
                    modified = DateTime.MinValue;
                }

                string ext = isDir ? string.Empty : info.Extension;
                entries.Add(new FileSystemEntry(info.FullName, info.Name, isDir, size, modified, ext));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            Debug.WriteLine($"Enumerate failed on '{path}': {ex.Message}");
            // Return whatever we managed to collect.
        }

        return entries.ToReadOnlyAndReset();
    }
}
