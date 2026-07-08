using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class PaneView : UserControl
{
    private const double WheelThumbnailStep = 16;

    private PaneViewModel? _pane;

    public PaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private PaneViewModel? Pane => DataContext as PaneViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_pane is not null)
            _pane.PropertyChanged -= OnPanePropertyChanged;

        _pane = DataContext as PaneViewModel;
        if (_pane is not null)
            _pane.PropertyChanged += OnPanePropertyChanged;
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.IsFilterVisible) && _pane?.IsFilterVisible == true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FilterBox.Focus();
                FilterBox.SelectAll();
            });
        }
    }

    private static FileSystemEntry? ExtractSelected(object? sender) => sender switch
    {
        DataGrid grid => grid.SelectedItem as FileSystemEntry?,
        ListBox list => list.SelectedItem as FileSystemEntry?,
        _ => null
    };

    private void OnItemActivated(object? sender, TappedEventArgs e)
    {
        if (ExtractSelected(sender) is { } entry)
            Pane?.ActivateEntry(entry);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Pane is null || sender is not Control { IsVisible: true })
            return;

        Pane.SelectedEntry = ExtractSelected(sender);
    }

    private void OnDetailsSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (Pane is null || e.Column.Tag is not string tag)
            return;

        var column = tag switch
        {
            "Size" => SortColumn.Size,
            "Modified" => SortColumn.Modified,
            "Type" => SortColumn.Type,
            _ => SortColumn.Name
        };

        if (Pane.SortColumn == column)
            Pane.SortDescending = !Pane.SortDescending;
        else
        {
            Pane.SortColumn = column;
            Pane.SortDescending = false;
        }

        e.Handled = true;
    }

    private void OnGridPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Pane is null || (e.KeyModifiers & KeyModifiers.Control) == 0)
            return;

        Pane.AdjustThumbnailSize(e.Delta.Y > 0 ? WheelThumbnailStep : -WheelThumbnailStep);
        e.Handled = true;
    }

    private void OnFilterBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (Pane is null)
            return;

        if (e.Key == Key.Escape)
        {
            Pane.ClearFilter();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
        }
    }

    private void OnFilterCloseClick(object? sender, RoutedEventArgs e)
    {
        Pane?.ClearFilter();
        Focus();
    }

    private void OnPaneKeyDown(object? sender, KeyEventArgs e)
    {
        if (Pane is null)
            return;

        if (e.Key == Key.Enter)
        {
            Pane.ActivateSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None)
        {
            Pane.GoUpCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            Pane.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Pane.IsFilterVisible)
        {
            Pane.ClearFilter();
            e.Handled = true;
        }
    }
}
