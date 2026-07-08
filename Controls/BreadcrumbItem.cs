using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HelixExplorer.Controls;

/// <summary>
/// One segment of the breadcrumb omnibar: a clickable path segment with a chevron
/// separator on the trailing edge. Right-clicking offers "Open in New Tab",
/// "Open in New Pane", and "Copy Path".
/// </summary>
public sealed class BreadcrumbItem : Button
{
    public static readonly StyledProperty<string> PathProperty =
        AvaloniaProperty.Register<BreadcrumbItem, string>(nameof(Path));

    public static readonly StyledProperty<bool> IsLastProperty =
        AvaloniaProperty.Register<BreadcrumbItem, bool>(nameof(IsLast));

    /// <summary>Raised when the user selects a custom action from the inline context menu.</summary>
    public event EventHandler<BreadcrumbActionEventArgs>? ActionRequested;

    static BreadcrumbItem()
    {
        PathProperty.Changed.AddClassHandler<BreadcrumbItem>((b, _) => b.InvalidateVisual());
    }

    public BreadcrumbItem()
    {
        var menu = new ContextMenu
        {
            Items =
            {
                MakeItem("Open in New Tab", BreadcrumbAction.NewTab),
                MakeItem("Open in New Pane", BreadcrumbAction.NewPane),
                MakeItem("Copy Path", BreadcrumbAction.CopyPath),
            }
        };
        ContextMenu = menu;
    }

    private MenuItem MakeItem(string header, BreadcrumbAction action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => ActionRequested?.Invoke(this, new BreadcrumbActionEventArgs(Path, action));
        return item;
    }

    /// <summary>The absolute path this breadcrumb represents.</summary>
    public string Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>True when this is the rightmost segment — hides the trailing chevron.</summary>
    public bool IsLast
    {
        get => GetValue(IsLastProperty);
        set => SetValue(IsLastProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (IsLast) return;

        // Chevron ">" — drawn two thirds toward the right edge of the segment.
        var w = Bounds.Width;
        var h = Bounds.Height;
        var pen = new Pen(Foreground ?? Brushes.Gray, 1);
        var cx = w - 8;
        var cy = h / 2;
        context.DrawLine(pen, new Point(cx - 3, cy - 4), new Point(cx, cy));
        context.DrawLine(pen, new Point(cx, cy), new Point(cx - 3, cy + 4));
    }
}

public enum BreadcrumbAction
{
    NewTab,
    NewPane,
    CopyPath
}

public sealed class BreadcrumbActionEventArgs : EventArgs
{
    public string Path { get; }
    public BreadcrumbAction Action { get; }
    public BreadcrumbActionEventArgs(string path, BreadcrumbAction action) { Path = path; Action = action; }
}