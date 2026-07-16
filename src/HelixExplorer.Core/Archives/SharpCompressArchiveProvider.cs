using System.Security.Cryptography;
using System.Text;
using HelixExplorer.Core.Collections;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

namespace HelixExplorer.Core.Archives;

public sealed class SharpCompressArchiveProvider(ILogger<SharpCompressArchiveProvider> logger) : IArchiveProvider
{
    private static string ExtractionRoot => Path.Combine(Path.GetTempPath(), "HelixExplorer");

    public bool IsArchiveFile(string path) => ArchivePath.IsArchiveFile(path);

    /// <summary>
    /// Derives a per-archive temp directory keyed on the FULL archive path (not just its file name)
    /// so that two archives sharing a name in different folders do not extract over each other.
    /// </summary>
    private static string GetArchiveTempDir(string archivePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(archivePath)));
        var archiveId = Convert.ToHexString(hash)[..12];
        return Path.Combine(ExtractionRoot, archiveId);
    }

    public void CleanupExtractedFiles()
    {
        try
        {
            if (Directory.Exists(ExtractionRoot))
                Directory.Delete(ExtractionRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: files may still be open (e.g. a preview handler). Leave them for next run.
            logger.LogWarning(ex, "Failed to clean up archive extraction directory '{Root}'", ExtractionRoot);
        }
    }

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

                    var tempDir = GetArchiveTempDir(archivePath);
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
