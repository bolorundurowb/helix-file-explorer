using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Mirrors the signals Windows File Explorer uses for its Network-folder prompt: firewall
/// "Network Discovery" rules for the active profile and the SSDP / FDResPub services.
/// </summary>
public sealed class WinNetworkDiscoveryAvailability : INetworkDiscoveryAvailability, IDisposable
{
    private const string NetworkDiscoveryFirewallGroup = "@FirewallAPI.dll,-32752";

    private static readonly string[] DiscoveryServices = ["FDResPub", "SSDPSRV"];

    private readonly ILogger<WinNetworkDiscoveryAvailability> _logger;
    private readonly object _gate = new();
    private bool _isUnavailable;
    private bool _disposed;

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
        var unavailable = Evaluate();
        bool changed;
        lock (_gate)
        {
            changed = unavailable != _isUnavailable;
            _isUnavailable = unavailable;
        }

        if (changed)
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
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

            // Firewall rules off for the active profile — same condition Explorer surfaces.
            return HasConnectedPrivateOrPublicNetwork();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Network discovery availability check failed; assuming available");
            return false;
        }
    }

    private static INetworkListManager CreateNetworkListManager()
        => (INetworkListManager)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"))!)!;

    private static bool HasConnectedNetwork()
    {
        var manager = CreateNetworkListManager();
        var flags = (int)NLM_CONNECTIVITY.NLM_CONNECTIVITY_IPV4_INTERNET
                    | (int)NLM_CONNECTIVITY.NLM_CONNECTIVITY_IPV4_LOCALNETWORK
                    | (int)NLM_CONNECTIVITY.NLM_CONNECTIVITY_IPV4_SUBNET
                    | (int)NLM_CONNECTIVITY.NLM_CONNECTIVITY_IPV6_INTERNET
                    | (int)NLM_CONNECTIVITY.NLM_CONNECTIVITY_IPV6_LOCALNETWORK
                    | (int)NLM_CONNECTIVITY.NLM_CONNECTIVITY_IPV6_SUBNET;

        return ((int)ReadConnectivity(manager) & flags) != 0;
    }

    private static bool HasConnectedPrivateNetwork()
        => GetConnectedCategories().Contains(NLM_NETWORK_CATEGORY.NLM_NETWORK_CATEGORY_PRIVATE);

    private static bool HasConnectedPrivateOrPublicNetwork()
    {
        var categories = GetConnectedCategories();
        return categories.Contains(NLM_NETWORK_CATEGORY.NLM_NETWORK_CATEGORY_PRIVATE)
               || categories.Contains(NLM_NETWORK_CATEGORY.NLM_NETWORK_CATEGORY_PUBLIC);
    }

    private static HashSet<NLM_NETWORK_CATEGORY> GetConnectedCategories()
    {
        var categories = new HashSet<NLM_NETWORK_CATEGORY>();
        var manager = CreateNetworkListManager();
        foreach (var network in EnumerateConnectedNetworks(manager))
        {
            if (network.IsConnectedToInternet || network.IsConnected)
                categories.Add(network.GetCategory());
        }

        return categories;
    }

    private static bool IsNetworkDiscoveryFirewallEnabled()
    {
        var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        if (policyType is null)
            return false;

        var policy = (INetFwPolicy2)Activator.CreateInstance(policyType)!;
        var activeProfiles = policy.CurrentProfileTypes;
        if (activeProfiles == 0)
            return false;

        var rules = policy.Rules;
        var count = rules.Count;
        for (var i = 1; i <= count; i++)
        {
            var rule = rules.Item(i);
            if (!string.Equals(rule.Grouping, NetworkDiscoveryFirewallGroup, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!rule.Enabled)
                continue;

            if ((rule.Profiles & activeProfiles) == 0)
                continue;

            return true;
        }

        return false;
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

    private static IEnumerable<INetwork> EnumerateConnectedNetworks(INetworkListManager manager)
    {
        if (manager.GetNetworks(NLM_ENUM_NETWORK.NLM_ENUM_NETWORK_CONNECTED, out var enumNetworks) != 0
            || enumNetworks is null)
        {
            yield break;
        }

        var buffer = new INetwork[1];
        while (true)
        {
            var fetched = 0u;
            var hr = enumNetworks.Next(1, buffer, out fetched);
            if (hr != 0 || fetched == 0)
                yield break;

            yield return buffer[0];
        }
    }

    private static NLM_CONNECTIVITY ReadConnectivity(INetworkListManager manager)
    {
        manager.GetConnectivity(out var connectivity);
        return connectivity;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Refresh();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
    }
}

[ComImport]
[Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetworkListManager
{
    [PreserveSig]
    int GetNetworks(NLM_ENUM_NETWORK flags, [MarshalAs(UnmanagedType.Interface)] out IEnumNetworks? ppEnumNetwork);

    void GetConnectivity(out NLM_CONNECTIVITY pConnectivity);
}

[ComImport]
[Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumNetworks
{
    [PreserveSig]
    int Next(
        uint celt,
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)]
        INetwork[] rgelt,
        out uint pceltFetched);
}

[ComImport]
[Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetwork
{
    NLM_NETWORK_CATEGORY GetCategory();

    bool IsConnectedToInternet { get; }

    bool IsConnected { get; }
}

internal enum NLM_ENUM_NETWORK
{
    NLM_ENUM_NETWORK_CONNECTED = 0x1
}

[Flags]
internal enum NLM_CONNECTIVITY
{
    NLM_CONNECTIVITY_IPV4_SUBNET = 0x10,
    NLM_CONNECTIVITY_IPV4_LOCALNETWORK = 0x20,
    NLM_CONNECTIVITY_IPV4_INTERNET = 0x40,
    NLM_CONNECTIVITY_IPV6_SUBNET = 0x100,
    NLM_CONNECTIVITY_IPV6_LOCALNETWORK = 0x200,
    NLM_CONNECTIVITY_IPV6_INTERNET = 0x400
}

internal enum NLM_NETWORK_CATEGORY
{
    NLM_NETWORK_CATEGORY_PUBLIC = 0,
    NLM_NETWORK_CATEGORY_PRIVATE = 1,
    NLM_NETWORK_CATEGORY_DOMAIN = 2
}

[ComImport]
[Guid("E2B3C97F-6AE1-41AC-817A-F6F92166D7DD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetFwPolicy2
{
    int CurrentProfileTypes { get; }

    INetFwRules Rules { get; }
}

[ComImport]
[Guid("9C4C6277-5027-441E-AFAE-CA1F542DA009")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetFwRules
{
    int Count { get; }

    INetFwRule Item(int index);
}

[ComImport]
[Guid("2C5BC601-0896-411F-9A18-55334EFC6FD5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetFwRule
{
    string Grouping { get; }

    bool Enabled { get; }

    int Profiles { get; }
}
