namespace HelixExplorer.Core.Git;

/// <summary>
/// Snapshot from a single <c>git status --porcelain=v2 -z --branch</c> invocation.
/// File keys are repo-relative paths normalized with forward slashes and no trailing slash.
/// </summary>
public sealed class GitStatusSnapshot(
    GitStatus status,
    string? repoRoot,
    IReadOnlyDictionary<string, GitFileStatus> files)
{
    public static readonly GitStatusSnapshot Empty = new(
        GitStatus.Empty,
        repoRoot: null,
        new Dictionary<string, GitFileStatus>(0, StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, GitFileStatus> _folderStatuses = BuildFolderIndex(files);

    public GitStatus Status { get; } = status;

    public string? RepoRoot { get; } = repoRoot;

    public IReadOnlyDictionary<string, GitFileStatus> Files { get; } = files;

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

        var prefix = relative.EndsWith('/') ? relative : relative + "/";
        if (_folderStatuses.TryGetValue(prefix, out var folderStatus))
            return folderStatus;

        return GitFileStatus.None;
    }

    private static Dictionary<string, GitFileStatus> BuildFolderIndex(IReadOnlyDictionary<string, GitFileStatus> files)
    {
        var index = new Dictionary<string, GitFileStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, status) in files)
        {
            var parts = path.Split('/');
            for (var i = 1; i < parts.Length; i++)
            {
                var prefix = string.Join("/", parts, 0, i) + "/";
                if (index.TryGetValue(prefix, out var existing))
                    index[prefix] = Max(existing, status);
                else
                    index[prefix] = status;
            }
        }

        return index;
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
