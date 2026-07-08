using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelixExplorer.Controls;
using HelixExplorer.Models;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

/// <summary>
/// Renders one file pane. Switches between Details, Grid, and Miller views based on
/// the bound <see cref="PaneViewModel.ViewMode"/>. Also routes right-click to the native
/// Win32 context menu via <see cref="IContextMenuService"/>.
/// </summary>
public sealed partial class PaneView : UserControl
{
    public PaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private PaneViewModel? _pane;

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_pane != null) _pane.PropertyChanged -= OnPanePropertyChanged;
        _pane = DataContext as PaneViewModel;
        if (_pane != null)
        {
            _pane.PropertyChanged += OnPanePropertyChanged;
            ApplyViewMode(_pane.ViewMode);
        }
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.ViewMode) && _pane != null)
        {
            Dispatcher.UIThread.Post(() => ApplyViewMode(_pane.ViewMode));
            SyncViewModeSelector();
        }
        else if (e.PropertyName == nameof(PaneViewModel.FilteredEntries))
        {
            // Miller view is custom-built per listing -> re-render on change.
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

    /// <summary>Populates the Miller column host. The first column always reflects the
    /// top-level listing; drilling appends child columns.</summary>
    private void RebuildMiller()
    {
        if (_pane is null) return;
        MillerPanel.Children.Clear();
        var column = BuildMillerColumn(_pane.FilteredEntries, columnIndex: 0);
        MillerPanel.Children.Add(column);
    }

    private Control BuildMillerColumn(IReadOnlyList<FileSystemEntry> entries, int columnIndex)
    {
        var lbs = new ListBox { Width = 250, ItemsSource = new AvaloniaList<FileSystemEntry>(entries) };
        lbs.DoubleTapped += (_, _) =>
        {
            if (lbs.SelectedItem is FileSystemEntry f) MillerPanel.RaiseActivated(columnIndex, f);
        };
        return lbs;
    }

    private void OnMillerColumnActivated(object? sender, MillerColumnActivatedEventArgs e)
    {
        if (_pane is null) return;
        if (e.Item is FileSystemEntry entry)
        {
            _pane.NavigateTo(entry.FullPath);
        }
    }

    private void OnItemActivated(object? sender, RoutedEventArgs e)
    {
        if (_pane is null) return;
        if (DetailsGrid.SelectedItem is FileSystemEntry entry)
        {
            _pane.ActivateCommand.Execute(entry);
        }
        else if (e.Source is Control c && c.DataContext is FileSystemEntry srcEntry)
        {
            _pane.ActivateCommand.Execute(srcEntry);
        }
    }

    private void OnTileDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.DataContext is FileSystemEntry entry && _pane != null)
        {
            _pane.ActivateCommand.Execute(entry);
        }
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            ShowNativeContextMenu(e.GetPosition(this));
            e.Handled = true;
        }
    }

    private void OnContentPointerPressed(object? sender, PointerPressedEventArgs e) { /* reserved */ }

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

    private void OnShowNativeContextMenu(object? sender, RoutedEventArgs e)
    {
        var pt = Bounds.Center;
        ShowNativeContextMenu(pt);
    }

    /// <summary>Requests the native context menu at the given pane-local point for the
    /// directory or the currently selected entries.</summary>
    private void ShowNativeContextMenu(Point localPoint)
    {
        if (_pane is null) return;

        var topLevel = this.GetVisualRoot() as TopLevel;
        var hwnd = topLevel?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;

        // Translate pane-local point to screen pixels.
        var pixel = this.PointToScreen(localPoint);

        // For now, request the background context menu (no specific selections).
        var folderPath = _pane.IsArchive ? string.Empty : _pane.CurrentPath;
        if (string.IsNullOrEmpty(folderPath)) return;

        Task.Run(() => ServiceLocator.ContextMenu.ShowContextMenu(hwnd, folderPath, null, pixel));
    }
}