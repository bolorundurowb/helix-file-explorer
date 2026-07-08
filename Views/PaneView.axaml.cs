using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelixExplorer.Controls;
using HelixExplorer.Models;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

/// <summary>
/// Renders one file pane. Switches between Details, Grid, and Miller views based on the
/// bound <see cref="PaneViewModel.ViewMode"/>, hosts drag/drop, and routes right-click to
/// the native Win32 context menu via <see cref="IContextMenuService"/> on the UI thread.
/// </summary>
public sealed partial class PaneView : UserControl
{
    private const string HelixPaths = "helix/paths";

    private PaneViewModel? _pane;
    private readonly List<Control> _millerColumns = new();

    private Point _pressPoint;
    private FileSystemEntry? _dragCandidate;
    private bool _mayDrag;

    public PaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_pane != null) _pane.PropertyChanged -= OnPanePropertyChanged;
        _pane = DataContext as PaneViewModel;
        if (_pane != null)
        {
            _pane.PropertyChanged += OnPanePropertyChanged;
            SyncViewModeSelector();
            ApplyViewMode(_pane.ViewMode);
        }
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.ViewMode) && _pane != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SyncViewModeSelector();
                ApplyViewMode(_pane.ViewMode);
            });
        }
        else if (e.PropertyName == nameof(PaneViewModel.FilteredEntries))
        {
            if (_pane?.ViewMode == LayoutMode.Miller) RebuildMiller();
        }
    }

    private void OnViewModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_pane is null) return;
        if (ViewModeSelector.SelectedIndex is < 0 or > 2) return;
        _pane.ViewMode = (LayoutMode)ViewModeSelector.SelectedIndex;
    }

    private void SyncViewModeSelector()
    {
        if (_pane is null) return;
        ViewModeSelector.SelectedIndex = (int)_pane.ViewMode;
    }

    private void ApplyViewMode(LayoutMode mode)
    {
        DetailsGrid.IsVisible = mode == LayoutMode.Details;
        GridScroll.IsVisible = mode == LayoutMode.Grid;
        MillerScroll.IsVisible = mode == LayoutMode.Miller;
        if (mode == LayoutMode.Miller) RebuildMiller();
    }

    private void OnDetailsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_pane != null && DetailsGrid.SelectedItem is FileSystemEntry entry)
        {
            _pane.SelectedEntry = entry;
        }
    }

    // ---- Miller columns (true cascade) ----

    private void RebuildMiller()
    {
        if (_pane is null) return;
        MillerPanel.Children.Clear();
        _millerColumns.Clear();
        var column = BuildMillerColumn(_pane.FilteredEntries, columnIndex: 0);
        MillerPanel.Children.Add(column);
        _millerColumns.Add(column);
    }

    private Control BuildMillerColumn(IReadOnlyList<FileSystemEntry> entries, int columnIndex)
    {
        var list = new ListBox
        {
            Width = 250,
            ItemsSource = new AvaloniaList<FileSystemEntry>(entries)
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is FileSystemEntry f) MillerPanel.RaiseActivated(columnIndex, f);
        };
        return list;
    }

    private async void OnMillerColumnActivated(object? sender, MillerColumnActivatedEventArgs e)
    {
        if (_pane is null || e.Item is not FileSystemEntry entry) return;

        // Files open; folders append a new column to the right.
        if (!entry.IsDirectory && !ServiceLocator.Archive.IsArchive(entry.FullPath))
        {
            _pane.ActivateCommand.Execute(entry);
            return;
        }

        var keep = e.ColumnIndex + 1;
        while (_millerColumns.Count > keep)
        {
            var last = _millerColumns[^1];
            MillerPanel.Children.Remove(last);
            _millerColumns.RemoveAt(_millerColumns.Count - 1);
        }

        var children = await LoadChildrenAsync(entry.FullPath).ConfigureAwait(true);
        var column = BuildMillerColumn(children, keep);
        MillerPanel.Children.Add(column);
        _millerColumns.Add(column);
    }

    private static async Task<IReadOnlyList<FileSystemEntry>> LoadChildrenAsync(string path)
    {
        if (path.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            var p = path.EndsWith('/') ? path : path + "/";
            return await ServiceLocator.Archive.EnumerateAsync(p).ConfigureAwait(true);
        }
        return await ServiceLocator.FileSystem.GetDirectoryContentsAsync(path).ConfigureAwait(true);
    }

    // ---- activation ----

    private void OnItemActivated(object? sender, RoutedEventArgs e)
    {
        if (_pane is null) return;
        if (DetailsGrid.SelectedItem is FileSystemEntry entry)
        {
            _pane.ActivateCommand.Execute(entry);
        }
        else if (e.Source is Control { DataContext: FileSystemEntry srcEntry })
        {
            _pane.ActivateCommand.Execute(srcEntry);
        }
    }

    private void OnTileDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FileSystemEntry entry } && _pane != null)
        {
            _pane.ActivateCommand.Execute(entry);
        }
    }

    // ---- drag source (tiles) ----

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            ShowNativeContextMenu(e.GetPosition(this));
            e.Handled = true;
            return;
        }
        if (props.IsLeftButtonPressed && sender is Control { DataContext: FileSystemEntry entry })
        {
            _pressPoint = e.GetPosition(this);
            _dragCandidate = entry;
            _mayDrag = true;
        }
    }

    private async void OnTilePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_mayDrag || _dragCandidate is null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _mayDrag = false; return; }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _pressPoint.X) < 4 && Math.Abs(pos.Y - _pressPoint.Y) < 4) return;

        _mayDrag = false;
        var path = _dragCandidate.Value.FullPath;
        if (path.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase)) return; // can't drag virtual entries

        var data = new DataObject();
        data.Set(HelixPaths, path);
        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        catch (Exception ex) { Debug.WriteLine($"DoDragDrop: {ex.Message}"); }
    }

    // ---- drop target ----

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var copy = (e.KeyModifiers & KeyModifiers.Control) != 0;
        e.DragEffects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_pane is null) return;
        var copy = (e.KeyModifiers & KeyModifiers.Control) != 0;

        var paths = new List<string>();
        if (e.Data.Contains(HelixPaths) && e.Data.Get(HelixPaths) is string s && s.Length > 0)
        {
            paths.Add(s);
        }
        else
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var f in files)
                {
                    var local = f.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(local)) paths.Add(local!);
                }
            }
        }

        if (paths.Count > 0) await _pane.AcceptDropAsync(paths, copy);
    }

    // ---- keyboard / wheel ----

    private void OnPaneKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnGridWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_pane is null || (e.KeyModifiers & KeyModifiers.Control) == 0) return;
        _pane.AdjustThumbnail(e.Delta.Y > 0 ? 16 : -16);
        e.Handled = true;
    }

    // ---- folder tint ----

    private void OnSetFolderColor(object? sender, RoutedEventArgs e)
    {
        // TODO: surface a full colour-picker popup (UX spec §5). For now apply the accent.
        if (sender is Control { DataContext: FileSystemEntry entry } && entry.IsDirectory)
        {
            ServiceLocator.Theme.SetFolderColor(entry.FullPath, ServiceLocator.Theme.Accent);
        }
    }

    // ---- terminal + native shell menu ----

    private void OnOpenInTerminal(object? sender, RoutedEventArgs e)
    {
        if (_pane is null || _pane.IsArchive) return;
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{_pane.CurrentPath}\"")
            {
                UseShellExecute = true,
                CreateNoWindow = true
            })?.Dispose();
        }
        catch (Exception ex) { Debug.WriteLine($"OnOpenInTerminal: {ex.Message}"); }
    }

    private void OnShowNativeContextMenu(object? sender, RoutedEventArgs e) => ShowNativeContextMenu(Bounds.Center);

    /// <summary>Shows the native shell context menu. Runs on the UI (STA) thread — shell COM
    /// and TrackPopupMenuEx must not be marshalled onto a thread-pool (MTA) thread.</summary>
    private void ShowNativeContextMenu(Point localPoint)
    {
        if (_pane is null || _pane.IsArchive) return;

        var topLevel = this.GetVisualRoot() as TopLevel;
        var hwnd = topLevel?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;

        var pixel = this.PointToScreen(localPoint);
        var folderPath = _pane.CurrentPath;
        if (string.IsNullOrEmpty(folderPath)) return;

        ServiceLocator.ContextMenu.ShowContextMenu(hwnd, folderPath, null, pixel);
    }
}
