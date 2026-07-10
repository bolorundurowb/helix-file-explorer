using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HelixExplorer.Controls;

/// <summary>Rubber-band selection rectangle overlay for file panes.</summary>
public sealed class SelectionMarquee : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<SelectionMarquee, bool>(nameof(IsActive));

    public static readonly StyledProperty<Rect> SelectionRectProperty =
        AvaloniaProperty.Register<SelectionMarquee, Rect>(nameof(SelectionRect));

    static SelectionMarquee()
    {
        AffectsRender<SelectionMarquee>(SelectionRectProperty, IsActiveProperty);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Rect SelectionRect
    {
        get => GetValue(SelectionRectProperty);
        set => SetValue(SelectionRectProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (!IsActive || SelectionRect.Width < 1 || SelectionRect.Height < 1)
            return;

        var fill = new SolidColorBrush(Color.FromArgb(40, 0, 120, 212));
        var border = new SolidColorBrush(Color.FromArgb(200, 0, 120, 212));
        var pen = new Pen(border, 1, dashStyle: DashStyle.Dash);
        context.DrawRectangle(fill, pen, SelectionRect);
    }
}
