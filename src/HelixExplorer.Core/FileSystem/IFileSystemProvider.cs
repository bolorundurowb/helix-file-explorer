using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

/// <summary>Portable filesystem enumeration / path resolution contract.</summary>
public interface IFileSystemProvider
{
    ValueTask<DirectoryListing> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default);

    string ResolvePath(string path);

    bool DirectoryExists(string path);

    bool FileExists(string path);
}
