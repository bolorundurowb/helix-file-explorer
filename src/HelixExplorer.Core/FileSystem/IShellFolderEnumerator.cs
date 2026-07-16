using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface IShellFolderEnumerator
{
    ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="destinationPath"/> can be omitted when the implementation can derive it
    /// from shell item metadata.
    /// </summary>
    ValueTask RestoreAsync(string itemPath, string? destinationPath = null, CancellationToken ct = default);

    ValueTask EmptyRecycleBinAsync(CancellationToken ct = default);

    ValueTask<(long ItemCount, long TotalSize)> QueryRecycleBinAsync(CancellationToken ct = default);

    /// <summary>
    /// Fast filesystem-based check; does not enumerate the shell namespace.
    /// </summary>
    bool HasRecycleBinItems();

    event EventHandler? RecycleBinChanged;

    void StartRecycleBinWatcher();

    void StopRecycleBinWatcher();
}
