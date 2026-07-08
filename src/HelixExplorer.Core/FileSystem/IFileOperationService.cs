namespace HelixExplorer.Core.FileSystem;

public interface IFileOperationService
{
    ValueTask CopyAsync(IReadOnlyList<string> sources, string destination, CancellationToken ct = default);

    ValueTask MoveAsync(IReadOnlyList<string> sources, string destination, CancellationToken ct = default);

    ValueTask DeleteAsync(IReadOnlyList<string> paths, bool permanently, CancellationToken ct = default);

    ValueTask RenameAsync(string path, string newName, CancellationToken ct = default);

    ValueTask<string> CreateFolderAsync(string parentPath, string name, CancellationToken ct = default);
}
