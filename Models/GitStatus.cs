namespace HelixExplorer.Models;

/// <summary>Snapshot of the git repository state for the directory currently displayed in a pane.</summary>
public record GitStatus(
    string Branch,
    int Staged,
    int Unstaged,
    int Untracked,
    bool HasRemote,
    int Ahead = 0,
    int Behind = 0)
{
    public static readonly GitStatus Empty = new(string.Empty, 0, 0, 0, false);

    public bool IsRepository => !string.IsNullOrEmpty(Branch);

    /// <summary>True when the working tree has staged, unstaged, or untracked changes.</summary>
    public bool IsDirty => Staged > 0 || Unstaged > 0 || Untracked > 0;

    /// <summary>Compact status summary, e.g. "main +2 ~1 ?0 ↑1".</summary>
    public string Display
    {
        get
        {
            if (!IsRepository) return string.Empty;
            string ab = (Ahead > 0 ? $" ↑{Ahead}" : string.Empty) + (Behind > 0 ? $" ↓{Behind}" : string.Empty);
            return $"{Branch} +{Staged} ~{Unstaged} ?{Untracked}{ab}";
        }
    }
}
