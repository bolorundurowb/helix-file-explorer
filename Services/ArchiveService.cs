using System.Diagnostics;
using System.IO;
using HelixExplorer.Infrastructure;
using HelixExplorer.Models;
using SharpCompress.Archives;

namespace HelixExplorer.Services;

/// <summary>
/// SharpCompress-backed virtual file system for zip/7z/rar/tar/gzip. Uses
/// <see cref="ArchiveFactory"/> (random-access) rather than the forward-only
/// <c>ReaderFactory</c>, so 7z and solid archives enumerate correctly. Paths use the
/// <c>archive://&lt;path-to-zip&gt;!&lt;inner/&gt;</c> scheme where the host portion is a
/// real archive path on disk and the inner portion is a forward-slash relative path
/// inside the archive.
/// </summary>
public sealed class ArchiveService : IArchiveService
{
    public const string Scheme = "archive://";

    private static readonly string[] s_extensions =
        { "zip", "7z", "rar", "tar", "gz", "bz2", "tgz", "txz", "xz" };

    /// <summary>Detect whether <paramref name="path"/> on disk is an archive file.</summary>
    public bool IsArchive(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        foreach (var e in s_extensions)
        {
            if (ext == e) return true;
        }
        return false;
    }

    /// <summary>Splits "archive://C:\a\b.zip!folder/inner" into ("C:\a\b.zip", "folder/inner").</summary>
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

    public async ValueTask<string?> ExtractEntryAsync(string virtualPath, CancellationToken token = default)
    {
        if (!ParseVirtual(virtualPath, out string archivePath, out string innerPath) || string.IsNullOrEmpty(innerPath))
        {
            return null;
        }
        string wanted = innerPath.Replace('\\', '/').Trim('/');

        return await Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(archivePath)) return null;
                using var archive = ArchiveFactory.Open(new FileInfo(archivePath));
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.IsDirectory) continue;
                    string key = (entry.Key ?? string.Empty).Replace('\\', '/').Trim('/');
                    if (!key.Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;

                    string tempDir = Path.Combine(Path.GetTempPath(), "HelixExplorer",
                        Path.GetFileNameWithoutExtension(archivePath));
                    Directory.CreateDirectory(tempDir);
                    string dest = Path.Combine(tempDir, Path.GetFileName(wanted));

                    await using var src = entry.OpenEntryStream();
                    await using var fs = File.Create(dest);
                    await src.CopyToAsync(fs, token).ConfigureAwait(false);
                    return dest;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"ArchiveService.ExtractEntryAsync failed for '{virtualPath}': {ex.Message}");
            }
            return null;
        }, token).ConfigureAwait(false);
    }

    private static IReadOnlyList<FileSystemEntry> Enumerate(string archivePath, string innerFilter, CancellationToken token)
    {
        if (!File.Exists(archivePath)) return Array.Empty<FileSystemEntry>();

        using var poolList = ArrayPoolList<FileSystemEntry>.Rent(128);
        // Walk every entry and project onto the immediate children of innerFilter — a
        // virtual directory listing without materialising the whole archive tree.
        string normalizedFilter = innerFilter.Replace('\\', '/').Trim('/');
        string filterPrefix = string.IsNullOrEmpty(normalizedFilter) ? string.Empty : normalizedFilter + "/";

        var seenChildren = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = ArchiveFactory.Open(new FileInfo(archivePath));
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                string key = (entry.Key ?? string.Empty).Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrEmpty(key)) continue;

                if (!key.StartsWith(filterPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                string tail = key[filterPrefix.Length..].TrimEnd('/');
                if (string.IsNullOrEmpty(tail)) continue;

                int slash = tail.IndexOf('/');
                if (slash < 0)
                {
                    // Direct child.
                    if (entry.IsDirectory)
                    {
                        if (seenChildren.Add(tail))
                        {
                            poolList.Add(new FileSystemEntry(
                                $"{Scheme}{archivePath}!{filterPrefix}{tail}/",
                                tail, true, 0L,
                                entry.LastModifiedTime ?? DateTime.MinValue, string.Empty));
                        }
                    }
                    else if (seenChildren.Add(tail))
                    {
                        poolList.Add(new FileSystemEntry(
                            $"{Scheme}{archivePath}!{filterPrefix}{tail}",
                            tail, false, entry.Size,
                            entry.LastModifiedTime ?? DateTime.MinValue,
                            Path.GetExtension(tail)));
                    }
                }
                else
                {
                    // Interior path — synthesise the intermediate directory child.
                    string child = tail[..slash];
                    if (seenChildren.Add(child))
                    {
                        poolList.Add(new FileSystemEntry(
                            $"{Scheme}{archivePath}!{filterPrefix}{child}/",
                            child, true, 0L, DateTime.MinValue, string.Empty));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            Debug.WriteLine($"ArchiveService.Enumerate failed for '{archivePath}': {ex.Message}");
        }

        return poolList.ToReadOnlyAndReset();
    }
}
