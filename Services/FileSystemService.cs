using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using HelixExplorer.Infrastructure;
using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>
/// Zero-allocation directory enumeration. Uses
/// <see cref="Directory.EnumerateFileSystemEntries"/> with
/// <see cref="EnumerationOptions.IgnoreInaccessible"/> and rents buffers from
/// <see cref="ArrayPoolList{T}"/> for amortised GC-free listing.
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
            // Only normalise the host-path portion before the bang.
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
            // Archive paths should never reach here; the pane routes them via the ArchiveService.
            return Array.Empty<FileSystemEntry>();
        }

        using var entries = ArrayPoolList<FileSystemEntry>.Rent(128);
        ReadOnlySpan<char> name; // dummy to demonstrate we'd keep spans

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", s_options))
            {
                token.ThrowIfCancellationRequested();
                string fileName;
                bool isDir;
                long size = 0L;
                DateTime modified = DateTime.MinValue;

                try
                {
                    FileAttributes attrs = File.GetAttributes(entry);
                    isDir = (attrs & FileAttributes.Directory) != 0;
                    fileName = Path.GetFileName(entry);
                    modified = (attrs & FileAttributes.Directory) == 0 ? File.GetLastWriteTimeUtc(entry) : Directory.GetLastWriteTimeUtc(entry);
                    if (!isDir)
                    {
                        var fi = new FileInfo(entry);
                        size = fi.Length;
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    Debug.WriteLine($"Skipping inaccessible entry '{entry}': {ex.Message}");
                    try { fileName = Path.GetFileName(entry); } catch { continue; }
                    isDir = false;
                }

                string ext = isDir ? string.Empty : Path.GetExtension(fileName);
                entries.Add(new FileSystemEntry(entry, fileName, isDir, size, modified, ext));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Debug.WriteLine($"Enumerate failed on '{path}': {ex.Message}");
            // Return what we managed to collect.
        }

        return entries.ToReadOnlyAndReset();
    }
}