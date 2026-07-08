using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private Point? _tabGrabOrigin;

    public MainWindow()
    {
        InitializeComponent();
    }

    // DataContext is assigned by the object initializer AFTER the constructor runs, so
    // subscribe here rather than in the constructor (the old code silently no-op'd).
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.SettingsRequested -= OnSettingsRequested;
        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null) _vm.SettingsRequested += OnSettingsRequested;
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        var dialog = new SettingsDialog();
        await dialog.ShowDialog(this);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    // ---- tab tinting ----

    private void OnTabColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (sender is Control { DataContext: TabViewModel tab }) tab.Tint = e.NewColor;
    }

    private void OnClearTabTint(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TabViewModel tab }) tab.Tint = null;
    }

    // ---- git branch flyout ----

    private async void OnBranchButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { ActiveTab: { } tab } && sender is Control c)
        {
            await tab.ActivePane.OpenBranchFlyoutCommand.ExecuteAsync(null);
            FlyoutBase.ShowAttachedFlyout(c);
        }
    }

    private void OnBranchSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: string branch } &&
            DataContext is MainWindowViewModel { ActiveTab: { } tab })
        {
            tab.ActivePane.CheckoutBranchCommand.Execute(branch);
        }
    }

    /// <summary>Mouse-wheel over the tab strip cycles through open tabs.</summary>
    private void OnTabStripWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CycleTab(e.Delta.Y < 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is not MainWindowViewModel vm) return;
        if (e.Source is Control { DataContext: TabViewModel dragged } && vm.Tabs.Contains(dragged))
        {
            _tabGrabOrigin = e.GetPosition(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_tabGrabOrigin is null) return;

        // Only continue while the left button is held.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _tabGrabOrigin = null;
            return;
        }

        var pos = e.GetPosition(this);
        var outside = pos.X < 0 || pos.Y < 0 || pos.X > Bounds.Width || pos.Y > Bounds.Height;
        if (outside && DataContext is MainWindowViewModel vm && vm.ActiveTab is not null)
        {
            // Tear out: open a new window at the dragged tab's location.
            // TODO: transparent thumbnail follow + true tab hand-off (see UX spec §1).
            var path = vm.ActiveTab.ActivePane.CurrentPath;
            var newVm = new MainWindowViewModel(restoreSession: false);
            newVm.NavigateActive(path);
            new MainWindow { DataContext = newVm }.Show();
            _tabGrabOrigin = null;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _tabGrabOrigin = null;
    }
}
