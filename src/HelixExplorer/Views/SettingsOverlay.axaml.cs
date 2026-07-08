using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class SettingsOverlay : Panel
{
    public SettingsOverlay()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
            Focus();
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == this && DataContext is MainWindowViewModel vm)
        {
            vm.CloseSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPanelPointerPressed(object? sender, PointerPressedEventArgs e)
        => e.Handled = true;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not MainWindowViewModel vm)
            return;

        vm.CloseSettingsCommand.Execute(null);
        e.Handled = true;
    }
}
