using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class CommandPaletteOverlay : Panel
{
    private MainWindowViewModel? _window;
    private ObservableCollection<CommandItem>? _commands;
    private int _filterPass;
    private bool _queryFocusedThisOpen;

    public CommandPaletteOverlay()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
        PropertyChanged += OnOverlayPropertyChanged;
        DataContextChanged += OnDataContextChanged;
        AttachCommands(DataContext as MainWindowViewModel);
    }

    private void OnOverlayPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // A collapsed overlay may not get another layout pass, so the next open must be allowed to focus.
        if (e.Property == IsVisibleProperty && e.NewValue is false)
            _queryFocusedThisOpen = false;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            _queryFocusedThisOpen = false;
            return;
        }

        // LayoutUpdated runs on every arrange. Refocusing whenever the box is idle steals the caret
        // back from a click in the results, so each open focuses the query once.
        if (_queryFocusedThisOpen || QueryBox is null)
            return;

        QueryBox.Focus();
        if (!QueryBox.IsFocused)
            return;

        QueryBox.SelectAll();
        _queryFocusedThisOpen = true;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
        => AttachCommands(DataContext as MainWindowViewModel);

    private void AttachCommands(MainWindowViewModel? window)
    {
        if (ReferenceEquals(_window, window))
            return;

        if (_commands is not null)
            _commands.CollectionChanged -= OnFilteredCommandsChanged;

        _window = window;
        _commands = window?.FilteredCommands;
        if (_commands is null)
            return;

        _commands.CollectionChanged += OnFilteredCommandsChanged;
        ScheduleFilteredSelection();
    }

    private void OnFilteredCommandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ScheduleFilteredSelection();

    private void ScheduleFilteredSelection()
    {
        var pass = ++_filterPass;
        Dispatcher.UIThread.Post(() =>
        {
            if (pass != _filterPass || _window is null || _commands is null)
                return;

            foreach (var item in _commands)
                item.RefreshAvailability(_window);

            if (ResultsList.ItemCount > 0)
            {
                ResultsList.SelectedIndex = 0;
                if (ResultsList.SelectedItem is not null)
                    ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            else
            {
                ResultsList.SelectedIndex = -1;
            }
        });
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
                if (ResultsList.SelectedItem is CommandItem item)
                    TryExecute(item);
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

    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (CommandItemFromSource(e.Source) is not CommandItem item)
            return;

        ResultsList.SelectedItem = item;
        e.Handled = true;
        TryExecute(item);
    }

    private void TryExecute(CommandItem item)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        item.RefreshAvailability(vm);
        if (!item.IsAvailable)
            return;

        vm.ExecuteCommandCommand.Execute(item);
    }

    private static CommandItem? CommandItemFromSource(object? source)
    {
        for (var current = source as StyledElement; current is not null; current = current.Parent as StyledElement)
        {
            if (current is ListBox)
                return null;

            if (current.DataContext is CommandItem item)
                return item;
        }

        return null;
    }
}
