using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface IShellFolderEnumerator
{
    ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default);

    ValueTask RestoreAsync(string itemPath, CancellationToken ct = default);

    ValueTask EmptyRecycleBinAsync(CancellationToken ct = default);
}
