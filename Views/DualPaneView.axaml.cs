using Avalonia.Controls;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class DualPaneView : UserControl
{
    public DualPaneView()
    {
        InitializeComponent();
    }

    private void OnSwap(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is TabViewModel tab) tab.SwapPanes();
    }
}