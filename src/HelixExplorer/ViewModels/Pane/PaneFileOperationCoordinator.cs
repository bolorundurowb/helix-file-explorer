using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
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
    ILogger<PaneFileOperationCoordinator> logger)
{
    public async Task<bool> HasPastePayloadAsync(CancellationToken cancellationToken = default)
        => await ResolvePastePayloadAsync(destinationPath: null, cancellationToken).ConfigureAwait(true) is not null;

    public async Task PasteAsync(
        string currentPath,
        Func<Task> refreshAsync,
        Action<string> setStatusText)
    {
        if (string.IsNullOrEmpty(currentPath))
            return;

        try
        {
            var payload = await ResolvePastePayloadAsync(currentPath).ConfigureAwait(true);
            if (payload is null || payload.Paths.Count == 0)
            {
                setStatusText(UiStrings.ClipboardHasNoFiles);
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

            await refreshAsync().ConfigureAwait(true);
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
                setStatusText).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                setStatusText(UiStrings.OperationCancelled);
                operationReporter.Cancelled(UiStrings.OperationCancelled);
                return;
            }

            logger.LogError(ex, "Paste failed");
            await dialogs.ShowErrorAsync(UiStrings.PasteFailed, ex.Message).ConfigureAwait(true);
            setStatusText(UiStrings.PasteFailed);
            operationReporter.Fail(UiStrings.PasteFailed);
        }
    }

    public async Task HandleDropAsync(
        string destinationPath,
        IReadOnlyList<string> paths,
        bool isCopy,
        Func<Task> refreshAsync,
        Action<string> setStatusText)
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

            await refreshAsync().ConfigureAwait(true);
            operationReporter.Complete(
                kind,
                result.Succeeded,
                isCopy ? UiStrings.CopiedItems(result.Succeeded) : UiStrings.MovedItems(result.Succeeded));

            await FileOperationUiHelper.ReportResultAsync(
                dialogs,
                result,
                isCopy ? "Copy" : "Move",
                setStatusText).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                setStatusText(UiStrings.OperationCancelled);
                operationReporter.Cancelled(UiStrings.OperationCancelled);
                return;
            }

            logger.LogError(ex, "Drop failed");
            await dialogs.ShowErrorAsync(UiStrings.DropFailed, ex.Message).ConfigureAwait(true);
            setStatusText(UiStrings.DropFailed);
            operationReporter.Fail(UiStrings.DropFailed);
        }
    }

    public async Task DeleteAsync(
        IReadOnlyList<string> paths,
        bool permanently,
        Func<Task> refreshAsync,
        Action<string> setStatusText)
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

            await refreshAsync().ConfigureAwait(true);
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
                setStatusText).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                setStatusText(UiStrings.OperationCancelled);
                operationReporter.Cancelled(UiStrings.OperationCancelled);
                return;
            }

            logger.LogError(ex, "Delete failed");
            await dialogs.ShowErrorAsync(UiStrings.DeleteFailed, ex.Message).ConfigureAwait(true);
            setStatusText(UiStrings.DeleteFailed);
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
        Action<string> setStatusText)
    {
        try
        {
            var result = await fileOps.RenameAsync(oldPath, newName).ConfigureAwait(true);
            if (result.Failed > 0)
            {
                await dialogs.ShowErrorAsync(UiStrings.RenameFailed, result.Failures[0].Message).ConfigureAwait(true);
                setStatusText(UiStrings.RenameFailed);
                onClearRename();
                return;
            }

            onClearRename();
            await refreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Rename failed for '{Path}'", oldPath);
            setStatusText(UiStrings.RenameFailed);
            onClearRename();
        }
    }

    public async Task CreateFolderAsync(
        string currentPath,
        Func<Task> refreshAsync,
        Action<string> setStatusText)
    {
        try
        {
            await fileOps.CreateFolderAsync(currentPath, "New Folder").ConfigureAwait(true);
            await refreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "NewFolder failed in '{Path}'", currentPath);
            setStatusText(UiStrings.NewFolderFailed);
        }
    }

    private async Task<ClipboardPayload?> ResolvePastePayloadAsync(
        string? destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (clipboard.Current is { } internalPayload && HasValidLocalPaths(internalPayload.Paths))
            return destinationPath is null
                ? internalPayload
                : new ClipboardPayload(internalPayload.Operation, internalPayload.Paths, destinationPath);

        var os = await osClipboard.TryGetFilesAsync(cancellationToken).ConfigureAwait(true);
        if (os is null || !HasValidLocalPaths(os.Value.Paths))
            return null;

        return new ClipboardPayload(os.Value.Operation, os.Value.Paths, destinationPath ?? string.Empty);
    }

    private static bool HasValidLocalPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return false;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return false;
        }

        return true;
    }
}
