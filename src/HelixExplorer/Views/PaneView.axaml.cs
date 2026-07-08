using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HelixExplorer.Controls;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class PaneView : UserControl
{
    private const double WheelThumbnailStep = 16;
    private const double DragThreshold = 4;

    private PaneViewModel? _pane;
    private Point? _pressPoint;
    private PointerPressedEventArgs? _pressArgs;
    private bool _dragStarted;
    private readonly List<Control> _millerColumns = new();
    private readonly FuncDataTemplate<EntryItemViewModel> _millerItemTemplate = new(
        (item, _) => new TextBlock { Text = item?.Name ?? string.Empty });

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
        {
            _pane.PropertyChanged += OnPanePropertyChanged;
            if (_pane.IsMillerView)
                RebuildMiller();
        }
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_pane is null)
            return;

        if (e.PropertyName == nameof(PaneViewModel.IsFilterVisible) && _pane.IsFilterVisible)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FilterBox.Focus();
                FilterBox.SelectAll();
            });
        }
        else if (e.PropertyName == nameof(PaneViewModel.IsRenaming) && _pane.IsRenaming)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RenameBox.Focus();
                RenameBox.SelectAll();
            });
        }
        else if (_pane.IsMillerView
                 && e.PropertyName is nameof(PaneViewModel.ViewMode)
                     or nameof(PaneViewModel.CurrentPath)
                     or nameof(PaneViewModel.ItemCount))
        {
            RebuildMiller();
        }
    }

    private static EntryItemViewModel? ExtractSelected(object? sender) => sender switch
    {
        DataGrid grid => grid.SelectedItem as EntryItemViewModel,
        ListBox list => list.SelectedItem as EntryItemViewModel,
        _ => null
    };

    private static IList<EntryItemViewModel> ExtractSelectedItems(object? sender)
    {
        var result = new List<EntryItemViewModel>();
        switch (sender)
        {
            case DataGrid { SelectedItems: { } items }:
                foreach (EntryItemViewModel entry in items)
                    result.Add(entry);
                break;
            case ListBox { SelectedItems: { } listBoxItems }:
                foreach (EntryItemViewModel entry in listBoxItems)
                    result.Add(entry);
                break;
        }
        return result;
    }

    private void OnItemActivated(object? sender, TappedEventArgs e)
    {
        if (ExtractSelected(sender) is { } entry)
            Pane?.ActivateEntry(entry);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Pane is null || sender is not Control { IsVisible: true })
            return;

        var items = ExtractSelectedItems(sender);
        Pane.UpdateSelection(items);
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

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (Pane is null)
            return;

        if (e.Key == Key.Enter)
        {
            Pane.CommitRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Pane.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }
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
        else if (e.Key == Key.Escape && Pane.IsRenaming)
        {
            Pane.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None)
        {
            Pane.BeginRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            Pane.DeleteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
        {
            Pane.SelectAll();
            SyncSelectionToView();
            e.Handled = true;
        }
    }

    private void SyncSelectionToView()
    {
        if (Pane is null)
            return;

        var visibleControl = GetVisibleControl();
        if (visibleControl is null)
            return;

        switch (visibleControl)
        {
            case DataGrid grid:
                grid.SelectedItems?.Clear();
                foreach (var entry in Pane.SelectedEntries)
                    grid.SelectedItems?.Add(entry);
                break;
            case ListBox list:
                list.SelectedItems?.Clear();
                foreach (var entry in Pane.SelectedEntries)
                    list.SelectedItems?.Add(entry);
                break;
        }
    }

    private Control? GetVisibleControl()
    {
        if (DetailsGrid.IsVisible) return DetailsGrid;
        if (ListView.IsVisible) return ListView;
        if (GridView.IsVisible) return GridView;
        return null;
    }

    // ── Miller columns ───────────────────────────────────────────────────────

    private void RebuildMiller()
    {
        if (Pane is null)
            return;

        MillerPanel.Children.Clear();
        _millerColumns.Clear();

        var entries = new ObservableCollection<EntryItemViewModel>(Pane.Entries);
        var column = BuildMillerColumn(entries, columnIndex: 0);
        MillerPanel.Children.Add(column);
        _millerColumns.Add(column);
    }

    private ListBox BuildMillerColumn(IEnumerable<EntryItemViewModel> entries, int columnIndex)
    {
        var list = new ListBox
        {
            Width = 250,
            ItemsSource = entries,
            ItemTemplate = _millerItemTemplate
        };
        MillerColumnPanel.SetColumnIndex(list, columnIndex);
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is EntryItemViewModel entry)
                MillerPanel.RaiseActivated(columnIndex, entry);
        };
        return list;
    }

    private async void OnMillerColumnActivated(object? sender, MillerColumnActivatedEventArgs e)
    {
        if (Pane is null || e.Item is not EntryItemViewModel entry)
            return;

        if (!entry.IsDirectory && !ArchivePath.IsArchiveFile(entry.FullPath))
        {
            Pane.ActivateEntry(entry);
            return;
        }

        var keep = e.ColumnIndex + 1;
        while (_millerColumns.Count > keep)
        {
            var last = _millerColumns[^1];
            MillerPanel.Children.Remove(last);
            _millerColumns.RemoveAt(_millerColumns.Count - 1);
        }

        var children = await Pane.EnumerateMillerChildrenAsync(entry.FullPath).ConfigureAwait(true);
        var column = BuildMillerColumn(children, keep);
        MillerPanel.Children.Add(column);
        _millerColumns.Add(column);

        Dispatcher.UIThread.Post(() =>
        {
            var width = MillerPanel.Bounds.Width;
            var viewport = MillerScroll.Viewport.Width;
            if (width > viewport)
                MillerScroll.Offset = new Vector(width - viewport, 0);
        });
    }

    // ── Drag source ──────────────────────────────────────────────────────────

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _pressPoint = e.GetPosition(this);
        _pressArgs = e;
        _dragStarted = false;
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStarted || _pressPoint is null || _pressArgs is null
            || Pane is null || Pane.SelectedEntries.Count == 0)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressPoint = null;
            _pressArgs = null;
            return;
        }

        var delta = e.GetPosition(this) - _pressPoint.Value;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        _dragStarted = true;
        var pressArgs = _pressArgs;
        _pressPoint = null;
        _pressArgs = null;

        var virtualPaths = Pane.SelectedEntries.Select(x => x.FullPath).ToList();
        var physicalPaths = await Pane.ResolvePhysicalPathsAsync(virtualPaths).ConfigureAwait(true);
        var transfer = await BuildFileTransferAsync(physicalPaths).ConfigureAwait(true);
        if (transfer is null)
            return;

        var effects = DragDropEffects.Copy | DragDropEffects.Move;
        await DragDrop.DoDragDropAsync(pressArgs, transfer, effects).ConfigureAwait(true);
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressPoint = null;
        _pressArgs = null;
        _dragStarted = false;
    }

    private async Task<DataTransfer?> BuildFileTransferAsync(IReadOnlyList<string> paths)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storage = topLevel?.StorageProvider;
        if (storage is null || paths.Count == 0)
            return null;

        var transfer = new DataTransfer();
        var added = 0;
        foreach (var path in paths)
        {
            IStorageItem? item = null;
            if (Directory.Exists(path))
                item = await storage.TryGetFolderFromPathAsync(path).ConfigureAwait(true);
            else if (File.Exists(path))
                item = await storage.TryGetFileFromPathAsync(path).ConfigureAwait(true);

            if (item is null)
                continue;

            transfer.Add(DataTransferItem.CreateFile(item));
            added++;
        }

        return added == 0 ? null : transfer;
    }

    // ── Drop target ──────────────────────────────────────────────────────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (Pane is null || Pane.IsArchive)
            return;

        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                ? DragDropEffects.Copy
                : DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (Pane is null || Pane.IsArchive)
            return;

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null || files.Length == 0)
            return;

        var paths = new List<string>(files.Length);
        foreach (var file in files)
        {
            var local = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(local))
                paths.Add(local);
        }

        if (paths.Count == 0)
            return;

        var isCopy = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                     || e.DragEffects == DragDropEffects.Copy;
        await Pane.HandleDropAsync(paths, isCopy);

        e.Handled = true;
    }
}
