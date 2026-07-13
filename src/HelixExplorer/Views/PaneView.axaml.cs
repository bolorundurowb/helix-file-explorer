using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelixExplorer.Controls;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.Models;
using HelixExplorer.Input;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class PaneView : UserControl
{
    private const double WheelThumbnailStep = 16;
    private const double DragThreshold = 4;

    private enum PointerInteractionMode
    {
        None,
        MarqueePending,
        DragOutPending
    }

    private PaneViewModel? _pane;
    private Point? _pressPoint;
    private PointerPressedEventArgs? _pressArgs;
    private bool _dragStarted;
    private bool _syncingSelectionToView;
    private PointerInteractionMode _interactionMode;
    private readonly List<Control> _millerColumns = new();
    private readonly FuncDataTemplate<EntryItemViewModel> _millerItemTemplate = new(
        (item, _) => new TextBlock { Text = item?.DisplayName ?? string.Empty });

    public PaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetailsGrid.ContextRequested += OnDetailsContextRequested;
    }

    private PaneViewModel? Pane => DataContext as PaneViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_pane is not null)
            _pane.PropertyChanged -= OnPanePropertyChanged;

        _pane = DataContext as PaneViewModel;
        if (_pane is null)
        {
            Classes.Set("inactive", false);
            return;
        }

        _pane.PropertyChanged += OnPanePropertyChanged;
        UpdateInactiveClass();
        if (_pane.IsMillerView)
            RebuildMiller();
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
                     or nameof(PaneViewModel.ItemCount)
                     or nameof(PaneViewModel.ShowFileExtensions))
        {
            RebuildMiller();
        }
        else if (e.PropertyName == nameof(PaneViewModel.IsSelectionActive))
        {
            UpdateInactiveClass();
        }
    }

    private void OnEntryContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        if (menu.PlacementTarget is Control { DataContext: PaneViewModel pane })
        {
            menu.DataContext = pane;
            pane.NotifyCommandsCanExecuteChanged();
        }
    }

    private void OnDetailsContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Pane is null)
            return;

        if (TryGetEntryFromSource(e.Source) is { } entry && !Pane.SelectedEntries.Contains(entry))
            Pane.UpdateSelection([entry]);
    }

    private void UpdateInactiveClass()
    {
        Classes.Set("inactive", _pane is not null && !_pane.IsSelectionActive);
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
        if (_syncingSelectionToView || Pane is null || sender is not Control { IsVisible: true })
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
        if (Pane is null || TextInputFocus.IsActive())
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

        var selected = Pane.SelectedEntries.ToList();

        _syncingSelectionToView = true;
        try
        {
            switch (visibleControl)
            {
                case DataGrid grid:
                    grid.SelectedItems?.Clear();
                    foreach (var entry in selected)
                        grid.SelectedItems?.Add(entry);
                    break;
                case ListBox list:
                    list.SelectedItems?.Clear();
                    foreach (var entry in selected)
                        list.SelectedItems?.Add(entry);
                    break;
            }
        }
        finally
        {
            _syncingSelectionToView = false;
        }
    }

    private Control? GetVisibleControl()
    {
        if (DetailsGrid.IsVisible) return DetailsGrid;
        if (ListView.IsVisible) return ListView;
        if (Pane?.IsGridView == true) return GridView;
        return null;
    }

    private void OnGridItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Pane is null || TryGetEntryFromSource(sender) is not { } entry)
            return;

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (!Pane.SelectedEntries.Contains(entry))
                SelectGridEntry(entry, KeyModifiers.None);
            return;
        }

        SelectGridEntry(entry, e.KeyModifiers);
        e.Handled = true;
    }

    private void SelectGridEntry(EntryItemViewModel entry, KeyModifiers modifiers)
    {
        Pane?.SelectEntry(entry, modifiers);
    }

    private static EntryItemViewModel? TryGetEntryFromSource(object? source)
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
        {
            if (control.DataContext is EntryItemViewModel entry)
                return entry;
        }

        return null;
    }

    private void OnGridItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: EntryItemViewModel entry })
            Pane?.ActivateEntry(entry);
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

    private const int MaxMillerColumns = 8;

    private ListBox BuildMillerColumn(IEnumerable<EntryItemViewModel> entries, int columnIndex)
    {
        var list = new ListBox
        {
            Width = 250,
            ItemsSource = entries,
            ItemTemplate = _millerItemTemplate
        };
        list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
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

        while (_millerColumns.Count >= MaxMillerColumns)
        {
            var first = _millerColumns[0];
            MillerPanel.Children.Remove(first);
            _millerColumns.RemoveAt(0);
            for (var i = 0; i < _millerColumns.Count; i++)
                MillerColumnPanel.SetColumnIndex(_millerColumns[i], i);
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
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed
                && Pane is not null
                && TryGetEntryFromSource(e.Source) is { } rightClicked
                && !Pane.SelectedEntries.Contains(rightClicked))
            {
                SelectGridEntry(rightClicked, KeyModifiers.None);
            }

            return;
        }

        if (Pane is null || Pane.IsHome)
            return;

        var pane = Pane;

        if (pane.IsGridView
            && ReferenceEquals(sender, GridView)
            && TryGetEntryFromSource(e.Source) is { } gridEntry)
        {
            SelectGridEntry(gridEntry, e.KeyModifiers);
            _interactionMode = pane.SelectedEntries.Contains(gridEntry)
                ? PointerInteractionMode.DragOutPending
                : PointerInteractionMode.None;
        }
        else if (TryGetEntryFromSource(e.Source) is { } entry)
        {
            SelectGridEntry(entry, e.KeyModifiers);
            _interactionMode = pane.SelectedEntries.Contains(entry)
                ? PointerInteractionMode.DragOutPending
                : PointerInteractionMode.None;
        }
        else
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                pane.UpdateSelection(Array.Empty<EntryItemViewModel>());

            _interactionMode = PointerInteractionMode.MarqueePending;
        }

        _pressPoint = e.GetPosition(this);
        _pressArgs = e;
        _dragStarted = false;
    }

    private void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressPoint is null || Pane is null)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ResetPointerInteraction();
            return;
        }

        var current = e.GetPosition(this);
        var delta = current - _pressPoint.Value;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        if (_interactionMode == PointerInteractionMode.MarqueePending)
        {
            UpdateMarquee(_pressPoint.Value, current, e.KeyModifiers);
            return;
        }

        if (_dragStarted || _interactionMode != PointerInteractionMode.DragOutPending
            || Pane.SelectedEntries.Count == 0 || _pressArgs is null)
            return;

        _dragStarted = true;
        var pressArgs = _pressArgs;
        ResetPointerInteraction();

        _ = StartDragOutAsync(pressArgs);
    }

    private void UpdateMarquee(Point start, Point current, KeyModifiers modifiers)
    {
        if (Pane is null)
            return;

        var rect = new Rect(
            Math.Min(start.X, current.X),
            Math.Min(start.Y, current.Y),
            Math.Abs(current.X - start.X),
            Math.Abs(current.Y - start.Y));

        SelectionMarquee.IsActive = true;
        SelectionMarquee.SelectionRect = rect;

        var hits = CollectEntriesInRect(rect);
        Pane.SelectByBounds(hits, modifiers.HasFlag(KeyModifiers.Control));
        SyncSelectionToView();
    }

    private List<EntryItemViewModel> CollectEntriesInRect(Rect rect)
    {
        var hits = new List<EntryItemViewModel>();
        if (Pane is null)
            return hits;

        if (DetailsGrid.IsVisible)
            CollectFromItemsControl(DetailsGrid, rect, hits);
        else if (ListView.IsVisible)
            CollectFromItemsControl(ListView, rect, hits);
        else if (Pane.IsGridView)
            CollectFromItemsControl(GridView, rect, hits);

        return hits;
    }

    private void CollectFromItemsControl(Control host, Rect rect, List<EntryItemViewModel> hits)
    {
        foreach (var child in host.GetVisualDescendants())
        {
            if (child is not Control { DataContext: EntryItemViewModel entry })
                continue;

            var topLeft = child.TranslatePoint(new Point(0, 0), this);
            if (topLeft is null)
                continue;

            var bounds = new Rect(topLeft.Value, child.Bounds.Size);
            if (rect.Intersects(bounds) && !hits.Contains(entry))
                hits.Add(entry);
        }
    }

    private async Task StartDragOutAsync(PointerPressedEventArgs pressArgs)
    {
        if (Pane is null)
            return;

        var virtualPaths = Pane.SelectedEntries.Select(x => x.FullPath).ToList();
        var physicalPaths = await Pane.ResolvePhysicalPathsAsync(virtualPaths).ConfigureAwait(true);
        var transfer = await BuildFileTransferAsync(physicalPaths).ConfigureAwait(true);
        if (transfer is null)
            return;

        var effects = DragDropEffects.Copy | DragDropEffects.Move;
        await DragDrop.DoDragDropAsync(pressArgs, transfer, effects).ConfigureAwait(true);
    }

    private void ResetPointerInteraction()
    {
        _pressPoint = null;
        _pressArgs = null;
        _dragStarted = false;
        _interactionMode = PointerInteractionMode.None;
        SelectionMarquee.IsActive = false;
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
        => ResetPointerInteraction();

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
