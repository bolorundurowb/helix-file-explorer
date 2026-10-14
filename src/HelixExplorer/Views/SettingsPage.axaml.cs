using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        // Tunnel so the chord is stored before a window KeyBinding runs the same keys.
        TerminalGestureCapture.AddHandler(
            InputElement.KeyDownEvent,
            OnTerminalGestureKeyDown,
            RoutingStrategies.Tunnel);
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

    private void OnResetTerminalGestureClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.Main.OpenInTerminalGesture = MainWindowViewModel.AppDefaultTerminalGesture;
    }

    private void OnTerminalGestureKeyDown(object? sender, KeyEventArgs e)
    {
        // Plain Tab still moves focus. Ctrl+Tab is a real chord and must be captured here.
        if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
            return;

        if (!TerminalGestureSyntax.TryFormat(e.Key, e.KeyModifiers, out var gesture))
            return;

        if (ViewModel is { } vm)
            vm.Main.OpenInTerminalGesture = gesture;

        e.Handled = true;
    }
}
