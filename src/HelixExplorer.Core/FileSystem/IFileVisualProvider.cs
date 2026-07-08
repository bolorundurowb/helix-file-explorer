namespace HelixExplorer.Core.FileSystem;

public interface IFileVisualProvider
{
    ValueTask<FileVisualData?> GetAsync(FileVisualRequest request, CancellationToken cancellationToken = default);
}
