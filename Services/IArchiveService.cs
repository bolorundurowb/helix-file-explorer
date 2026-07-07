using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>Reads entries out of zip/7z/rar archives as if they were virtual folders.</summary>
public interface IArchiveService
{
    /// <summary>Tests whether <paramref name="path"/> looks like a supported archive on disk.</summary>
    bool IsArchive(string path);

    /// <summary>
    /// Lists the children of <paramref name="virtualPath"/> inside an already-mounted archive.
    /// <paramref name="virtualPath"/> uses the <c>archive://</c> scheme.
    /// </summary>
    ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string virtualPath, CancellationToken token = default);

    /// <summary>Mounts the archive at <paramref name="archivePath"/> and returns its root entries.</summary>
    ValueTask<IReadOnlyList<FileSystemEntry>> MountAsync(string archivePath, CancellationToken token = default);
}