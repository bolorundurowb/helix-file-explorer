using HelixExplorer.Core.Git;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class GitPorcelainParserTests
{
    private static string NulDelimited(params string[] lines)
        => string.Join("\0", lines) + "\0";

    private static readonly string BranchHeader = NulDelimited(
        "# branch.head main",
        "# branch.upstream origin/main",
        "# branch.ab +2 -1");

    private static readonly string StatusLines = NulDelimited(
        "1 A. N... 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 staged.txt",
        "1 .M N... 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 modified.txt",
        "1 MM N... 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 both.txt",
        "u UU N... 100644 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 3333333333333333333333333333333333333333 conflict.txt",
        "? untracked.txt");

    private static readonly string Sample = BranchHeader + StatusLines;

    [Fact]
    public void Parse_ReadsBranchAheadBehindAndCounts()
    {
        var snapshot = GitPorcelainParser.Parse(Sample, @"C:\repo");

        Assert.True(snapshot.IsRepository);
        Assert.Equal("main", snapshot.Status.Branch);
        Assert.Equal(2, snapshot.Status.Ahead);
        Assert.Equal(1, snapshot.Status.Behind);
        Assert.True(snapshot.Status.HasRemote);
        Assert.Equal(3, snapshot.Status.Staged); // A., MM, UU
        Assert.Equal(3, snapshot.Status.Unstaged); // .M, MM, UU
        Assert.Equal(1, snapshot.Status.Untracked);
        Assert.Equal("main ↑2 ↓1 · 7 modified", snapshot.Status.Display);
    }

    [Fact]
    public void Parse_MapsPerFileStatuses()
    {
        var snapshot = GitPorcelainParser.Parse(Sample, @"C:\repo");

        Assert.Equal(GitFileStatus.AddedOrStaged, snapshot.GetStatusForPath(@"C:\repo\staged.txt"));
        Assert.Equal(GitFileStatus.Modified, snapshot.GetStatusForPath(@"C:\repo\modified.txt"));
        Assert.Equal(GitFileStatus.Modified, snapshot.GetStatusForPath(@"C:\repo\both.txt"));
        Assert.Equal(GitFileStatus.Conflict, snapshot.GetStatusForPath(@"C:\repo\conflict.txt"));
        Assert.Equal(GitFileStatus.Untracked, snapshot.GetStatusForPath(@"C:\repo\untracked.txt"));
        Assert.Equal(GitFileStatus.None, snapshot.GetStatusForPath(@"C:\repo\clean.txt"));
    }

    [Fact]
    public void Parse_FolderInheritsChildStatus()
    {
        var output = NulDelimited(
            "# branch.head feature",
            "1 .M N... 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 src/a.txt");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");
        Assert.Equal(GitFileStatus.Modified, snapshot.GetStatusForPath(@"C:\repo\src"));
        Assert.Equal("feature · 1 modified", snapshot.Status.Display);
    }

    [Fact]
    public void Parse_Empty_ReturnsEmptySnapshot()
    {
        var snapshot = GitPorcelainParser.Parse(string.Empty, null);
        Assert.False(snapshot.IsRepository);
        Assert.Equal(GitStatus.Empty, snapshot.Status);
    }

    [Fact]
    public void Parse_QuotedPath_DecodesSpaces()
    {
        var output = NulDelimited(
            "# branch.head main",
            "1 .M N... 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 \"file with spaces.txt\"");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");

        Assert.Equal(GitFileStatus.Modified, snapshot.GetStatusForPath(@"C:\repo\file with spaces.txt"));
    }

    [Fact]
    public void Parse_QuotedPath_DecodesOctalEscape()
    {
        var output = NulDelimited(
            "# branch.head main",
            "? \"caf\\303\\251.txt\"");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");

        Assert.Equal(GitFileStatus.Untracked, snapshot.GetStatusForPath(@"C:\repo\café.txt"));
    }

    [Fact]
    public void Parse_DetachedHead()
    {
        var output = NulDelimited(
            "# branch.head ",
            "? untracked.txt");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");

        Assert.Equal("(detached)", snapshot.Status.Branch);
        Assert.False(snapshot.Status.HasRemote);
    }

    [Fact]
    public void Parse_PathWithExclamationMark()
    {
        var output = NulDelimited(
            "# branch.head main",
            "1 .M N... 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 \"file!name.txt\"");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");

        Assert.Equal(GitFileStatus.Modified, snapshot.GetStatusForPath(@"C:\repo\file!name.txt"));
    }
}
