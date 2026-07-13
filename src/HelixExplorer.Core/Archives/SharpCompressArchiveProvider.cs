using HelixExplorer.Core.Collections;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

namespace HelixExplorer.Core.Archives;

/// <summary>
/// SharpCompress-backed virtual file system for zip/7z/rar/tar/gzip archives.
/// Uses <see cref="ArchiveFactory.OpenArchive"/> for random-access enumeration.
/// </summary>
public sealed class SharpCompressArchiveProvider(ILogger<SharpCompressArchiveProvider> logger) : IArchiveProvider
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
                logger.LogError(ex, "Archive extract failed for '{VirtualPath}'", virtualPath);
            }

            return null;
        }, token).ConfigureAwait(false);
    }

    public async ValueTask CreateZipAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationZipPath,
        CancellationToken token = default)
    {
        if (sourcePaths.Count == 0)
            return;

        token.ThrowIfCancellationRequested();
        await Task.Run(() =>
        {
            var directory = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var archive = ZipArchive.CreateArchive();
            foreach (var source in sourcePaths)
            {
                token.ThrowIfCancellationRequested();
                AddSourceToArchive(archive, source);
            }

            archive.SaveTo(destinationZipPath, CompressionType.Deflate);
        }, token).ConfigureAwait(false);
    }

    public async ValueTask ExtractArchiveToDirectoryAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(destinationDirectory);
            using var archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath));
            archive.WriteToDirectory(destinationDirectory, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            });
        }, token).ConfigureAwait(false);
    }

    public async ValueTask ExtractVirtualEntriesAsync(
        IReadOnlyList<string> virtualPaths,
        string destinationDirectory,
        CancellationToken token = default)
    {
        if (virtualPaths.Count == 0)
            return;

        token.ThrowIfCancellationRequested();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(destinationDirectory);

            var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var virtualPath in virtualPaths)
            {
                if (!ArchivePath.TryParse(virtualPath, out var archiveFile, out var inner))
                    continue;

                if (!grouped.TryGetValue(archiveFile, out var prefixes))
                {
                    prefixes = new List<string>();
                    grouped[archiveFile] = prefixes;
                }

                prefixes.Add(inner.Replace('\\', '/').Trim('/'));
            }

            foreach (var (archiveFile, prefixes) in grouped)
            {
                token.ThrowIfCancellationRequested();
                if (!File.Exists(archiveFile))
                    continue;

                using var archive = ArchiveFactory.OpenArchive(new FileInfo(archiveFile));
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.IsDirectory)
                        continue;

                    var key = (entry.Key ?? string.Empty).Replace('\\', '/').TrimStart('/').TrimEnd('/');
                    if (string.IsNullOrEmpty(key))
                        continue;

                    if (!prefixes.Any(prefix => EntryMatches(key, prefix)))
                        continue;

                    var destPath = Path.Combine(
                        destinationDirectory,
                        key.Replace('/', Path.DirectorySeparatorChar));

                    var fullDest = Path.GetFullPath(destPath);
                    var fullBase = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!fullDest.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    entry.WriteToFile(destPath, new ExtractionOptions { Overwrite = true });
                }
            }
        }, token).ConfigureAwait(false);
    }

    private static void AddSourceToArchive(IWritableArchive<ZipWriterOptions> archive, string source)
    {
        if (File.Exists(source))
        {
            archive.AddEntry(Path.GetFileName(source), source);
            return;
        }

        if (!Directory.Exists(source))
            return;

        var rootName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            archive.AddEntry($"{rootName}/{relative}", file);
        }
    }

    private static bool EntryMatches(string entryKey, string wantedPrefix)
    {
        if (string.IsNullOrEmpty(wantedPrefix))
            return true;

        return entryKey.Equals(wantedPrefix, StringComparison.OrdinalIgnoreCase)
               || entryKey.StartsWith(wantedPrefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<FileSystemEntry> Enumerate(
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
                                ArchivePath.Combine(archivePath, filterPrefix + tail + "/"),
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
                            ArchivePath.Combine(archivePath, filterPrefix + tail),
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
                            ArchivePath.Combine(archivePath, filterPrefix + child + "/"),
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
            logger.LogError(ex, "Archive enumerate failed for '{ArchivePath}'", archivePath);
        }

        return poolList.ToArray();
    }
}
