using Avalonia.Controls;
using Avalonia.Interactivity;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class StatusCentreFlyout : UserControl
{
    public StatusCentreFlyout()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ToggleStatusCentreCommand.Execute(null);
    }
}
