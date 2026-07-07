namespace HelixExplorer.Models;

/// <summary>Snapshot of the git repository state for the directory currently displayed in a pane.</summary>
public record GitStatus(string Branch, int Staged, int Unstaged, int Untracked, bool HasRemote)
{
    public static readonly GitStatus Empty = new(string.Empty, 0, 0, 0, false);

    public bool IsRepository => !string.IsNullOrEmpty(Branch);

    /// <summary>Compact status summary, e.g. "main +2 ~1 ?0".</summary>
    public string Display => IsRepository
        ? $"{Branch} +{Staged} ~{Unstaged} ?{Untracked}"
        : string.Empty;
}