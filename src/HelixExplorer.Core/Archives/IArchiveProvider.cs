using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Archives;

public interface IArchiveProvider
{
    bool IsArchiveFile(string path);

    ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string virtualPath, CancellationToken token = default);

    ValueTask<string?> ExtractEntryAsync(string virtualPath, CancellationToken token = default);

    ValueTask CreateZipAsync(IReadOnlyList<string> sourcePaths, string destinationZipPath, CancellationToken token = default);

    ValueTask ExtractArchiveToDirectoryAsync(string archivePath, string destinationDirectory, CancellationToken token = default);

    ValueTask ExtractVirtualEntriesAsync(IReadOnlyList<string> virtualPaths, string destinationDirectory, CancellationToken token = default);

    /// <summary>
    /// Deletes any temporary files/directories the provider created while extracting archive
    /// entries for preview/open. Safe to call on application shutdown; best-effort and never throws.
    /// </summary>
    void CleanupExtractedFiles();
}
