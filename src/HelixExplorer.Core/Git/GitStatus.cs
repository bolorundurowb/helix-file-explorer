namespace HelixExplorer.Core.Git;

public sealed record GitStatus(
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

    public bool IsDirty => Staged > 0 || Unstaged > 0 || Untracked > 0;

    public int ModifiedCount => Staged + Unstaged + Untracked;

    public string Display
    {
        get
        {
            if (!IsRepository)
                return string.Empty;

            var text = Branch;
            if (Ahead > 0)
                text += $" ↑{Ahead}";
            if (Behind > 0)
                text += $" ↓{Behind}";

            var modified = ModifiedCount;
            if (modified > 0)
                text += $" · {modified} modified";

            return text;
        }
    }
}
