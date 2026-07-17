namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Reports whether Windows network discovery is disabled or required services are unavailable.
/// Enumeration results alone are not a reliable signal — an empty sidebar can still mean discovery works.
/// </summary>
public interface INetworkDiscoveryAvailability
{
    /// <summary>
    /// True only when the OS settings or services positively block network discovery.
    /// </summary>
    bool IsUnavailable { get; }

    event EventHandler? AvailabilityChanged;

    void Refresh();
}
