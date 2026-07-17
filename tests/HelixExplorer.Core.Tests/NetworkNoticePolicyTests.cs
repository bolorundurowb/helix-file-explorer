using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public class NetworkNoticePolicyTests
{
    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void ShouldShowUnavailableNotice_RequiresUnavailableAvailability(
        bool browsingVerified,
        bool hasLocations,
        bool discoveryAvailable,
        bool expected)
    {
        var actual = NetworkNoticePolicy.ShouldShowUnavailableNotice(
            browsingVerified,
            hasLocations,
            isUnavailable: !discoveryAvailable);

        actual.Must().Be(expected);
    }
}
