using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Windows.Shell;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Shell <see cref="IFileOperation"/> via Vanara — recycle-bin, elevation, progress UI, and shell items.
/// </summary>
internal static class ShellFileOperationsHelper
{
    /// <summary>
    /// Sends <paramref name="paths"/> to the recycle bin.
    /// </summary>
    /// <returns>
    /// Whether the operation ran to completion, and the source paths the shell confirmed it deleted.
    /// The caller needs the actual paths, not just a count: the shell may fail any item in the batch,
    /// and undo has to know which items really produced a bin entry.
    /// </returns>
    public static Task<(bool Success, IReadOnlyList<string> DeletedPaths)> DeleteToRecycleBinAsync(
        IReadOnlyList<string> paths,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return STATask.Run(() => DeleteToRecycleBin(paths, progress, cancellationToken), cancellationToken);
    }

    public static Task<bool> RestoreFromRecycleBinAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        return STATask.Run(() => RestoreFromRecycleBin(sourcePath, destinationPath, cancellationToken), cancellationToken);
    }

    public static Task<bool> CanMoveToRecycleBinAsync(string path, CancellationToken cancellationToken)
    {
        return STATask.Run(() => CanMoveToRecycleBin(path, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Sends a single item the caller is about to overwrite to the recycle bin, if the shell will
    /// take it.
    /// </summary>
    /// <returns>True when the item is now in the bin and the caller may proceed with the overwrite.</returns>
    /// <remarks>
    /// Blocking rather than async because the conflict resolution it serves runs synchronously deep
    /// inside a recursive copy walk. That walk is already on a <see cref="Task.Run"/> worker with no
    /// synchronization context, and the shell calls each hop onto their own STA thread, so the wait
    /// cannot deadlock. Failure is not exceptional: plenty of items (network paths, oversized files,
    /// a disabled bin) simply cannot be recycled, and the caller falls back to a hard delete.
    /// </remarks>
    public static bool TryRecycleDisplacedItem(string path, CancellationToken cancellationToken)
    {
        try
        {
            // The delete itself is the authoritative recyclability probe. Asking SHQueryRecycleBin
            // first doubled STA/COM traffic for every conflict and still could not guarantee success.
            var (success, deleted) = DeleteToRecycleBinAsync([path], progress: null, cancellationToken)
                .GetAwaiter().GetResult();

            return success && deleted.Count > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static (bool Success, IReadOnlyList<string> DeletedPaths) DeleteToRecycleBin(
        IReadOnlyList<string> paths,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var op = CreateOperation();
        op.Options |= ShellFileOperations.OperationFlags.RecycleOnDelete;

        var total = paths.Count;
        var completed = 0;
        var deleted = new List<string>(total);

        op.PostDeleteItem += (s, e) =>
        {
            completed++;
            var parsingName = e.SourceItem?.ParsingName;

            if (e.Result.Succeeded && !string.IsNullOrEmpty(parsingName))
                deleted.Add(parsingName);

            progress?.Report(new FileOperationProgress(completed, total, parsingName, FileOperationKind.Delete));
        };

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var item = new ShellItem(path);
            op.QueueDeleteOperation(item);
        }

        op.PerformOperations();
        return (!op.AnyOperationsAborted, deleted);
    }

    private static bool RestoreFromRecycleBin(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var op = CreateOperation();
        op.Options |= ShellFileOperations.OperationFlags.NoConfirmMkDir;

        var destDir = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(destDir))
            return false;

        using var sourceItem = new ShellItem(sourcePath);
        using var destFolder = new ShellFolder(destDir);
        var destName = Path.GetFileName(destinationPath);

        op.QueueMoveOperation(sourceItem, destFolder, destName);
        op.PerformOperations();

        return !op.AnyOperationsAborted;
    }

    private static bool CanMoveToRecycleBin(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || NetworkPath.IsUnc(path))
            return false;

        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                return false;
        }
        catch (Exception)
        {
            return false;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            return false;

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType is DriveType.Network or DriveType.NoRootDirectory)
                return false;
        }
        catch (Exception)
        {
            return false;
        }

        var info = new Shell32.SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<Shell32.SHQUERYRBINFO>() };
        var hr = Shell32.SHQueryRecycleBin(root, ref info);
        return hr.Succeeded;
    }

    private static ShellFileOperations CreateOperation()
    {
        var op = new ShellFileOperations(HWND.NULL)
        {
            Options =
                ShellFileOperations.OperationFlags.Silent |
                ShellFileOperations.OperationFlags.NoConfirmation |
                ShellFileOperations.OperationFlags.NoErrorUI
        };
        return op;
    }
}
