using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using HelixExplorer.Core.Collections;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

namespace HelixExplorer.Core.Archives;

public sealed class SharpCompressArchiveProvider : IArchiveProvider
{
    private const int CopyBufferSize = 64 * 1024;
    private readonly ILogger<SharpCompressArchiveProvider> _logger;
    private readonly ArchiveExtractionLimits _limits;
    private readonly IAppPathProvider _paths;

    public SharpCompressArchiveProvider(
        ILogger<SharpCompressArchiveProvider> logger,
        ArchiveExtractionLimits? limits = null,
        IAppPathProvider? paths = null)
    {
        _logger = logger;
        _limits = limits ?? ArchiveExtractionLimits.Default;
        _limits.Validate();
        _paths = paths ?? DefaultAppPathProvider.Instance;
    }

    private string ExtractionRoot => _paths.TempRoot;

    public bool IsArchiveFile(string path) => ArchivePath.IsArchiveFile(path);

    /// <summary>
    /// Derives a per-archive temp directory keyed on the FULL archive path (not just its file name)
    /// so that two archives sharing a name in different folders do not extract over each other.
    /// </summary>
    private string GetArchiveTempDir(string archivePath)
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
        // CA1031 flags the catch clause's declared type (Exception), not the `when` filter, so this
        // multi-type catch - the only way to catch two unrelated exception types in C# without a
        // shared base - reads as "general" to the analyzer despite already being narrowed to exactly
        // IOException/UnauthorizedAccessException.
#pragma warning disable CA1031
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: files may still be open (e.g. a preview handler). Leave them for next run.
            _logger.LogWarning(ex, "Failed to clean up archive extraction directory '{Root}'", ExtractionRoot);
        }
#pragma warning restore CA1031
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
                var entryCount = 0;
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    // Tar symlink entries (LinkTarget set) are never extracted: SharpCompress only
                    // materializes them as real filesystem symlinks via a caller-supplied
                    // SymbolicLinkHandler, which we do not configure, but a future ExtractionOptions
                    // change could enable a symlink-then-write-through escape (CVE-2026-44788-style)
                    // if this guard were removed. There is no legitimate reason for this file
                    // browser to recreate archive-embedded symlinks.
                    if (entry.IsDirectory || entry.LinkTarget is not null)
                        continue;
                    EnforceEntryMetadataLimits(entry, ref entryCount, totalBytes: 0);

                    var key = (entry.Key ?? string.Empty).Replace('\\', '/').Trim('/');
                    if (!key.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var tempDir = GetArchiveTempDir(archivePath);
                    Directory.CreateDirectory(tempDir);
                    // Key the temp file on the full inner path so equal basenames in different
                    // folders (e.g. one/readme.txt vs two/readme.txt) do not clobber each other.
                    var dest = Path.Combine(tempDir, MakeUniqueTempFileName(wanted));

                    // Defense in depth: MakeUniqueTempFileName uses Path.GetFileName, so the
                    // basename is always safe, but mirror the ExtractEntryToDestination bound
                    // check so any future refactor that changes that pattern still rejects
                    // entries that escape the per-archive temp directory.
                    var fullDest = Path.GetFullPath(dest);
                    var fullBase = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!fullDest.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                        return null;

                    try
                    {
                        await using var src = await entry.OpenEntryStreamAsync(token).ConfigureAwait(false);
                        await using var fs = new FileStream(
                            dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await CopyEntryAsync(src, fs, totalBeforeEntry: 0, token).ConfigureAwait(false);
                        return dest;
                    }
                    catch
                    {
                        TryDeleteFile(dest);
                        throw;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            // Deliberately broad: SharpCompress and the underlying archive I/O can fail in many
            // ways (corrupt archive, unsupported format quirk, disk error mid-extract). The contract
            // for this method is already "return null on failure" (see the earlier bound-check
            // return above), so any unexpected exception should degrade the same way, not crash the
            // caller trying to preview or open a file inside an archive.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                _logger.LogError(ex, "Archive extract failed for '{VirtualPath}'", virtualPath);
            }
#pragma warning restore CA1031

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
            return ExtractArchiveCoreAsync(archivePath, destinationDirectory, token);
        }, token).ConfigureAwait(false);
    }

    private async Task ExtractArchiveCoreAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            using var archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath));
            var entryCount = 0;
            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                // See ExtractEntryAsync: never materialize archive-embedded symlinks.
                if (entry.IsDirectory || entry.LinkTarget is not null)
                    continue;
                EnforceEntryMetadataLimits(entry, ref entryCount, totalBytes);

                var key = (entry.Key ?? string.Empty).Replace('\\', '/').TrimStart('/').TrimEnd('/');
                if (string.IsNullOrEmpty(key))
                    continue;

                totalBytes += await ExtractEntryToDestinationAsync(entry, key, destinationDirectory, totalBytes, token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArchiveExtractionLimitException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive extraction failed for '{ArchivePath}'", archivePath);
        }
