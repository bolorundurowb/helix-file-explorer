namespace HelixExplorer.Core.Git;

public enum GitFileStatus
{
    None = 0,
    Untracked,
    AddedOrStaged,
    Modified,
    Conflict
}
