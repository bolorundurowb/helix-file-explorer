using HelixExplorer.Core.Filtering;

namespace HelixExplorer.Core.Tests;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("report.pdf", "*.pdf", true)]
    [InlineData("report.PDF", "*.pdf", true)]
    [InlineData("report.docx", "*.pdf", false)]
    [InlineData("readme.txt", "read*", true)]
    [InlineData("readme.txt", "READ*", true)]
    [InlineData("a.txt", "?.txt", true)]
    [InlineData("ab.txt", "?.txt", false)]
    public void IsMatch_GlobPatterns(string name, string pattern, bool expected)
    {
        GlobMatcher.IsMatch(name, pattern).Must().Be(expected);
    }

    [Fact]
    public void HasGlobMetacharacters_DetectsStarsAndQuestions()
    {
        GlobMatcher.HasGlobMetacharacters("*.cs").Must().BeTrue();
        GlobMatcher.HasGlobMetacharacters("file?.txt").Must().BeTrue();
        GlobMatcher.HasGlobMetacharacters("readme").Must().BeFalse();
    }

    [Theory]
    [InlineData("Report.docx", "report", true)]
    [InlineData("Report.docx", "*.docx", true)]
    [InlineData("Report.docx", "*.pdf", false)]
    public void EntryNameMatcher_SubstringOrGlob(string name, string query, bool expected)
    {
        EntryNameMatcher.Matches(name, query).Must().Be(expected);
    }
}
