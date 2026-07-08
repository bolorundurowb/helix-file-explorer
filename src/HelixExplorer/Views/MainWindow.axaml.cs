using Avalonia.Controls;
using Avalonia.Input;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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

        // Scroll up → previous tab, scroll down → next tab.
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
}
