using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Transformation;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// The tab strip is a ListBox, which treats <c>ObservableCollection.Move</c> as remove + insert and drops its
/// selection. Reordering while the pointer was down tore down the dragged chip and nulled SelectedTab, which
/// left the content host without a DataContext (settings page and both panes drawn on top of each other).
/// </summary>
public sealed class MainWindowTabReorderTests : IDisposable
{
    private const double WindowWidth = 1600;
    private const double WindowHeight = 900;

    private readonly string _root;

    public MainWindowTabReorderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-tab-reorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(1, 2)]
    public void MoveTab_WithTheStripBound_KeepsTheSelectedTabAndContent(int from, int to)
    {
        var result = WithWindow((vm, window) =>
        {
            var moved = vm.Tabs[from];
            vm.SelectedTab = moved;

            vm.MoveTab(from, to);

            return (
                Order: vm.Tabs.IndexOf(moved),
                SelectionKept: ReferenceEquals(vm.SelectedTab, moved),
                StripSelectionKept: ReferenceEquals(TabStrip(window).SelectedItem, moved),
                ContentBound: ReferenceEquals(window.FindControl<DualPaneView>("BrowserPanes")!.DataContext, moved));
        });

        result.Order.Must().Be(to);
        result.SelectionKept.Must().BeTrue();
        result.StripSelectionKept.Must().BeTrue();
        result.ContentBound.Must().BeTrue();
    }

    [Fact]
    public void MoveTab_SelectedTabNotTheMovedOne_StaysSelected()
    {
        var result = WithWindow((vm, _) =>
        {
            var selected = vm.Tabs[1];
            vm.SelectedTab = selected;

            vm.MoveTab(0, 2);

            return ReferenceEquals(vm.SelectedTab, selected);
        });

        result.Must().BeTrue();
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(1, 1)]
    public void MoveTab_InvalidOrSameIndex_LeavesTheOrderAlone(int from, int to)
    {
        var unchanged = WithWindow((vm, _) =>
        {
            var before = vm.Tabs.ToArray();
            vm.MoveTab(from, to);
            return before.SequenceEqual(vm.Tabs);
        });

        unchanged.Must().BeTrue();
    }

    [Fact]
    public void DraggingATab_LeavesTheOrderAloneUntilDrop_ThenReordersOnceAndKeepsItSelected()
    {
        var result = WithWindow((vm, window) =>
        {
            var strip = TabStrip(window);
            var dragged = vm.Tabs[0];
            var start = ChipCentre(window, strip, 0);
            var end = ChipCentre(window, strip, 2) + new Vector(10, 0);

            // Headless input carries button state only in the modifiers, so moves must say the button is held.
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);

            var orderDuringDrag = vm.Tabs.IndexOf(dragged);
            var chipFollowsPointer = IsTranslated(strip.ContainerFromIndex(0)!);
            var neighbourMovedAside = IsTranslated(strip.ContainerFromIndex(1)!);

            window.MouseUp(end, MouseButton.Left);

            return (
                orderDuringDrag,
                chipFollowsPointer,
                neighbourMovedAside,
                OrderAfterDrop: vm.Tabs.IndexOf(dragged),
                SelectionKept: ReferenceEquals(vm.SelectedTab, dragged),
                NeighbourCleared: strip.ContainerFromIndex(0)!.RenderTransform is null);
        });

        result.orderDuringDrag.Must().Be(0);
        result.chipFollowsPointer.Must().BeTrue();
        result.neighbourMovedAside.Must().BeTrue();
        result.OrderAfterDrop.Must().Be(2);
        result.SelectionKept.Must().BeTrue();
        result.NeighbourCleared.Must().BeTrue();
    }

    [Fact]
    public void ClickingATabWithoutMoving_DoesNotReorder()
    {
        var result = WithWindow((vm, window) =>
        {
            var strip = TabStrip(window);
            var before = vm.Tabs.ToArray();
            var point = ChipCentre(window, strip, 1);

            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(3, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(point + new Vector(3, 0), MouseButton.Left);

            return (Unchanged: before.SequenceEqual(vm.Tabs), Selected: ReferenceEquals(vm.SelectedTab, before[1]));
        });

        result.Unchanged.Must().BeTrue();
        result.Selected.Must().BeTrue();
    }

    private T WithWindow<T>(Func<MainWindowViewModel, MainWindow, T> body)
    {
        var settingsPath = Path.Combine(_root, "settings.json");
        new JsonSettingsStore(settingsPath).Save(new AppSettings { RestoreSessionOnStartup = false, AutoCheckForUpdates = false });

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddHelixApplicationServices();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(settingsPath));
        services.AddSingleton<IAppDatabase>(sp =>
        {
            var db = new SqliteAppDatabase(sp.GetRequiredService<ISettingsStore>(), Path.Combine(_root, "helix.db"), settingsPath);
            db.Initialize();
            return db;
        });
        // See SidebarWidthRestoreTests: real network discovery would finish after the dispatcher job ends.
        services.AddSingleton<INetworkLocationProvider, EmptyNetworkLocations>();
        services.AddSingleton<INetworkDiscoveryAvailability, AvailableNetworkDiscovery>();

        return HeadlessSession.RunOnUiThread(() =>
        {
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var scope = provider.CreateScope();

            var vm = scope.ServiceProvider.GetRequiredService<MainWindowViewModel>();
            var window = scope.ServiceProvider.GetRequiredService<MainWindow>();
            window.DataContext = vm;
            window.Width = WindowWidth;
            window.Height = WindowHeight;
            try
            {
                window.Show();
                while (vm.Tabs.Count < 3)
                    vm.NewTabCommand.Execute(null);

                window.Measure(new Size(WindowWidth, WindowHeight));
                window.Arrange(new Rect(0, 0, WindowWidth, WindowHeight));
                return body(vm, window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // Neighbours animate their transform, so read the value the drag set rather than the in-flight animated one.
    private static bool IsTranslated(Control container)
        => container.GetBaseValue(Visual.RenderTransformProperty).GetValueOrDefault() is TransformOperations { IsIdentity: false };

    private static ListBox TabStrip(MainWindow window) => window.FindControl<ListBox>("TabStrip")!;

    private static Point ChipCentre(MainWindow window, ListBox strip, int index)
    {
        var container = strip.ContainerFromIndex(index)!;
        var centre = new Point(container.Bounds.Width / 2, container.Bounds.Height / 2);
        return container.TranslatePoint(centre, window)!.Value;
    }

    private sealed class EmptyNetworkLocations : INetworkLocationProvider
    {
        public ValueTask<NetworkDiscoveryResult> GetNetworkLocationsAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NetworkDiscoveryResult.Empty);
    }

    private sealed class AvailableNetworkDiscovery : INetworkDiscoveryAvailability
    {
        public bool IsUnavailable => false;
        public event EventHandler? AvailabilityChanged;
        public void Refresh() => AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
