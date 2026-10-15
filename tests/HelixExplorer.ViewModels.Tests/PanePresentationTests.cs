using System.Globalization;
using HelixExplorer.Core.Git;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels.Tests;

public sealed class PanePresentationTests
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly DateTime Now = new(2026, 10, 14, 18, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, false, false, false, 0, false, PaneContentState.Ready)]
    [InlineData(false, false, false, false, 0, false, PaneContentState.Ready)]
    [InlineData(false, true, true, true, 0, false, PaneContentState.AccessDenied)]
    [InlineData(false, true, true, false, 0, false, PaneContentState.Unavailable)]
    [InlineData(false, true, false, false, 0, true, PaneContentState.NoMatches)]
    [InlineData(false, true, false, false, 0, false, PaneContentState.Empty)]
    [InlineData(false, true, false, false, 4, false, PaneContentState.Ready)]
    public void ResolveContentState_PicksThePanePanel(
        bool isHome,
        bool hasFreshListing,
        bool hasFault,
        bool accessDenied,
        int itemCount,
        bool filterOrSearch,
        PaneContentState expected)
    {
        PanePresentation.ResolveContentState(
                isHome, hasFreshListing, hasFault, accessDenied, itemCount, filterOrSearch)
            .Must().Be(expected);
    }

    [Fact]
    public void DescribeContent_EmptyAndFilterMiss()
    {
        var empty = PanePresentation.DescribeContent(PaneContentState.Empty, null, null);
        empty.Title.Must().Be("This folder is empty");
        empty.ShowClearFilter.Must().BeFalse();

        var miss = PanePresentation.DescribeContent(PaneContentState.NoMatches, "report", null);
        miss.Title.Must().Be("No items match \"report\"");
        miss.ShowClearFilter.Must().BeTrue();

        var denied = PanePresentation.DescribeContent(PaneContentState.AccessDenied, null, "Access denied");
        denied.Title.Must().Be("Access denied");
        denied.Detail.Must().BeNull();

        var offline = PanePresentation.DescribeContent(
            PaneContentState.Unavailable, null, "Network location is unavailable");
        offline.Title.Must().Be("This location is unavailable");
        offline.Detail.Must().Be("Network location is unavailable");
        offline.ShowRetry.Must().BeTrue();
    }

    [Theory]
    [InlineData(0, "0 items")]
    [InlineData(1, "1 item")]
    [InlineData(12, "12 items")]
    public void ItemCountText_UsesSingularForOne(int count, string expected)
        => PanePresentation.ItemCountText(count).Must().Be(expected);

    [Fact]
    public void SelectionSummary_OmitsSizeWhenNothingSelectedOrOnlyFolders()
    {
        PanePresentation.SelectionSummary([]).Must().BeNull();
        PanePresentation.SelectionSummary([new SelectionSizePart(true, 0)])
            .Must().Be("1 selected");
        PanePresentation.SelectionSummary(
            [
                new SelectionSizePart(true, 0),
                new SelectionSizePart(true, 0)
            ]).Must().Be("2 selected");
    }

    [Fact]
    public void SelectionSummary_IncludesKnownFileBytesOnly()
    {
        PanePresentation.SelectionSummary([new SelectionSizePart(false, 0)])
            .Must().Be("1 selected | 0 B");
        PanePresentation.SelectionSummary([new SelectionSizePart(false, 10)])
            .Must().Be("1 selected | 10 B");
        PanePresentation.SelectionSummary(
            [
                new SelectionSizePart(true, 0),
                new SelectionSizePart(false, 1536)
            ]).Must().Be("2 selected | 1.5 KiB");
        PanePresentation.SelectionSummary([new SelectionSizePart(false, -1)])
            .Must().Be("1 selected");
    }

    [Theory]
    [InlineData(GitFileStatus.Modified, "M", "Modified, not staged")]
    [InlineData(GitFileStatus.AddedOrStaged, "A", "Staged")]
    [InlineData(GitFileStatus.Untracked, "U", "Untracked")]
    [InlineData(GitFileStatus.Conflict, "!", "Conflict")]
    public void GitBadge_LetterAndTooltip(GitFileStatus status, string letter, string tooltip)
    {
        PanePresentation.GitLetter(status).Must().Be(letter);
        PanePresentation.GitTooltip(status).Must().Be(tooltip);
    }

    [Fact]
    public void GitBadge_NoneIsBlank()
    {
        PanePresentation.GitLetter(GitFileStatus.None).Must().BeEmpty();
        PanePresentation.GitTooltip(GitFileStatus.None).Must().BeNull();
    }

    [Fact]
    public void FormatModified_UsesTodayWithinADayAndLocaleGeneralOtherwise()
    {
        var recent = new DateTime(2026, 10, 14, 16, 3, 0, DateTimeKind.Utc);
        PanePresentation.FormatModified(recent, Now, TimeZoneInfo.Utc, EnGb)
            .Must().Be("Today 16:03");

        var older = new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);
        PanePresentation.FormatModified(older, Now, TimeZoneInfo.Utc, EnGb)
            .Must().Be(older.ToString("g", EnGb));

        PanePresentation.FormatModifiedTooltip(recent, TimeZoneInfo.Utc, EnGb)
            .Must().Be(recent.ToString("F", EnGb));

        PanePresentation.FormatModified(DateTime.MinValue, Now, TimeZoneInfo.Utc, EnGb)
            .Must().BeEmpty();
    }

    [Fact]
    public void FormatAutomationName_JoinsNameAndType()
        => PanePresentation.FormatAutomationName("Notes.txt", "TXT File").Must().Be("Notes.txt, TXT File");
}
