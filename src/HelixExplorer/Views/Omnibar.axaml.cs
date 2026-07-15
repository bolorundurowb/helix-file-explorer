using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HelixExplorer.Controls;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class Omnibar : UserControl
{
    private PaneViewModel? _pane;

    public Omnibar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_pane is not null)
            _pane.PropertyChanged -= OnPanePropertyChanged;

        _pane = DataContext as PaneViewModel;
        if (_pane is not null)
        {
            _pane.PropertyChanged += OnPanePropertyChanged;
            RebuildBreadcrumbs();
        }
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaneViewModel.CurrentPath) or nameof(PaneViewModel.Breadcrumbs))
            Dispatcher.UIThread.Post(RebuildBreadcrumbs);
    }

    private void RebuildBreadcrumbs()
    {
        BreadcrumbHost.Children.Clear();
        if (_pane is null)
            return;

        foreach (var segment in _pane.Breadcrumbs)
        {
            var item = BreadcrumbItem.FromSegment(segment);
            item.Click += OnBreadcrumbClick;
            DragDrop.SetAllowDrop(item, true);
            item.AddHandler(DragDrop.DragOverEvent, OnBreadcrumbDragOver);
            item.AddHandler(DragDrop.DragLeaveEvent, OnBreadcrumbDragLeave);
            item.AddHandler(DragDrop.DropEvent, OnBreadcrumbDrop);
            BreadcrumbHost.Children.Add(item);
        }
    }

    private void OnBreadcrumbClick(object? sender, RoutedEventArgs e)
    {
        if (sender is BreadcrumbItem item && _pane is not null)
            _pane.NavigateTo(item.SegmentPath);
    }

    private void OnBreadcrumbAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == BreadcrumbHost || e.Source == BreadcrumbScroll)
            _pane?.BeginEditPath();
    }

    private void OnEditToggle(object? sender, RoutedEventArgs e)
        => _pane?.BeginEditPath();

    private void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_pane is null)
            return;

        if (e.Key == Key.Enter)
        {
            _pane.CommitEditablePath();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _pane.CancelEditablePath();
            e.Handled = true;
        }
    }

    private void OnPathBoxLostFocus(object? sender, RoutedEventArgs e)
        => _pane?.CancelEditablePath();

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_pane is null)
            return;

        if (e.Key == Key.Escape)
        {
            _pane.ExitSearchModeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSearchBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_pane is { IsSearchMode: true, IsFilterActive: false })
            _pane.ExitSearchModeCommand.Execute(null);
    }

    private void OnBreadcrumbDragOver(object? sender, DragEventArgs e)
    {
        if (_pane is null || sender is not BreadcrumbItem { SegmentPath: var path })
            return;

        if (!e.DataTransfer.Contains(DataFormat.File) || string.IsNullOrEmpty(path))
            return;

        e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnBreadcrumbDragLeave(object? sender, DragEventArgs e)
        => e.Handled = true;

    private async void OnBreadcrumbDrop(object? sender, DragEventArgs e)
    {
        if (_pane is null || sender is not BreadcrumbItem { SegmentPath: var path }
            || string.IsNullOrEmpty(path))
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
        await _pane.HandleDropAsync(paths, path, isCopy);

        e.Handled = true;
    }
}
