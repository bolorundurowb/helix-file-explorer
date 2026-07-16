using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Tracks "apply to all" so a single copy/move batch only prompts once when chosen.
/// </summary>
public interface IFileConflictResolver
{
    /// <returns>The chosen action, or <c>null</c> if the user cancels the whole batch.</returns>
    Task<FileConflictChoice?> ResolveAsync(FileConflictInfo conflict);

    /// <summary>Shell ops run off-UI; sync path avoids marshaling each conflict onto the dispatcher.</summary>
    FileConflictChoice? ResolveSync(FileConflictInfo conflict);

    bool ApplyToAllChosen { get; }
}

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
