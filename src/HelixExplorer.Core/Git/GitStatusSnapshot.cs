namespace HelixExplorer.Core.Git;

/// <summary>
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
    private readonly string? _normalizedRepoRoot = repoRoot is null ? null : NormalizeDirectory(repoRoot);

    public GitStatus Status { get; } = status;

    public string? RepoRoot { get; } = repoRoot;

    public IReadOnlyDictionary<string, GitFileStatus> Files { get; } = files;

    public bool IsRepository => Status.IsRepository && !string.IsNullOrEmpty(RepoRoot);

    public GitFileStatus GetStatusForPath(string fullPath)
    {
        var root = _normalizedRepoRoot;
        if (!IsRepository || string.IsNullOrEmpty(fullPath) || root is null)
            return GitFileStatus.None;

        var relative = TryMakeRelative(root.AsSpan(), fullPath.AsSpan());
        if (relative is null)
            return GitFileStatus.None;

        if (Files.TryGetValue(relative, out var exact))
            return exact;

        return _folderStatuses.GetValueOrDefault(relative + "/", GitFileStatus.None);
    }

    private static Dictionary<string, GitFileStatus> BuildFolderIndex(IReadOnlyDictionary<string, GitFileStatus> files)
    {
        var index = new Dictionary<string, GitFileStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, status) in files)
        {
            var start = 0;
            while (start < path.Length)
            {
                var slash = path.IndexOf('/', start);
                if (slash < 0)
                    break;

                var prefix = string.Concat(path.AsSpan(0, slash), "/");
                if (index.TryGetValue(prefix, out var existing))
                    index[prefix] = Max(existing, status);
                else
                    index[prefix] = status;

                start = slash + 1;
            }
        }

        return index;
    }

    private static string? TryMakeRelative(ReadOnlySpan<char> root, ReadOnlySpan<char> full)
    {
        if (full.Length < root.Length)
            return null;

        for (var i = 0; i < root.Length; i++)
        {
            if (!PathCharEqualsIgnoreCase(root[i], full[i]))
                return null;
        }

        var rest = full[root.Length..];

        while (!rest.IsEmpty && IsSeparator(rest[0]))
            rest = rest[1..];

        while (!rest.IsEmpty && IsSeparator(rest[^1]))
            rest = rest[..^1];

        return rest.IsEmpty ? null : NormalizeRelative(rest);
    }

    private static bool PathCharEqualsIgnoreCase(char a, char b)
    {
        if (a == b)
            return true;

        if ((a is '/' or '\\') && (b is '/' or '\\'))
            return true;

        return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
    }

    private static bool IsSeparator(char c) => c is '/' or '\\';

    private static string NormalizeRelative(ReadOnlySpan<char> span)
    {
        if (span.IndexOf('\\') < 0)
            return span.ToString();

        return string.Create(span.Length, span, static (chars, src) =>
        {
            for (var i = 0; i < src.Length; i++)
                chars[i] = src[i] == '\\' ? '/' : src[i];
        });
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
