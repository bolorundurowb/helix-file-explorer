using HelixExplorer.Core.Search;

namespace HelixExplorer.Core.Tests;

public sealed class FuzzyMatcherTests
{
    [Fact]
    public void Score_EmptyQuery_MatchesEverything()
    {
        FuzzyMatcher.Score("Toggle Sidebar", string.Empty).Must().Be(0);
    }

    [Fact]
    public void Score_Subsequence_AppliesBoundaryBonus()
    {
        var score = FuzzyMatcher.Score("Toggle Sidebar", "ts");
        score.Must().BeGreaterThan(0);
    }

    [Fact]
    public void Score_MissingCharacters_ReturnsNegative()
    {
        FuzzyMatcher.Score("New Tab", "xyz").Must().Be(-1);
    }
}
