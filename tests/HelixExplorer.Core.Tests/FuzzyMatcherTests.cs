using HelixExplorer.Core.Search;
using Xunit;

namespace HelixExplorer.Core.Tests;

public sealed class FuzzyMatcherTests
{
    [Fact]
    public void Score_empty_query_matches_everything()
        => Assert.Equal(0, FuzzyMatcher.Score("Toggle Sidebar", string.Empty));

    [Fact]
    public void Score_finds_subsequence_with_boundary_bonus()
    {
        var score = FuzzyMatcher.Score("Toggle Sidebar", "ts");
        Assert.True(score > 0);
    }

    [Fact]
    public void Score_returns_negative_when_chars_missing()
        => Assert.Equal(-1, FuzzyMatcher.Score("New Tab", "xyz"));
}
