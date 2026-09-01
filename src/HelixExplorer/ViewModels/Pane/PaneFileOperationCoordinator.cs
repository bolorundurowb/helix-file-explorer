using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.FileSystem.Undo;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Localization;
using HelixExplorer.Services;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Pane;

public sealed class PaneFileOperationCoordinator(
    IFileOperationService fileOps,
    IClipboardService clipboard,
    IOsFileClipboard osClipboard,
    IUserDialogService dialogs,
    IFileOperationReporter operationReporter,
    IFileOperationHistory history,
    ILogger<PaneFileOperationCoordinator> logger)
{
    /// <summary>
    /// Records a completed operation on the undo stack when it left something reversible behind.
    /// </summary>
    /// <remarks>
    /// Merge is excluded outright: it destroys the information needed to tell the merged result apart
    /// from what the destination already held, so no inverse exists. Everything else is pushed on its
    /// recorded successes alone, which is why a partly failed batch still yields a usable undo.
    /// </remarks>
    private void PushHistory(FileOperationResult result, UndoableOperationKind kind, string description)
    {
        if (result.UsedMerge || result.Changes.Count == 0)
            return;

        history.Push(new FileOperationBatch(kind, description, result.Changes)
        {
            ReplacedItemsRecycled = result.ReplacedItemsRecycled
        });
    }

    public async Task<bool> HasPastePayloadAsync(CancellationToken cancellationToken = default)
        => await ResolvePastePayloadAsync(destinationPath: null, cancellationToken).ConfigureAwait(true) is not null;

    public async Task PasteAsync(
        string currentPath,
        IPaneOperationHost host)
    {
        if (string.IsNullOrEmpty(currentPath))
            return;

        try
        {
            var payload = await ResolvePastePayloadAsync(currentPath).ConfigureAwait(true);
            if (payload is null || payload.Paths.Count == 0)
            {
                host.SetOperationStatus(UiStrings.ClipboardHasNoFiles);
                return;
            }

            var kind = payload.Operation == ClipboardOperation.Cut
                ? FileOperationKind.Move
                : FileOperationKind.Copy;
            var title = kind == FileOperationKind.Move ? UiStrings.MovingItems : UiStrings.CopyingItems;
            operationReporter.Begin(kind, payload.Paths.Count, title);

            var progress = new Progress<FileOperationProgress>(p => operationReporter.Report(p));
            var conflicts = FileOperationUiHelper.CreateConflictResolver(dialogs);
            FileOperationResult result;
            if (payload.Operation == ClipboardOperation.Cut)
            {
                result = await fileOps.MoveAsync(
                    payload.Paths,
                    currentPath,
                    progress,
                    conflicts,
                    operationReporter.CancellationToken,
                    operationReporter).ConfigureAwait(true);
                if (result.Succeeded > 0)
                    clipboard.Clear();
            }
            else
            {
                result = await fileOps.CopyAsync(
                    payload.Paths,
                    currentPath,
                    progress,
                    conflicts,
                    operationReporter.CancellationToken,
                    operationReporter).ConfigureAwait(true);
            }

            PushHistory(
                result,
                kind == FileOperationKind.Move ? UndoableOperationKind.Move : UndoableOperationKind.Copy,
                kind == FileOperationKind.Move
                    ? UiStrings.UndoMoveDescription(result.Changes.Count)
                    : UiStrings.UndoCopyDescription(result.Changes.Count));

            await host.RefreshAfterOperationAsync().ConfigureAwait(true);
            operationReporter.Complete(
                kind,
                result.Succeeded,
                result.Succeeded > 0
                    ? (kind == FileOperationKind.Move
                        ? UiStrings.MovedItems(result.Succeeded)
                        : UiStrings.CopiedItems(result.Succeeded))
                    : UiStrings.NoItemsCopied);

            await FileOperationUiHelper.ReportResultAsync(
                dialogs,
                result,
                kind == FileOperationKind.Move ? "Move" : "Copy",
                host.SetOperationStatus).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                host.SetOperationStatus(UiStrings.OperationCancelled);
                operationReporter.Cancelled(UiStrings.OperationCancelled);
                return;
            }

            logger.LogError(ex, "Paste failed");
            await dialogs.ShowErrorAsync(UiStrings.PasteFailed, ex.Message).ConfigureAwait(true);
            host.SetOperationStatus(UiStrings.PasteFailed);
            operationReporter.Fail(UiStrings.PasteFailed);
        }
    }

    public async Task HandleDropAsync(
        string destinationPath,
        IReadOnlyList<string> paths,
        bool isCopy,
        IPaneOperationHost host)
    {
        if (string.IsNullOrEmpty(destinationPath) || paths.Count == 0)
            return;

        var filtered = GetDroppablePaths(destinationPath, paths, isCopy);
        if (filtered.Count == 0)
            return;

        try
        {
            var kind = isCopy ? FileOperationKind.Copy : FileOperationKind.Move;
            operationReporter.Begin(
                kind,
                filtered.Count,
                isCopy ? UiStrings.CopyingItems : UiStrings.MovingItems);

            var progress = new Progress<FileOperationProgress>(p => operationReporter.Report(p));
            var conflicts = FileOperationUiHelper.CreateConflictResolver(dialogs);
            FileOperationResult result;
            if (isCopy)
                result = await fileOps.CopyAsync(
                    filtered,
                    destinationPath,
                    progress,
                    conflicts,
                    operationReporter.CancellationToken,
                    operationReporter).ConfigureAwait(true);
            else
                result = await fileOps.MoveAsync(
                    filtered,
                    destinationPath,
                    progress,
                    conflicts,
                    operationReporter.CancellationToken,
                    operationReporter).ConfigureAwait(true);

            PushHistory(
                result,
                isCopy ? UndoableOperationKind.Copy : UndoableOperationKind.Move,
                isCopy
                    ? UiStrings.UndoCopyDescription(result.Changes.Count)
                    : UiStrings.UndoMoveDescription(result.Changes.Count));

            await host.RefreshAfterOperationAsync().ConfigureAwait(true);
            operationReporter.Complete(
                kind,
                result.Succeeded,
                isCopy ? UiStrings.CopiedItems(result.Succeeded) : UiStrings.MovedItems(result.Succeeded));

            await FileOperationUiHelper.ReportResultAsync(
                dialogs,
                result,
                isCopy ? "Copy" : "Move",
                host.SetOperationStatus).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                host.SetOperationStatus(UiStrings.OperationCancelled);
                operationReporter.Cancelled(UiStrings.OperationCancelled);
                return;
            }

            logger.LogError(ex, "Drop failed");
            await dialogs.ShowErrorAsync(UiStrings.DropFailed, ex.Message).ConfigureAwait(true);
            host.SetOperationStatus(UiStrings.DropFailed);
            operationReporter.Fail(UiStrings.DropFailed);
        }
    }

    public async Task DeleteAsync(
        IReadOnlyList<string> paths,
        bool permanently,
        IPaneOperationHost host)
    {
        if (paths.Count == 0)
            return;

        try
        {
            var kind = FileOperationKind.Delete;
            var title = permanently ? UiStrings.PermanentlyDeleteTitle : UiStrings.DeletingItems;
            operationReporter.Begin(kind, paths.Count, title);

            var progress = new Progress<FileOperationProgress>(p => operationReporter.Report(p));
            var result = await fileOps.DeleteAsync(
                paths,
                permanently,
                progress,
                operationReporter.CancellationToken,
                operationReporter).ConfigureAwait(true);

            // A permanent delete has no inverse, so only recycle deletes reach the history. Changes
            // whose bin entry could not be located are dropped: restoring needs the $R path.
            if (!permanently)
            {
                var restorable = result.Changes.Where(c => c.RecycleItemPath is not null).ToList();
                PushHistory(
                    result with { Changes = restorable },
                    UndoableOperationKind.RecycleDelete,
                    UiStrings.UndoDeleteDescription(restorable.Count));
            }

            await host.RefreshAfterOperationAsync().ConfigureAwait(true);
            operationReporter.Complete(
                kind,
                result.Succeeded,
                result.Succeeded > 0
                    ? UiStrings.DeletedItems(result.Succeeded)
                    : UiStrings.NoItemsDeleted);

            await FileOperationUiHelper.ReportResultAsync(
                dialogs,
                result,
                "Delete",
                host.SetOperationStatus).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                host.SetOperationStatus(UiStrings.OperationCancelled);
                operationReporter.Cancelled(UiStrings.OperationCancelled);
                return;
            }

            logger.LogError(ex, "Delete failed");
            await dialogs.ShowErrorAsync(UiStrings.DeleteFailed, ex.Message).ConfigureAwait(true);
            host.SetOperationStatus(UiStrings.DeleteFailed);
            operationReporter.Fail(UiStrings.DeleteFailed);
        }
    }

    public async Task PublishToOsClipboardAsync(IReadOnlyList<string> paths, ClipboardOperation operation)
    {
        try
        {
            await osClipboard.SetFilesAsync(paths, operation).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OS clipboard publish failed");
        }
    }

    public static string? GetPhysicalHostDirectory(string currentPath, bool isArchive)
    {
        if (ArchivePath.IsVirtual(currentPath)
            && ArchivePath.TryParse(currentPath, out var archiveFile, out _))
        {
            return Path.GetDirectoryName(archiveFile);
        }

        if (!isArchive && !string.IsNullOrEmpty(currentPath))
            return currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return null;
    }

    public static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; i < 100; i++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{fileName} ({Guid.NewGuid():N}){extension}");
    }

    public static string GetUniqueDirectory(string path)
    {
        if (!Directory.Exists(path))
            return path;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate))
                return candidate;
        }

        return $"{path} ({Guid.NewGuid():N})";
    }

    public static bool IsSameOrChildPath(string directory, string path)
        => PathUtilities.IsSameOrChildPath(directory, path);

    public static IReadOnlyList<string> GetDroppablePaths(
        string destinationPath,
        IReadOnlyList<string> paths,
        bool isCopy)
        => paths
            .Where(path => CanDropPath(destinationPath, path, isCopy))
            .ToList();

    public static bool CanDropPath(string destinationPath, string sourcePath, bool isCopy)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || string.IsNullOrWhiteSpace(sourcePath))
            return false;

        if (PathUtilities.PathsEqual(destinationPath, sourcePath))
            return false;

        if (PathUtilities.IsSameOrChildPath(sourcePath, destinationPath))
            return false;

        if (!isCopy
            && GetParentDirectory(sourcePath) is { } parent
            && PathUtilities.PathsEqual(parent, destinationPath))
        {
            return false;
        }

        return true;
    }

    private static string? GetParentDirectory(string path)
    {
        try
        {
            var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrEmpty(normalized) ? null : Path.GetDirectoryName(normalized);
        }
        catch
        {
            return null;
        }
    }

    public async Task RenameAsync(
        string oldPath,
        string newName,
        Func<Task> refreshAsync,
        Action onClearRename,
        IPaneOperationHost host)
    {
        try
        {
            var result = await fileOps.RenameAsync(oldPath, newName).ConfigureAwait(true);
            if (result.Failed > 0)
            {
                await dialogs.ShowErrorAsync(UiStrings.RenameFailed, result.Failures[0].Message).ConfigureAwait(true);
                host.SetOperationStatus(UiStrings.RenameFailed);
                onClearRename();
                return;
            }

            PushHistory(result, UndoableOperationKind.Rename, UiStrings.UndoRenameDescription(newName));

            onClearRename();
            await refreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Rename failed for '{Path}'", oldPath);
            host.SetOperationStatus(UiStrings.RenameFailed);
            onClearRename();
        }
    }

    public async Task CreateFolderAsync(
        string currentPath,
        IPaneOperationHost host)
    {
        try
        {
            // The service uniquifies the name, so the folder that actually appeared may be
            // "New Folder (2)". Redo has to recreate that exact path rather than uniquifying again and
            // landing on a third name.
            var createdPath = await fileOps.CreateFolderAsync(currentPath, UiStrings.NewFolderDefaultName)
                .ConfigureAwait(true);

            history.Push(new FileOperationBatch(
                UndoableOperationKind.CreateFolder,
                UiStrings.UndoNewFolderDescription,
                [new FileOperationChange(currentPath, createdPath)]));

            await host.RefreshAfterOperationAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "NewFolder failed in '{Path}'", currentPath);
            host.SetOperationStatus(UiStrings.NewFolderFailed);
        }
    }

    private async Task<ClipboardPayload?> ResolvePastePayloadAsync(
        string? destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (clipboard.Current is { } internalPayload
            && await HasValidLocalPathsAsync(internalPayload.Paths, cancellationToken).ConfigureAwait(true))
        {
            return destinationPath is null
                ? internalPayload
                : new ClipboardPayload(internalPayload.Operation, internalPayload.Paths, destinationPath);
        }

        var os = await osClipboard.TryGetFilesAsync(cancellationToken).ConfigureAwait(true);
        if (os is null
            || !await HasValidLocalPathsAsync(os.Value.Paths, cancellationToken).ConfigureAwait(true))
            return null;

        return new ClipboardPayload(os.Value.Operation, os.Value.Paths, destinationPath ?? string.Empty);
    }

    private static Task<bool> HasValidLocalPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
        => Task.Run(() => HasValidLocalPaths(paths, cancellationToken), cancellationToken);

    private static bool HasValidLocalPaths(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
            return false;

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return false;
        }

        return true;
    }
}
