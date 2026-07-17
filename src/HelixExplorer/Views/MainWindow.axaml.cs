using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _layoutSaveTimer;

    public MainWindow()
    {
        InitializeComponent();
        Activated += (_, _) => SetWindowActive(true);
        Deactivated += (_, _) => SetWindowActive(false);
        Opened += OnOpened;
        Closing += OnClosing;
        PositionChanged += OnLayoutChanged;
        SizeChanged += OnLayoutChanged;
        PropertyChanged += OnWindowPropertyChanged;
        SidebarSplitter.DragCompleted += OnSidebarDragCompleted;

        AttachTabOverflowHandlers();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ApplyWindowLayout(this);
            ApplyOpenInTerminalKeyBinding(vm.OpenInTerminalGesture);
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        Dispatcher.UIThread.Post(() =>
        {
            ScrollToSelectedTab();
            UpdateTabOverflow();
        }, DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.OpenInTerminalGesture)
            && DataContext is MainWindowViewModel vm)
        {
            ApplyOpenInTerminalKeyBinding(vm.OpenInTerminalGesture);
        }
    }

    private void AttachTabOverflowHandlers()
    {
        if (TabStripHost is null || TabScrollViewer is null || TabStrip is null)
            return;

        TabScrollViewer.Transitions = new Transitions
        {
            new VectorTransition
            {
                Property = ScrollViewer.OffsetProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new CubicEaseOut()
            }
        };

        TabStripHost.AddHandler(PointerWheelChangedEvent, OnTabStripWheel, RoutingStrategies.Tunnel, handledEventsToo: false);
        TabScrollViewer.ScrollChanged += OnTabScrollChanged;
        TabScrollViewer.SizeChanged += OnTabScrollSizeChanged;
        TabStripContainer.SizeChanged += OnTabScrollSizeChanged;
        TabStrip.SelectionChanged += OnTabStripSelectionChanged;
    }

    private void OnTabScrollChanged(object? sender, ScrollChangedEventArgs e)
        => UpdateTabOverflow();

    private void OnTabScrollSizeChanged(object? sender, SizeChangedEventArgs e)
        => UpdateTabOverflow();

    private void OnTabStripSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ScrollToSelectedTab();
            UpdateTabOverflow();
        }, DispatcherPriority.Loaded);
    }

    private void UpdateTabOverflow()
    {
        if (TabScrollViewer is null || TabScrollLeftButton is null || TabScrollRightButton is null)
            return;

        var extent = TabScrollViewer.Extent;
        var viewport = TabScrollViewer.Viewport;
        var offset = TabScrollViewer.Offset;

        // Measure overflow against the full host width, not the current viewport. This prevents
        // a layout ping-pong where showing the arrows shrinks the viewport and toggles overflow.
        var availableWidth = TabStripHost?.Bounds.Width ?? viewport.Width;
        if (availableWidth <= 0)
            availableWidth = viewport.Width;

        if (availableWidth <= 0 || extent.Width <= 0)
            return;

        const double epsilon = 0.5;
        var hasOverflow = extent.Width > availableWidth + epsilon;
        var canScrollLeft = hasOverflow && offset.X > epsilon;
        var canScrollRight = hasOverflow && offset.X + viewport.Width < extent.Width - epsilon;

        TabScrollLeftButton.IsVisible = hasOverflow;
        TabScrollRightButton.IsVisible = hasOverflow;
        TabScrollLeftButton.IsEnabled = canScrollLeft;
        TabScrollRightButton.IsEnabled = canScrollRight;
    }

    private void ScrollToSelectedTab()
    {
        if (TabScrollViewer is null || TabStrip is null)
            return;

        var selectedIndex = TabStrip.SelectedIndex;
        if (selectedIndex < 0)
            return;

        var container = TabStrip.ContainerFromIndex(selectedIndex);
        if (container is null)
            return;

        var bounds = container.Bounds;
        if (bounds.Width <= 0)
            return;

        var itemLeft = bounds.X;
        var itemRight = bounds.X + bounds.Width;
        var viewportLeft = TabScrollViewer.Offset.X;
        var viewportRight = viewportLeft + TabScrollViewer.Viewport.Width;

        if (itemLeft < viewportLeft)
        {
            TabScrollViewer.Offset = new Vector(itemLeft, 0);
        }
        else if (itemRight > viewportRight)
        {
            TabScrollViewer.Offset = new Vector(itemRight - TabScrollViewer.Viewport.Width, 0);
        }
    }

    private void OnTabScrollLeftClick(object? sender, RoutedEventArgs e)
    {
        if (TabScrollViewer is null)
            return;

        var amount = Math.Max(200, TabScrollViewer.Viewport.Width * 0.5);
        var newOffset = Math.Max(0, TabScrollViewer.Offset.X - amount);
        TabScrollViewer.Offset = new Vector(newOffset, 0);
    }

    private void OnTabScrollRightClick(object? sender, RoutedEventArgs e)
    {
        if (TabScrollViewer is null)
            return;

        var amount = Math.Max(200, TabScrollViewer.Viewport.Width * 0.5);
        var maxOffset = Math.Max(0, TabScrollViewer.Extent.Width - TabScrollViewer.Viewport.Width);
        var newOffset = Math.Min(maxOffset, TabScrollViewer.Offset.X + amount);
        TabScrollViewer.Offset = new Vector(newOffset, 0);
    }

    private void ApplyOpenInTerminalKeyBinding(string gesture)
    {
        var binding = KeyBindings.OfType<KeyBinding>().FirstOrDefault(b => b.Command is { } cmd
            && cmd == (DataContext as MainWindowViewModel)?.OpenInTerminalCommand);

        if (binding is null)
            return;

        try
        {
            binding.Gesture = KeyGesture.Parse(gesture);
        }
        catch
        {
            binding.Gesture = KeyGesture.Parse(MainWindowViewModel.AppDefaultTerminalGesture);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _layoutSaveTimer?.Stop();
        if (DataContext is MainWindowViewModel vm)
            vm.CaptureWindowLayout(this);
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.ShouldRestoreWindowLayout)
            return;

        if (WindowState != WindowState.Normal)
            return;

        ScheduleLayoutSave(vm);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty || DataContext is not MainWindowViewModel vm || !vm.ShouldRestoreWindowLayout)
            return;

        ScheduleLayoutSave(vm);
    }

    private void ScheduleLayoutSave(MainWindowViewModel vm)
    {
        _layoutSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Tick -= OnLayoutSaveTimerTick;
        _layoutSaveTimer.Tick += OnLayoutSaveTimerTick;
        _layoutSaveTimer.Tag = vm;
        _layoutSaveTimer.Start();
    }

    private void OnLayoutSaveTimerTick(object? sender, EventArgs e)
    {
        if (_layoutSaveTimer is null)
            return;

        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Tick -= OnLayoutSaveTimerTick;

        if (_layoutSaveTimer.Tag is MainWindowViewModel vm)
            vm.CaptureWindowLayout(this);
    }

    private void OnSidebarDragCompleted(object? sender, VectorEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.SyncSidebarWidth(SidebarBorder.Bounds.Width);
    }

    private void SetWindowActive(bool isActive)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.IsWindowActive = isActive;
    }

    private void OnSidebarItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (sender is not Border border)
            return;

        var item = border.DataContext as SidebarItemViewModel
                   ?? border.Tag as SidebarItemViewModel;
        if (item is null || !item.IsNavigable)
            return;

        if (DataContext is MainWindowViewModel vm)
            vm.NavigateSidebarCommand.Execute(item);
    }

    private void OnSidebarDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border { DataContext: SidebarItemViewModel item } || !item.IsNavigable)
            return;

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        ClearSidebarDropTarget();
        item.IsDropTarget = true;
        _sidebarDropTarget = item;

        e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnSidebarDragLeave(object? sender, DragEventArgs e)
        => ClearSidebarDropTarget();

    private void ClearSidebarDropTarget()
    {
        if (_sidebarDropTarget is not null)
        {
            _sidebarDropTarget.IsDropTarget = false;
            _sidebarDropTarget = null;
        }
    }

    private async void OnSidebarDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border { DataContext: SidebarItemViewModel item }
            || !item.IsNavigable
            || string.IsNullOrEmpty(item.Path))
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        ClearSidebarDropTarget();

        var files = e.DataTransfer.TryGetFiles();
        if (files is null || files.Length == 0)
            return;

        var paths = new List<string>(files.Length);
        foreach (var file in files)
        {
            var local = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(local))
                paths.Add(local);
        }

        if (paths.Count == 0)
            return;

        var isCopy = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                     || e.DragEffects == DragDropEffects.Copy;
        await vm.HandleSidebarDropAsync(paths, item.Path, isCopy);

        e.Handled = true;
    }

    private SidebarItemViewModel? _sidebarDropTarget;

    private void OnTabStripWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.CycleSelectedTab(e.Delta.Y > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Middle)
            return;

        if (sender is Control { DataContext: TabViewModel tab })
        {
            tab.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnBranchButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { ActivePane: { } pane })
            return;

        await pane.OpenBranchFlyoutCommand.ExecuteAsync(null);
        if (sender is Button button)
            FlyoutBase.ShowAttachedFlyout(button);
    }

    private async void OnBranchSelected(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: string branch })
            return;

        if (DataContext is not MainWindowViewModel { ActivePane: { } pane })
            return;

        await pane.CheckoutBranchCommand.ExecuteAsync(branch);
        if (BranchButton is not null)
            FlyoutBase.GetAttachedFlyout(BranchButton)?.Hide();
    }

    private void OnSidebarSetColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string hex })
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        if (GetSidebarItemFromMenu(sender) is not SidebarItemViewModel item)
            return;

        vm.SetSidebarFolderColor(item, hex);
    }

    private void OnSidebarClearColorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (GetSidebarItemFromMenu(sender) is not SidebarItemViewModel item)
            return;

        vm.ClearSidebarFolderColor(item);
    }

    private void OnSidebarPinClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (GetSidebarItemFromMenu(sender) is not SidebarItemViewModel { Path: { } path })
            return;

        vm.PinPath(path);
    }

    private void OnSidebarUnpinClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (GetSidebarItemFromMenu(sender) is not SidebarItemViewModel { Path: { } path })
            return;

        vm.UnpinPath(path);
    }

    private void OnSidebarContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || DataContext is not MainWindowViewModel vm)
            return;

        var item = GetSidebarItemFromMenu(sender);
        foreach (var menuItem in menu.Items.OfType<MenuItem>())
        {
            switch (menuItem.Header?.ToString())
            {
                case "Pin to sidebar":
                    menuItem.IsVisible = item is not null && vm.CanPinSidebarItem(item);
                    break;
                case "Unpin from sidebar":
                    menuItem.IsVisible = item is not null && vm.CanUnpinSidebarItem(item);
                    break;
            }
        }
    }

    private static SidebarItemViewModel? GetSidebarItemFromMenu(object? sender)
    {
        if (sender is not ContextMenu menu)
            return null;

        if (menu.Tag is SidebarItemViewModel tagItem)
            return tagItem;

        if (menu.DataContext is SidebarItemViewModel dcItem)
            return dcItem;

        if (menu.PlacementTarget is { } target)
        {
            for (var t = target as Control; t is not null; t = t.Parent as Control)
            {
                if (t is Control { DataContext: SidebarItemViewModel targetItem })
                    return targetItem;
            }
        }

        return null;
    }
}
