using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.FileSystem.Undo;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Windows.Shell;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

public sealed class WinFileOperationService(ILogger<WinFileOperationService> logger) : IFileOperationService
{
    /// <summary>
    /// Slack allowed between this process's clock and the deletion time the shell stamps into a
    /// <c>$I</c> file, when deciding which bin entries a delete batch produced.
    /// </summary>
    private static readonly TimeSpan RecycleTimestampSkew = TimeSpan.FromSeconds(5);

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

            // Stamped before the operation so the bin scan afterwards can ignore everything already
            // there. The shell writes the deletion time from its own clock, so allow a little skew
            // rather than losing entries stamped a moment "before" we started.
            var batchStart = DateTime.UtcNow - RecycleTimestampSkew;

            var (_, deletedPaths) = await ShellFileOperationsHelper
                .DeleteToRecycleBinAsync(paths, progress, ct).ConfigureAwait(false);

            var deletedSet = new HashSet<string>(deletedPaths, StringComparer.OrdinalIgnoreCase);
            var recycleFailures = paths
                .Where(p => !deletedSet.Contains(p))
                .Select(p => new FileOperationFailure(p, "Recycle bin operation failed."))
                .ToList();

            var recycleChanges = await Task.Run(
                () => RecycleBinMatcher.Match(deletedPaths, RecycleBinPaths.ReadEntries(batchStart), batchStart),
                ct).ConfigureAwait(false);

            return new FileOperationResult(deletedPaths.Count, 0, recycleFailures.Count, recycleFailures)
            {
                Changes = recycleChanges
            };
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
        // newName must be a single leaf name. Without this check, Path.Combine silently discards
        // "parent" for a rooted newName (e.g. "C:\Windows\evil"), and a "../" segment walks the
        // move target out of the current folder — either turns "rename" into an arbitrary move.
        if (!TryValidateNewName(newName, out var validationError))
        {
            logger.LogError("Rename rejected for '{Path}': {Reason}", path, validationError);
            return new FileOperationResult(0, 0, 1, [new FileOperationFailure(path, validationError!)]);
        }

