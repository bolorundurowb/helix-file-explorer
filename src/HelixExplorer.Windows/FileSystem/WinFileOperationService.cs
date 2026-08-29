using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

public sealed class WinFileOperationService(ILogger<WinFileOperationService> logger) : IFileOperationService
{
    public async ValueTask<FileOperationResult> CopyAsync(
        IReadOnlyList<string> sources,
        string destination,
        IProgress<FileOperationProgress>? progress = null,
        IFileConflictResolver? conflicts = null,
        CancellationToken ct = default,
        IFileOperationControl? control = null)
    {
        return await Task.Run(() => ProcessSources(
            sources, destination, FileOperationKind.Copy, progress, ct, control, conflicts,
            (s, d, t, r, c, o) => CopyOne(s, d, t, r, c, o)), ct).ConfigureAwait(false);
    }

    public async ValueTask<FileOperationResult> MoveAsync(
        IReadOnlyList<string> sources,
        string destination,
        IProgress<FileOperationProgress>? progress = null,
        IFileConflictResolver? conflicts = null,
        CancellationToken ct = default,
        IFileOperationControl? control = null)
    {
        return await Task.Run(() => ProcessSources(
            sources, destination, FileOperationKind.Move, progress, ct, control, conflicts,
            (s, d, t, r, c, o) => MoveOne(s, d, t, r, c, o)), ct).ConfigureAwait(false);
    }

    public async ValueTask<FileOperationResult> DeleteAsync(
        IReadOnlyList<string> paths,
        bool permanently,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default,
        IFileOperationControl? control = null)
    {
        var total = paths.Count;
        if (total == 0)
            return new FileOperationResult(0, 0, 0, Array.Empty<FileOperationFailure>());

        if (!permanently)
        {
            control?.WaitIfPaused(ct);
            var (success, count) = await ShellFileOperationsHelper.DeleteToRecycleBinAsync(paths, progress, ct).ConfigureAwait(false);
            var recycleFailures = new List<FileOperationFailure>();
            for (var i = count; i < total; i++)
                recycleFailures.Add(new FileOperationFailure(paths[i], "Recycle bin operation failed."));
            return new FileOperationResult(count, 0, recycleFailures.Count, recycleFailures);
        }

        var succeeded = 0;
        var failures = new List<FileOperationFailure>();

        await Task.Run(() =>
        {
            for (var i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                control?.WaitIfPaused(ct);
                var path = paths[i];
                progress?.Report(new FileOperationProgress(i, total, path, FileOperationKind.Delete));

                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    else if (Directory.Exists(path))
                        Directory.Delete(path, recursive: true);
                    else
                        throw new FileNotFoundException($"The item '{path}' no longer exists.", path);

                    succeeded++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Delete failed for '{Path}'", path);
                    failures.Add(new FileOperationFailure(path, ex.Message));
                }

                progress?.Report(new FileOperationProgress(i + 1, total, path, FileOperationKind.Delete));
            }
        }, ct).ConfigureAwait(false);

