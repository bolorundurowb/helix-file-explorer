using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>Low-allocation directory enumeration service.</summary>
public interface IFileSystemService
{
    /// <summary>Enumerates the children of <paramref name="path"/> off the thread pool.</summary>
    ValueTask<IReadOnlyList<FileSystemEntry>> GetDirectoryContentsAsync(string path, CancellationToken token = default);

    /// <summary>Resolves relative segments and symlinks to an absolute, canonical path.</summary>
    string ResolvePath(string path);

    /// <summary>Quick existence check that avoids allocating a FileInfo.</summary>
    bool DirectoryExists(string path);
}