        try
        {
            var newPath = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var parent = Path.GetDirectoryName(path) ?? string.Empty;
                var target = Path.Combine(parent, newName);

                if (File.Exists(path))
                    File.Move(path, target);
                else if (Directory.Exists(path))
                    Directory.Move(path, target);
                else
                    throw new FileNotFoundException("Path not found.", path);

                return target;
            }, ct).ConfigureAwait(false);

            return new FileOperationResult(1, 0, 0, Array.Empty<FileOperationFailure>())
            {
                Changes = [new FileOperationChange(path, newPath)]
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rename failed for '{Path}'", path);
            return new FileOperationResult(0, 0, 1, [new FileOperationFailure(path, ex.Message)]);
        }
    }

    /// <summary>
    /// Rejects anything that is not a single, plain leaf name: empty/whitespace, <c>.</c>/<c>..</c>,
    /// a rooted path, a path containing a directory separator, or a name with a character Windows
    /// itself disallows in a file name.
    /// </summary>
    private static bool TryValidateNewName(string? newName, out string? error)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            error = "The new name cannot be empty.";
            return false;
        }

        if (newName is "." or "..")
        {
            error = $"'{newName}' is not a valid name.";
            return false;
        }

        if (Path.IsPathRooted(newName) || newName.Contains('\\') || newName.Contains('/'))
        {
            error = "The new name cannot contain a path.";
            return false;
        }

        var invalidIndex = newName.IndexOfAny(Path.GetInvalidFileNameChars());
        if (invalidIndex >= 0)
        {
            error = $"The name contains an invalid character: '{newName[invalidIndex]}'.";
            return false;
        }

        error = null;
        return true;
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
        var changes = new List<FileOperationChange>();
        var usedMerge = false;
        var usedReplace = false;
        var replacedItemsRecycled = true;

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

                usedMerge |= state.UsedMerge;
                usedReplace |= state.UsedReplace;
                replacedItemsRecycled &= state.ReplacedItemsRecycled;

                if (state.WasCancelled)
                    break;

                if (state.WasSkipped)
                {
                    skipped++;
                }
                else
                {
                    succeeded++;

                    // One change per top-level source, never per nested file: undoing a folder paste
                    // should recycle the folder, not walk back through everything inside it.
                    if (state.ResolvedDestinationPath is { Length: > 0 } dest)
                        changes.Add(new FileOperationChange(source, dest));
                }
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

        return new FileOperationResult(succeeded, skipped, failures.Count, failures)
        {
            Changes = changes,
            UsedMerge = usedMerge,
            UsedReplace = usedReplace,
            ReplacedItemsRecycled = replacedItemsRecycled
        };
    }

    private sealed class FileOperationRunState
    {
        public bool WasSkipped { get; set; }
        public bool WasCancelled { get; set; }

        /// <summary>
        /// Where the item actually landed, after Keep Both uniquifying. Null when nothing was written.
        /// </summary>
        /// <remarks>
        /// The only place the post-conflict destination is known is inside the per-item operation, and
        /// undo needs it: recycling <c>Foo</c> when the paste actually created <c>Foo (2)</c> would
        /// delete the wrong item.
        /// </remarks>
        public string? ResolvedDestinationPath { get; set; }

        public bool UsedMerge { get; set; }

        public bool UsedReplace { get; set; }

        /// <summary>True when every replaced item in this batch went to the bin rather than being erased.</summary>
        public bool ReplacedItemsRecycled { get; set; } = true;

        /// <summary>Carries conflict outcomes from a nested walk back up to the batch-level state.</summary>
        public void AbsorbFlags(FileOperationRunState inner)
        {
            UsedMerge |= inner.UsedMerge;
            UsedReplace |= inner.UsedReplace;
            ReplacedItemsRecycled &= inner.ReplacedItemsRecycled;
        }
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
            if (File.Exists(destPath) && !TryResolveFileConflict(source, destPath, isDirectory: false, conflicts, state, ct, out destPath))
                return;

            File.Copy(source, destPath, overwrite: false);
        }
        else if (Directory.Exists(source))
        {
            // A recursive copy must never traverse a junction/symlink into an unrelated tree.
            // Recreating every reparse tag safely needs tag-specific data, so unsupported links are
            // reported as skipped instead of silently producing a dangerous or misleading copy.
            if (IsDirectoryReparsePoint(source))
            {
                state.WasSkipped = true;
                return;
            }

            // Dest under source would recurse into the newly created tree forever.
            if (PathUtilities.IsSameOrChildPath(source, destPath))
                throw new InvalidOperationException("Cannot copy a folder into itself or one of its subfolders.");

            if (Directory.Exists(destPath) && !TryResolveDirectoryConflict(source, destPath, ct, conflicts, control, state, out destPath, isMove: false))
                return;

            CopyDirectory(source, destPath, ct, conflicts, state, control);
            if (state.WasCancelled)
                return;
        }
        else
        {
            return;
        }

        // Recorded only on the way out, so a skipped or cancelled item leaves nothing for undo.
        state.ResolvedDestinationPath = destPath;
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
            if (File.Exists(destPath) && !TryResolveFileConflict(source, destPath, isDirectory: false, conflicts, state, ct, out destPath))
                return;

            // File.Move copy-and-deletes internally when the destination is on another volume, so it
            // already works across drives/network locations.
            File.Move(source, destPath);
        }
        else if (Directory.Exists(source))
        {
            if (PathUtilities.IsSameOrChildPath(source, destPath))
                throw new InvalidOperationException("Cannot move a folder into itself or one of its subfolders.");

            if (IsDirectoryReparsePoint(source) && !PathUtilities.IsSameVolume(source, destPath))
            {
                state.WasSkipped = true;
                return;
            }

            if (Directory.Exists(destPath) && !TryResolveDirectoryConflict(source, destPath, ct, conflicts, control, state, out destPath, isMove: true))
                return;

            MoveDirectory(source, destPath, ct, conflicts, state, control);
            if (state.WasCancelled)
                return;
        }
        else
        {
            return;
        }

        state.ResolvedDestinationPath = destPath;
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
        CancellationToken ct,
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

            state.UsedReplace = true;
            DiscardDisplacedItem(resolvedDestPath, isDirectory, state, ct);
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

            // A merge cannot be undone: files that existed only in the destination are indistinguishable
            // afterwards from files the merge brought in, so there is no way to reconstruct the
            // pre-merge tree. Flagging it here stops the whole batch being pushed onto the history.
            state.UsedMerge = true;

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
            state.UsedReplace = true;
            DiscardDisplacedItem(destPath, isDirectory: true, state, ct);
            return true;
        }

        resolvedDestPath = FileOperationPathHelper.EnsureUniqueDirectoryPath(destPath);
        return true;
    }

    /// <summary>
    /// Gets rid of an item a Replace is about to overwrite, preferring the recycle bin.
    /// </summary>
    /// <remarks>
    /// Overwriting used to erase the displaced item outright, which made "replace" quietly the most
    /// destructive thing the app could do and left undo unable to honestly claim it restored anything.
    /// Routing it through the bin costs one shell call and makes the overwrite recoverable. When the
    /// shell refuses — network paths, items past the bin's size cap, a bin disabled by policy — fall
    /// back to the hard delete and record that the batch's replaced data is gone for good.
    /// </remarks>
    private static void DiscardDisplacedItem(
        string path,
        bool isDirectory,
        FileOperationRunState state,
        CancellationToken ct)
    {
        if (ShellFileOperationsHelper.TryRecycleDisplacedItem(path, ct))
            return;

        state.ReplacedItemsRecycled = false;

        if (isDirectory)
            Directory.Delete(path, recursive: true);
        else
            File.Delete(path);
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
                // A nested conflict gets its own state so a skip here does not mark the whole top-level
                // item skipped, but its Merge/Replace outcomes still have to reach the batch.
                var localState = new FileOperationRunState();
                var resolved = TryResolveFileConflict(file, destFile, isDirectory: false, conflicts, localState, ct, out destFile);
                state.AbsorbFlags(localState);

                if (!resolved)
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

            if (IsDirectoryReparsePoint(dir))
                continue;

            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            if (Directory.Exists(destDir))
            {
                var localState = new FileOperationRunState();
                var resolved = TryResolveDirectoryConflict(dir, destDir, ct, conflicts, control, localState, out destDir, isMove: false);
                state.AbsorbFlags(localState);

                if (!resolved)
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

    internal static bool IsDirectoryReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception)
        {
            // Let the subsequent normal file-system operation surface its more useful error.
            return false;
        }
    }

}
