using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.ViewModels.Tests;

public class WindowLayoutCoordinatorTests
{
    [Theory]
    [InlineData(100, 200)]
    [InlineData(300, 300)]
    [InlineData(600, 450)]
    public void ClampSidebarWidth_EnforcesSupportedRange(double value, double expected)
        => WindowLayoutCoordinator.ClampSidebarWidth(value).Must().Be(expected);
}

public class PaneThumbnailCoordinatorTests
{
    [Fact]
    public void ApplySizeChange_InvalidValueClampsWithoutPublishingIntermediateState()
    {
        using var coordinator = new PaneThumbnailCoordinator(
            NullLogger<PaneThumbnailCoordinator>.Instance);
        var callbackCount = 0;

        var result = coordinator.ApplySizeChange(new PaneThumbnailSizeChange(
            500,
            IsGridView: true,
            RequestEntryVisuals: () => callbackCount++,
            OnLayoutChanged: () => callbackCount++,
            PersistPreferences: () => callbackCount++));

        result.Must().Be(PaneViewModel.MaxThumbnailSize);
        callbackCount.Must().Be(0);
    }

    [Fact]
    public void ApplyViewModeChange_NonGridReloadsAndPublishesLayout()
    {
        using var coordinator = new PaneThumbnailCoordinator(
            NullLogger<PaneThumbnailCoordinator>.Instance);
        var calls = new List<string>();

        coordinator.ApplyViewModeChange(new PaneThumbnailViewModeChange(
            IsGridView: false,
            RequestEntryVisuals: () => calls.Add("visuals"),
            RebuildGridItems: () => calls.Add("grid"),
            NotifyGroupProperties: () => calls.Add("groups"),
            OnLayoutChanged: () => calls.Add("layout"),
            PersistPreferences: () => calls.Add("persist")));

        calls.Must().BeSequenceEqual(["visuals", "grid", "groups", "layout", "persist"]);
    }
}

public class PaneInlineRenameCoordinatorTests
{
    [Fact]
    public void BeginAndClear_KeepPaneAndEntryStateInSync()
    {
        var entry = CreateEntry();
        var host = new RenameHost([entry]);
        var coordinator = new PaneInlineRenameCoordinator(null!);

        coordinator.Begin(host);

        host.IsRenaming.Must().BeTrue();
        host.RenameText.Must().Be(entry.Name);
        entry.IsRenaming.Must().BeTrue();
        entry.RenameText.Must().Be(entry.Name);

        coordinator.Clear(host);

        host.IsRenaming.Must().BeFalse();
        host.RenameText.Must().BeEmpty();
        entry.IsRenaming.Must().BeFalse();
        entry.RenameText.Must().BeEmpty();
    }

    [Fact]
    public async Task CommitAsync_WhitespaceNameClearsWithoutFileOperation()
    {
        var entry = CreateEntry();
        var host = new RenameHost([entry]);
        var coordinator = new PaneInlineRenameCoordinator(null!);
        coordinator.Begin(host);
        entry.RenameText = "   ";

        await coordinator.CommitAsync(host);

        host.IsRenaming.Must().BeFalse();
        entry.IsRenaming.Must().BeFalse();
    }

    private static EntryItemViewModel CreateEntry()
        => new(
            new FileSystemEntry(
                @"C:\folder\document.txt",
                "document.txt",
                false,
                10,
                DateTime.UtcNow,
                ".txt"),
            showFileExtensions: true);

    private sealed class RenameHost(IReadOnlyList<EntryItemViewModel> entries) : IPaneInlineRenameHost
    {
        public IReadOnlyList<EntryItemViewModel> SelectedEntries => entries;
        public bool IsRenaming { get; set; }
        public string RenameText { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public Task RefreshAfterRenameAsync(string oldPath) => Task.CompletedTask;
    }
}

public class NetworkLocationCoordinatorTests
{
    [Fact]
    public void NoteBrowsing_RecognizesUncPathAndClearsNotice()
    {
        using var coordinator = new NetworkLocationCoordinator(
            new EmptyNetworkLocations(),
            new AvailableNetworkDiscovery(),
            new InlineDispatcher());
        var host = new NetworkHost { HasNetworkNotice = true };

        coordinator.NoteBrowsing(host, @"\\server\share");

        host.HasNetworkNotice.Must().BeFalse();
    }

    private sealed class EmptyNetworkLocations : INetworkLocationProvider
    {
        public ValueTask<NetworkDiscoveryResult> GetNetworkLocationsAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new NetworkDiscoveryResult([], NetworkDiscoveryStatus.NoLocationsFound));
    }

    private sealed class AvailableNetworkDiscovery : INetworkDiscoveryAvailability
    {
        public bool IsUnavailable => false;
        public event EventHandler? AvailabilityChanged;
        public void Refresh() => AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class InlineDispatcher : IUiThreadDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class NetworkHost : INetworkLocationHost
    {
        public bool IsDisposed => false;
        public bool IsDiscoveringNetwork { get; set; }
        public bool HasNetworkNotice { get; set; }
        public string NetworkBannerText { get; set; } = string.Empty;
        public void RebuildSidebar(IReadOnlyList<NetworkLocationInfo> locations) { }
        public void RefreshHomeDashboard(IReadOnlyList<NetworkLocationInfo> locations) { }
    }
}
