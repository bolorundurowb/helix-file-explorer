using Avalonia.Controls;
using Avalonia.Interactivity;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(ServiceLocator.Theme, ServiceLocator.Settings);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}