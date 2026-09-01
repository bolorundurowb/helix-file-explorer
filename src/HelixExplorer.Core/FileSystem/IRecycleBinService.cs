using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface IRecycleBinService
{
    ValueTask<bool> RestoreAsync(string itemPath, string? destinationPath = null, CancellationToken ct = default);

    ValueTask EmptyRecycleBinAsync(CancellationToken ct = default);

    ValueTask<(long ItemCount, long TotalSize)> QueryRecycleBinAsync(CancellationToken ct = default);

    bool HasRecycleBinItems();

    event EventHandler? RecycleBinChanged;

    void StartRecycleBinWatcher();

    void StopRecycleBinWatcher();
}
