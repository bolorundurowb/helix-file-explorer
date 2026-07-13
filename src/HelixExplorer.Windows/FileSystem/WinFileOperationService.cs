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
        CancellationToken ct = default)
    {
        return await Task.Run(() => ProcessSources(
            sources, destination, FileOperationKind.Copy, progress, ct, conflicts,
            (s, d, t, r, c) => CopyOne(s, d, t, r, c)), ct).ConfigureAwait(false);
    }

    public async ValueTask<FileOperationResult> MoveAsync(
        IReadOnlyList<string> sources,
        string destination,
        IProgress<FileOperationProgress>? progress = null,
        IFileConflictResolver? conflicts = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() => ProcessSources(
            sources, destination, FileOperationKind.Move, progress, ct, conflicts,
            (s, d, t, r, c) => MoveOne(s, d, t, r, c)), ct).ConfigureAwait(false);
    }

    public async ValueTask<FileOperationResult> DeleteAsync(IReadOnlyList<string> paths, bool permanently, CancellationToken ct = default)
    {
        var succeeded = 0;
        var failures = new List<FileOperationFailure>();

        await Task.Run(() =>
        {
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (permanently)
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                        else if (Directory.Exists(path))
                            Directory.Delete(path, recursive: true);
                    }
                    else
                    {
                        SendToRecycleBin(path);
                    }

                    succeeded++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Delete failed for '{Path}'", path);
                    failures.Add(new FileOperationFailure(path, ex.Message));
                }
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

    private FileOperationResult ProcessSources(
        IReadOnlyList<string> sources,
        string destination,
        FileOperationKind kind,
        IProgress<FileOperationProgress>? progress,
        CancellationToken ct,
        IFileConflictResolver? conflicts,
        Action<string, string, CancellationToken, FileOperationRunState, IFileConflictResolver?> operation)
    {
        var total = sources.Count;
        var succeeded = 0;
        var skipped = 0;
        var failures = new List<FileOperationFailure>();

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var source = sources[i];
            progress?.Report(new FileOperationProgress(i, total, source, kind));

            var state = new FileOperationRunState();
            try
            {
                operation(source, destination, ct, state, conflicts);
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
                failures.Add(new FileOperationFailure(source, ex.Message));
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
        IFileConflictResolver? conflicts)
    {
        var destPath = Path.Combine(destination, Path.GetFileName(source));

        if (File.Exists(source))
        {
            if (File.Exists(destPath) && !TryResolveFileConflict(source, destPath, isDirectory: false, conflicts, state, out destPath))
                return;

            File.Copy(source, destPath, overwrite: false);
        }
        else if (Directory.Exists(source))
        {
            if (Directory.Exists(destPath) && !TryResolveDirectoryConflict(source, destPath, ct, conflicts, state, out destPath, merge: true))
                return;

            CopyDirectory(source, destPath, ct, conflicts, state);
        }
    }

    private static void MoveOne(
        string source,
        string destination,
        CancellationToken ct,
        FileOperationRunState state,
        IFileConflictResolver? conflicts)
    {
        var destPath = Path.Combine(destination, Path.GetFileName(source));

        if (File.Exists(source))
        {
            if (File.Exists(destPath) && !TryResolveFileConflict(source, destPath, isDirectory: false, conflicts, state, out destPath))
                return;

            File.Move(source, destPath);
        }
        else if (Directory.Exists(source))
        {
            if (Directory.Exists(destPath) && !TryResolveDirectoryConflict(source, destPath, ct, conflicts, state, out destPath, merge: false))
                return;

            Directory.Move(source, destPath);
        }
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
        FileOperationRunState state,
        out string resolvedDestPath,
        bool merge)
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

        if (choice == FileConflictChoice.Replace)
        {
            if (merge)
            {
                CopyDirectory(source, destPath, ct, conflicts, state);
                state.WasSkipped = false;
                return false;
            }

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
        FileOperationRunState state)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();
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
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            if (Directory.Exists(destDir))
            {
                var localState = new FileOperationRunState();
                if (!TryResolveDirectoryConflict(dir, destDir, ct, conflicts, localState, out destDir, merge: true))
                {
                    if (localState.WasCancelled)
                    {
                        state.WasCancelled = true;
                        return;
                    }

                    continue;
                }
            }

            CopyDirectory(dir, destDir, ct, conflicts, state);
            if (state.WasCancelled)
                return;
        }
    }

    private void SendToRecycleBin(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return;
            }

            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.Exists)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recycle bin operation failed for '{Path}'", path);
            throw;
        }
    }
}