#pragma warning restore CA1031
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
            return ExtractVirtualEntriesCoreAsync(virtualPaths, destinationDirectory, token);
        }, token).ConfigureAwait(false);
    }

    private async Task ExtractVirtualEntriesCoreAsync(
        IReadOnlyList<string> virtualPaths,
        string destinationDirectory,
        CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var virtualPath in virtualPaths)
            {
                if (!ArchivePath.TryParse(virtualPath, out var archiveFile, out var inner))
                    continue;
                if (!grouped.TryGetValue(archiveFile, out var prefixes))
                    grouped[archiveFile] = prefixes = [];
                prefixes.Add(inner.Replace('\\', '/').Trim('/'));
            }

            long totalBytes = 0;
            var entryCount = 0;
            foreach (var (archiveFile, prefixes) in grouped)
            {
                token.ThrowIfCancellationRequested();
                if (!File.Exists(archiveFile))
                    continue;

                using var archive = ArchiveFactory.OpenArchive(new FileInfo(archiveFile));
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.IsDirectory || entry.LinkTarget is not null)
                        continue;
                    EnforceEntryMetadataLimits(entry, ref entryCount, totalBytes);

                    var key = (entry.Key ?? string.Empty).Replace('\\', '/').TrimStart('/').TrimEnd('/');
                    if (string.IsNullOrEmpty(key) || !prefixes.Any(prefix => EntryMatches(key, prefix)))
                        continue;

                    totalBytes += await ExtractEntryToDestinationAsync(entry, key, destinationDirectory, totalBytes, token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArchiveExtractionLimitException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Virtual archive extraction failed");
        }
#pragma warning restore CA1031
    }

    private async Task<long> ExtractEntryToDestinationAsync(
        IArchiveEntry entry,
        string key,
        string destinationDirectory,
        long totalBeforeEntry,
        CancellationToken token)
    {
        var destPath = Path.Combine(
            destinationDirectory,
            key.Replace('/', Path.DirectorySeparatorChar));

        var fullDest = Path.GetFullPath(destPath);
        var fullBase = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullDest.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (File.Exists(destPath))
            destPath = FileOperationPathHelper.EnsureUniqueFilePath(destPath);

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        try
        {
            await using var source = await entry.OpenEntryStreamAsync(token).ConfigureAwait(false);
            await using var destination = new FileStream(
                destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await CopyEntryAsync(source, destination, totalBeforeEntry, token).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteFile(destPath);
            throw;
        }
    }

    private void EnforceEntryMetadataLimits(IArchiveEntry entry, ref int entryCount, long totalBytes)
    {
        if (++entryCount > _limits.MaxEntryCount)
            throw new ArchiveExtractionLimitException(
                $"Archive contains more than {_limits.MaxEntryCount} file entries.");

        if (entry.Size > _limits.MaxEntryUncompressedBytes)
            throw new ArchiveExtractionLimitException(
                $"Archive entry '{entry.Key}' exceeds the per-entry extraction limit.");

        if (entry.Size > 0 && entry.Size > _limits.MaxTotalUncompressedBytes - totalBytes)
            throw new ArchiveExtractionLimitException("Archive exceeds the total uncompressed extraction limit.");
    }

    internal async Task<long> CopyEntryAsync(
        Stream source,
        Stream destination,
        long totalBeforeEntry,
        CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        long entryBytes = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                if (read == 0)
                    return entryBytes;

                if (read > _limits.MaxEntryUncompressedBytes - entryBytes)
                    throw new ArchiveExtractionLimitException("Archive entry exceeds the per-entry extraction limit.");
                if (read > _limits.MaxTotalUncompressedBytes - totalBeforeEntry - entryBytes)
                    throw new ArchiveExtractionLimitException("Archive exceeds the total uncompressed extraction limit.");

                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                entryBytes += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup after cancellation or a malformed entry.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup after cancellation or a malformed entry.
        }
    }

    private static string MakeUniqueTempFileName(string innerKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(innerKey)))[..16];
        var fileName = Path.GetFileName(innerKey);
        if (string.IsNullOrEmpty(fileName))
            fileName = "entry";
        return $"{hash}_{fileName}";
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

    private FileSystemEntry[] Enumerate(
        string archivePath,
        string innerFilter,
        CancellationToken token)
    {
        if (!File.Exists(archivePath))
            return [];

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
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            // Archive readers surface corrupt/encrypted inputs through several exception families.
            // Enumeration is best-effort UI data, so malformed input is an empty listing.
            _logger.LogError(ex, "Archive enumerate failed for '{ArchivePath}'", archivePath);
        }
#pragma warning restore CA1031

        return poolList.ToArray();
    }
}
