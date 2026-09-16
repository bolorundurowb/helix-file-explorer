using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Models;
using HelixExplorer.Localization;

namespace HelixExplorer.ViewModels;

public interface INetworkLocationHost
{
    bool IsDisposed { get; }
    bool IsDiscoveringNetwork { get; set; }
    bool HasNetworkNotice { get; set; }
    string NetworkBannerText { get; set; }

    void RebuildSidebar(IReadOnlyList<NetworkLocationInfo> locations);
    void RefreshHomeDashboard(IReadOnlyList<NetworkLocationInfo> locations);
}

public sealed class NetworkLocationCoordinator(
    INetworkLocationProvider networkLocations,
    INetworkDiscoveryAvailability networkAvailability,
    IUiThreadDispatcher dispatcher) : IDisposable
{
    private IReadOnlyList<NetworkLocationInfo> _locations = [];
    private CancellationTokenSource? _cancellation;
    private bool _networkBrowsingVerified;
    private bool _disposed;

    public IReadOnlyList<NetworkLocationInfo> Locations => _locations;

    public event EventHandler? AvailabilityChanged;

    public void Start(INetworkLocationHost host)
    {
        networkAvailability.AvailabilityChanged += OnAvailabilityChanged;
        _ = RefreshAsync(host);
    }

    public async Task RefreshAsync(INetworkLocationHost? host = null)
    {
        if (_disposed)
            return;

        var previous = Interlocked.Exchange(ref _cancellation, new CancellationTokenSource());
        try { previous?.Cancel(); } catch (ObjectDisposedException) { }
        previous?.Dispose();

        var cts = _cancellation!;
        var cancellationToken = cts.Token;
        if (host is not null)
        {
            host.IsDiscoveringNetwork = true;
            host.HasNetworkNotice = false;
            host.NetworkBannerText = UiStrings.NetworkDiscoveryBanner;
        }

        try
        {
            networkAvailability.Refresh();
            var result = await networkLocations.GetNetworkLocationsAsync(cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested || _disposed)
                return;

            _locations = result.Locations;
            if (result.Status == NetworkDiscoveryStatus.Discovered)
                _networkBrowsingVerified = true;

            if (host is not null && !host.IsDisposed)
            {
                host.RebuildSidebar(_locations);
                UpdateNotice(host);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && host is not null && !host.IsDisposed)
            {
                host.IsDiscoveringNetwork = false;
                host.RefreshHomeDashboard(_locations);
            }
        }
    }

    public void UpdateNotice(INetworkLocationHost host)
    {
        if (!NetworkNoticePolicy.ShouldShowUnavailableNotice(
                _networkBrowsingVerified,
                _locations.Count > 0,
                networkAvailability.IsUnavailable))
        {
            host.HasNetworkNotice = false;
            return;
        }

        host.NetworkBannerText = UiStrings.NetworkDiscoveryFailed;
        host.HasNetworkNotice = true;
    }

    public void RefreshAvailability() => networkAvailability.Refresh();

    public void NoteBrowsing(INetworkLocationHost host, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!NetworkPath.IsUnc(path) && !NetworkPath.IsNetworkRoot(path)))
        {
            return;
        }

        _networkBrowsingVerified = true;
        host.HasNetworkNotice = false;
    }

    private void OnAvailabilityChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        dispatcher.Post(() => AvailabilityChanged?.Invoke(this, EventArgs.Empty));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        networkAvailability.AvailabilityChanged -= OnAvailabilityChanged;
        var cts = Interlocked.Exchange(ref _cancellation, null);
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        cts?.Dispose();
    }
}
