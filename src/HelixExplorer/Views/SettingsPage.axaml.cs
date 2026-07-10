using Avalonia.Controls;
using Avalonia.Interactivity;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private SettingsPageViewModel? ViewModel => DataContext as SettingsPageViewModel;

    private void OnSectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SettingsSection section })
            ViewModel?.SelectSection(section);
    }

    private void OnAccentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string hex })
            ViewModel?.Main.SetAccentColorCommand.Execute(hex);
    }
}