        return new FileOperationResult(succeeded, 0, failures.Count, failures);
    }

    public async ValueTask<FileOperationResult> RenameAsync(string path, string newName, CancellationToken ct = default)
    {
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var parent = Path.GetDirectoryName(path) ?? string.Empty;
                var newPath = Path.Combine(parent, newName);

                if (File.Exists(path))
                    File.Move(path, newPath);
                else if (Directory.Exists(path))
                    Directory.Move(path, newPath);
                else
                    throw new FileNotFoundException("Path not found.", path);
            }, ct).ConfigureAwait(false);

            return new FileOperationResult(1, 0, 0, Array.Empty<FileOperationFailure>());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rename failed for '{Path}'", path);
            return new FileOperationResult(0, 0, 1, [new FileOperationFailure(path, ex.Message)]);
        }
    }

    public async ValueTask<string> CreateFolderAsync(string parentPath, string name, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(parentPath, name);
            fullPath = FileOperationPathHelper.EnsureUniqueDirectoryPath(fullPath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> CanMoveToRecycleBinAsync(string path, CancellationToken ct = default)
    {
        return await ShellFileOperationsHelper.CanMoveToRecycleBinAsync(path, ct).ConfigureAwait(false);
    }

    private FileOperationResult ProcessSources(
        IReadOnlyList<string> sources,
        string destination,
        FileOperationKind kind,
        IProgress<FileOperationProgress>? progress,
        CancellationToken ct,
        IFileOperationControl? control,
        IFileConflictResolver? conflicts,
        Action<string, string, CancellationToken, FileOperationRunState, IFileConflictResolver?, IFileOperationControl?> operation)
    {
        var total = sources.Count;
        var succeeded = 0;
        var skipped = 0;
        var failures = new List<FileOperationFailure>();

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            control?.WaitIfPaused(ct);
            var source = sources[i];
            progress?.Report(new FileOperationProgress(i, total, source, kind));

            var state = new FileOperationRunState();
            try
            {
                operation(source, destination, ct, state, conflicts, control);
                if (state.WasCancelled)
                    break;

                if (state.WasSkipped)
                    skipped++;
                else
                    succeeded++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Kind} failed for '{Source}'", kind, source);
                failures.Add(new FileOperationFailure(source, FileSystemError.DescribeFileOperation(ex)));
            }

            progress?.Report(new FileOperationProgress(i + 1, total, source, kind));
        }

        return new FileOperationResult(succeeded, skipped, failures.Count, failures);
    }

    private sealed class FileOperationRunState
    {
        public bool WasSkipped { get; set; }
        public bool WasCancelled { get; set; }
    }

    private static void CopyOne(
        string source,
        string destination,
        CancellationToken ct,
        FileOperationRunState state,
        IFileConflictResolver? conflicts,
        IFileOperationControl? control)
    {
        var destPath = Path.Combine(destination, Path.GetFileName(source));

        // Copying onto the same path is a no-op; Replace must not delete the source.
        if (PathUtilities.PathsEqual(source, destPath))
            return;

        if (File.Exists(source))
        {
            if (File.Exists(destPath) && !TryResolveFileConflict(source, destPath, isDirectory: false, conflicts, state, out destPath))
                return;

            File.Copy(source, destPath, overwrite: false);
        }
        else if (Directory.Exists(source))
        {
            // Dest under source would recurse into the newly created tree forever.
            if (PathUtilities.IsSameOrChildPath(source, destPath))
                throw new InvalidOperationException("Cannot copy a folder into itself or one of its subfolders.");

            if (Directory.Exists(destPath) && !TryResolveDirectoryConflict(source, destPath, ct, conflicts, control, state, out destPath, isMove: false))
                return;

            CopyDirectory(source, destPath, ct, conflicts, state, control);
        }
    }

    private static void MoveOne(
        string source,
        string destination,
        CancellationToken ct,
        FileOperationRunState state,
        IFileConflictResolver? conflicts,
        IFileOperationControl? control)
    {
        var destPath = Path.Combine(destination, Path.GetFileName(source));

        // Moving onto the same path is a no-op; Replace must not delete the source.
        if (PathUtilities.PathsEqual(source, destPath))
            return;

        if (File.Exists(source))
        {
            if (File.Exists(destPath) && !TryResolveFileConflict(source, destPath, isDirectory: false, conflicts, state, out destPath))
                return;

            // File.Move copy-and-deletes internally when the destination is on another volume, so it
            // already works across drives/network locations.
            File.Move(source, destPath);
        }
        else if (Directory.Exists(source))
        {
            if (PathUtilities.IsSameOrChildPath(source, destPath))
                throw new InvalidOperationException("Cannot move a folder into itself or one of its subfolders.");

            if (Directory.Exists(destPath) && !TryResolveDirectoryConflict(source, destPath, ct, conflicts, control, state, out destPath, isMove: true))
                return;

            MoveDirectory(source, destPath, ct, conflicts, state, control);
        }
    }

    /// <summary>
    /// Moves a directory tree, falling back to copy-then-delete when the source and destination live on
    /// different volumes or a network location, where <see cref="Directory.Move"/> throws
    /// ("source and destination path must have the same root").
    /// </summary>
    private static void MoveDirectory(
        string source,
        string destination,
        CancellationToken ct,
        IFileConflictResolver? conflicts,
        FileOperationRunState state,
        IFileOperationControl? control)
    {
        if (PathUtilities.IsSameVolume(source, destination))
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (IOException)
            {
                // subst / mapped drives can share a root string but still refuse rename.
            }
        }

        // Cross-volume / network move: copy the tree, then delete the source so the net effect is a move.
        CopyDirectory(source, destination, ct, conflicts, state, control);
        if (state.WasCancelled)
            return;

        Directory.Delete(source, recursive: true);
    }

    private static bool TryResolveFileConflict(
        string source,
        string destPath,
        bool isDirectory,
        IFileConflictResolver? conflicts,
        FileOperationRunState state,
        out string resolvedDestPath)
    {
        resolvedDestPath = destPath;
        var choice = ResolveConflict(source, destPath, isDirectory, conflicts);

        // Files cannot be merged; treat a Merge choice as Replace so overwrite still occurs.
        if (choice == FileConflictChoice.Merge)
            choice = FileConflictChoice.Replace;

        if (choice is null || choice == FileConflictChoice.Cancel)
        {
            state.WasCancelled = true;
            return false;
        }

        if (choice == FileConflictChoice.Skip)
        {
            state.WasSkipped = true;
            return false;
        }

        if (choice == FileConflictChoice.KeepBoth)
        {
            resolvedDestPath = isDirectory
                ? FileOperationPathHelper.EnsureUniqueDirectoryPath(destPath)
                : FileOperationPathHelper.EnsureUniqueFilePath(destPath);
            return true;
        }

        if (choice == FileConflictChoice.Replace)
        {
            // Never delete the source when the conflict target is the same path.
            if (PathUtilities.PathsEqual(source, resolvedDestPath))
                return false;

            if (isDirectory)
                Directory.Delete(resolvedDestPath, recursive: true);
            else
                File.Delete(resolvedDestPath);
            return true;
        }

        resolvedDestPath = isDirectory
            ? FileOperationPathHelper.EnsureUniqueDirectoryPath(destPath)
            : FileOperationPathHelper.EnsureUniqueFilePath(destPath);
        return true;
    }

    private static bool TryResolveDirectoryConflict(
        string source,
        string destPath,
        CancellationToken ct,
        IFileConflictResolver? conflicts,
        IFileOperationControl? control,
        FileOperationRunState state,
        out string resolvedDestPath,
        bool isMove)
    {
        resolvedDestPath = destPath;
        var choice = ResolveConflict(source, destPath, isDirectory: true, conflicts);
        if (choice is null || choice == FileConflictChoice.Cancel)
        {
            state.WasCancelled = true;
            return false;
        }

        if (choice == FileConflictChoice.Skip)
        {
            state.WasSkipped = true;
            return false;
        }

        if (choice == FileConflictChoice.KeepBoth)
        {
            resolvedDestPath = FileOperationPathHelper.EnsureUniqueDirectoryPath(destPath);
            return true;
        }

        if (choice == FileConflictChoice.Merge)
        {
            // Never merge onto the same path.
            if (PathUtilities.PathsEqual(source, destPath))
                return false;

            // Recursively merge source into the existing destination (nested conflicts are resolved by
            // the same resolver). For a move, remove the emptied source afterwards.
            CopyDirectory(source, destPath, ct, conflicts, state, control);
            if (isMove && !state.WasCancelled)
                Directory.Delete(source, recursive: true);

            state.WasSkipped = false;
            return false;
        }

        if (choice == FileConflictChoice.Replace)
        {
            // Never delete/merge the source when the conflict target is the same path.
            if (PathUtilities.PathsEqual(source, destPath))
                return false;

            // Copy and move both replace: remove the existing tree so dest-only files are gone,
            // then the caller copies or moves source into the emptied path.
            Directory.Delete(destPath, recursive: true);
            return true;
        }

        resolvedDestPath = FileOperationPathHelper.EnsureUniqueDirectoryPath(destPath);
        return true;
    }

    private static FileConflictChoice? ResolveConflict(
        string source,
        string destPath,
        bool isDirectory,
        IFileConflictResolver? conflicts)
    {
        if (conflicts is not null)
            return conflicts.ResolveSync(new FileConflictInfo(source, destPath, isDirectory));

        return FileConflictChoice.KeepBoth;
    }

    private static void CopyDirectory(
        string source,
        string destination,
        CancellationToken ct,
        IFileConflictResolver? conflicts,
        FileOperationRunState state,
        IFileOperationControl? control)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            control?.WaitIfPaused(ct);
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(destFile))
            {
                var localState = new FileOperationRunState();
                if (!TryResolveFileConflict(file, destFile, isDirectory: false, conflicts, localState, out destFile))
                {
                    if (localState.WasCancelled)
                    {
                        state.WasCancelled = true;
                        return;
                    }

                    continue;
                }
            }

            File.Copy(file, destFile, overwrite: false);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            control?.WaitIfPaused(ct);
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            if (Directory.Exists(destDir))
            {
                var localState = new FileOperationRunState();
                if (!TryResolveDirectoryConflict(dir, destDir, ct, conflicts, control, localState, out destDir, isMove: false))
                {
                    if (localState.WasCancelled)
                    {
                        state.WasCancelled = true;
                        return;
                    }

                    continue;
                }
            }

            CopyDirectory(dir, destDir, ct, conflicts, state, control);
            if (state.WasCancelled)
                return;
        }
    }

}
