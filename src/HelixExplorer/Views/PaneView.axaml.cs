using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelixExplorer.Controls;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using HelixExplorer.Input;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
    private IExternalFileDragPayloadBuilder? _dragPayloadBuilder;
    private HelixExplorer.Core.Infrastructure.IExternalFileDragService? _externalFileDragService;
    private Point? _pressPoint;
    private PointerPressedEventArgs? _pressArgs;
    private Control? _pressHost;
    private bool _dragStarted;
    private bool _syncingSelectionToView;
    private PointerInteractionMode _interactionMode;
    private EntryItemViewModel? _dropTargetEntry;
    private readonly List<Control> _millerColumns = new();
    private readonly Dictionary<Control, List<Control>> _headerAndScrollbarCache = new();
    private int _millerRequestVersion;
    private readonly FuncDataTemplate<EntryItemViewModel> _millerItemTemplate = new(CreateMillerItem);

    private static Control CreateMillerItem(EntryItemViewModel? item, INameScope _)
    {
        var icon = new EntryVisualView
        {
            Width = 20,
            Height = 20,
            DataContext = item,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var name = new TextBlock
        {
            Text = item?.DisplayName ?? string.Empty,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 1);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*") };
        row.Children.Add(icon);
        row.Children.Add(name);
        return row;
    }

    public PaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetailsGrid.ContextRequested += OnDetailsContextRequested;

        // The DataGrid and ListBox mark PointerPressed/PointerMoved handled for their own selection
        // before the event bubbles to the normally-attached handlers, so the drag-out gesture never
        // started on an item press. Register these with handledEventsToo:true so drag detection
        // still runs after the control has handled selection.
        AttachDragGestureHandlers(DetailsGrid);
        AttachDragGestureHandlers(ListView);
        AttachDragGestureHandlers(GridView);
        // Grid's inner ListBox marks presses handled; XAML PointerPressed on GridView never sees blank
        // clicks without handledEventsToo — arm marquee/clear here instead.
        GridView.AddHandler(PointerPressedEvent, OnGridBlankPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnBlankAreaPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void AttachDragGestureHandlers(Control control)
    {
        control.AddHandler(PointerPressedEvent, OnItemDragArmPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        control.AddHandler(PointerPressedEvent, OnBlankAreaPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        control.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        control.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
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

        if (e.PropertyName == nameof(PaneViewModel.IsRenaming) && _pane.IsRenaming)
        {
            Dispatcher.UIThread.Post(FocusRenameEditor);
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
        else if (e.PropertyName == nameof(PaneViewModel.SelectedCount))
        {
            // Keep DataGrid/ListBox chrome in sync when selection changes programmatically
            // (blank clear, Clear/Invert commands, Select All from the window VM).
            SyncSelectionToView();
        }
    }

    private async void OnEntryContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        if (menu.PlacementTarget is not Control { DataContext: PaneViewModel pane })
            return;

        menu.DataContext = pane;
        // Resolve clipboard validity before command state is applied so Paste enablement matches
        // the current internal/OS payload (including external Explorer copies).
        await pane.RefreshPasteAvailabilityAsync().ConfigureAwait(true);
        pane.NotifyCommandsCanExecuteChanged();
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

        var (nextColumn, nextDescending) = SortSelection.Toggle(Pane.SortColumn, Pane.SortDescending, column);
        Pane.SortColumn = nextColumn;
        Pane.SortDescending = nextDescending;

        e.Handled = true;
    }

    private void OnGridPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Pane is null || (e.KeyModifiers & KeyModifiers.Control) == 0)
            return;

        Pane.AdjustThumbnailSize(e.Delta.Y > 0 ? WheelThumbnailStep : -WheelThumbnailStep);
        e.Handled = true;
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (Pane is null)
            return;

        switch (RenameKeyGesture.Resolve(e.Key, e.KeyModifiers))
        {
            case RenameKeyAction.Commit:
                Pane.CommitRenameCommand.Execute(null);
                e.Handled = true;
                break;
            case RenameKeyAction.Cancel:
                Pane.CancelRenameCommand.Execute(null);
                e.Handled = true;
                break;
            case RenameKeyAction.Contain:
                // The TextBox has already moved the caret via its own class handler; mark the event
                // handled so it does not bubble to the list and change the item selection.
                e.Handled = true;
                break;
        }
    }

    private void OnRenameTextBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: EntryItemViewModel { IsRenaming: true } } textBox)
            Dispatcher.UIThread.Post(() => FocusRenameEditor(textBox));
    }

    private void OnRenameTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_pane?.IsRenaming != true)
            return;

        var entry = _pane.Entries.FirstOrDefault(x => x.IsRenaming);
        if (entry is not null && string.IsNullOrWhiteSpace(entry.RenameText))
            _pane.CancelRenameCommand.Execute(null);
        else
            _pane.CommitRenameCommand.Execute(null);
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
        else if (e.Key == Key.Escape && (Pane.IsFilterMode || Pane.IsSearchMode))
        {
            Pane.ClearFilter();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Pane.IsRenaming)
        {
            Pane.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Pane.SelectedCount > 0)
        {
            // Lower priority than filter/search/rename; window Escape binding covers other focus cases.
            Pane.ClearSelection();
            SyncSelectionToView();
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
        else if (e.Key == Key.A && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            Pane.InvertSelection();
            SyncSelectionToView();
            e.Handled = true;
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (Pane is null || TextInputFocus.IsActive() || !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return;

        var current = Pane.SelectedEntry ?? Pane.SelectedEntries.LastOrDefault();
        if (current is null)
            return;

        var currentIndex = Pane.Entries.IndexOf(current);
        if (GridView.TryGetAdjacentIndex(currentIndex, Pane.Entries.Count, e.Key, out var targetIndex))
        {
            Pane.SelectGridNavigationTarget(Pane.Entries[targetIndex], e.KeyModifiers);
            SyncSelectionToView();
        }

        e.Handled = true;
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
                    SyncDataGridSelection(grid, selected);
                    break;
                case ListBox list:
                    list.SelectedItems?.Clear();
                    if (selected.Count == 0)
                        list.SelectedItem = null;
                    else
                    {
                        foreach (var entry in selected)
                            list.SelectedItems?.Add(entry);
                    }

                    break;
            }
        }
        finally
        {
            _syncingSelectionToView = false;
        }
    }

    private static void SyncDataGridSelection(DataGrid grid, List<EntryItemViewModel> selected)
    {
        // Avalonia DataGrid often leaves :selected row chrome after SelectedItems.Clear() alone.
        if (grid.SelectedItems is { } items)
        {
            while (items.Count > 0)
                items.RemoveAt(items.Count - 1);
        }

        grid.SelectedItem = null;
        grid.SelectedIndex = -1;

        if (selected.Count == 0)
            return;

        foreach (var entry in selected)
            grid.SelectedItems?.Add(entry);

        if (selected.Count == 1)
            grid.SelectedItem = selected[0];
    }

    private Control? GetVisibleControl()
    {
        if (Pane is null)
            return null;

        if (Pane.IsDetailsView) return DetailsGrid;
        if (Pane.IsListView) return ListView;
        if (Pane.IsGridView) return GridView;
        return null;
    }

    private void OnGridItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsTextBoxSource(e.Source))
            return;

        if (Pane is null || TryGetEntryFromSource(sender) is not { } entry)
            return;

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (!Pane.SelectedEntries.Contains(entry))
                SelectGridEntry(entry, KeyModifiers.None);
            return;
        }

        SelectGridEntry(entry, e.KeyModifiers);
        GridView.Focus();

        // Arm a potential drag-out. The grid tile marks the event handled (to stop the underlying
        // ListBox from selecting the row), so the container-level OnListPointerPressed never sees
        // it — record the press state here instead so OnListPointerMoved can start the drag.
        _pressHost = GridView;
        _pressPoint = e.GetPosition(GridView);
        _pressArgs = e;
        _dragStarted = false;
        _interactionMode = Pane.SelectedEntries.Contains(entry)
            ? PointerInteractionMode.DragOutPending
            : PointerInteractionMode.None;

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

    private static bool IsTextBoxSource(object? source)
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
        {
            if (control is TextBox)
                return true;
        }

        return false;
    }

    private void OnGridItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: EntryItemViewModel entry })
            Pane?.ActivateEntry(entry);
    }

    private void RebuildMiller()
    {
        if (Pane is null)
            return;

        MillerPanel.Children.Clear();
        _millerColumns.Clear();
        _millerRequestVersion++;

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
            ItemTemplate = _millerItemTemplate,
            SelectionMode = SelectionMode.Single
        };
        list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        MillerColumnPanel.SetColumnIndex(list, columnIndex);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is EntryItemViewModel entry)
            {
                Pane?.SelectEntry(entry, KeyModifiers.None);
                if (entry.IsDirectory || ArchivePath.IsArchiveFile(entry.FullPath))
                    MillerPanel.RaiseActivated(columnIndex, entry);
            }
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is EntryItemViewModel { IsDirectory: false } entry
                && !ArchivePath.IsArchiveFile(entry.FullPath))
                Pane?.ActivateEntry(entry);
        };
        return list;
    }

    private async void OnMillerColumnActivated(object? sender, MillerColumnActivatedEventArgs e)
    {
        if (Pane is null || e.Item is not EntryItemViewModel entry)
            return;

        if (!entry.IsDirectory && !ArchivePath.IsArchiveFile(entry.FullPath))
            return;

        var requestVersion = ++_millerRequestVersion;

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
        if (requestVersion != _millerRequestVersion || Pane is null)
            return;
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

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsTextBoxSource(e.Source))
            return;

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
                ClearSelectionAndSync();

            _interactionMode = PointerInteractionMode.MarqueePending;
        }

        _pressHost = GetVisibleControl();
        _pressPoint = _pressHost is null ? e.GetPosition(this) : e.GetPosition(_pressHost);
        _pressArgs = e;
        _dragStarted = false;

        if (_interactionMode == PointerInteractionMode.MarqueePending && _pressHost is not null)
            e.Pointer.Capture(_pressHost);
    }

    /// <summary>
    /// Grid-only blank press with handledEventsToo. The inner ListBox swallows presses, so the
    /// XAML OnListPointerPressed on VirtualizingFileGrid often never runs for empty-area drags.
    /// </summary>
    private void OnGridBlankPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Pane is null
            || !Pane.IsGridView
            || Pane.IsHome
            || IsTextBoxSource(e.Source)
            || !e.GetCurrentPoint(GridView).Properties.IsLeftButtonPressed
            || TryGetEntryFromSource(e.Source) is not null)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        var positionInControl = e.GetPosition(GridView);
        if (IsPointOnHeaderOrScrollbar(GridView, positionInControl))
            return;

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            ClearSelectionAndSync();

        _pressHost = GridView;
        _pressPoint = positionInControl;
        _pressArgs = e;
        _dragStarted = false;
        _interactionMode = PointerInteractionMode.MarqueePending;
        e.Pointer.Capture(GridView);
    }

    // Fires even when the DataGrid/ListBox mark the press handled for their own selection (which is
    // why OnListPointerPressed, attached via normal routing, never sees an item press on those
    // controls). This arms a drag-out for the pressed item without duplicating selection — the
    // native control has already updated the selection by the time this runs. Empty-area, header
    // and scrollbar presses (no item under the pointer) are intentionally ignored here so the
    // marquee/selection behaviour in OnListPointerPressed is left untouched.
    private void OnItemDragArmPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsTextBoxSource(e.Source)
            || Pane is null
            || Pane.IsHome
            || ReferenceEquals(sender, GridView)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || TryGetEntryFromSource(e.Source) is not { } entry)
            return;

        _pressHost = sender as Control ?? GetVisibleControl();
        _pressPoint = _pressHost is null ? e.GetPosition(this) : e.GetPosition(_pressHost);
        _pressArgs = e;
        _dragStarted = false;
        _interactionMode = Pane.SelectedEntries.Contains(entry)
            ? PointerInteractionMode.DragOutPending
            : PointerInteractionMode.None;
    }

    /// <summary>
    /// Attached with handledEventsToo:true because DataGrid/ListBox/VirtualizingFileGrid mark the press
    /// handled for their own selection — without that, blank-area clicks never clear the previous selection.
    /// </summary>
    private void OnBlankAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Grid blank presses are owned by OnGridBlankPointerPressed (ListBox swallows the XAML path).
        if (ReferenceEquals(sender, GridView) || Pane?.IsGridView == true)
            return;

        if (Pane is null
            || Pane.IsHome
            || IsTextBoxSource(e.Source)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || TryGetEntryFromSource(e.Source) is not null)
            return;

        // Ctrl-click on blank space extends the existing selection rather than clearing it,
        // matching Explorer's behaviour.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        // The grid's tile container handles its own press for selection; we still want blank-area
        // clicks (between tiles, after the last row) to clear. The grid ListBox class handler
        // fires before this handler and marks the event handled, but handledEventsToo:true lets
        // us run afterwards.
        var grid = GetVisibleControl();
        if (grid is null)
            return;

        var positionInControl = e.GetPosition(grid);
        if (IsPointOnHeaderOrScrollbar(grid, positionInControl))
            return;

        // Always sync even when the model is already empty — OnListPointerPressed may have cleared
        // the VM first, leaving DataGrid/ListBox :selected chrome stale.
        ClearSelectionAndSync();
        // Clear first so marquee doesn't union with stale selection.
        _pressHost = grid;
        _pressPoint = e.GetPosition(grid);
        _pressArgs = e;
        _dragStarted = false;
        _interactionMode = PointerInteractionMode.MarqueePending;
        e.Pointer.Capture(grid);
    }

    private void ClearSelectionAndSync()
    {
        if (Pane is null)
            return;

        if (Pane.SelectedEntries.Count > 0)
            Pane.UpdateSelection(Array.Empty<EntryItemViewModel>());

        SyncSelectionToView();
    }

    private bool IsPointOnHeaderOrScrollbar(Control host, Point position)
    {
        if (!_headerAndScrollbarCache.TryGetValue(host, out var controls))
        {
            controls = [];
            foreach (var descendant in host.GetVisualDescendants())
            {
                if (descendant is Control c && (c is ScrollBar or DataGridColumnHeader))
                    controls.Add(c);
            }
            _headerAndScrollbarCache.Add(host, controls);
        }

        foreach (var c in controls)
        {
            if (!c.IsVisible)
                continue;

            if (c is ScrollBar or DataGridColumnHeader)
            {
                var topLeft = c.TranslatePoint(new Point(0, 0), host);
                if (topLeft is { } origin && new Rect(origin, c.Bounds.Size).Contains(position))
                    return true;
            }
        }

        return false;
    }

    private void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressPoint is null || Pane is null)
            return;

        var host = _pressHost ?? GetVisibleControl() ?? this;
        if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
        {
            ResetPointerInteraction(e);
            return;
        }

        var current = e.GetPosition(host);
        var delta = current - _pressPoint.Value;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        if (_interactionMode == PointerInteractionMode.MarqueePending)
        {
            UpdateMarquee(host, _pressPoint.Value, current, e.KeyModifiers);
            return;
        }

        if (_dragStarted || _interactionMode != PointerInteractionMode.DragOutPending
            || Pane.SelectedEntries.Count == 0 || _pressArgs is null)
            return;

        _dragStarted = true;
        var pressArgs = _pressArgs;
        ResetPointerInteraction(e);

        _ = StartDragOutAsync(pressArgs);
    }

    private void UpdateMarquee(Control host, Point start, Point current, KeyModifiers modifiers)
    {
        if (Pane is null)
            return;

        var rectInHost = new Rect(
            Math.Min(start.X, current.X),
            Math.Min(start.Y, current.Y),
            Math.Abs(current.X - start.X),
            Math.Abs(current.Y - start.Y));

        SelectionMarquee.IsActive = true;
        SelectionMarquee.SelectionRect = MapRectToMarquee(host, rectInHost);

        var hits = CollectEntriesInRect(host, rectInHost);
        Pane.SelectByBounds(hits, modifiers.HasFlag(KeyModifiers.Control));
        SyncSelectionToView();
    }

    private Rect MapRectToMarquee(Control host, Rect rectInHost)
    {
        var topLeft = host.TranslatePoint(new Point(rectInHost.X, rectInHost.Y), SelectionMarquee);
        var bottomRight = host.TranslatePoint(
            new Point(rectInHost.Right, rectInHost.Bottom),
            SelectionMarquee);
        if (topLeft is null || bottomRight is null)
            return rectInHost;

        return new Rect(
            Math.Min(topLeft.Value.X, bottomRight.Value.X),
            Math.Min(topLeft.Value.Y, bottomRight.Value.Y),
            Math.Abs(bottomRight.Value.X - topLeft.Value.X),
            Math.Abs(bottomRight.Value.Y - topLeft.Value.Y));
    }

    private List<EntryItemViewModel> CollectEntriesInRect(Control host, Rect rectInHost)
    {
        var hits = new List<EntryItemViewModel>();
        if (Pane is null)
            return hits;

        if (host is VirtualizingFileGrid grid)
        {
            grid.CollectEntriesInRect(rectInHost, hits);
            return hits;
        }

        CollectFromItemsControl(host, rectInHost, hits);
        return hits;
    }

    private void CollectFromItemsControl(Control host, Rect rectInHost, List<EntryItemViewModel> hits)
    {
        var hostBounds = new Rect(host.Bounds.Size);
        var clip = rectInHost.Intersect(hostBounds);
        if (clip.Width < 1 || clip.Height < 1)
            return;

        foreach (var child in host.GetVisualDescendants())
        {
            if (child is not Control control
                || !control.IsVisible
                || control.DataContext is not EntryItemViewModel entry
                || !IsMarqueeHitTarget(control, host)
                || !TryGetBoundsInSpace(control, host, out var bounds))
                continue;

            bounds = bounds.Intersect(hostBounds);
            if (bounds.Width < 1 || bounds.Height < 1)
                continue;

            if (clip.Intersects(bounds) && !hits.Contains(entry))
                hits.Add(entry);
        }
    }

    /// <summary>
    /// Details/List hit row containers. Grid uses <see cref="VirtualizingFileGrid.CollectEntriesInRect"/>.
    /// </summary>
    private static bool IsMarqueeHitTarget(Control control, Control host)
    {
        if (host is VirtualizingFileGrid)
            return false;

        return control is DataGridRow or ListBoxItem;
    }

    private static bool TryGetBoundsInSpace(Control control, Visual space, out Rect bounds)
    {
        bounds = default;
        var width = control.Bounds.Width;
        var height = control.Bounds.Height;
        if (width < 1 || height < 1 || double.IsNaN(width) || double.IsNaN(height))
            return false;

        var matrix = control.TransformToVisual(space);
        if (matrix is null)
            return false;

        var m = matrix.Value;
        var p0 = m.Transform(new Point(0, 0));
        var p1 = m.Transform(new Point(width, 0));
        var p2 = m.Transform(new Point(0, height));
        var p3 = m.Transform(new Point(width, height));

        var minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        var minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        var maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        var maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        var w = maxX - minX;
        var h = maxY - minY;
        if (w < 1 || h < 1)
            return false;

        bounds = new Rect(minX, minY, w, h);
        return true;
    }

    private async Task StartDragOutAsync(PointerPressedEventArgs pressArgs)
    {
        if (Pane is null)
            return;

        // Resolve and materialize the payload BEFORE starting the OS drag. Building the transfer while
        // the drag is already in flight risks the user releasing over the target (e.g. a browser upload
        // field) before DoDragDropAsync has actually begun, which drops the operation silently.
        var virtualPaths = Pane.SelectedEntries.Select(x => x.FullPath).ToList();
        if (virtualPaths.Count == 0)
            return;

        var physicalPaths = await Pane.ResolvePhysicalPathsAsync(virtualPaths).ConfigureAwait(true);

        if (ExternalFileDragService is { } nativeDrag)
        {
            nativeDrag.DoDragDrop(
                physicalPaths,
                HelixExplorer.Core.Infrastructure.DragDropEffects.Copy
                | HelixExplorer.Core.Infrastructure.DragDropEffects.Move);
            return;
        }

        var transfer = await BuildFileTransferAsync(physicalPaths).ConfigureAwait(true);
        if (transfer is null)
            return;

        // Copy is the default/preferred effect so browser upload fields treat the drop as an upload;
        // Move stays available for internal Explorer targets that request it via modifier keys.
        var effects = DragDropEffects.Copy | DragDropEffects.Move;
        await DragDrop.DoDragDropAsync(pressArgs, transfer, effects).ConfigureAwait(true);
    }

    private void ResetPointerInteraction(PointerEventArgs? e = null)
    {
        if (e is not null)
            e.Pointer.Capture(null);
        else if (_pressHost is not null)
        {
            // Best-effort release when we don't have the event (e.g. cancelled mid-gesture).
        }

        _pressPoint = null;
        _pressArgs = null;
        _pressHost = null;
        _dragStarted = false;
        _interactionMode = PointerInteractionMode.None;
        SelectionMarquee.IsActive = false;
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
        => ResetPointerInteraction(e);

    private void FocusRenameEditor()
    {
        var editor = this.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(x => x.DataContext is EntryItemViewModel { IsRenaming: true });

        if (editor is not null)
            FocusRenameEditor(editor);
    }

    private void FocusRenameEditor(TextBox editor)
    {
        if (editor.DataContext is not EntryItemViewModel entry || !entry.IsRenaming)
            return;

        editor.Focus();
        var length = PaneViewModel.GetRenameBaseNameLength(editor.Text ?? string.Empty, entry.IsDirectory);
        editor.SelectionStart = 0;
        editor.SelectionEnd = length;
    }

    private async Task<DataTransfer?> BuildFileTransferAsync(IReadOnlyList<string> paths)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || paths.Count == 0)
            return null;

        return await DragPayloadBuilder.BuildAsync(storage, paths).ConfigureAwait(true);
    }

    /// <summary>
    /// Falls back to a direct construct when <see cref="App.Services"/> is unavailable (design-time previews).
    /// </summary>
    private IExternalFileDragPayloadBuilder DragPayloadBuilder =>
        _dragPayloadBuilder ??= App.Services?.GetService<IExternalFileDragPayloadBuilder>()
            ?? new AvaloniaExternalFileDragPayloadBuilder(
                NullLogger<AvaloniaExternalFileDragPayloadBuilder>.Instance);

    private HelixExplorer.Core.Infrastructure.IExternalFileDragService? ExternalFileDragService =>
        _externalFileDragService ??= App.Services?.GetService<HelixExplorer.Core.Infrastructure.IExternalFileDragService>();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (Pane is null)
            return;

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        var targetEntry = FindEntryUnderDrag(e);
        var destinationPath = Pane.ResolveDropDestination(targetEntry);
        if (string.IsNullOrEmpty(destinationPath))
        {
            ClearDropTarget();
            return;
        }

        UpdateDropTarget(targetEntry);

        e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
        => ClearDropTarget();

    private void UpdateDropTarget(EntryItemViewModel? entry)
    {
        if (ReferenceEquals(_dropTargetEntry, entry))
            return;

        if (_dropTargetEntry is not null)
            _dropTargetEntry.IsDropTarget = false;

        _dropTargetEntry = entry;

        if (_dropTargetEntry is { IsDirectory: true })
            _dropTargetEntry.IsDropTarget = true;
    }

    private void ClearDropTarget()
    {
        if (_dropTargetEntry is not null)
        {
            _dropTargetEntry.IsDropTarget = false;
            _dropTargetEntry = null;
        }
    }

    private EntryItemViewModel? FindEntryUnderDrag(DragEventArgs e)
    {
        if (DetailsGrid.IsVisible)
            return HitTestEntry(DetailsGrid, e.GetPosition(DetailsGrid));
        if (ListView.IsVisible)
            return HitTestEntry(ListView, e.GetPosition(ListView));
        if (Pane?.IsGridView == true)
            return HitTestEntry(GridView, e.GetPosition(GridView));

        return null;
    }

    private static EntryItemViewModel? HitTestEntry(Control host, Point position)
    {
        EntryItemViewModel? fallback = null;
        foreach (var child in host.GetVisualDescendants())
        {
            if (child is not Control { DataContext: EntryItemViewModel entry, IsVisible: true } control)
                continue;

            var topLeft = control.TranslatePoint(new Point(0, 0), host);
            var bottomRight = control.TranslatePoint(
                new Point(control.Bounds.Width, control.Bounds.Height),
                host);
            if (topLeft is null || bottomRight is null)
                continue;

            var bounds = new Rect(
                Math.Min(topLeft.Value.X, bottomRight.Value.X),
                Math.Min(topLeft.Value.Y, bottomRight.Value.Y),
                Math.Abs(bottomRight.Value.X - topLeft.Value.X),
                Math.Abs(bottomRight.Value.Y - topLeft.Value.Y));
            if (!bounds.Contains(position))
                continue;

            // Prefer tagged grid tiles over nested EntryVisualView descendants.
            if (control.Classes.Contains("fileTile")
                || control is DataGridRow or ListBoxItem)
                return entry;

            fallback ??= entry;
        }

        return fallback;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (Pane is null)
            return;

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        var targetEntry = FindEntryUnderDrag(e);
        var destinationPath = Pane.ResolveDropDestination(targetEntry);
        if (string.IsNullOrEmpty(destinationPath))
            return;

        ClearDropTarget();

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
        await Pane.HandleDropAsync(paths, destinationPath, isCopy);

        e.Handled = true;
    }

}
