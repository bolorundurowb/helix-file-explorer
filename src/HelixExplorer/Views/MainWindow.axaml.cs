using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Input;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _layoutSaveTimer;

    public MainWindow()
    {
        InitializeComponent();
        ApplyBuildIcon();
        Activated += OnActivated;
        Deactivated += (_, _) => SetWindowActive(false);
        Opened += OnOpened;
        Closing += OnClosing;
        PositionChanged += OnLayoutChanged;
        SizeChanged += OnLayoutChanged;
        PropertyChanged += OnWindowPropertyChanged;
        SidebarSplitter.DragCompleted += OnSidebarDragCompleted;

        AttachTabOverflowHandlers();
        SidebarList.AddHandler(KeyDownEvent, OnSidebarKeyDown, RoutingStrategies.Tunnel);
        SidebarList.ContainerPrepared += OnSidebarContainerPrepared;
        BranchList.ItemsSource = _branchChoices;
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnChromeKeyDown, RoutingStrategies.Tunnel);
    }

    private void ApplyBuildIcon()
    {
        var uri = new Uri(
#if DEBUG
            "avares://HelixExplorer/Assets/helix-explorer-icon-dev.png"
#else
            "avares://HelixExplorer/Assets/helix-explorer-icon.png"
#endif
        );

        Icon = new WindowIcon(AssetLoader.Open(uri));
        AppLogoImage.Source = new Bitmap(AssetLoader.Open(uri));
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        SetWindowActive(true);
        // Fallback when WM_DEVICECHANGE is missed while the window was inactive.
        if (DataContext is MainWindowViewModel vm)
            vm.RefreshDriveLists();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ApplyWindowLayout(this);
            ApplyOpenInTerminalKeyBinding(vm.OpenInTerminalGesture);
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.EditPathRequested += OnEditPathRequested;
            vm.WindowCloseRequested += OnWindowCloseRequested;
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

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsXButton1Pressed || properties.IsXButton2Pressed)
        {
            if (DataContext is MainWindowViewModel window)
            {
                var command = properties.IsXButton1Pressed ? window.GoBackCommand : window.GoForwardCommand;
                if (command.CanExecute(null))
                    command.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (DataContext is not MainWindowViewModel vm || !vm.IsStatusCentreOpen)
            return;

        if (IsWithin(StatusCentreOverlay, e.Source) || IsWithin(StatusCentreChip, e.Source))
            return;

        vm.CloseStatusCentreCommand.Execute(null);
    }

    private static bool IsWithin(Visual? ancestor, object? source)
        => ancestor is not null
           && source is Visual visual
           && (ReferenceEquals(ancestor, visual) || ancestor.IsVisualAncestorOf(visual));

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
        if (SidebarItemFrom(sender) is not { IsNavigable: true } item)
            return;

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        ClearSidebarDropTarget();
        item.IsDropTarget = true;
        _sidebarDropTarget = item;

        e.DragEffects = ToAvaloniaDropEffect(ResolveSidebarDropEffect(e, item.Path));
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
        if (SidebarItemFrom(sender) is not { IsNavigable: true, Path: { Length: > 0 } path })
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

        var effect = ResolveSidebarDropEffect(e, path);
        e.Handled = true;

        // The drop pipeline copies or moves. A shortcut request must not fall through to a move.
        if (effect == HelixExplorer.Core.Infrastructure.DragDropEffects.Link)
            return;

        await vm.HandleSidebarDropAsync(paths, path, effect == HelixExplorer.Core.Infrastructure.DragDropEffects.Copy);
    }

    private static HelixExplorer.Core.Infrastructure.DragDropEffects ResolveSidebarDropEffect(DragEventArgs e, string? destination)
    {
        string? source = null;
        var files = e.DataTransfer.TryGetFiles();
        if (files is { Length: > 0 })
            source = files[0].TryGetLocalPath();

        return DropEffectPolicy.Resolve(
            source,
            destination,
            e.KeyModifiers.HasFlag(KeyModifiers.Control),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            e.KeyModifiers.HasFlag(KeyModifiers.Alt));
    }

    private static DragDropEffects ToAvaloniaDropEffect(HelixExplorer.Core.Infrastructure.DragDropEffects effect)
        => effect switch
        {
            HelixExplorer.Core.Infrastructure.DragDropEffects.Copy => DragDropEffects.Copy,
            HelixExplorer.Core.Infrastructure.DragDropEffects.Move => DragDropEffects.Move,
            HelixExplorer.Core.Infrastructure.DragDropEffects.Link => DragDropEffects.Link,
            _ => DragDropEffects.None
        };

    private static SidebarItemViewModel? SidebarItemFrom(object? sender)
        => sender is Control { DataContext: SidebarItemViewModel item } ? item : null;

    private SidebarItemViewModel? _sidebarDropTarget;
    private readonly ObservableCollection<BranchChoice> _branchChoices = new();
    private int _chromeFocusIndex = -1;

    private int _dragSourceIndex = -1;
    private Point _dragStartPosition;
    private bool _isDragging;
    private long _lastTabWheelTimestamp;

    private const double TabDragThreshold = 6;
    private static readonly TimeSpan TabWheelScrollInterval = TimeSpan.FromMilliseconds(360);

    private void OnTabStripWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var timestamp = Stopwatch.GetTimestamp();
            if (_lastTabWheelTimestamp != 0
                && Stopwatch.GetElapsedTime(_lastTabWheelTimestamp, timestamp) < TabWheelScrollInterval)
            {
                e.Handled = true;
                return;
            }

            _lastTabWheelTimestamp = timestamp;
            var delta = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
            vm.CycleSelectedTab(delta > 0 ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (TabScrollViewer is null)
            return;

        var extent = TabScrollViewer.Extent.Width;
        var viewport = TabScrollViewer.Viewport.Width;
        if (extent <= viewport + 0.5)
            return;

        var notch = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        var maxOffset = Math.Max(0, extent - viewport);
        var next = Math.Clamp(TabScrollViewer.Offset.X - notch * 72, 0, maxOffset);
        TabScrollViewer.Offset = new Vector(next, 0);
        e.Handled = true;
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            if (sender is Control { DataContext: TabViewModel tab })
            {
                tab.CloseCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            if (_isDragging)
                e.Handled = true;
            EndTabDrag();
        }
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(TabStrip).Properties.IsLeftButtonPressed)
            return;

        if (sender is not Control { DataContext: TabViewModel tab } || DataContext is not MainWindowViewModel vm)
            return;

        _dragSourceIndex = vm.Tabs.IndexOf(tab);
        _dragStartPosition = e.GetPosition(TabStrip);
        _isDragging = false;
    }

    private void OnTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSourceIndex < 0 || DataContext is not MainWindowViewModel vm)
            return;

        if (!e.GetCurrentPoint(TabStrip).Properties.IsLeftButtonPressed)
        {
            EndTabDrag();
            return;
        }

        var position = e.GetPosition(TabStrip);

        if (!_isDragging)
        {
            if (Math.Abs(position.X - _dragStartPosition.X) < TabDragThreshold
                && Math.Abs(position.Y - _dragStartPosition.Y) < TabDragThreshold)
                return;

            _isDragging = true;
            if (sender is Control chip)
                e.Pointer.Capture(chip);
        }

        var targetIndex = HitTestTabIndex(position);
        if (targetIndex >= 0 && targetIndex != _dragSourceIndex)
        {
            vm.MoveTab(_dragSourceIndex, targetIndex);
            _dragSourceIndex = targetIndex;
        }
    }

    private void OnTabPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => EndTabDrag();

    private int HitTestTabIndex(Point position)
    {
        for (var i = 0; i < TabStrip.ItemCount; i++)
        {
            if (TabStrip.ContainerFromIndex(i) is not Control container)
                continue;

            var bounds = container.Bounds;
            if (position.X >= bounds.X && position.X < bounds.Right)
                return i;
        }

        return -1;
    }

    private void EndTabDrag()
    {
        _dragSourceIndex = -1;
        _isDragging = false;
    }

    private async void OnBranchButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { ActivePane: { } pane })
            return;

        await pane.OpenBranchFlyoutCommand.ExecuteAsync(null);
        if (BranchFilterBox is not null)
            BranchFilterBox.Text = string.Empty;
        RefreshBranchChoices();
        if (sender is Button button)
            FlyoutBase.ShowAttachedFlyout(button);
    }

    private void OnBranchFilterChanged(object? sender, TextChangedEventArgs e)
        => RefreshBranchChoices();

    private void OnBranchFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        BranchChoice? selected = BranchList.SelectedItem as BranchChoice
            ?? _branchChoices.FirstOrDefault(choice => choice.IsCurrent)
            ?? _branchChoices.FirstOrDefault();
        if (selected is null)
            return;

        e.Handled = true;
        _ = CheckoutBranchAsync(selected);
    }

    private void OnBranchListTapped(object? sender, TappedEventArgs e)
    {
        if (BranchChoiceFrom(e.Source) is not { } choice)
            return;

        e.Handled = true;
        _ = CheckoutBranchAsync(choice);
    }

    private void OnBranchListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || BranchList.SelectedItem is not BranchChoice choice)
            return;

        e.Handled = true;
        _ = CheckoutBranchAsync(choice);
    }

    private void RefreshBranchChoices()
    {
        var query = BranchFilterBox?.Text?.Trim() ?? string.Empty;
        var pane = (DataContext as MainWindowViewModel)?.ActivePane;
        var current = pane?.RepositoryStatus.Branch ?? string.Empty;
        _branchChoices.Clear();
        if (pane is null)
            return;

        foreach (var branch in pane.Branches)
        {
            if (query.Length > 0 && !branch.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            _branchChoices.Add(new BranchChoice
            {
                Name = branch,
                IsCurrent = string.Equals(branch, current, StringComparison.Ordinal)
            });
        }

        BranchList.SelectedItem = _branchChoices.FirstOrDefault(choice => choice.IsCurrent) ?? _branchChoices.FirstOrDefault();
    }

    private async Task CheckoutBranchAsync(BranchChoice choice)
    {
        if (DataContext is not MainWindowViewModel { ActivePane: { } pane })
            return;

        await pane.CheckoutBranchCommand.ExecuteAsync(choice.Name);
        if (BranchButton is not null)
            FlyoutBase.GetAttachedFlyout(BranchButton)?.Hide();
    }

    private static BranchChoice? BranchChoiceFrom(object? source)
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
        {
            if (control.DataContext is BranchChoice choice)
                return choice;
        }

        return null;
    }

    private void OnSidebarContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not ListBoxItem container)
            return;

        var item = container.DataContext as SidebarItemViewModel;
        var navigable = item is { IsNavigable: true };
        container.Focusable = navigable;
        if (item is { IsSectionHeader: true })
            AutomationProperties.SetName(container, item.Title);

        if (container.Classes.Contains("sidebarWired"))
            return;

        container.Classes.Add("sidebarWired");
        DragDrop.SetAllowDrop(container, true);
        container.AddHandler(DragDrop.DragOverEvent, OnSidebarDragOver);
        container.AddHandler(DragDrop.DragLeaveEvent, OnSidebarDragLeave);
        container.AddHandler(DragDrop.DropEvent, OnSidebarDrop);
    }

    private void OnSidebarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            MoveSidebarSelection(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter
            && DataContext is MainWindowViewModel vm
            && SidebarList.SelectedItem is SidebarItemViewModel item
            && item.IsNavigable)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                vm.OpenSidebarInNewTabCommand.Execute(item);
            else
                vm.NavigateSidebarCommand.Execute(item);
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift) || e.Key == Key.Apps)
        {
            OpenSidebarContextMenu();
            e.Handled = true;
        }
    }

    private void MoveSidebarSelection(int delta)
    {
        var count = SidebarList.ItemCount;
        if (count == 0)
            return;

        var index = SidebarList.SelectedIndex;
        if (index < 0)
            index = delta > 0 ? -1 : count;

        for (var step = 0; step < count; step++)
        {
            index += delta;
            if (index < 0 || index >= count)
                return;

            if (SidebarList.Items[index] is not SidebarItemViewModel { IsNavigable: true })
                continue;

            SidebarList.SelectedIndex = index;
            if (SidebarList.SelectedItem is { } selected)
                SidebarList.ScrollIntoView(selected);
            if (SidebarList.ContainerFromIndex(index) is Control container)
                container.Focus();
            return;
        }
    }

    private void OpenSidebarContextMenu()
    {
        if (SidebarList.SelectedItem is not SidebarItemViewModel { IsNavigable: true })
            return;

        if (SidebarList.ContainerFromItem(SidebarList.SelectedItem) is not Control container)
            return;

        var host = container.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.ContextMenu is not null);
        host?.ContextMenu?.Open(host);
    }

    private void OnEditPathRequested(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(FocusAddress, DispatcherPriority.Loaded);

    private void OnWindowCloseRequested(object? sender, EventArgs e)
        => Close();

    private void OnChromeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F6 || e.KeyModifiers != KeyModifiers.None || TextInputFocus.IsActive())
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        _chromeFocusIndex = ChromeFocusCycle.Next(CurrentChromeSlot(vm), vm.IsDualPaneActive);
        ApplyChromeFocus(vm, _chromeFocusIndex);
        e.Handled = true;
    }

    private int CurrentChromeSlot(MainWindowViewModel vm)
    {
        if (FocusManager?.GetFocusedElement() is not Visual focused)
            return _chromeFocusIndex;

        if (OmnibarFocus.IsWithin(SidebarList, focused))
            return ChromeFocusCycle.Sidebar;
        if (OmnibarFocus.IsWithin(AddressOmnibar, focused) || BrowserPanes.ContainsAddress(focused))
            return ChromeFocusCycle.Address;
        if (OmnibarFocus.IsWithin(StatusBar, focused))
            return ChromeFocusCycle.StatusBar;

        var other = OtherPane(vm);
        if (other is not null && BrowserPanes.FileSurfaceContains(other, focused))
            return ChromeFocusCycle.OtherPane;
        if (vm.ActivePane is { } active && BrowserPanes.FileSurfaceContains(active, focused))
            return ChromeFocusCycle.ActivePane;

        return _chromeFocusIndex;
    }

    private void ApplyChromeFocus(MainWindowViewModel vm, int slot)
    {
        switch (slot)
        {
            case ChromeFocusCycle.Sidebar:
                SidebarList.Focus();
                if (SidebarList.SelectedIndex < 0)
                    MoveSidebarSelection(1);
                break;
            case ChromeFocusCycle.Address:
                FocusAddress();
                break;
            case ChromeFocusCycle.ActivePane:
                BrowserPanes.FocusFileSurface(vm.ActivePane);
                break;
            case ChromeFocusCycle.OtherPane:
                BrowserPanes.FocusFileSurface(OtherPane(vm));
                break;
            case ChromeFocusCycle.StatusBar:
                if (BranchButton.IsVisible)
                    BranchButton.Focus();
                else
                    StatusCentreChip.Focus();
                break;
        }
    }

    private void FocusAddress()
    {
        if (DataContext is MainWindowViewModel { IsDualPaneActive: true, ActivePane: { } pane })
        {
            BrowserPanes.FocusAddress(pane);
            return;
        }

        OmnibarFocus.Focus(AddressOmnibar);
    }

    private static PaneViewModel? OtherPane(MainWindowViewModel vm)
    {
        if (vm.SelectedTab is not { IsDualPane: true } tab)
            return null;

        return ReferenceEquals(tab.ActivePane, tab.LeftPane) ? tab.RightPane : tab.LeftPane;
    }

    private void OnPaneCommandFlyoutOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout { Target: Control { Tag: IReadOnlyList<IReadOnlyList<CommandDefinition<PaneViewModel>>> groups } } flyout)
            return;

        if (DataContext is MainWindowViewModel { ActivePane: { } pane })
            CommandMenuBuilder.Fill(flyout.Items, groups, pane);
    }

    private void OnTabChipLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { ContextMenu: { } menu })
            return;

        // Filled up front because colours added during Opening do not turn the item into a submenu on the
        // first open. The tab is resolved at click time since the menu's DataContext follows the chip.
        CommandMenuBuilder.FillTintSwatches(
            menu.Items,
            new RelayCommand<string>(hex => (menu.DataContext as TabViewModel)?.SetTintCommand.Execute(hex)));
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

        if (item is not null)
        {
            CommandMenuBuilder.FillColorSubmenus(
                menu.Items,
                new RelayCommand<string>(hex =>
                {
                    if (hex is not null)
                        vm.SetSidebarFolderColor(item, hex);
                }));
        }
    }

    private static SidebarItemViewModel? GetSidebarItemFromMenu(object? sender)
    {
        // Menu item Click handlers pass the item, not the menu that knows which sidebar row was right-clicked.
        var menu = sender as ContextMenu ?? (sender as MenuItem)?.FindLogicalAncestorOfType<ContextMenu>();
        if (menu is null)
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

public sealed class BranchChoice
{
    public required string Name { get; init; }

    public bool IsCurrent { get; init; }
}
