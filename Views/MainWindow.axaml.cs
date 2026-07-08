using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SettingsRequested += OnSettingsRequested;
        }
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        var dialog = new SettingsDialog();
        await dialog.ShowDialog(this);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Tab tear-out: Detect dragging on a TabItem beyond the title bar bounds. We do
        // a minimal approximation — on pointer-down over the TabControl, remember which
        // tab was hit; if the pointer leaves the window while still pressed, tear out.
        base.OnPointerPressed(e);
        BeginTabTearOutTracking(e);
    }

    private Point? _tabGrabOrigin;

    private void BeginTabTearOutTracking(PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (e.Source is Control { DataContext: TabViewModel dragged } && vm.Tabs.Contains(dragged))
        {
            _tabGrabOrigin = e.GetPosition(this);
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_tabGrabOrigin is null) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != default(PointerUpdateKind) &&
            e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) { _tabGrabOrigin = null; return; }

        var pos = e.GetPosition(this);
        var bounds = Bounds;
        var outside = pos.X < 0 || pos.Y < 0 || pos.X > bounds.Width || pos.Y > bounds.Height;
        if (outside && DataContext is MainWindowViewModel vm)
        {
            // Tear out — create a new window carrying a copy of this tab's path.
            if (vm.ActiveTab != null)
            {
                var path = vm.ActiveTab.ActivePane.CurrentPath;
                var newVm = new MainWindowViewModel();
                if (newVm.ActiveTab != null)
                {
                    newVm.ActiveTab.ActivePane.NavigateTo(path);
                }
                var newWindow = new MainWindow { DataContext = newVm };
                newWindow.Show();
            }
            _tabGrabOrigin = null;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _tabGrabOrigin = null;
    }
}