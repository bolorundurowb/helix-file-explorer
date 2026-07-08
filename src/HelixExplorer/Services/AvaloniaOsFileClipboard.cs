using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Services;

/// <summary>
/// Avalonia 12 OS clipboard for file paths. Keeps an in-process operation tag so Helix cut
/// vs copy is preserved; external Explorer payloads default to copy.
/// </summary>
public sealed class AvaloniaOsFileClipboard : IOsFileClipboard
{
    private ClipboardOperation? _lastPublishedOperation;

    public async Task SetFilesAsync(IReadOnlyList<string> paths, ClipboardOperation operation, CancellationToken ct = default)
    {
        var topLevel = GetTopLevel();
        var clipboard = topLevel?.Clipboard;
        var storage = topLevel?.StorageProvider;
        if (clipboard is null || storage is null || paths.Count == 0)
            return;

        var transfer = new DataTransfer();
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var item = await TryCreateStorageItemAsync(storage, path).ConfigureAwait(true);
            if (item is not null)
                transfer.Add(DataTransferItem.CreateFile(item));
        }

        await clipboard.SetDataAsync(transfer).ConfigureAwait(true);
        _lastPublishedOperation = operation;
    }

    public async Task<(IReadOnlyList<string> Paths, ClipboardOperation Operation)?> TryGetFilesAsync(CancellationToken ct = default)
    {
        var clipboard = GetTopLevel()?.Clipboard;
        if (clipboard is null)
            return null;

        using var data = await clipboard.TryGetDataAsync().ConfigureAwait(true);
        if (data is null || !data.Contains(DataFormat.File))
            return null;

        var files = await data.TryGetFilesAsync().ConfigureAwait(true);
        if (files is null || files.Length == 0)
            return null;

        var paths = new List<string>(files.Length);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var local = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(local))
                paths.Add(local);
        }

        if (paths.Count == 0)
            return null;

        var operation = _lastPublishedOperation ?? ClipboardOperation.Copy;
        return (paths, operation);
    }

    private static async Task<IStorageItem?> TryCreateStorageItemAsync(IStorageProvider storage, string path)
    {
        if (Directory.Exists(path))
            return await storage.TryGetFolderFromPathAsync(path).ConfigureAwait(true);

        if (File.Exists(path))
            return await storage.TryGetFileFromPathAsync(path).ConfigureAwait(true);

        return null;
    }

    private static Avalonia.Controls.TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;

        return null;
    }
}
