using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class CommandPaletteOverlay : Panel
{
    public CommandPaletteOverlay()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!IsVisible || QueryBox is null)
            return;

        if (!QueryBox.IsFocused)
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == this && DataContext is MainWindowViewModel vm)
        {
            vm.ToggleCommandPaletteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (ResultsList.SelectedItem is CommandItem item && DataContext is MainWindowViewModel vm)
                    vm.ExecuteCommandCommand.Execute(item);
                e.Handled = true;
                break;
            case Key.Escape:
                if (DataContext is MainWindowViewModel closeVm)
                    closeVm.ToggleCommandPaletteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                ResultsList.SelectedIndex = Math.Min(ResultsList.ItemCount - 1, ResultsList.SelectedIndex + 1);
                e.Handled = true;
                break;
            case Key.Up:
                ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 1);
                e.Handled = true;
                break;
        }
    }

    private void OnResultDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is CommandItem item && DataContext is MainWindowViewModel vm)
            vm.ExecuteCommandCommand.Execute(item);
    }
}
