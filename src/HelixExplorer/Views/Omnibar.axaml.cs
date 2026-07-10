using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
}
