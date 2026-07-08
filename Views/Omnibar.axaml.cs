using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelixExplorer.Controls;
using HelixExplorer.ViewModels;
using Avalonia;

namespace HelixExplorer.Views;

/// <summary>
/// Breadcrumb omnibar. Re-renders the path as <see cref="BreadcrumbItem"/> segments whenever
/// the bound pane's <see cref="PaneViewModel.CurrentPath"/> changes. Enter in the path box
/// navigates the pane.
/// </summary>
public sealed partial class Omnibar : UserControl
{
    public Omnibar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private PaneViewModel? _pane;

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_pane != null)
        {
            _pane.PropertyChanged -= OnPanePropertyChanged;
        }
        _pane = DataContext as PaneViewModel;
        if (_pane != null)
        {
            _pane.PropertyChanged += OnPanePropertyChanged;
            RebuildBreadcrumbs(_pane.CurrentPath);
        }
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.CurrentPath) && _pane != null)
        {
            Dispatcher.UIThread.Post(() => RebuildBreadcrumbs(_pane.CurrentPath));
        }
    }

    private void RebuildBreadcrumbs(string path)
    {
        BreadcrumbHost.Children.Clear();
        if (string.IsNullOrEmpty(path)) return;

        // Split path respecting the archive:// scheme.
        var working = path;
        if (working.StartsWith(Services.ArchiveService.Scheme, System.StringComparison.OrdinalIgnoreCase))
        {
            // Show as a single root segment for the archive host plus ordinary inner splits.
            var bang = working.IndexOf('!');
            if (bang > 0)
            {
                AddBreadcrumb(working[..bang]);
                var inner = working[(bang + 1)..];
                AddInnerSegments(inner);
                return;
            }
            AddBreadcrumb(working);
            return;
        }

        var parts = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var accumulator = string.Empty;
        foreach (var part in parts)
        {
            var isDrive = part.Length == 2 && part[1] == ':';
            accumulator = isDrive
                ? part + Path.DirectorySeparatorChar
                : (accumulator.EndsWith(Path.DirectorySeparatorChar) ? accumulator + part : accumulator + Path.DirectorySeparatorChar + part);
            if (!accumulator.EndsWith(Path.DirectorySeparatorChar)) accumulator += Path.DirectorySeparatorChar;
            AddBreadcrumb(accumulator);
        }
        MarkLast();
    }

    private void AddInnerSegments(string inner)
    {
        if (string.IsNullOrEmpty(inner)) { MarkLast(); return; }
        var parts = inner.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var accumulator = string.Empty;
        foreach (var p in parts)
        {
            accumulator = string.IsNullOrEmpty(accumulator) ? p + "/" : accumulator + p + "/";
            AddBreadcrumb(Services.ArchiveService.Scheme + "!" + accumulator);
        }
        MarkLast();
    }

    private void AddBreadcrumb(string path)
    {
        var item = new BreadcrumbItem
        {
            Path = path,
            Content = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path,
        };
        item.Click += OnBreadcrumbClick;
        item.ActionRequested += OnBreadcrumbAction;
        BreadcrumbHost.Children.Add(item);
    }

    private void MarkLast()
    {
        if (BreadcrumbHost.Children.Count > 0 && BreadcrumbHost.Children[^1] is BreadcrumbItem last)
        {
            last.IsLast = true;
        }
    }

    private void OnBreadcrumbClick(object? sender, RoutedEventArgs e)
    {
        if (sender is BreadcrumbItem item && _pane != null)
        {
            _pane.NavigateTo(item.Path);
        }
    }

    private void OnBreadcrumbAction(object? sender, BreadcrumbActionEventArgs e)
    {
        switch (e.Action)
        {
            case BreadcrumbAction.NewTab:
                // Bubble to the MainWindow ViewModel via the visual tree.
                if (this.GetVisualRoot() is MainWindow mw && mw.DataContext is MainWindowViewModel mvm)
                {
                    mvm.OpenNewTabCommand.Execute(null);
                    if (mvm.ActiveTab != null) mvm.ActiveTab.ActivePane.NavigateTo(e.Path);
                }
                break;
            case BreadcrumbAction.NewPane:
                if (this.GetVisualRoot() is MainWindow mw2 && mw2.DataContext is MainWindowViewModel mvm2 && mvm2.ActiveTab != null)
                {
                    var inactive = mvm2.ActiveTab.ActivePane == mvm2.ActiveTab.Left ? mvm2.ActiveTab.Right : mvm2.ActiveTab.Left;
                    inactive.NavigateTo(e.Path);
                    mvm2.ActiveTab.ActivePane = inactive;
                }
                break;
            case BreadcrumbAction.CopyPath:
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
                {
                    desk.MainWindow?.Clipboard?.SetTextAsync(e.Path);
                }
                break;
        }
    }

    /// <summary>Clicking empty space in the breadcrumb strip switches to editable-path mode.</summary>
    private void OnBreadcrumbAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        // Ignore clicks that landed on an actual breadcrumb segment (those navigate).
        if (e.Source is BreadcrumbItem) return;
        EnterEditMode();
    }

    private void OnEditToggle(object? sender, RoutedEventArgs e) => EnterEditMode();

    private void EnterEditMode()
    {
        if (_pane is null) return;
        PathBox.Text = _pane.CurrentPath;
        _pane.IsEditingPath = true;
        Dispatcher.UIThread.Post(() =>
        {
            PathBox.Focus();
            PathBox.SelectAll();
        });
    }

    private void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_pane is null) return;
        if (e.Key == Key.Enter)
        {
            if (PathBox.Text is { Length: > 0 } text) _pane.NavigateTo(text);
            _pane.IsEditingPath = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _pane.IsEditingPath = false;
            e.Handled = true;
        }
    }
}