using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using HelixExplorer.Infrastructure;
using HelixExplorer.Models;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace HelixExplorer.Services;

/// <summary>
/// SharpCompress-backed virtual file system for zip/7z/rar/tar/gzip. Paths use the
/// <c>archive://&lt;path-to-zip&gt;!&lt;inner/&gt;</c> scheme where the host portion
/// is a real archive path on disk and the inner portion is a forward-slash relative
/// path inside the archive.
/// </summary>
public sealed class ArchiveService : IArchiveService
{
    public const string Scheme = "archive://";

    /// <summary>Detect whether <paramref name="absolutePath"/> on disk is an archive file.</summary>
    public bool IsArchive(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        ReadOnlySpan<char> ext = Path.GetExtension(path.AsSpan()).TrimStart('.').ToString().ToLowerInvariant().AsSpan();
        return ext.SequenceEqual("zip") || ext.SequenceEqual("7z") || ext.SequenceEqual("rar")
               || ext.SequenceEqual("tar") || ext.SequenceEqual("gz") || ext.SequenceEqual("bz2")
               || ext.SequenceEqual("tgz") || ext.SequenceEqual("txz") || ext.SequenceEqual("xz");
    }

    // Parsing of the archive:// scheme ----

    /// <summary>Splits "archive://C:\a\b.zip!folder/inner" into ("C:\a\b.zip", "folder/inner").
    /// Returns false if <paramref name="path"/> is not an archive path.</summary>
    internal static bool ParseVirtual(string path, out string archivePath, out string innerPath)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            archivePath = string.Empty;
            innerPath = string.Empty;
            return false;
        }
        string body = path[Scheme.Length..];
        int bang = body.IndexOf('!');
        if (bang < 0)
        {
            archivePath = body;
            innerPath = string.Empty;
            return true;
        }
        archivePath = body[..bang];
        innerPath = body[(bang + 1)..];
        return true;
    }

    public async ValueTask<IReadOnlyList<FileSystemEntry>> MountAsync(string archivePath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return await Task.Run(() => Enumerate(archivePath, string.Empty, token), token).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string virtualPath, CancellationToken token = default)
    {
        if (!ParseVirtual(virtualPath, out string archivePath, out string innerPath))
        {
            return Array.Empty<FileSystemEntry>();
        }
        token.ThrowIfCancellationRequested();
        return await Task.Run(() => Enumerate(archivePath, innerPath, token), token).ConfigureAwait(false);
    }

    private static IReadOnlyList<FileSystemEntry> Enumerate(string archivePath, string innerFilter, CancellationToken token)
    {
        if (!File.Exists(archivePath)) return Array.Empty<FileSystemEntry>();

        using var poolList = ArrayPoolList<FileSystemEntry>.Rent(128);
        // The set-of-children approach: walking all entries and projecting onto the
        // immediate children of innerFilter gives us virtual directory listings without
        // ever materialising the archive tree.
        string normalizedFilter = innerFilter.Replace('\\', '/').Trim('/').TrimEnd('/');
        string filterPrefix = string.IsNullOrEmpty(normalizedFilter)
            ? string.Empty
            : normalizedFilter + "/";

        var seenChildren = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var stream = File.OpenRead(archivePath);
            var readerOptions = new ReaderOptions { LeaveStreamOpen = false };

            // Use IReader so we get a flat walk of every entry.
            using var reader = SharpCompress.Readers.ReaderFactory.Open(stream, readerOptions);

            while (reader.MoveToNextEntry())
            {
                token.ThrowIfCancellationRequested();
                string key = reader.Entry.Key?.Replace('\\', '/').TrimStart('/') ?? string.Empty;
                if (string.IsNullOrEmpty(key)) continue;

                if (!key.StartsWith(filterPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string tail = key[filterPrefix.Length..];
                if (string.IsNullOrEmpty(tail)) continue;

                int slash = tail.IndexOf('/');
                if (slash < 0)
                {
                    // File directly under the current virtual dir.
                    if (reader.Entry.IsDirectory) continue; // dirs-without-trailing-slash edge case
                    if (seenChildren.Add(tail))
                    {
                        poolList.Add(new FileSystemEntry(
                            $"{Scheme}{archivePath}!{filterPrefix}{tail}".TrimEnd('/'),
                            tail,
                            false,
                            (long)reader.Entry.Size,
                            (reader.Entry.LastModifiedTime ?? DateTime.MinValue),
                            Path.GetExtension(tail)));
                    }
                }
                else
                {
                    string child = tail[..slash];
                    if (seenChildren.Add(child))
                    {
                        // Synthesise a child directory entry. Real dir entries that come
                        // with their own metadata (size/date) are honoured, otherwise use
                        // the parent of an interior file.
                        bool isDirEnd = slash == tail.Length - 1; // tail ends with '/'
                        var modified = isDirEnd
                            ? ((reader.Entry.LastModifiedTime ?? DateTime.MinValue))
                            : DateTime.MinValue;
                        long size = isDirEnd && reader.Entry.IsDirectory ? (long)reader.Entry.Size : 0L;
                        poolList.Add(new FileSystemEntry(
                            $"{Scheme}{archivePath}!{filterPrefix}{child}/",
                            child,
                            true,
                            size,
                            modified,
                            string.Empty));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Debug.WriteLine($"ArchiveService.Enumerate failed for '{archivePath}': {ex.Message}");
        }

        return poolList.ToReadOnlyAndReset();
    }
}