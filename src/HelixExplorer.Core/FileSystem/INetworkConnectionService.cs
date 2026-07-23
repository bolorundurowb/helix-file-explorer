namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Establishes authenticated SMB/UNC connections when browsing requires credentials.
/// </summary>
public interface INetworkConnectionService
{
    /// <summary>
    /// Ensures <paramref name="uncPath"/> is reachable, prompting for credentials when required.
    /// Returns <c>true</c> when a connection was established or the path is already accessible.
    /// </summary>
    ValueTask<bool> EnsureConnectedAsync(string uncPath, CancellationToken cancellationToken = default);
}
