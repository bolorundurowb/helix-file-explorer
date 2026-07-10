using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ApplyWindowLayout(this);
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
        if (sender is not Border border)
            return;

        var item = border.DataContext as SidebarItemViewModel
                   ?? border.Tag as SidebarItemViewModel;
        if (item is null || !item.IsNavigable)
            return;

        if (DataContext is MainWindowViewModel vm)
            vm.NavigateSidebarCommand.Execute(item);
    }

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
        for (var current = sender as Control; current is not null; current = current.Parent as Control)
        {
            if (current is ContextMenu { PlacementTarget: Border border }
                && border.DataContext is SidebarItemViewModel item)
                return item;
        }

        return null;
    }
}
