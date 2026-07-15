using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
    private bool _dragStarted;
    private bool _syncingSelectionToView;
    private PointerInteractionMode _interactionMode;
    private EntryItemViewModel? _dropTargetEntry;
    private readonly List<Control> _millerColumns = new();
    private readonly FuncDataTemplate<EntryItemViewModel> _millerItemTemplate = new(
        (item, _) => new TextBlock { Text = item?.DisplayName ?? string.Empty });

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

        // Arm a potential drag-out. The grid tile marks the event handled (to stop the underlying
        // ListBox from selecting the row), so the container-level OnListPointerPressed never sees
        // it — record the press state here instead so OnListPointerMoved can start the drag.
        _pressPoint = e.GetPosition(this);
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
                pane.UpdateSelection(Array.Empty<EntryItemViewModel>());

            _interactionMode = PointerInteractionMode.MarqueePending;
        }

        _pressPoint = e.GetPosition(this);
        _pressArgs = e;
        _dragStarted = false;
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

        _pressPoint = e.GetPosition(this);
        _pressArgs = e;
        _dragStarted = false;
        _interactionMode = Pane.SelectedEntries.Contains(entry)
            ? PointerInteractionMode.DragOutPending
            : PointerInteractionMode.None;
    }

    /// <summary>
    /// Clears selection when the user left-clicks blank space inside the list/grid/details area.
    /// Attached with handledEventsToo:true because the underlying DataGrid/ListBox/VirtualizingFileGrid
    /// class handlers mark the press handled for their own selection management — without that, the
    /// normally attached <see cref="OnListPointerPressed"/> never sees the blank-area press and the
    /// previous selection survives. Skips text boxes, headers, scrollbars, and any press on an entry.
    /// </summary>
    private void OnBlankAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
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

        if (Pane.SelectedEntries.Count == 0)
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

        Pane.UpdateSelection(Array.Empty<EntryItemViewModel>());
        SyncSelectionToView();
        // Arm marquee after clearing so dragging from blank space still rubber-bands.
        _pressPoint = e.GetPosition(this);
        _pressArgs = e;
        _dragStarted = false;
        _interactionMode = PointerInteractionMode.MarqueePending;
    }

    /// <summary>Detects clicks on column headers, scrollbars, or other non-list chrome that should not clear the selection.</summary>
    private static bool IsPointOnHeaderOrScrollbar(Control host, Point position)
    {
        foreach (var descendant in host.GetVisualDescendants())
        {
            if (descendant is not Control c || !c.IsVisible)
                continue;

            // Scrollbars and column headers must not trigger deselection.
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
    /// The external drag payload builder, resolved once from DI. Falls back to a directly-constructed
    /// default when the app service provider is unavailable (e.g. in design-time previews).
    /// </summary>
    private IExternalFileDragPayloadBuilder DragPayloadBuilder =>
        _dragPayloadBuilder ??= App.Services?.GetService<IExternalFileDragPayloadBuilder>()
            ?? new AvaloniaExternalFileDragPayloadBuilder(
                NullLogger<AvaloniaExternalFileDragPayloadBuilder>.Instance);

    private HelixExplorer.Core.Infrastructure.IExternalFileDragService? ExternalFileDragService =>
        _externalFileDragService ??= App.Services?.GetService<HelixExplorer.Core.Infrastructure.IExternalFileDragService>();

    // ── Drop target ──────────────────────────────────────────────────────────

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
        foreach (var child in host.GetVisualDescendants())
        {
            if (child is not Control { DataContext: EntryItemViewModel entry, IsVisible: true })
                continue;

            var topLeft = child.TranslatePoint(new Point(0, 0), host);
            if (topLeft is null)
                continue;

            var bounds = new Rect(topLeft.Value, child.Bounds.Size);
            if (bounds.Contains(position))
                return entry;
        }

        return null;
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

    private string? ResolveDropDestination(EntryItemViewModel? targetEntry)
        => Pane?.ResolveDropDestination(targetEntry);
}
