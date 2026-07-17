using HelixExplorer.Core.Git;

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

        snapshot.IsRepository.Must().BeTrue();
        snapshot.Status.Branch.Must().Be("main");
        snapshot.Status.Ahead.Must().Be(2);
        snapshot.Status.Behind.Must().Be(1);
        snapshot.Status.HasRemote.Must().BeTrue();
        snapshot.Status.Staged.Must().Be(3);
        snapshot.Status.Unstaged.Must().Be(3);
        snapshot.Status.Untracked.Must().Be(1);
        snapshot.Status.Display.Must().Be("main ↑2 ↓1 · 7 modified");
    }

    [Fact]
    public void Parse_MapsPerFileStatuses()
    {
        var snapshot = GitPorcelainParser.Parse(Sample, @"C:\repo");

        snapshot.GetStatusForPath(@"C:\repo\staged.txt").Must().Be(GitFileStatus.AddedOrStaged);
        snapshot.GetStatusForPath(@"C:\repo\modified.txt").Must().Be(GitFileStatus.Modified);
        snapshot.GetStatusForPath(@"C:\repo\both.txt").Must().Be(GitFileStatus.Modified);
        snapshot.GetStatusForPath(@"C:\repo\conflict.txt").Must().Be(GitFileStatus.Conflict);
        snapshot.GetStatusForPath(@"C:\repo\untracked.txt").Must().Be(GitFileStatus.Untracked);
        snapshot.GetStatusForPath(@"C:\repo\clean.txt").Must().Be(GitFileStatus.None);
    }

    [Fact]
    public void Parse_FolderInheritsChildStatus()
    {
        var output = NulDelimited(
            "# branch.head feature",
            "1 .M N... 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 src/a.txt");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");
        snapshot.GetStatusForPath(@"C:\repo\src").Must().Be(GitFileStatus.Modified);
        snapshot.Status.Display.Must().Be("feature · 1 modified");
    }

    [Fact]
    public void Parse_Empty_ReturnsEmptySnapshot()
    {
        var snapshot = GitPorcelainParser.Parse(string.Empty, null);
        snapshot.IsRepository.Must().BeFalse();
        snapshot.Status.Must().Be(GitStatus.Empty);
    }

    [Fact]
    public void Parse_QuotedPath_DecodesSpaces()
    {
        var output = NulDelimited(
            "# branch.head main",
            "1 .M N... 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 \"file with spaces.txt\"");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");

        snapshot.GetStatusForPath(@"C:\repo\file with spaces.txt").Must().Be(GitFileStatus.Modified);
    }

    [Fact]
    public void Parse_QuotedPath_DecodesOctalEscape()
    {
        var output = NulDelimited(
            "# branch.head main",
            "? \"caf\\303\\251.txt\"");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");

        snapshot.GetStatusForPath(@"C:\repo\café.txt").Must().Be(GitFileStatus.Untracked);
    }

    [Fact]
    public void Parse_DetachedHead()
    {
        var output = NulDelimited(
            "# branch.head ",
            "? untracked.txt");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");

        snapshot.Status.Branch.Must().Be("(detached)");
        snapshot.Status.HasRemote.Must().BeFalse();
    }

    [Fact]
    public void Parse_PathWithExclamationMark()
    {
        var output = NulDelimited(
            "# branch.head main",
            "1 .M N... 100644 100644 100644 1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 \"file!name.txt\"");

        var snapshot = GitPorcelainParser.Parse(output, @"C:\repo");

        snapshot.GetStatusForPath(@"C:\repo\file!name.txt").Must().Be(GitFileStatus.Modified);
    }
}
