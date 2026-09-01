using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Windows.Shell;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Mirrors Explorer's Network-folder prompt using late-bound IDispatch (dual) COM, not
/// hand-rolled IUnknown vtables. Refresh is off the UI thread so activation does not hitch.
/// </summary>
public sealed class WinNetworkDiscoveryAvailability : INetworkDiscoveryAvailability, IDisposable
{
    private const string NetworkDiscoveryFirewallGroup = "@FirewallAPI.dll,-32752";
    private static readonly string[] DiscoveryServices = ["FDResPub", "SSDPSRV"];
    private static readonly Guid NetworkListManagerClsid = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    private readonly ILogger<WinNetworkDiscoveryAvailability> _logger;
    private readonly object _gate = new();
    private int _refreshBusy;
    private bool _isUnavailable;
    private int _disposed;

    public WinNetworkDiscoveryAvailability(ILogger<WinNetworkDiscoveryAvailability> logger)
    {
        _logger = logger;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        Refresh();
    }

    public bool IsUnavailable
    {
        get
        {
            lock (_gate)
                return _isUnavailable;
        }
    }

    public event EventHandler? AvailabilityChanged;

    public void Refresh()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (Interlocked.Exchange(ref _refreshBusy, 1) == 1)
            return;

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var evaluation = STATask.Run(Evaluate);
            var unavailable = await NativeCallTimeout
                .AwaitAsync(evaluation, TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
            bool changed;
            lock (_gate)
            {
                if (_disposed != 0)
                    return;
                changed = unavailable != _isUnavailable;
                _isUnavailable = unavailable;
            }

            if (changed)
            {
                try
                {
                    AvailabilityChanged?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Network availability subscriber failed");
                }
            }
        }
        catch (System.TimeoutException)
        {
            _logger.LogDebug("Network discovery availability check timed out");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshBusy, 0);
        }
    }

    private bool Evaluate()
    {
        try
        {
            if (!HasConnectedNetwork())
                return false;

            if (IsNetworkDiscoveryFirewallEnabled())
                return false;

            if (HasConnectedPrivateNetwork() && AreDiscoveryServicesDisabled())
                return true;

            return HasConnectedPrivateOrPublicNetwork();
        }
        catch (COMException ex)
        {
            _logger.LogWarning(ex, "Network discovery COM check failed; assuming available");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Network discovery availability check failed; assuming available");
            return false;
        }
    }

    private static bool HasConnectedNetwork()
    {
        if (TryNlmConnectivity(out var connected) && connected)
            return true;
        return NetworkInterface.GetIsNetworkAvailable();
    }

    private static bool TryNlmConnectivity(out bool connected)
    {
        connected = false;
        var type = Type.GetTypeFromCLSID(NetworkListManagerClsid);
        if (type is null)
            return false;

        dynamic manager = Activator.CreateInstance(type)!;
        try
        {
            int connectivity = manager.GetConnectivity();
            const int flags = 0x10 | 0x20 | 0x40 | 0x100 | 0x200 | 0x400;
            connected = (connectivity & flags) != 0;
            return true;
        }
        finally
        {
            if (manager is IDisposable d)
                d.Dispose();
            else
                Marshal.FinalReleaseComObject(manager);
        }
    }

    private static bool HasConnectedPrivateNetwork()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Any(n => n.OperationalStatus == OperationalStatus.Up
                      && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                      && n.GetIPProperties().GetIPv4Properties() is { IsAutomaticPrivateAddressingActive: false });

    private static bool HasConnectedPrivateOrPublicNetwork()
        => NetworkInterface.GetIsNetworkAvailable();

    private static bool IsNetworkDiscoveryFirewallEnabled()
    {
        var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        if (policyType is null)
            return false;

        dynamic policy = Activator.CreateInstance(policyType)!;
        try
        {
            int profiles = policy.CurrentProfileTypes;
            if (profiles == 0)
                return false;
            return (bool)policy.IsRuleGroupEnabled(profiles, NetworkDiscoveryFirewallGroup);
        }
        finally
        {
            if (policy is IDisposable d)
                d.Dispose();
            else
                Marshal.FinalReleaseComObject(policy);
        }
    }

    private static bool AreDiscoveryServicesDisabled()
    {
        foreach (var serviceName in DiscoveryServices)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                if (service.StartType != ServiceStartMode.Disabled)
                    return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return true;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Refresh();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
    }
}
