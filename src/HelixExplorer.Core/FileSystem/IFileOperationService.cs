using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.FileSystem;

public interface IFileOperationService
{
    ValueTask<FileOperationResult> CopyAsync(
        IReadOnlyList<string> sources,
        string destination,
        IProgress<FileOperationProgress>? progress = null,
        IFileConflictResolver? conflicts = null,
        CancellationToken ct = default);

    ValueTask<FileOperationResult> MoveAsync(
        IReadOnlyList<string> sources,
        string destination,
        IProgress<FileOperationProgress>? progress = null,
        IFileConflictResolver? conflicts = null,
        CancellationToken ct = default);

    ValueTask<FileOperationResult> DeleteAsync(IReadOnlyList<string> paths, bool permanently, CancellationToken ct = default);

    ValueTask<FileOperationResult> RenameAsync(string path, string newName, CancellationToken ct = default);

    ValueTask<string> CreateFolderAsync(string parentPath, string name, CancellationToken ct = default);
}
