using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class SidebarTreeView : UserControl
{
    public SidebarTreeView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ObservableCollection<SidebarNode> roots)
        {
            foreach (var node in roots)
            {
                _ = node.EnsurePopulatedAsync();
            }
        }
    }

    private void OnTreeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is SidebarNode node)
        {
            _ = node.EnsurePopulatedAsync();
            if (!string.IsNullOrEmpty(node.FullPath) && this.GetVisualRoot() is MainWindow mw
                && mw.DataContext is MainWindowViewModel mvm && mvm.ActiveTab != null)
            {
                mvm.ActiveTab.ActivePane.NavigateTo(node.FullPath);
            }
        }
    }
}