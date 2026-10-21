using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    // The pointer tests raise the chip's pointer events directly rather than going through headless mouse input:
    // the tab strip sits in the window's title bar, which headless hit testing does not route to reliably, and
    // what is under test here is the drag handling, not Avalonia's hit testing.
    [Fact]
    public void DraggingATab_LeavesTheOrderAloneUntilDrop_ThenReordersOnceAndKeepsItSelected()
    {
        var result = WithWindow((vm, window) =>
        {
            var strip = TabStrip(window);
            var dragged = vm.Tabs[0];
            vm.SelectedTab = dragged;

            var chip = Chip(strip, 0);
            var mouse = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var start = ChipCentre(window, strip, 0);
            var end = ChipCentre(window, strip, 2) + new Vector(10, 0);

            Press(window, chip, mouse, start);
            Move(window, chip, mouse, start + new Vector(20, 0));
            Move(window, chip, mouse, end);

            var orderDuringDrag = vm.Tabs.IndexOf(dragged);
            var chipFollowsPointer = IsTranslated(strip.ContainerFromIndex(0)!);
            var neighbourMovedAside = IsTranslated(strip.ContainerFromIndex(1)!);

            Release(window, chip, mouse, end);
            Dispatcher.UIThread.RunJobs();

            return (
                orderDuringDrag,
                chipFollowsPointer,
                neighbourMovedAside,
                OrderAfterDrop: vm.Tabs.IndexOf(dragged),
                SelectionKept: ReferenceEquals(vm.SelectedTab, dragged),
                ContentBound: ReferenceEquals(window.FindControl<DualPaneView>("BrowserPanes")!.DataContext, dragged),
                NeighbourCleared: strip.ContainerFromIndex(0)!.RenderTransform is null);
        });

        result.orderDuringDrag.Must().Be(0);
        result.chipFollowsPointer.Must().BeTrue();
        result.neighbourMovedAside.Must().BeTrue();
        result.OrderAfterDrop.Must().Be(2);
        result.SelectionKept.Must().BeTrue();
        result.ContentBound.Must().BeTrue();
        result.NeighbourCleared.Must().BeTrue();
    }

    [Fact]
    public void PressingATabWithoutPassingTheThreshold_DoesNotReorderOrMoveAnything()
    {
        var result = WithWindow((vm, window) =>
        {
            var strip = TabStrip(window);
            var before = vm.Tabs.ToArray();
            var chip = Chip(strip, 1);
            var mouse = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var point = ChipCentre(window, strip, 1);

            Press(window, chip, mouse, point);
            Move(window, chip, mouse, point + new Vector(3, 0));
            var translated = Enumerable.Range(0, strip.ItemCount).Any(i => IsTranslated(strip.ContainerFromIndex(i)!));
            Release(window, chip, mouse, point + new Vector(3, 0));

            return (Unchanged: before.SequenceEqual(vm.Tabs), translated);
        });

        result.Unchanged.Must().BeTrue();
        result.translated.Must().BeFalse();
    }

    [Fact]
    public void LosingCaptureMidDrag_CancelsWithoutReordering()
    {
        var result = WithWindow((vm, window) =>
        {
            var strip = TabStrip(window);
            var before = vm.Tabs.ToArray();
            var chip = Chip(strip, 0);
            var mouse = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var start = ChipCentre(window, strip, 0);
            var end = ChipCentre(window, strip, 2) + new Vector(10, 0);

            Press(window, chip, mouse, start);
            Move(window, chip, mouse, start + new Vector(20, 0));
            Move(window, chip, mouse, end);
            chip.RaiseEvent(new PointerCaptureLostEventArgs(chip, mouse));

            // A release after the cancel (as when focus returns) must not commit the abandoned drag.
            Release(window, chip, mouse, end);

            var translated = Enumerable.Range(0, strip.ItemCount).Any(i => IsTranslated(strip.ContainerFromIndex(i)!));
            return (Unchanged: before.SequenceEqual(vm.Tabs), translated);
        });

        result.Unchanged.Must().BeTrue();
        result.translated.Must().BeFalse();
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
                // Tabs added after Show only get their item templates realised by a full layout pass.
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
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

    private static Control Chip(ListBox strip, int index)
    {
        var container = strip.ContainerFromIndex(index)
            ?? throw new InvalidOperationException($"Tab strip has no container for index {index} (ItemCount {strip.ItemCount}).");

        if (container is ContentControl contentControl)
            contentControl.ApplyTemplate();

        var chip = container.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "TabChip");
        if (chip is not null)
            return chip;

        // Report what the container actually holds so a template or layout change is obvious from the failure.
        var tree = string.Join(", ", container.GetVisualDescendants()
            .Select(v => $"{v.GetType().Name}{(v is Control { Name: { } name } ? "#" + name : string.Empty)}"));
        throw new InvalidOperationException(
            $"No TabChip under tab container {index} ({container.GetType().Name}, bounds {container.Bounds}, " +
            $"visible {container.IsEffectivelyVisible}). Visual descendants: [{tree}]");
    }

    private static void Press(MainWindow window, Control chip, IPointer pointer, Point position)
        => chip.RaiseEvent(new PointerPressedEventArgs(
            chip, pointer, window, position, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));

    private static void Move(MainWindow window, Control chip, IPointer pointer, Point position)
        => chip.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent, chip, pointer, window, position, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));

    private static void Release(MainWindow window, Control chip, IPointer pointer, Point position)
        => chip.RaiseEvent(new PointerReleasedEventArgs(
            chip, pointer, window, position, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));

    private static Point ChipCentre(MainWindow window, ListBox strip, int index)
    {
        var chip = Chip(strip, index);
        if (chip.Bounds.Width <= 0)
            throw new InvalidOperationException($"Tab chip {index} was never laid out (bounds {chip.Bounds}).");

        var centre = new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2);
        return chip.TranslatePoint(centre, window)!.Value;
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
