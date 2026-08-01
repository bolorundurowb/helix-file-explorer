using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.FileSystem;

public sealed class MacNetworkDiscoveryAvailability(ILogger<MacNetworkDiscoveryAvailability> logger) : INetworkDiscoveryAvailability
{
    private bool _wasUnavailable;

    public bool IsUnavailable { get; private set; }

    public event EventHandler? AvailabilityChanged;

    public void Refresh()
    {
        var nowUnavailable = !HasNetworkInterface();
        var changed = nowUnavailable != _wasUnavailable;
        _wasUnavailable = nowUnavailable;
        IsUnavailable = nowUnavailable;

        if (changed)
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool HasNetworkInterface()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            return interfaces.Any(nic =>
                nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                && nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Network interface detection failed");
            return false;
        }
    }
}