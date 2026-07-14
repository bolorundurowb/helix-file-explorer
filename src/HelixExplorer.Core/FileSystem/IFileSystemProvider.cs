using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

/// <summary>Portable filesystem enumeration / path resolution contract.</summary>
public interface IFileSystemProvider
{
    ValueTask<DirectoryListing> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<FileSystemEntry>> SearchRecursiveAsync(string path, string query, CancellationToken cancellationToken = default);

    string ResolvePath(string path);

    bool DirectoryExists(string path);

    bool FileExists(string path);
}
