using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Per-batch conflict resolver. Wraps <see cref="IUserDialogService"/> and tracks
/// "apply to all" state so a single copy/move batch only prompts once when chosen.
/// </summary>
public interface IFileConflictResolver
{
    /// <summary>Resolves a single conflict, using cached "apply to all" state when available.</summary>
    /// <returns>The chosen action, or <c>null</c> if the user cancels the whole batch.</returns>
    Task<FileConflictChoice?> ResolveAsync(FileConflictInfo conflict);

    /// <summary>Synchronous resolve for background file-operation threads.</summary>
    FileConflictChoice? ResolveSync(FileConflictInfo conflict);

    bool ApplyToAllChosen { get; }
}

/// <summary>Default implementation backed by <see cref="IUserDialogService"/>.</summary>
public sealed class FileConflictResolver(IUserDialogService dialogs, bool canApplyToAll = true) : IFileConflictResolver
{
    private FileConflictChoice? _applyToAll;

    public bool ApplyToAllChosen => _applyToAll.HasValue;

    public async Task<FileConflictChoice?> ResolveAsync(FileConflictInfo conflict)
    {
        if (_applyToAll.HasValue)
            return _applyToAll.Value;

        var resolution = await dialogs.ResolveConflictAsync(conflict, canApplyToAll).ConfigureAwait(true);
        if (resolution is null)
            return null;

        if (resolution.ApplyToAll)
            _applyToAll = resolution.Choice;

        return resolution.Choice;
    }

    public FileConflictChoice? ResolveSync(FileConflictInfo conflict)
        => ResolveAsync(conflict).GetAwaiter().GetResult();
}
