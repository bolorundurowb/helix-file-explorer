namespace HelixExplorer.Core.Git;

/// <summary>
/// Snapshot from a single <c>git status --porcelain=v2 --branch</c> invocation.
/// File keys are repo-relative paths normalized with forward slashes and no trailing slash.
/// </summary>
public sealed class GitStatusSnapshot
{
    public static readonly GitStatusSnapshot Empty = new(
        GitStatus.Empty,
        repoRoot: null,
        files: new Dictionary<string, GitFileStatus>(0, StringComparer.OrdinalIgnoreCase));

    public GitStatusSnapshot(
        GitStatus status,
        string? repoRoot,
        IReadOnlyDictionary<string, GitFileStatus> files)
    {
        Status = status;
        RepoRoot = repoRoot;
        Files = files;
    }

    public GitStatus Status { get; }

    public string? RepoRoot { get; }

    public IReadOnlyDictionary<string, GitFileStatus> Files { get; }

    public bool IsRepository => Status.IsRepository && !string.IsNullOrEmpty(RepoRoot);

    public GitFileStatus GetStatusForPath(string fullPath)
    {
        if (!IsRepository || string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(RepoRoot))
            return GitFileStatus.None;

        var relative = TryMakeRelative(RepoRoot, fullPath);
        if (relative is null)
            return GitFileStatus.None;

        if (Files.TryGetValue(relative, out var exact))
            return exact;

        // Folders inherit the strongest status of any child.
        var prefix = relative.EndsWith('/') ? relative : relative + "/";
        var best = GitFileStatus.None;
        foreach (var (path, status) in Files)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            best = Max(best, status);
            if (best == GitFileStatus.Conflict)
                break;
        }

        return best;
    }

    private static string? TryMakeRelative(string repoRoot, string fullPath)
    {
        var root = NormalizeDirectory(repoRoot);
        var full = fullPath.Replace('\\', '/').TrimEnd('/');
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;

        var relative = full[root.Length..].TrimStart('/');
        return relative.Length == 0 ? null : relative;
    }

    private static string NormalizeDirectory(string path)
        => path.Replace('\\', '/').TrimEnd('/') + "/";

    internal static GitFileStatus Max(GitFileStatus a, GitFileStatus b)
    {
        // Conflict > Modified > AddedOrStaged > Untracked > None
        static int Rank(GitFileStatus s) => s switch
        {
            GitFileStatus.Conflict => 4,
            GitFileStatus.Modified => 3,
            GitFileStatus.AddedOrStaged => 2,
            GitFileStatus.Untracked => 1,
            _ => 0
        };

        return Rank(a) >= Rank(b) ? a : b;
    }
}
