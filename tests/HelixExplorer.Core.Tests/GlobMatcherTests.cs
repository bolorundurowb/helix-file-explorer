using HelixExplorer.Core.Filtering;
using Xunit;

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
        Assert.Equal(expected, GlobMatcher.IsMatch(name, pattern));
    }

    [Fact]
    public void HasGlobMetacharacters_DetectsStarsAndQuestions()
    {
        Assert.True(GlobMatcher.HasGlobMetacharacters("*.cs"));
        Assert.True(GlobMatcher.HasGlobMetacharacters("file?.txt"));
        Assert.False(GlobMatcher.HasGlobMetacharacters("readme"));
    }

    [Theory]
    [InlineData("Report.docx", "report", true)]
    [InlineData("Report.docx", "*.docx", true)]
    [InlineData("Report.docx", "*.pdf", false)]
    public void EntryNameMatcher_SubstringOrGlob(string name, string query, bool expected)
    {
        Assert.Equal(expected, EntryNameMatcher.Matches(name, query));
    }
}
