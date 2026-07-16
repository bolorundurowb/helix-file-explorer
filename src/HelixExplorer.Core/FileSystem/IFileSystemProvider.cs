using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface IFileSystemProvider
{
    ValueTask<DirectoryListing> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default);

    ValueTask<SearchResult> SearchRecursiveAsync(
        string path,
        string query,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    string ResolvePath(string path);

    bool DirectoryExists(string path);

    bool FileExists(string path);
}
