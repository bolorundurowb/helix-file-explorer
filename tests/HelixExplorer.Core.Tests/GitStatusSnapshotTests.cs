using HelixExplorer.Core.Git;

namespace HelixExplorer.Core.Tests;

public sealed class GitStatusSnapshotTests
{
    [Theory]
    [InlineData(GitFileStatus.Conflict, GitFileStatus.Modified, GitFileStatus.Conflict)]
    [InlineData(GitFileStatus.Modified, GitFileStatus.Conflict, GitFileStatus.Conflict)]
    [InlineData(GitFileStatus.Modified, GitFileStatus.AddedOrStaged, GitFileStatus.Modified)]
    [InlineData(GitFileStatus.AddedOrStaged, GitFileStatus.Untracked, GitFileStatus.AddedOrStaged)]
    [InlineData(GitFileStatus.Untracked, GitFileStatus.None, GitFileStatus.Untracked)]
    [InlineData(GitFileStatus.None, GitFileStatus.None, GitFileStatus.None)]
    [InlineData(GitFileStatus.Modified, GitFileStatus.Modified, GitFileStatus.Modified)]
    public void Max_ReturnsHigherRankedStatus(GitFileStatus a, GitFileStatus b, GitFileStatus expected)
    {
        GitStatusSnapshot.Max(a, b).Must().Be(expected);
    }

    [Fact]
    public void GetStatusForPath_ExactFileMatch_ReturnsThatFilesStatus()
    {
        var snapshot = CreateSnapshot(("readme.txt", GitFileStatus.Modified));

        snapshot.GetStatusForPath(@"C:\repo\readme.txt").Must().Be(GitFileStatus.Modified);
    }

    [Fact]
    public void GetStatusForPath_UnknownFile_ReturnsNone()
    {
        var snapshot = CreateSnapshot(("readme.txt", GitFileStatus.Modified));

        snapshot.GetStatusForPath(@"C:\repo\other.txt").Must().Be(GitFileStatus.None);
    }

    [Fact]
    public void GetStatusForPath_Folder_AggregatesHighestRankedChildStatus()
    {
        var snapshot = CreateSnapshot(
            ("src/a.txt", GitFileStatus.Untracked),
            ("src/b.txt", GitFileStatus.Modified));

        snapshot.GetStatusForPath(@"C:\repo\src").Must().Be(GitFileStatus.Modified);
    }

    [Fact]
    public void GetStatusForPath_Folder_OrderOfChildrenDoesNotAffectAggregation()
    {
        var snapshot = CreateSnapshot(
            ("src/b.txt", GitFileStatus.Conflict),
            ("src/a.txt", GitFileStatus.Untracked));

        snapshot.GetStatusForPath(@"C:\repo\src").Must().Be(GitFileStatus.Conflict);
    }

    [Fact]
    public void GetStatusForPath_NotARepository_ReturnsNone()
    {
        var snapshot = new GitStatusSnapshot(
            GitStatus.Empty,
            repoRoot: @"C:\repo",
            new Dictionary<string, GitFileStatus>
            {
                ["readme.txt"] = GitFileStatus.Modified
            });

        snapshot.IsRepository.Must().BeFalse();
        snapshot.GetStatusForPath(@"C:\repo\readme.txt").Must().Be(GitFileStatus.None);
    }

    [Fact]
    public void GetStatusForPath_NullRepoRoot_ReturnsNone()
    {
        var snapshot = new GitStatusSnapshot(
            new GitStatus("main", 0, 1, 0, true),
            repoRoot: null,
            new Dictionary<string, GitFileStatus>
            {
                ["readme.txt"] = GitFileStatus.Modified
            });

        snapshot.IsRepository.Must().BeFalse();
        snapshot.GetStatusForPath(@"C:\repo\readme.txt").Must().Be(GitFileStatus.None);
    }

    [Fact]
    public void GetStatusForPath_PathOutsideRepoRoot_ReturnsNone()
    {
        var snapshot = CreateSnapshot(("readme.txt", GitFileStatus.Modified));

        snapshot.GetStatusForPath(@"D:\elsewhere\readme.txt").Must().Be(GitFileStatus.None);
    }

    [Fact]
    public void GetStatusForPath_EmptyPath_ReturnsNone()
    {
        var snapshot = CreateSnapshot(("readme.txt", GitFileStatus.Modified));

        snapshot.GetStatusForPath(string.Empty).Must().Be(GitFileStatus.None);
    }

    [Fact]
    public void GetStatusForPath_IsCaseInsensitiveForRepoRootPrefix()
    {
        var snapshot = CreateSnapshot(("readme.txt", GitFileStatus.Modified));

        snapshot.GetStatusForPath(@"c:\REPO\README.TXT").Must().Be(GitFileStatus.Modified);
    }

    [Fact]
    public void GetStatusForPath_NormalizesForwardSlashesInQueriedPath()
    {
        var snapshot = CreateSnapshot(("src/a.txt", GitFileStatus.Modified));

        snapshot.GetStatusForPath(@"C:\repo/src/a.txt").Must().Be(GitFileStatus.Modified);
    }

    private static GitStatusSnapshot CreateSnapshot(params (string Path, GitFileStatus Status)[] files)
    {
        var dictionary = new Dictionary<string, GitFileStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, status) in files)
            dictionary[path] = status;

        return new GitStatusSnapshot(
            new GitStatus("main", 0, files.Length, 0, true),
            repoRoot: @"C:\repo",
            dictionary);
    }
}
