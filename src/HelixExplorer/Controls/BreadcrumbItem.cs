using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Controls;

public sealed class BreadcrumbItem : StackPanel
{
    public static readonly StyledProperty<string> SegmentPathProperty =
        AvaloniaProperty.Register<BreadcrumbItem, string>(nameof(SegmentPath), string.Empty);

    public static readonly StyledProperty<bool> IsLastProperty =
        AvaloniaProperty.Register<BreadcrumbItem, bool>(nameof(IsLast));

    private readonly Button _segment;
    private readonly Button _separator;

    public BreadcrumbItem()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Spacing = 0;

        _segment = CreateChromeButton();
        _segment.Click += (_, e) =>
        {
            e.Handled = true;
            SegmentActivated?.Invoke(this, EventArgs.Empty);
        };

        _separator = CreateChromeButton();
        _separator.Width = 16;
        _separator.MinWidth = 16;
        _separator.Padding = new Thickness(1, 0);
        _separator.Content = CreateChevron();
        ToolTip.SetTip(_separator, "Show subfolders");
        _separator.Click += (_, e) =>
        {
            // The chevron is not the segment itself; a click must open children, not navigate.
            e.Handled = true;
            SeparatorActivated?.Invoke(this, EventArgs.Empty);
        };

        // Drops land on the label or the chevron, both of which sit above this panel.
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(_segment, true);
        DragDrop.SetAllowDrop(_separator, true);

        Children.Add(_segment);
        Children.Add(_separator);
    }

    public string SegmentPath
    {
        get => GetValue(SegmentPathProperty);
        set => SetValue(SegmentPathProperty, value);
    }

    public bool IsLast
    {
        get => GetValue(IsLastProperty);
        set => SetValue(IsLastProperty, value);
    }

    public Control SeparatorAnchor => _separator;

    public event EventHandler? SegmentActivated;

    public event EventHandler? SeparatorActivated;

    public static BreadcrumbItem FromSegment(BreadcrumbSegment segment)
    {
        var item = new BreadcrumbItem
        {
            SegmentPath = segment.Path,
            IsLast = segment.IsLast,
        };
        item._segment.Content = segment.DisplayName;
        // Shell and archive crumbs are not filesystem directories, so they have no child menu.
        item._separator.IsVisible = CanListChildren(segment.Path);
        return item;
    }

    private static bool CanListChildren(string path)
        => !string.IsNullOrEmpty(path)
           && !ShellPath.IsShellPath(path)
           && !ArchivePath.IsVirtual(path);

    private static Button CreateChromeButton()
        => new()
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            MinHeight = 0,
            MinWidth = 0,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

    private static Control CreateChevron()
        => new PathIcon
        {
            Data = ChevronGeometry(),
            Width = 10,
            Height = 10,
            Opacity = 0.75,
        };

    private static Geometry ChevronGeometry()
    {
        if (Application.Current?.TryGetResource("HelixIconChevronGeometry", Application.Current.ActualThemeVariant, out var resource) == true
            && resource is Geometry geometry)
            return geometry;

        return Geometry.Parse("M7.65,4.15 L13.1,9.6 L7.65,15.05");
    }
}
