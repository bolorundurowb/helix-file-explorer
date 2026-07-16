using Avalonia.Input;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Services;

/// <summary>
/// Kept behind an interface so <c>PaneView</c> does not embed platform drag details and a richer
/// shell payload can be swapped in later.
/// </summary>
public interface IExternalFileDragPayloadBuilder
{
    /// <summary>
    /// Paths must already be physical (caller resolves archives) so this stays fast and does no I/O
    /// beyond StorageProvider lookups.
    /// </summary>
    Task<DataTransfer?> BuildAsync(
        IStorageProvider storage,
        IReadOnlyList<string> physicalPaths,
        CancellationToken ct = default);
}

/// <summary>
/// Avalonia maps <see cref="DataFormat.File"/> to <c>CF_HDROP</c> on Windows; browser upload fields
/// are the fragile case — see docs/external-drag-verification.md.
/// </summary>
public sealed class AvaloniaExternalFileDragPayloadBuilder(ILogger<AvaloniaExternalFileDragPayloadBuilder> logger)
    : IExternalFileDragPayloadBuilder
{
    private const int DropEffectCopy = 1;

    private static readonly DataFormat<byte[]> PreferredDropEffectFormat =
        DataFormat.CreateBytesPlatformFormat("Preferred DropEffect");

    private static readonly DataFormat<string> FileNameWFormat =
        DataFormat.CreateStringPlatformFormat("FileNameW");

    private static readonly byte[] PreferredCopyDropEffectBytes = BitConverter.GetBytes(DropEffectCopy);

    public async Task<DataTransfer?> BuildAsync(
        IStorageProvider storage,
        IReadOnlyList<string> physicalPaths,
        CancellationToken ct = default)
    {
        if (physicalPaths.Count == 0)
            return null;

        var transfer = new DataTransfer();
        var added = 0;

        foreach (var path in physicalPaths)
        {
            ct.ThrowIfCancellationRequested();

            IStorageItem? item = null;
            try
            {
                if (Directory.Exists(path))
                    item = await storage.TryGetFolderFromPathAsync(path).ConfigureAwait(true);
                else if (File.Exists(path))
                    item = await storage.TryGetFileFromPathAsync(path).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve storage item for drag payload: {Path}", path);
                continue;
            }

            if (item is null)
            {
                logger.LogDebug("Drag payload skipped missing/unresolvable path: {Path}", path);
                continue;
            }

            var dragItem = DataTransferItem.CreateFile(item);
            dragItem.Set(PreferredDropEffectFormat, PreferredCopyDropEffectBytes);

            // Several Windows targets still probe the legacy single-file format before CF_HDROP.
            if (physicalPaths.Count == 1)
                dragItem.Set(FileNameWFormat, path);

            transfer.Add(dragItem);
            added++;
        }

        return added == 0 ? null : transfer;
    }
}
