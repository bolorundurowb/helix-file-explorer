namespace HelixExplorer.Core.Models;

/// <summary>
/// Distinguishes "nothing shared / visible" from "discovery could not run" for UI messaging.
/// </summary>
public enum NetworkDiscoveryStatus
{
    Discovered,
    NoLocationsFound,
    DiscoveryFailed
}

public sealed record NetworkDiscoveryResult(
    IReadOnlyList<NetworkLocationInfo> Locations,
    NetworkDiscoveryStatus Status)
{
    public static readonly NetworkDiscoveryResult Empty =
        new(Array.Empty<NetworkLocationInfo>(), NetworkDiscoveryStatus.NoLocationsFound);

    public static NetworkDiscoveryResult Failed(IReadOnlyList<NetworkLocationInfo>? partial = null) =>
        new(partial ?? Array.Empty<NetworkLocationInfo>(), NetworkDiscoveryStatus.DiscoveryFailed);

    public static NetworkDiscoveryResult From(IReadOnlyList<NetworkLocationInfo> locations) =>
        new(locations, locations.Count == 0 ? NetworkDiscoveryStatus.NoLocationsFound : NetworkDiscoveryStatus.Discovered);
}
