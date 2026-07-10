using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using HelixExplorer.ViewModels;
using HelixExplorer.Views;

namespace HelixExplorer.Controls;

public sealed class PaneContentHost : ContentControl
{
    public static readonly StyledProperty<PaneViewModel?> PaneProperty =
        AvaloniaProperty.Register<PaneContentHost, PaneViewModel?>(nameof(Pane));

    public static readonly StyledProperty<HomePageViewModel?> HomeProperty =
        AvaloniaProperty.Register<PaneContentHost, HomePageViewModel?>(nameof(Home));

    private readonly HomePageView _homeView = new();
    private readonly PaneView _fileView = new();
    private PaneViewModel? _subscribedPane;

    static PaneContentHost()
    {
        PaneProperty.Changed.AddClassHandler<PaneContentHost>((host, e) =>
            host.OnPaneChanged(e.OldValue as PaneViewModel, e.NewValue as PaneViewModel));
        HomeProperty.Changed.AddClassHandler<PaneContentHost>((host, _) => host.UpdateContent());
    }

    public PaneContentHost()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    public PaneViewModel? Pane
    {
        get => GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }

    public HomePageViewModel? Home
    {
        get => GetValue(HomeProperty);
        set => SetValue(HomeProperty, value);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromPane();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPaneChanged(PaneViewModel? oldPane, PaneViewModel? newPane)
    {
        if (ReferenceEquals(oldPane, newPane))
            return;

        UnsubscribeFromPane();
        _subscribedPane = newPane;
        if (newPane is not null)
            newPane.PropertyChanged += OnPanePropertyChanged;

        UpdateContent();
    }

    private void UnsubscribeFromPane()
    {
        if (_subscribedPane is null)
            return;

        _subscribedPane.PropertyChanged -= OnPanePropertyChanged;
        _subscribedPane = null;
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaneViewModel.IsHome)
            or nameof(PaneViewModel.LocationKind)
            or nameof(PaneViewModel.CurrentPath))
        {
            UpdateContent();
        }
    }

    private void UpdateContent()
    {
        var pane = Pane;
        if (pane is null)
        {
            Content = null;
            return;
        }

        if (pane.IsHome)
        {
            _homeView.DataContext = Home;
            Content = _homeView;
            return;
        }

        _fileView.DataContext = pane;
        Content = _fileView;
    }
}
