using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface INetworkLocationProvider
{
    /// <summary>
    /// Enumerates visible network locations. An empty result only means nothing was found —
    /// use <see cref="INetworkDiscoveryAvailability"/> to determine whether discovery is disabled.
    /// </summary>
    ValueTask<NetworkDiscoveryResult> GetNetworkLocationsAsync(CancellationToken cancellationToken = default);
}
