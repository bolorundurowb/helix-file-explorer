using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class PaneView : UserControl
{
    public PaneView()
    {
        InitializeComponent();
    }

    private PaneViewModel? Pane => DataContext as PaneViewModel;

    private void OnItemActivated(object? sender, TappedEventArgs e)
    {
        if (DetailsGrid.SelectedItem is FileSystemEntry entry)
            Pane?.ActivateEntry(entry);
    }

    private void OnDetailsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Pane is null)
            return;

        Pane.SelectedEntry = DetailsGrid.SelectedItem is FileSystemEntry entry ? entry : null;
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
    }
}
