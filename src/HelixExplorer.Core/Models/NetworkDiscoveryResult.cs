namespace HelixExplorer.Core.Models;

/// <summary>Outcome of a network-location discovery pass, so the UI can distinguish
/// "nothing is shared / visible" from "discovery could not run".</summary>
public enum NetworkDiscoveryStatus
{
    /// <summary>Discovery ran and returned one or more locations.</summary>
    Discovered,

    /// <summary>Discovery ran successfully but found no network locations.</summary>
    NoLocationsFound,

    /// <summary>Discovery failed (provider unavailable, timed out, or errored).</summary>
    DiscoveryFailed
}

/// <summary>Result of a network discovery pass: the locations plus a status for UI messaging.</summary>
public sealed record NetworkDiscoveryResult(
    IReadOnlyList<NetworkLocationInfo> Locations,
    NetworkDiscoveryStatus Status)
{
    public static readonly NetworkDiscoveryResult Empty =
        new(Array.Empty<NetworkLocationInfo>(), NetworkDiscoveryStatus.NoLocationsFound);

    public static NetworkDiscoveryResult Failed(IReadOnlyList<NetworkLocationInfo>? partial = null) =>
        new(partial ?? Array.Empty<NetworkLocationInfo>(), NetworkDiscoveryStatus.DiscoveryFailed);

    /// <summary>Builds a result, choosing <see cref="NetworkDiscoveryStatus.NoLocationsFound"/> when empty.</summary>
    public static NetworkDiscoveryResult From(IReadOnlyList<NetworkLocationInfo> locations) =>
        new(locations, locations.Count == 0 ? NetworkDiscoveryStatus.NoLocationsFound : NetworkDiscoveryStatus.Discovered);
}
