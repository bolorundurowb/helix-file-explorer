namespace HelixExplorer.Core.FileSystem;

public static class NetworkNoticePolicy
{
    public static bool ShouldShowUnavailableNotice(
        bool browsingVerified,
        bool hasLocations,
        bool isUnavailable)
        => !browsingVerified && !hasLocations && isUnavailable;
}
