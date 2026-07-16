using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Controls;

public sealed class BreadcrumbItem : Button
{
    public static readonly StyledProperty<string> SegmentPathProperty =
        AvaloniaProperty.Register<BreadcrumbItem, string>(nameof(SegmentPath));

    public static readonly StyledProperty<bool> IsLastProperty =
        AvaloniaProperty.Register<BreadcrumbItem, bool>(nameof(IsLast));

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

    public BreadcrumbItem()
    {
        Padding = new Thickness(6, 2);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (IsLast)
            return;

        var w = Bounds.Width;
        var h = Bounds.Height;
        var pen = new Pen(Foreground ?? Brushes.Gray, 1);
        var cx = w - 6;
        var cy = h / 2;
        context.DrawLine(pen, new Point(cx - 3, cy - 4), new Point(cx, cy));
        context.DrawLine(pen, new Point(cx, cy), new Point(cx - 3, cy + 4));
    }

    public static BreadcrumbItem FromSegment(BreadcrumbSegment segment)
    {
        return new BreadcrumbItem
        {
            Content = segment.DisplayName,
            SegmentPath = segment.Path,
            IsLast = segment.IsLast,
            Padding = new Thickness(6, 2, segment.IsLast ? 6 : 14, 2)
        };
    }
}
