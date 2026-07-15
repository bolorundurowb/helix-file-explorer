using Avalonia.Input;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Services;

/// <summary>
/// Builds the drag payload used when the user drags entries out of Helix to an external target
/// (Explorer, a browser upload field, another app). Kept behind an interface so the view no longer
/// embeds platform drag details and so a richer, shell-compatible payload can be swapped in later
/// without touching <c>PaneView</c>.
/// </summary>
public interface IExternalFileDragPayloadBuilder
{
    /// <summary>
    /// Materializes an Avalonia <see cref="DataTransfer"/> for the supplied already-resolved physical
    /// paths, or <c>null</c> when nothing usable could be added. Paths must already be physical
    /// (archive/virtual entries resolved by the caller) so this method stays fast and does no I/O
    /// beyond StorageProvider lookups.
    /// </summary>
    Task<DataTransfer?> BuildAsync(
        IStorageProvider storage,
        IReadOnlyList<string> physicalPaths,
        CancellationToken ct = default);
}

/// <summary>
/// Default builder using Avalonia's file transfer items. On Windows Avalonia maps
/// <see cref="DataFormat.File"/> to the shell <c>CF_HDROP</c> format, which most drop targets accept.
/// Browser upload fields are the fragile case; see docs/external-drag-verification.md for the manual
/// checklist and the follow-up plan for a full shell <c>IDataObject</c> if a target rejects this payload.
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
