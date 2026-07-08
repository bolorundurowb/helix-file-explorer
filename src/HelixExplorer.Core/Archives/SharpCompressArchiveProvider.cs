using System.Diagnostics;
using HelixExplorer.Core.Collections;
using HelixExplorer.Core.Models;
using SharpCompress.Archives;

namespace HelixExplorer.Core.Archives;

/// <summary>
/// SharpCompress-backed virtual file system for zip/7z/rar/tar/gzip archives.
/// Uses <see cref="ArchiveFactory.OpenArchive"/> for random-access enumeration.
/// </summary>
public sealed class SharpCompressArchiveProvider : IArchiveProvider
{
    public bool IsArchiveFile(string path) => ArchivePath.IsArchiveFile(path);

    public async ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(
        string virtualPath,
        CancellationToken token = default)
    {
        if (!ArchivePath.TryParse(virtualPath, out var archivePath, out var innerPath))
            return Array.Empty<FileSystemEntry>();

        token.ThrowIfCancellationRequested();
        return await Task.Run(() => Enumerate(archivePath, innerPath, token), token).ConfigureAwait(false);
    }

    public async ValueTask<string?> ExtractEntryAsync(string virtualPath, CancellationToken token = default)
    {
        if (!ArchivePath.TryParse(virtualPath, out var archivePath, out var innerPath)
            || string.IsNullOrEmpty(innerPath))
        {
            return null;
        }

        var wanted = innerPath.Replace('\\', '/').Trim('/');
        if (wanted.EndsWith('/'))
            return null;

        return await Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(archivePath))
                    return null;

                using var archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath));
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.IsDirectory)
                        continue;

                    var key = (entry.Key ?? string.Empty).Replace('\\', '/').Trim('/');
                    if (!key.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var tempDir = Path.Combine(
                        Path.GetTempPath(),
                        "HelixExplorer",
                        Path.GetFileNameWithoutExtension(archivePath));
                    Directory.CreateDirectory(tempDir);
                    var dest = Path.Combine(tempDir, Path.GetFileName(wanted));

                    await using var src = await entry.OpenEntryStreamAsync(token).ConfigureAwait(false);
                    await using var fs = File.Create(dest);
                    await src.CopyToAsync(fs, token).ConfigureAwait(false);
                    return dest;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExtractEntryAsync failed for '{virtualPath}': {ex.Message}");
            }

            return null;
        }, token).ConfigureAwait(false);
    }

    private static IReadOnlyList<FileSystemEntry> Enumerate(
        string archivePath,
        string innerFilter,
        CancellationToken token)
    {
        if (!File.Exists(archivePath))
            return Array.Empty<FileSystemEntry>();

        using var poolList = new ArrayPoolList<FileSystemEntry>(128);
        var normalizedFilter = innerFilter.Replace('\\', '/').Trim('/');
        var filterPrefix = string.IsNullOrEmpty(normalizedFilter) ? string.Empty : normalizedFilter + "/";
        var seenChildren = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath));
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                var key = (entry.Key ?? string.Empty).Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrEmpty(key))
                    continue;

                if (!key.StartsWith(filterPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var tail = key[filterPrefix.Length..].TrimEnd('/');
                if (string.IsNullOrEmpty(tail))
                    continue;

                var slash = tail.IndexOf('/');
                if (slash < 0)
                {
                    if (entry.IsDirectory)
                    {
                        if (seenChildren.Add(tail))
                        {
                            poolList.Add(new FileSystemEntry(
                                $"{ArchivePath.Scheme}{archivePath}!{filterPrefix}{tail}/",
                                tail,
                                true,
                                0L,
                                entry.LastModifiedTime ?? DateTime.MinValue,
                                string.Empty));
                        }
                    }
                    else if (seenChildren.Add(tail))
                    {
                        poolList.Add(new FileSystemEntry(
                            $"{ArchivePath.Scheme}{archivePath}!{filterPrefix}{tail}",
                            tail,
                            false,
                            entry.Size,
                            entry.LastModifiedTime ?? DateTime.MinValue,
                            Path.GetExtension(tail)));
                    }
                }
                else
                {
                    var child = tail[..slash];
                    if (seenChildren.Add(child))
                    {
                        poolList.Add(new FileSystemEntry(
                            $"{ArchivePath.Scheme}{archivePath}!{filterPrefix}{child}/",
                            child,
                            true,
                            0L,
                            DateTime.MinValue,
                            string.Empty));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or InvalidOperationException)
        {
            Debug.WriteLine($"Archive enumerate failed for '{archivePath}': {ex.Message}");
        }

        return poolList.ToArray();
    }
}
