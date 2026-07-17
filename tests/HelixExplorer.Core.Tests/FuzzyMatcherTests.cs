using HelixExplorer.Core.Search;

namespace HelixExplorer.Core.Tests;

public sealed class FuzzyMatcherTests
{
    [Fact]
    public void Score_empty_query_matches_everything()
    {
        FuzzyMatcher.Score("Toggle Sidebar", string.Empty).Must().Be(0);
    }

    [Fact]
    public void Score_finds_subsequence_with_boundary_bonus()
    {
        var score = FuzzyMatcher.Score("Toggle Sidebar", "ts");
        score.Must().BeGreaterThan(0);
    }

    [Fact]
    public void Score_returns_negative_when_chars_missing()
    {
        FuzzyMatcher.Score("New Tab", "xyz").Must().Be(-1);
    }
}
